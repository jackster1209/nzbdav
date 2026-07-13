using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NzbWebDAV.Database.MetricsMigrations
{
    /// <inheritdoc />
    public partial class AddMissesCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Misses",
                table: "ThroughputMinutes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Misses",
                table: "ProviderMinutes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Misses",
                table: "ProviderHourly",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Prior Errors counted Missing as failures. Split going forward:
            // Misses = Status Missing (1); Errors = hard failures (Status NOT IN Ok/Missing).
            var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 24L * 60 * 60 * 1000;

            // Older than raw SegmentFetches retention: treat prior Errors as Misses.
            migrationBuilder.Sql(
                $"UPDATE ThroughputMinutes SET Misses = Errors, Errors = 0 WHERE Minute < {cutoff};");
            migrationBuilder.Sql(
                $"UPDATE ProviderMinutes SET Misses = Errors, Errors = 0 WHERE Minute < {cutoff};");
            migrationBuilder.Sql(
                $"UPDATE ProviderHourly SET Misses = Errors, Errors = 0 WHERE Hour < {cutoff};");

            // Last 24h: recompute accurately from raw SegmentFetches.
            migrationBuilder.Sql(
                $"""
                UPDATE ThroughputMinutes
                SET
                  Misses = COALESCE((
                    SELECT COUNT(*) FROM SegmentFetches sf
                    WHERE sf.At >= ThroughputMinutes.Minute
                      AND sf.At < ThroughputMinutes.Minute + 60000
                      AND sf.Status = 1), 0),
                  Errors = COALESCE((
                    SELECT COUNT(*) FROM SegmentFetches sf
                    WHERE sf.At >= ThroughputMinutes.Minute
                      AND sf.At < ThroughputMinutes.Minute + 60000
                      AND sf.Status NOT IN (0, 1)), 0)
                WHERE Minute >= {cutoff};
                """);

            migrationBuilder.Sql(
                $"""
                UPDATE ProviderMinutes
                SET
                  Misses = COALESCE((
                    SELECT COUNT(*) FROM SegmentFetches sf
                    WHERE sf.At >= ProviderMinutes.Minute
                      AND sf.At < ProviderMinutes.Minute + 60000
                      AND sf.Provider = ProviderMinutes.Provider
                      AND sf.Status = 1), 0),
                  Errors = COALESCE((
                    SELECT COUNT(*) FROM SegmentFetches sf
                    WHERE sf.At >= ProviderMinutes.Minute
                      AND sf.At < ProviderMinutes.Minute + 60000
                      AND sf.Provider = ProviderMinutes.Provider
                      AND sf.Status NOT IN (0, 1)), 0)
                WHERE Minute >= {cutoff};
                """);

            migrationBuilder.Sql(
                $"""
                UPDATE ProviderHourly
                SET
                  Misses = COALESCE((
                    SELECT COUNT(*) FROM SegmentFetches sf
                    WHERE sf.At >= ProviderHourly.Hour
                      AND sf.At < ProviderHourly.Hour + 3600000
                      AND sf.Provider = ProviderHourly.Provider
                      AND sf.Status = 1), 0),
                  Errors = COALESCE((
                    SELECT COUNT(*) FROM SegmentFetches sf
                    WHERE sf.At >= ProviderHourly.Hour
                      AND sf.At < ProviderHourly.Hour + 3600000
                      AND sf.Provider = ProviderHourly.Provider
                      AND sf.Status NOT IN (0, 1)), 0)
                WHERE Hour >= {cutoff};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fold Misses back into Errors so totals stay roughly comparable.
            migrationBuilder.Sql(
                "UPDATE ThroughputMinutes SET Errors = Errors + Misses;");
            migrationBuilder.Sql(
                "UPDATE ProviderMinutes SET Errors = Errors + Misses;");
            migrationBuilder.Sql(
                "UPDATE ProviderHourly SET Errors = Errors + Misses;");

            migrationBuilder.DropColumn(
                name: "Misses",
                table: "ThroughputMinutes");

            migrationBuilder.DropColumn(
                name: "Misses",
                table: "ProviderMinutes");

            migrationBuilder.DropColumn(
                name: "Misses",
                table: "ProviderHourly");
        }
    }
}
