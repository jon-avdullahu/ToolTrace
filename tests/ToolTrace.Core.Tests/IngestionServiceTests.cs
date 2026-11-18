using static ToolTrace.Core.Tests.TestData;

namespace ToolTrace.Core.Tests;

public class IngestionServiceTests
{
    private readonly InMemoryEventStore _store = new();

    private IngestReport Ingest(string text) =>
        new IngestionService(_store, Specs(), new FixedTimeProvider(Now)).Ingest(new StringReader(text));

    private const string Productive = """{"type":"state","equipment":"ETCH-01","at":"2026-11-16T06:00:00+01:00","state":"PRODUCTIVE"}""";

    [Fact]
    public void Keeps_good_lines_when_others_are_bad()
    {
        var report = Ingest($"{Productive}\nnot json\n");

        Assert.Equal(1, report.Accepted);
        Assert.Equal(1, report.Rejected);
        Assert.Equal(2, Assert.Single(report.Errors).LineNumber);
        Assert.Single(_store.GetStateChanges("ETCH-01"));
    }

    [Fact]
    public void Resent_events_are_counted_as_duplicates_even_with_a_different_offset()
    {
        var sameInstantInUtc = Productive.Replace("2026-11-16T06:00:00+01:00", "2026-11-16T05:00:00Z", StringComparison.Ordinal);

        var report = Ingest($"{Productive}\n{Productive}\n{sameInstantInUtc}");

        Assert.Equal(1, report.Accepted);
        Assert.Equal(2, report.Duplicates);
        Assert.Equal(0, report.Rejected);
    }

    [Fact]
    public void Rejects_timestamps_beyond_the_allowed_clock_skew()
    {
        static string Line(DateTimeOffset at) => Productive.Replace("2026-11-16T06:00:00+01:00", at.ToString("yyyy-MM-dd'T'HH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var report = Ingest($"{Line(Now + IngestionService.MaxClockSkew)}\n{Line(Now + IngestionService.MaxClockSkew + TimeSpan.FromSeconds(1))}");

        Assert.Equal(1, report.Accepted);
        Assert.Contains("in the future", Assert.Single(report.Errors).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_sample_whose_unit_differs_from_the_spec()
    {
        var report = Ingest("""{"type":"sample","equipment":"ETCH-01","at":"2026-11-16T06:00:00+01:00","run":"R-1","recipe":"ETCH-OX-200","parameter":"ChamberPressure","value":1.6,"unit":"Pa"}""");

        Assert.Equal(0, report.Accepted);
        Assert.Contains("does not match the spec unit 'mTorr'", Assert.Single(report.Errors).Reason, StringComparison.Ordinal);
        Assert.False(_store.HasRun("R-1"));
    }

    [Fact]
    public void Handles_windows_line_endings_and_blank_lines()
    {
        var report = Ingest($"\r\n{Productive}\r\n   \r\n{Productive.Replace("PRODUCTIVE", "STANDBY", StringComparison.Ordinal)}\r\n");

        Assert.Equal(2, report.Accepted);
        Assert.Equal(0, report.Rejected);
    }

    [Fact]
    public void Caps_the_error_list_but_still_counts_every_rejection()
    {
        var garbage = string.Join('\n', Enumerable.Repeat("garbage", IngestionService.MaxReportedErrors + 50));

        var report = Ingest(garbage);

        Assert.Equal(IngestionService.MaxReportedErrors + 50, report.Rejected);
        Assert.Equal(IngestionService.MaxReportedErrors, report.Errors.Count);
    }
}
