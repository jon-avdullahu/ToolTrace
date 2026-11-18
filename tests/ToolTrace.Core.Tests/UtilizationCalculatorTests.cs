using static ToolTrace.Core.Tests.TestData;

namespace ToolTrace.Core.Tests;

public class UtilizationCalculatorTests
{
    private static StateChanged Change(int hour, int minute, EquipmentState state) => new("ETCH-01", At(hour, minute), state);

    [Fact]
    public void Splits_a_shift_into_time_per_state()
    {
        StateChanged[] changes =
        [
            Change(6, 0, EquipmentState.Productive),
            Change(9, 0, EquipmentState.UnscheduledDowntime),
            Change(9, 45, EquipmentState.Engineering),
            Change(10, 15, EquipmentState.Productive),
        ];

        var report = UtilizationCalculator.Calculate("ETCH-01", changes, At(6), At(12));

        Assert.Equal(TimeSpan.FromMinutes(180 + 105), report.TimeInState[EquipmentState.Productive]);
        Assert.Equal(TimeSpan.FromMinutes(45), report.TimeInState[EquipmentState.UnscheduledDowntime]);
        Assert.Equal(TimeSpan.FromMinutes(30), report.TimeInState[EquipmentState.Engineering]);
        Assert.Equal(TimeSpan.Zero, report.Unknown);
        Assert.Equal(report.Total, report.TimeInState.Values.Aggregate(TimeSpan.Zero, (sum, t) => sum + t));
        Assert.Equal(315.0 / 360, report.Availability!.Value, precision: 10);
        Assert.Equal(285.0 / 360, report.Utilization!.Value, precision: 10);
    }

    [Fact]
    public void The_state_before_the_window_carries_into_it()
    {
        var report = UtilizationCalculator.Calculate("ETCH-01", [Change(5, 30, EquipmentState.Productive)], At(6), At(7));

        Assert.Equal(TimeSpan.FromHours(1), report.TimeInState[EquipmentState.Productive]);
        Assert.Equal(TimeSpan.Zero, report.Unknown);
    }

    [Fact]
    public void A_change_exactly_at_the_start_applies_and_one_exactly_at_the_end_does_not()
    {
        StateChanged[] changes = [Change(6, 0, EquipmentState.Standby), Change(7, 0, EquipmentState.Productive)];

        var report = UtilizationCalculator.Calculate("ETCH-01", changes, At(6), At(7));

        Assert.Equal(TimeSpan.FromHours(1), report.TimeInState[EquipmentState.Standby]);
        Assert.Equal(TimeSpan.Zero, report.TimeInState[EquipmentState.Productive]);
    }

    [Fact]
    public void Time_before_the_first_known_state_is_unknown_and_left_out_of_the_ratios()
    {
        var report = UtilizationCalculator.Calculate("ETCH-01", [Change(7, 0, EquipmentState.Productive)], At(6), At(8));

        Assert.Equal(TimeSpan.FromHours(1), report.Unknown);
        Assert.Equal(TimeSpan.FromHours(1), report.OperationsTime);
        Assert.Equal(1.0, report.Utilization);
    }

    [Fact]
    public void Non_scheduled_time_does_not_count_against_availability()
    {
        StateChanged[] changes = [Change(6, 0, EquipmentState.NonScheduled), Change(10, 0, EquipmentState.Productive)];

        var report = UtilizationCalculator.Calculate("ETCH-01", changes, At(6), At(14));

        Assert.Equal(TimeSpan.FromHours(4), report.OperationsTime);
        Assert.Equal(1.0, report.Availability);
    }

    [Fact]
    public void Ratios_are_null_when_there_is_no_operations_time()
    {
        var noData = UtilizationCalculator.Calculate("ETCH-01", [], At(6), At(7));
        var notPlanned = UtilizationCalculator.Calculate("ETCH-01", [Change(5, 0, EquipmentState.NonScheduled)], At(6), At(7));

        Assert.Null(noData.Availability);
        Assert.Null(notPlanned.Utilization);
    }

    [Fact]
    public void For_equal_timestamps_the_last_reported_change_wins()
    {
        StateChanged[] changes = [Change(6, 0, EquipmentState.Standby), Change(6, 0, EquipmentState.Productive)];

        var report = UtilizationCalculator.Calculate("ETCH-01", changes, At(6), At(7));

        Assert.Equal(TimeSpan.FromHours(1), report.TimeInState[EquipmentState.Productive]);
    }

    [Fact]
    public void Unsorted_input_and_mixed_utc_offsets_give_the_same_result()
    {
        StateChanged[] changes =
        [
            new("ETCH-01", new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero), EquipmentState.Standby), // 09:00 CET
            Change(6, 0, EquipmentState.Productive),
        ];

        var report = UtilizationCalculator.Calculate("ETCH-01", changes, At(6), At(10));

        Assert.Equal(TimeSpan.FromHours(3), report.TimeInState[EquipmentState.Productive]);
        Assert.Equal(TimeSpan.FromHours(1), report.TimeInState[EquipmentState.Standby]);
    }

    [Fact]
    public void Rejects_an_empty_or_reversed_window()
    {
        Assert.Throws<ArgumentException>(() => UtilizationCalculator.Calculate("ETCH-01", [], At(7), At(7)));
        Assert.Throws<ArgumentException>(() => UtilizationCalculator.Calculate("ETCH-01", [], At(8), At(7)));
    }
}
