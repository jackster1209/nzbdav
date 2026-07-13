using NzbWebDAV.Services;

namespace NzbWebDAV.Tests.Services;

public class MigrationProgressTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(1, false, false)]
    [InlineData(2, true, false)]
    public void IsIdleMaintenance_MatchesPendingAndVacuum(
        int pendingCount,
        bool vacuumEnabled,
        bool expected)
    {
        Assert.Equal(expected, MigrationProgress.IsIdleMaintenance(pendingCount, vacuumEnabled));
    }
}
