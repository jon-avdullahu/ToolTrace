using static ToolTrace.Core.Tests.TestData;

namespace ToolTrace.Core.Tests;

/// <summary>
/// Golden-file tests: the files in samples/ are realistic tool logs with known contents,
/// so any change in how they are parsed or reported shows up here.
/// </summary>
public class SampleFileTests
{
    private static (IngestReport Report, InMemoryEventStore Store) IngestFile(string name)
    {
        var store = new InMemoryEventStore();
        using var reader = File.OpenText(SamplePath(name));
        return (new IngestionService(store, Specs(), new FixedTimeProvider(Now)).Ingest(reader), store);
    }

    [Fact]
    public void The_clean_shift_log_is_accepted_completely()
    {
        var (report, _) = IngestFile("etch-01-shift.ndjson");

        Assert.Equal(19, report.Accepted);
        Assert.Equal(0, report.Rejected);
        Assert.Equal(0, report.Duplicates);
    }

    [Fact]
    public void The_shift_log_gives_the_expected_e10_breakdown()
    {
        var (_, store) = IngestFile("etch-01-shift.ndjson");

        var report = UtilizationCalculator.Calculate("ETCH-01", store.GetStateChanges("ETCH-01"), At(6), At(14));

        Assert.Equal(315, report.TimeInState[EquipmentState.Productive].TotalMinutes);
        Assert.Equal(30, report.TimeInState[EquipmentState.Standby].TotalMinutes);
        Assert.Equal(30, report.TimeInState[EquipmentState.Engineering].TotalMinutes);
        Assert.Equal(60, report.TimeInState[EquipmentState.ScheduledDowntime].TotalMinutes); // written in UTC in the log
        Assert.Equal(45, report.TimeInState[EquipmentState.UnscheduledDowntime].TotalMinutes);
        Assert.Equal(0.78125, report.Availability);
        Assert.Equal(0.65625, report.Utilization);
    }

    [Fact]
    public void The_shift_log_run_has_three_spec_violations()
    {
        var (_, store) = IngestFile("etch-01-shift.ndjson");

        var report = SpecChecker.Check("R-1001", store.GetSamples("R-1001"), Specs());

        Assert.Equal(8, report.SamplesChecked);
        Assert.Equal(1, report.SamplesWithoutSpec);
        Assert.Equal(
            [("ChamberPressure", Deviation.AboveUpper), ("RfPower", Deviation.AboveUpper), ("ChuckTemperature", Deviation.BelowLower)],
            report.Violations.Select(v => (v.Sample.Parameter, v.Deviation)));
    }

    [Fact]
    public void The_defect_log_rejects_exactly_the_broken_lines()
    {
        var (report, _) = IngestFile("etch-01-defects.ndjson");

        Assert.Equal(2, report.Accepted);
        Assert.Equal(1, report.Duplicates);
        Assert.Equal(8, report.Rejected);
        Assert.Equal([2, 3, 4, 5, 6, 7, 10, 11], report.Errors.Select(e => e.LineNumber));
    }
}
