using System.Text;
using NzbWebDAV.Models;
using NzbWebDAV.Streams;
using NzbWebDAV.Tests.Fakes;

namespace NzbWebDAV.Tests.Streams;

public class NzbFileStreamTests
{
    private static readonly byte[][] SegmentBytes =
    [
        Encoding.ASCII.GetBytes("abcde"),
        Encoding.ASCII.GetBytes("fghij"),
        Encoding.ASCII.GetBytes("klmno")
    ];

    private static readonly string[] SegmentIds = ["one", "two", "three"];
    private static readonly LongRange[] SegmentRanges =
    [
        new(0, 5),
        new(5, 10),
        new(10, 15)
    ];

    [Theory]
    [InlineData(0, "abcdefghijklmno")]
    [InlineData(1, "abcdefghijklmno")]
    [InlineData(4, "abcdefghijklmno")]
    public async Task ReadAsync_ConcatenatesSegmentsWithConfiguredPipeline(
        int articleBufferSize, string expected)
    {
        var client = CreateClient();
        await using var stream = new NzbFileStream(
            SegmentIds, 15, client, articleBufferSize, SegmentRanges);

        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);

        Assert.Equal(expected, Encoding.ASCII.GetString(destination.ToArray()));
        if (articleBufferSize > 0) Assert.True(client.BatchRequestCount > 0);
    }

    [Theory]
    [InlineData(0, "abc")]
    [InlineData(4, "efg")]
    [InlineData(5, "fgh")]
    [InlineData(9, "jkl")]
    [InlineData(14, "o")]
    public async Task Seek_ReadsAcrossSegmentBoundaries(long offset, string expected)
    {
        var client = CreateClient();
        await using var stream = new NzbFileStream(
            SegmentIds, 15, client, 2, SegmentRanges);
        stream.Seek(offset, SeekOrigin.Begin);
        var buffer = new byte[3];

        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);

        Assert.Equal(expected, Encoding.ASCII.GetString(buffer, 0, read));
        Assert.Equal(offset + read, stream.Position);
    }

    [Fact]
    public void Seek_RejectsPositionsOutsideFile()
    {
        using var stream = new NzbFileStream(
            SegmentIds, 15, CreateClient(), 1, SegmentRanges);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => stream.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => stream.Seek(16, SeekOrigin.Begin));
    }

    [Fact]
    public async Task SmallForwardSeek_DrainsExistingPipeline()
    {
        var client = CreateClient();
        await using var stream = new NzbFileStream(
            SegmentIds, 15, client, 2, SegmentRanges);
        var initial = new byte[2];
        Assert.Equal(2, await stream.ReadAsync(initial));

        stream.Seek(7, SeekOrigin.Begin);
        var buffer = new byte[3];
        var read = await stream.ReadAsync(buffer);

        Assert.Equal("hij", Encoding.ASCII.GetString(buffer, 0, read));
    }

    private static FakeNntpClient CreateClient()
    {
        return new FakeNntpClient(
            SegmentIds.Zip(SegmentBytes).ToDictionary(pair => pair.First, pair => pair.Second));
    }
}
