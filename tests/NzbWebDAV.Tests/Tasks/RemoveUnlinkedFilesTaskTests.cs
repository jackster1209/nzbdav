using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using NzbWebDAV.Config;
using NzbWebDAV.Database;
using NzbWebDAV.Database.Interceptors;
using NzbWebDAV.Database.MigrationHelpers;
using NzbWebDAV.Database.Models;
using NzbWebDAV.Tasks;
using NzbWebDAV.Websocket;

namespace NzbWebDAV.Tests.Tasks;

[Collection(nameof(BaseTaskCollection))]
public class RemoveUnlinkedFilesTaskTests
{
    [Fact]
    public async Task RemoveEmptyDirectoriesAsync_RemovesNestedEmptyDirsAndTerminates()
    {
        await using var harness = await TempDb.CreateAsync();
        var ctx = harness.Context;
        var createdBefore = DateTime.Now.AddMinutes(1);

        var parent = NewDir(Guid.NewGuid(), DavItem.ContentFolder, "show");
        var child = NewDir(Guid.NewGuid(), parent, "season");
        var grandchild = NewDir(Guid.NewGuid(), child, "empty");
        // keptFile under ContentFolder keeps that root non-empty; empty show/season tree is removed.
        var keptFile = DavItem.New(
            Guid.NewGuid(),
            DavItem.ContentFolder,
            "keep.mkv",
            10,
            DavItem.ItemType.UsenetFile,
            DavItem.ItemSubType.NzbFile,
            null,
            null,
            null,
            null);
        await SeedRootsAsync(ctx);
        ctx.Items.AddRange(parent, child, grandchild, keptFile);
        await ctx.SaveChangesAsync();

        var removed = await RemoveUnlinkedFilesTask.RemoveEmptyDirectoriesAsync(
            ctx,
            createdBefore);

        Assert.True(removed >= 2);
        Assert.False(await ctx.Items.AnyAsync(x => x.Id == grandchild.Id));
        Assert.False(await ctx.Items.AnyAsync(x => x.Id == child.Id));
        Assert.False(await ctx.Items.AnyAsync(x => x.Id == parent.Id));
        Assert.True(await ctx.Items.AnyAsync(x => x.Id == keptFile.Id));
    }

    [Fact]
    public async Task RemoveEmptyDirectoriesAsync_ReturnsZero_WhenNoEmptyDirectories()
    {
        await using var harness = await TempDb.CreateAsync();
        var ctx = harness.Context;
        await SeedRootsAsync(ctx);
        var file = DavItem.New(
            Guid.NewGuid(),
            DavItem.ContentFolder,
            "only.mkv",
            10,
            DavItem.ItemType.UsenetFile,
            DavItem.ItemSubType.NzbFile,
            null,
            null,
            null,
            null);
        ctx.Items.Add(file);
        await ctx.SaveChangesAsync();

        // Drain migration-seeded empty category folders (e.g. /content/uncategorized
        // from Fix-Empty-Categories). Filling them via EF fails: ParentId is stored
        // uppercase while the seeded folder Id is lowercase, so raw SQL still sees
        // the folder as empty.
        var createdBefore = DateTime.Now.AddMinutes(1);
        await RemoveUnlinkedFilesTask.RemoveEmptyDirectoriesAsync(ctx, createdBefore);

        var removed = await RemoveUnlinkedFilesTask.RemoveEmptyDirectoriesAsync(
            ctx,
            createdBefore);

        Assert.Equal(0, removed);
        Assert.True(await ctx.Items.AnyAsync(x => x.Id == file.Id));
    }

    [Fact]
    public async Task Execute_ReturnsFalse_WhenAnotherTaskIsRunning()
    {
        await BaseTask.ResetRunningTaskForTestsAsync();
        try
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var longRunning = new BlockingTask(gate.Task);
            var run = longRunning.Execute();

            // Give the long-running task time to claim the single-flight slot.
            await Task.Delay(50);
            var second = new NoOpTask();
            Assert.False(await second.Execute());

            gate.SetResult();
            Assert.True(await run);
        }
        finally
        {
            await BaseTask.ResetRunningTaskForTestsAsync();
        }
    }

    [Fact]
    public async Task DryRun_ReportsDone_WithTerminalProgress()
    {
        await BaseTask.ResetRunningTaskForTestsAsync();
        var libraryDir = Path.Combine(Path.GetTempPath(), $"nzbdav-lib-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryDir);
        await using var harness = await TempDb.CreateAsync();
        try
        {
            var ctx = harness.Context;
            await SeedRootsAsync(ctx);

            var linkedIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
            var orphanId = Guid.NewGuid();
            foreach (var id in linkedIds.Append(orphanId))
            {
                ctx.Items.Add(DavItem.New(
                    id,
                    DavItem.ContentFolder,
                    $"{id:N}.mkv",
                    10,
                    DavItem.ItemType.UsenetFile,
                    DavItem.ItemSubType.NzbFile,
                    null,
                    null,
                    null,
                    null));
            }

            await ctx.SaveChangesAsync();

            foreach (var id in linkedIds)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(libraryDir, $"{id:N}.strm"),
                    $"http://localhost/view/.ids/{id}.mkv");
            }

            var config = new ConfigManager();
            config.UpdateValues(
            [
                new ConfigItem { ConfigName = ConfigKeys.MediaLibraryDir, ConfigValue = libraryDir },
            ]);

            var websocket = new WebsocketManager();
            var task = new RemoveUnlinkedFilesTask(
                config,
                websocket,
                isDryRun: true,
                createContext: () => harness.CreateContext());

            Assert.True(await task.Execute());

            var progress = websocket.PeekLastMessage(WebsocketTopic.CleanupTaskProgress);
            Assert.NotNull(progress);
            Assert.StartsWith("Dry Run - Done.", progress);
            Assert.Contains("Identified 1 unlinked files", progress);
        }
        finally
        {
            await BaseTask.ResetRunningTaskForTestsAsync();
            RemoveUnlinkedFilesTask.ClearAuditPathsForTests();
            try { Directory.Delete(libraryDir, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task InsertLinkedIdBatchAsync_InsertsAllIds()
    {
        await using var harness = await TempDb.CreateAsync();
        var ctx = harness.Context;
        await ctx.Database.ExecuteSqlRawAsync(
            """
            DROP TABLE IF EXISTS TMP_LINKED_FILES;
            CREATE TABLE TMP_LINKED_FILES (Id TEXT NOT NULL);
            """);

        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        await RemoveUnlinkedFilesTask.InsertLinkedIdBatchAsync(ctx, ids);

        var count = await ctx.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM TMP_LINKED_FILES")
            .FirstAsync();
        Assert.Equal(3, count);
    }

    private static DavItem NewDir(Guid id, DavItem parent, string name) =>
        DavItem.New(id, parent, name, null, DavItem.ItemType.Directory, DavItem.ItemSubType.Directory,
            null, null, null, null);

    private static async Task SeedRootsAsync(DavDatabaseContext ctx)
    {
        // Migrations already insert roots; ensure local references match persisted rows.
        if (!await ctx.Items.AnyAsync(x => x.Id == DavItem.Root.Id))
            ctx.Items.Add(DavItem.Root);
        if (!await ctx.Items.AnyAsync(x => x.Id == DavItem.ContentFolder.Id))
            ctx.Items.Add(DavItem.ContentFolder);
        await ctx.SaveChangesAsync();
    }

    private sealed class BlockingTask(Task gate) : BaseTask
    {
        protected override Task ExecuteInternal() => gate;
    }

    private sealed class NoOpTask : BaseTask
    {
        protected override Task ExecuteInternal() => Task.CompletedTask;
    }

    private sealed class TempDb : IAsyncDisposable
    {
        private readonly string _path;
        private TempDb(string path, DavDatabaseContext context)
        {
            _path = path;
            Context = context;
        }

        public DavDatabaseContext Context { get; }

        public DavDatabaseContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<DavDatabaseContext>()
                .UseSqlite($"Data Source={_path}")
                .AddInterceptors(new SqliteMainDbPragmas())
                .ReplaceService<IMigrationsSqlGenerator, SqliteMigrationsSqlGenerator<SqliteMigrationsSqlGenerator>>()
                .Options;
            return new DavDatabaseContext(options);
        }

        public static async Task<TempDb> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"nzbdav-unlinked-{Guid.NewGuid():N}.sqlite");
            var options = new DbContextOptionsBuilder<DavDatabaseContext>()
                .UseSqlite($"Data Source={path}")
                .AddInterceptors(new SqliteMainDbPragmas())
                .ReplaceService<IMigrationsSqlGenerator, SqliteMigrationsSqlGenerator<SqliteMigrationsSqlGenerator>>()
                .Options;
            var context = new DavDatabaseContext(options);
            await context.Database.MigrateAsync();
            return new TempDb(path, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            try { File.Delete(_path); } catch { /* best effort */ }
            try { File.Delete(_path + "-wal"); } catch { /* best effort */ }
            try { File.Delete(_path + "-shm"); } catch { /* best effort */ }
        }
    }
}

[CollectionDefinition(nameof(BaseTaskCollection))]
public class BaseTaskCollection : ICollectionFixture<object>;
