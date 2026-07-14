using NzbWebDAV.Api.Controllers.GetOverviewStats;
using NzbWebDAV.Database.Models.Metrics;

namespace NzbWebDAV.Tests.Api;

public class GetOverviewStatsProviderFilterTests
{
    private static readonly string ConfiguredKey = Guid.NewGuid().ToString("N");
    private static readonly string DeletedKey = Guid.NewGuid().ToString("N");

    private static readonly IReadOnlyDictionary<string, string?> Labels =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ConfiguredKey] = "Primary",
        };

    [Fact]
    public void IsConfiguredMetricsKey_OnlyMatchesLabelMap()
    {
        Assert.True(GetOverviewStatsController.IsConfiguredMetricsKey(ConfiguredKey, Labels));
        Assert.False(GetOverviewStatsController.IsConfiguredMetricsKey(DeletedKey, Labels));
    }

    [Fact]
    public void BuildProvidersFromMinutes_OmitsDeletedProviderKeys()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = nowMs - 60_000;
        var minutes = new[]
        {
            (windowStart, ConfiguredKey, 10L, 1000L, 0L, 0L, 100L),
            (windowStart, DeletedKey, 50L, 5000L, 1L, 2L, 500L),
        };

        var rows = GetOverviewStatsController.BuildProvidersFromMinutes(
            minutes,
            windowStart,
            GetOverviewStatsRequest.OverviewWindow.Last1Hour,
            Labels);

        Assert.Single(rows);
        Assert.Equal(ConfiguredKey, rows[0].Provider);
        Assert.Equal("Primary", rows[0].Nickname);
        Assert.Equal(10, rows[0].Articles);
    }

    [Fact]
    public void BuildFailover_OmitsDeletedProvidersFromListsButKeepsAggregateTotals()
    {
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rescues = new[]
        {
            (at, ConfiguredKey, 3L),
            (at, DeletedKey, 7L),
        };
        var misses = new[]
        {
            (ConfiguredKey, SegmentFetch.FetchStatus.Missing, 2L),
            (DeletedKey, SegmentFetch.FetchStatus.Timeout, 4L),
        };

        var block = GetOverviewStatsController.BuildFailover(
            rescues,
            misses,
            totalArticles: 100,
            readSessions: 5,
            readsSaved: 2,
            previousSaves: 1,
            chartBucketSize: 60_000,
            Labels);

        Assert.Equal(10, block.ArticlesRecovered);
        Assert.Equal(6, block.SegmentsCovered);

        Assert.Single(block.RescuedBy);
        Assert.Equal(ConfiguredKey, block.RescuedBy[0].Provider);
        Assert.Equal("Primary", block.RescuedBy[0].Nickname);
        Assert.Equal(3, block.RescuedBy[0].Saves);

        Assert.Single(block.RescuedFrom);
        Assert.Equal(ConfiguredKey, block.RescuedFrom[0].Provider);
        Assert.Equal(2, block.RescuedFrom[0].Misses);

        Assert.All(block.Buckets, b => Assert.Single(b.Counts));
        Assert.Equal(3, block.Buckets.Sum(b => b.Counts.Sum()));
    }
}
