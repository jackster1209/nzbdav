using NzbWebDAV.Services.Benchmark;

namespace NzbWebDAV.Tests.Services.Benchmark;

public class BenchmarkMathTests
{
    [Fact]
    public void ComputeSteadyRate_UsesMedianOfBuckets()
    {
        var buckets = new List<(long Bytes, double Seconds)>
        {
            (10_000_000, 0.5),
            (12_000_000, 0.5),
            (11_000_000, 0.5),
        };

        var (mbPerSec, cv) = UsenetBenchmarkService.ComputeSteadyRate(buckets, 0, 0);

        Assert.Equal(22, mbPerSec, 6);
        Assert.InRange(cv, 0.07, 0.08);
    }

    [Fact]
    public void ComputeSteadyRate_IdenticalBucketsHaveZeroVariation()
    {
        var buckets = Enumerable.Repeat((Bytes: 10_000_000L, Seconds: 0.5), 3).ToList();

        var (_, cv) = UsenetBenchmarkService.ComputeSteadyRate(buckets, 0, 0);

        Assert.Equal(0, cv);
    }

    [Fact]
    public void ComputeSteadyRate_FallsBackWhenThereAreTooFewBuckets()
    {
        var buckets = new List<(long Bytes, double Seconds)>
        {
            (10_000_000, 0.5),
            (12_000_000, 0.5),
        };

        var (mbPerSec, cv) = UsenetBenchmarkService.ComputeSteadyRate(
            buckets, fallbackBytes: 9_000_000, fallbackSeconds: 0.5);

        Assert.Equal(18, mbPerSec, 6);
        Assert.True(cv >= 0.5);
    }

    [Fact]
    public void ComputeSteadyRate_ZeroDurationFallbackReturnsZero()
    {
        var (mbPerSec, cv) = UsenetBenchmarkService.ComputeSteadyRate(
            [], fallbackBytes: 9_000_000, fallbackSeconds: 0);

        Assert.Equal(0, mbPerSec);
        Assert.Equal(1, cv);
    }

    [Fact]
    public void AdaptiveTargetBytes_UsesProfileFloorWithoutEstimate()
    {
        var profile = BenchmarkProfile.For(BenchmarkIntensity.Quick);

        var bytes = UsenetBenchmarkService.AdaptiveTargetBytes(0, profile, 500_000_000);

        Assert.Equal(profile.PerLevelBytes, bytes);
    }

    [Fact]
    public void AdaptiveTargetBytes_ScalesFastEstimateAcrossFullWindow()
    {
        var profile = BenchmarkProfile.For(BenchmarkIntensity.Quick);

        var bytes = UsenetBenchmarkService.AdaptiveTargetBytes(10, profile, 500_000_000);

        Assert.Equal(80_000_000, bytes);
    }

    [Fact]
    public void AdaptiveTargetBytes_HandlesBudgetBelowProfileFloor()
    {
        var profile = BenchmarkProfile.For(BenchmarkIntensity.Quick);

        var exception = Record.Exception(
            () => UsenetBenchmarkService.AdaptiveTargetBytes(10, profile, 5_000_000));
        var bytes = UsenetBenchmarkService.AdaptiveTargetBytes(10, profile, 5_000_000);

        Assert.Null(exception);
        Assert.Equal(5_000_000, bytes);
    }

    [Fact]
    public void AdaptiveTargetBytes_RespectsRemainingBudget()
    {
        var profile = BenchmarkProfile.For(BenchmarkIntensity.Thorough);

        var bytes = UsenetBenchmarkService.AdaptiveTargetBytes(100, profile, 75_000_000);

        Assert.Equal(75_000_000, bytes);
    }

    [Fact]
    public void MinUsefulBytes_HasFloorAndScalesWithSpeed()
    {
        var profile = BenchmarkProfile.For(BenchmarkIntensity.Quick);

        Assert.Equal(4_000_000, UsenetBenchmarkService.MinUsefulBytes(0.5, profile));
        Assert.Equal(25_000_000, UsenetBenchmarkService.MinUsefulBytes(10, profile));
    }

    [Theory]
    [InlineData(0.1, false, false, false, true, "high")]
    [InlineData(0.2, false, false, false, true, "medium")]
    [InlineData(0.1, true, false, false, true, "medium")]
    [InlineData(0.35, false, false, false, true, "low")]
    [InlineData(0.1, false, true, true, true, "low")]
    [InlineData(0.1, false, true, false, true, "medium")]
    [InlineData(0.1, false, false, false, false, "low")]
    public void ComputeConfidence_ReflectsMeasurementQuality(
        double cv,
        bool wrappedPool,
        bool budgetLimited,
        bool stillClimbing,
        bool throughputTested,
        string expected)
    {
        var result = new BenchmarkResult
        {
            ThroughputTested = throughputTested,
            WrappedPool = wrappedPool,
            BudgetLimited = budgetLimited,
            StillClimbing = stillClimbing,
            RecommendedConnections = 1,
            Sweep = [new BenchmarkSweepPoint { Connections = 1, MbPerSec = 10, Cv = cv }],
        };

        Assert.Equal(expected, UsenetBenchmarkService.ComputeConfidence(result));
    }

    [Fact]
    public void ComputeConfidence_IgnoresNoisyLowConnectionPointNearKnee()
    {
        var result = new BenchmarkResult
        {
            ThroughputTested = true,
            RecommendedConnections = 16,
            Sweep =
            [
                new BenchmarkSweepPoint { Connections = 1, MbPerSec = 10, Cv = 0.8 },
                new BenchmarkSweepPoint { Connections = 8, MbPerSec = 80, Cv = 0.05 },
                new BenchmarkSweepPoint { Connections = 16, MbPerSec = 100, Cv = 0.08 },
            ],
        };

        Assert.Equal("high", UsenetBenchmarkService.ComputeConfidence(result));
    }

    [Fact]
    public void ComputeConfidence_TightConfirmOverridesWrappedPoolCap()
    {
        var result = new BenchmarkResult
        {
            ThroughputTested = true,
            WrappedPool = true,
            ConfirmDeltaPct = 3,
            RecommendedConnections = 8,
            Sweep = [new BenchmarkSweepPoint { Connections = 8, MbPerSec = 40, Cv = 0.05 }],
        };

        Assert.Equal("high", UsenetBenchmarkService.ComputeConfidence(result));
    }

    [Fact]
    public void ComputeConfidence_LooseConfirmDowngradesOneLevel()
    {
        var result = new BenchmarkResult
        {
            ThroughputTested = true,
            ConfirmDeltaPct = 25,
            RecommendedConnections = 8,
            Sweep = [new BenchmarkSweepPoint { Connections = 8, MbPerSec = 40, Cv = 0.05 }],
        };

        Assert.Equal("medium", UsenetBenchmarkService.ComputeConfidence(result));
    }

    [Fact]
    public void ComputeConfidence_BudgetLimitedWithoutClimbingCapsAtMedium()
    {
        var result = new BenchmarkResult
        {
            ThroughputTested = true,
            BudgetLimited = true,
            StillClimbing = false,
            RecommendedConnections = 8,
            Sweep =
            [
                new BenchmarkSweepPoint { Connections = 4, MbPerSec = 38, Cv = 0.05 },
                new BenchmarkSweepPoint { Connections = 8, MbPerSec = 40, Cv = 0.05 },
            ],
        };

        Assert.Equal("medium", UsenetBenchmarkService.ComputeConfidence(result));
    }
}
