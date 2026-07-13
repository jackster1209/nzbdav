using NzbWebDAV.Clients.Usenet;
using NzbWebDAV.Clients.Usenet.Models;
using NzbWebDAV.Models.Nzb;
using UsenetSharp.Models;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class WrappingNntpClientRetirementTests
{
    [Fact]
    public async Task ReplaceUnderlyingClient_DrainsUntilInFlightZero()
    {
        var oldClient = new CountingDisposableClient { InFlightConnections = 1 };
        var wrapper = new TestWrappingClient(oldClient);
        var newClient = new CountingDisposableClient();

        var drainTask = wrapper.ReplaceUnderlyingClientForTestsAsync(
            newClient, TimeSpan.FromSeconds(5));

        Assert.False(oldClient.Disposed);

        oldClient.InFlightConnections = 0;
        await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(oldClient.Disposed);
        Assert.False(newClient.Disposed);
        Assert.Equal(0, wrapper.InFlightConnections);
    }

    [Fact]
    public async Task ReplaceUnderlyingClient_ForceDisposesAfterGracePeriod()
    {
        var oldClient = new CountingDisposableClient { InFlightConnections = 5 };
        var wrapper = new TestWrappingClient(oldClient);
        var newClient = new CountingDisposableClient();

        await wrapper.ReplaceUnderlyingClientForTestsAsync(
            newClient, TimeSpan.FromMilliseconds(50));

        Assert.True(oldClient.Disposed);
    }

    private sealed class TestWrappingClient(INntpClient inner) : WrappingNntpClient(inner);

    private sealed class CountingDisposableClient : NntpClient, INntpConnectionStats
    {
        public int InFlightConnections { get; set; }
        public bool Disposed { get; private set; }

        public override Task ConnectAsync(string host, int port, bool useSsl, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public override Task<UsenetResponse> AuthenticateAsync(
            string user, string pass, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetStatResponse> StatAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetHeadResponse> HeadAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyResponse> DecodedBodyAsync(
            SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedBodyBatch> DecodedBodiesAsync(
            IReadOnlyList<SegmentId> segmentIds, Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDecodedArticleResponse> DecodedArticleAsync(
            SegmentId segmentId, Action<ArticleBodyResult>? onConnectionReadyAgain,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetDateResponse> DateAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<UsenetYencHeader> GetYencHeadersAsync(
            string segmentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task<long> GetFileSizeAsync(NzbFile file, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override void Dispose()
        {
            Disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
