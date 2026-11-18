namespace ToolTrace.Core;

/// <summary>
/// How a tool spent a time window, in SEMI E10 terms.
/// <list type="bullet">
/// <item><b>Unknown</b>: time before the first state we know about. It is excluded from the ratios rather than guessed.</item>
/// <item><b>Operations time</b>: known time minus non-scheduled time (the tool was not planned to run).</item>
/// <item><b>Uptime</b>: productive + standby + engineering.</item>
/// <item><b>Availability</b> = uptime / operations time; <b>utilisation</b> = productive / operations time.</item>
/// </list>
/// </summary>
public sealed record UtilizationReport(
    string EquipmentId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyDictionary<EquipmentState, TimeSpan> TimeInState,
    TimeSpan Unknown)
{
    public TimeSpan Total => To - From;

    public TimeSpan OperationsTime => Total - Unknown - TimeInState[EquipmentState.NonScheduled];

    public TimeSpan Uptime =>
        TimeInState[EquipmentState.Productive] + TimeInState[EquipmentState.Standby] + TimeInState[EquipmentState.Engineering];

    /// <summary>Null when there is no operations time to divide by.</summary>
    public double? Availability => OperationsTime > TimeSpan.Zero ? Uptime / OperationsTime : null;

    public double? Utilization => OperationsTime > TimeSpan.Zero ? TimeInState[EquipmentState.Productive] / OperationsTime : null;
}

public static class UtilizationCalculator
{
    /// <summary>
    /// Splits [<paramref name="from"/>, <paramref name="to"/>) into time per state.
    /// The state in force at <paramref name="from"/> is the last change at or before it, so a tool
    /// that went productive at 05:30 counts as productive from the start of a 06:00 window.
    /// </summary>
    public static UtilizationReport Calculate(
        string equipmentId, IEnumerable<StateChanged> changes, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (to <= from)
        {
            throw new ArgumentException("The end of the window must be after its start.", nameof(to));
        }

        var totals = Enum.GetValues<EquipmentState>().ToDictionary(s => s, _ => TimeSpan.Zero);
        var unknown = TimeSpan.Zero;
        EquipmentState? current = null;
        var cursor = from;

        // OrderBy is stable: for equal timestamps the change reported last wins.
        foreach (var change in changes.OrderBy(c => c.At))
        {
            if (change.At <= from)
            {
                current = change.State;
                continue;
            }

            if (change.At >= to)
            {
                break;
            }

            Add(current, change.At - cursor);
            cursor = change.At;
            current = change.State;
        }

        Add(current, to - cursor);
        return new UtilizationReport(equipmentId, from, to, totals, unknown);

        void Add(EquipmentState? state, TimeSpan duration)
        {
            if (state is { } known)
            {
                totals[known] += duration;
            }
            else
            {
                unknown += duration;
            }
        }
    }
}
