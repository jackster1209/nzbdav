using NzbWebDAV.Api.Controllers.UsenetMigration;

namespace NzbWebDAV.Tests.UsenetMigration;

public class MigrationRunStateTests
{
    [Theory]
    [InlineData("scanned", true)]
    [InlineData("running", false)]
    [InlineData("paused", false)]
    [InlineData("cancelling", false)]
    [InlineData("complete", false)]
    [InlineData("cancelled", false)]
    [InlineData("scanning", false)]
    public void StartMigration_RequiresFreshCompletedScan(string status, bool expected) =>
        Assert.Equal(expected, UsenetMigrationController.CanStartMigration(status));

    [Theory]
    [InlineData("connected", true)]
    [InlineData("mapped", true)]
    [InlineData("scanned", true)]
    [InlineData("scanning", false)]
    [InlineData("running", false)]
    [InlineData("paused", false)]
    [InlineData("cancelling", false)]
    [InlineData("complete", false)]
    [InlineData("cancelled", false)]
    [InlineData("linking", false)]
    [InlineData("linked", false)]
    [InlineData("applying", false)]
    public void CategoryMappings_AreEditableOnlyBeforeMigrationWork(
        string status, bool expected) =>
        Assert.Equal(expected, UsenetMigrationController.CanEditCategoryMappings(status));

    [Theory]
    [InlineData("scanned", true)]
    [InlineData("mapped", false)]
    [InlineData("scanning", false)]
    [InlineData("running", false)]
    [InlineData("paused", false)]
    [InlineData("cancelling", false)]
    [InlineData("complete", false)]
    [InlineData("cancelled", false)]
    [InlineData("linked", false)]
    public void ReleaseSelection_IsEditableOnlyDuringReview(string status, bool expected) =>
        Assert.Equal(expected, UsenetMigrationController.CanEditReleaseSelection(status));

    [Theory]
    [InlineData("running", false)]
    [InlineData("paused", false)]
    [InlineData("cancelling", false)]
    [InlineData("complete", true)]
    [InlineData("cancelled", true)]
    [InlineData("scanned", true)]
    public void StartScan_IsBlockedOnlyByActiveRun(string status, bool expected) =>
        Assert.Equal(expected, UsenetMigrationController.CanStartScan(status));

    [Theory]
    [InlineData("paused", true)]
    [InlineData("scanned", false)]
    [InlineData("running", false)]
    [InlineData("complete", false)]
    [InlineData("cancelled", false)]
    [InlineData("cancelling", false)]
    public void ResumeMigration_RequiresPausedRun(string status, bool expected) =>
        Assert.Equal(expected, UsenetMigrationController.CanResumeMigration(status));

    [Fact]
    public void PauseAndCancel_ExposeOnlyValidActiveTransitions()
    {
        Assert.True(UsenetMigrationController.CanPauseMigration("running"));
        Assert.False(UsenetMigrationController.CanPauseMigration("paused"));
        Assert.True(UsenetMigrationController.CanCancelMigration("running"));
        Assert.True(UsenetMigrationController.CanCancelMigration("paused"));
        Assert.False(UsenetMigrationController.CanCancelMigration("cancelling"));
        Assert.False(UsenetMigrationController.CanCancelMigration("scanned"));
        Assert.False(UsenetMigrationController.CanCancelMigration("complete"));
    }

    [Theory]
    [InlineData("scanning", true)]
    [InlineData("running", true)]
    [InlineData("paused", true)]
    [InlineData("cancelling", true)]
    [InlineData("linking", true)]
    [InlineData("applying", true)]
    [InlineData("idle", false)]
    [InlineData("connected", false)]
    [InlineData("mapped", false)]
    [InlineData("scanned", false)]
    [InlineData("complete", false)]
    [InlineData("cancelled", false)]
    [InlineData("linked", false)]
    public void DestructiveActions_AreBlockedUntilWorkIsTerminal(string status, bool expected) =>
        Assert.Equal(expected, UsenetMigrationController.IsMigrationWorkActive(status));
}
