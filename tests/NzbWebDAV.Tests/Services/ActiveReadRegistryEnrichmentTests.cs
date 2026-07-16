using NzbWebDAV.Database.Models.Metrics;
using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Services;

public class ActiveReadRegistryEnrichmentTests
{
    [Fact]
    public void GetOrCreate_StoresClientMetadata()
    {
        var registry = new ActiveReadRegistry();
        var id = registry.GetOrCreate(
            "/view/movie.mkv",
            "127.0.0.1|VLC",
            "movie.mkv",
            1024,
            "VLC/3.0",
            "127.0.0.1");

        var snap = registry.Snapshot().Single(e => e.Id == id);
        Assert.Equal("VLC/3.0", snap.ClientUserAgent);
        Assert.Equal("127.0.0.1", snap.ClientIp);
        Assert.Equal(ReadSession.EndReasonCode.Completed, snap.EndReason);
    }

    [Fact]
    public void SetEndReason_And_AddBytesFetched_UpdateEntry()
    {
        var registry = new ActiveReadRegistry();
        var id = registry.GetOrCreate("/p", "k", "f", 100);
        registry.AddBytesFetched(id, 40);
        registry.AddBytesFetched(id, 10);
        registry.Touch(id, 25, 25);
        registry.SetEndReason(id, ReadSession.EndReasonCode.Aborted);

        var entry = registry.Snapshot().Single(e => e.Id == id);
        Assert.Equal(50, Interlocked.Read(ref entry.BytesFetched));
        Assert.Equal(25, Interlocked.Read(ref entry.BytesRead));
        Assert.Equal(ReadSession.EndReasonCode.Aborted, entry.EndReason);
        Assert.Equal(25, registry.GetBytesRead(id));
    }
}
