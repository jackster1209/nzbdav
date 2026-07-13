using System.Collections.Concurrent;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Models.Nzb;
using Serilog;
using UsenetSharp.Models;

namespace NzbWebDAV.Clients.Usenet;

public class WrappingNntpClient(INntpClient usenetClient) : NntpClient, INntpConnectionStats
{
    private const int MaxRetiringClients = 4;
    private static readonly TimeSpan RetirementGracePeriod = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DrainPollInterval = TimeSpan.FromMilliseconds(250);

    private INntpClient _usenetClient = usenetClient;
    private readonly ConcurrentQueue<(INntpClient Client, DateTimeOffset Deadline)> _retiringClients = new();
    private int _retiringCount;
    private int _drainLoopRunning;

    public int InFlightConnections =>
        _usenetClient is INntpConnectionStats stats ? stats.InFlightConnections : 0;

    public override Task ConnectAsync(
        string host, int port, bool useSsl, CancellationToken cancellationToken) =>
        _usenetClient.ConnectAsync(host, port, useSsl, cancellationToken);

    public override Task<UsenetResponse> AuthenticateAsync(
        string user, string pass, CancellationToken cancellationToken) =>
        _usenetClient.AuthenticateAsync(user, pass, cancellationToken);

    public override Task<UsenetStatResponse> StatAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.StatAsync(segmentId, cancellationToken);

    public override Task<UsenetHeadResponse> HeadAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.HeadAsync(segmentId, cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodyAsync(segmentId, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, CancellationToken cancellationToken) =>
        _usenetClient.DecodedArticleAsync(segmentId, cancellationToken);

    public override Task<UsenetDateResponse> DateAsync(
        CancellationToken cancellationToken) =>
        _usenetClient.DateAsync(cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodyAsync(segmentId, onConnectionReadyAgain, cancellationToken);

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds, Action<ArticleBodyResult>? onConnectionReadyAgain,
        CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodiesAsync(segmentIds, onConnectionReadyAgain, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain, CancellationToken cancellationToken) =>
        _usenetClient.DecodedArticleAsync(segmentId, onConnectionReadyAgain, cancellationToken);

    public override int PipeliningDepth => _usenetClient.PipeliningDepth;

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        string segmentId, CancellationToken cancellationToken) =>
        _usenetClient.AcquireExclusiveConnectionAsync(segmentId, cancellationToken);

    public override Task<UsenetExclusiveConnection> AcquireExclusiveConnectionAsync(
        IReadOnlyList<SegmentId> segmentIds, CancellationToken cancellationToken) =>
        _usenetClient.AcquireExclusiveConnectionAsync(segmentIds, cancellationToken);

    public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
        SegmentId segmentId, UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodyAsync(segmentId, exclusiveConnection, cancellationToken);

    public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
        IReadOnlyList<SegmentId> segmentIds, UsenetExclusiveConnection exclusiveConnection,
        CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodiesAsync(segmentIds, exclusiveConnection, cancellationToken);

    public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
        SegmentId segmentId, UsenetExclusiveConnection exclusiveConnection, CancellationToken cancellationToken) =>
        _usenetClient.DecodedArticleAsync(segmentId, exclusiveConnection, cancellationToken);

    public override Task<UsenetYencHeader> GetYencHeadersAsync(
        string segmentId, CancellationToken cancellationToken) =>
        _usenetClient.GetYencHeadersAsync(segmentId, cancellationToken);

    public override Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken) =>
        _usenetClient.GetFileSizeAsync(file, cancellationToken);

    public override IAsyncEnumerable<PipelinedStatResult> StatsPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth, CancellationToken cancellationToken) =>
        _usenetClient.StatsPipelinedAsync(segmentIds, depth, cancellationToken);

    public override IAsyncEnumerable<PipelinedBodyResult> DecodedBodiesPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth, CancellationToken cancellationToken) =>
        _usenetClient.DecodedBodiesPipelinedAsync(segmentIds, depth, cancellationToken);

    public override IAsyncEnumerable<PipelinedArticleResult> DecodedArticlesPipelinedAsync(
        IReadOnlyList<string> segmentIds, int depth, CancellationToken cancellationToken) =>
        _usenetClient.DecodedArticlesPipelinedAsync(segmentIds, depth, cancellationToken);

    /// <summary>
    /// Swap the live client immediately so new requests use new pools, then retire the
    /// old client after in-flight borrows drain (or a bounded grace period).
    /// </summary>
    protected void ReplaceUnderlyingClient(INntpClient usenetClient)
    {
        var old = _usenetClient;
        _usenetClient = usenetClient;
        EnqueueForRetirement(old);
    }

    /// <summary>
    /// Test hook: swap with an explicit grace period and wait for the drain loop to finish.
    /// Drains inline (no background loop) so exactly one consumer works the queue.
    /// </summary>
    internal Task ReplaceUnderlyingClientForTestsAsync(
        INntpClient usenetClient, TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        var old = _usenetClient;
        _usenetClient = usenetClient;
        EnqueueForRetirement(old, DateTimeOffset.UtcNow + gracePeriod, startDrainLoop: false);
        return DrainRetiringClientsAsync(cancellationToken);
    }

    private void EnqueueForRetirement(
        INntpClient old, DateTimeOffset? deadline = null, bool startDrainLoop = true)
    {
        deadline ??= DateTimeOffset.UtcNow + RetirementGracePeriod;

        // Bound stacked rapid saves: force-dispose the oldest excess clients.
        while (Volatile.Read(ref _retiringCount) >= MaxRetiringClients
               && _retiringClients.TryDequeue(out var excess))
        {
            Interlocked.Decrement(ref _retiringCount);
            TryDispose(excess.Client, forced: true);
        }

        Interlocked.Increment(ref _retiringCount);
        _retiringClients.Enqueue((old, deadline.Value));
        if (startDrainLoop)
            EnsureDrainLoop();
    }

    private void EnsureDrainLoop()
    {
        if (Interlocked.CompareExchange(ref _drainLoopRunning, 1, 0) != 0)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await DrainRetiringClientsAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _drainLoopRunning, 0);
                // A replace may have enqueued while we were clearing the flag.
                if (!_retiringClients.IsEmpty)
                    EnsureDrainLoop();
            }
        });
    }

    private async Task DrainRetiringClientsAsync(CancellationToken cancellationToken)
    {
        while (!_retiringClients.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_retiringClients.TryPeek(out var next))
                break;

            var inFlight = next.Client is INntpConnectionStats stats
                ? stats.InFlightConnections
                : 0;
            var expired = DateTimeOffset.UtcNow >= next.Deadline;

            if (inFlight > 0 && !expired)
            {
                await Task.Delay(DrainPollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!_retiringClients.TryDequeue(out next))
                break;

            Interlocked.Decrement(ref _retiringCount);
            TryDispose(next.Client, forced: inFlight > 0);
        }
    }

    private static void TryDispose(INntpClient client, bool forced)
    {
        try
        {
            if (forced)
                Log.Debug("Force-disposing replaced NNTP client after grace period with in-flight work remaining");
            client.Dispose();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to dispose replaced NNTP client");
        }
    }

    public override void Dispose()
    {
        // Dispose the live client and anything still retiring.
        while (_retiringClients.TryDequeue(out var retiring))
        {
            Interlocked.Decrement(ref _retiringCount);
            TryDispose(retiring.Client, forced: true);
        }

        _usenetClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
