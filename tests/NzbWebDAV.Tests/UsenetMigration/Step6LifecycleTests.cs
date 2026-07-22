using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Config;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Database.Models.UsenetMigration;
using NzbWebDAV.Queue;
using NzbWebDAV.Services;
using NzbWebDAV.Services.Metrics;
using NzbWebDAV.Services.StreamTrace;
using NzbWebDAV.UsenetMigration;
using NzbWebDAV.UsenetMigration.Runner;
using NzbWebDAV.UsenetMigration.Symlinks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Tests.UsenetMigration;

public sealed class Step6LifecycleTests
{
    private sealed class BlockingSymlinkOps(string path, string currentTarget) : ISymlinkOps
    {
        internal TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? ReadLink(string libraryRoot, string candidatePath) =>
            candidatePath == path ? currentTarget : null;

        public void CreateOrReplaceSymlink(string libraryRoot, string candidatePath, string target)
        {
            Entered.TrySetResult(true);
            Release.Task.GetAwaiter().GetResult();
            currentTarget = target;
        }
    }

    [Fact]
    public async Task Restore_BlocksPlanAndApply_AndDrainsAfterClientDisconnect()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var root = Directory.CreateTempSubdirectory("altmig-library-");
        var backups = Directory.CreateTempSubdirectory("altmig-backups-");
        var link = Path.Combine(root.FullName, "movie.mkv");
        var original = "/mnt/altmount/movie.mkv";
        var replacement = "/mnt/nzbdav/.ids/x";
        var archiveName = "altmount-symlink-backup-20260721-120000.tar.gz";
        await SymlinkBackup.WriteAsync(
            Path.Combine(backups.FullName, archiveName),
            [new SymlinkBackup.Entry(link, original, replacement)]);
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "linked";
            s.SymlinkLibraryRoot = root.FullName;
            s.SymlinkBackupDir = backups.FullName;
        });

        using var queueManager = CreateQueueManager();
        var runner = new UsenetMigrationRunner(
            h.Store, queueManager, new ConfigManager(), new WebsocketManager());
        var ops = new BlockingSymlinkOps(link, replacement);
        runner.SymlinkRestoreServiceForTests.Ops = ops;
        using var disconnectedClient = new CancellationTokenSource();

        try
        {
            var restore = runner.RestoreSymlinksAsync(archiveName, disconnectedClient.Token);
            await ops.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal("restoring", (await h.Store.GetSessionAsync()).Status);
            var plan = await h.Store.StartSymlinkPlanAsync(root.FullName, backups.FullName);
            var apply = await h.Store.TryTransitionSessionAsync(MigrationSessionTransition.StartApply);
            Assert.Equal(MigrationSessionTransitionOutcome.Rejected, plan.Outcome);
            Assert.Equal(MigrationSessionTransitionOutcome.Rejected, apply.Outcome);

            disconnectedClient.Cancel();
            ops.Release.TrySetResult(true);

            var summary = await restore.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, summary.Restored);
            Assert.Equal("linked", (await h.Store.GetSessionAsync()).Status);
        }
        finally
        {
            ops.Release.TrySetResult(true);
            root.Delete(recursive: true);
            backups.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PlanFailure_ReturnsToLinkedInsteadOfTrappingWizard()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s =>
        {
            s.Status = "linking";
            s.SymlinkLibraryRoot = "/library";
        });
        using var queueManager = CreateQueueManager();
        var runner = new UsenetMigrationRunner(
            h.Store, queueManager, new ConfigManager(), new WebsocketManager());
        runner.SymlinkPlannerForTests.LibraryRootValidator = _ =>
            throw new IOException("simulated plan failure");

        await runner.TickOnceForTestsAsync();

        Assert.Equal("linked", (await h.Store.GetSessionAsync()).Status);
    }

    [Fact]
    public async Task ApplyFailure_ReturnsToLinkedInsteadOfTrappingWizard()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        var notDirectory = Path.GetTempFileName();
        try
        {
            await h.Store.UpdateSessionAsync(s =>
            {
                s.Status = "applying";
                s.SymlinkLibraryRoot = "/library";
                s.SymlinkBackupDir = notDirectory;
            });
            await using (var migration = h.Mig())
            {
                migration.SymlinkRewrites.Add(new MigrationSymlinkRewrite
                {
                    SymlinkPath = "/library/movie.mkv",
                    OldTarget = "/mnt/altmount/movie.mkv",
                    NewTarget = "/mnt/nzbdav/.ids/x",
                    Status = "rewrite",
                    UpdatedAt = DateTime.UtcNow,
                });
                await migration.SaveChangesAsync();
            }
            using var queueManager = CreateQueueManager();
            var runner = new UsenetMigrationRunner(
                h.Store, queueManager, new ConfigManager(), new WebsocketManager());

            await runner.TickOnceForTestsAsync();

            Assert.Equal("linked", (await h.Store.GetSessionAsync()).Status);
        }
        finally
        {
            File.Delete(notDirectory);
        }
    }

    [Fact]
    public async Task StaleRestoringState_ReturnsToLinkedOnRunnerTick()
    {
        await using var h = await MigrationTestHarness.CreateAsync();
        await h.Store.UpdateSessionAsync(s => s.Status = "restoring");
        using var queueManager = CreateQueueManager();
        var runner = new UsenetMigrationRunner(
            h.Store, queueManager, new ConfigManager(), new WebsocketManager());

        await runner.TickOnceForTestsAsync();

        Assert.Equal("linked", (await h.Store.GetSessionAsync()).Status);
    }

    private static QueueManager CreateQueueManager()
    {
        var config = new ConfigManager();
        config.UpdateValues(
        [
            new ConfigItem
            {
                ConfigName = ConfigKeys.UsenetProviders,
                ConfigValue = JsonSerializer.Serialize(new UsenetProviderConfig()),
            },
        ]);
        var websocket = new WebsocketManager();
        var usenet = new UsenetStreamingClient(
            config,
            websocket,
            new ProviderUsageTracker(),
            new MetricsWriter(),
            new ProviderBytesTracker(),
            new StreamTraceBuffer(100),
            new ActiveReadRegistry());
        return new QueueManager(
            usenet,
            config,
            websocket,
            new ProviderUsageTracker(),
            new WatchdogLog(),
            new QueueItemSourceTracker(),
            new BenchmarkGate(),
            startLoop: false);
    }
}
