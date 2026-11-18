namespace ToolTrace.Core;

/// <summary>
/// The six basic equipment states of SEMI E10, the industry standard for
/// measuring equipment reliability, availability and utilisation.
/// </summary>
public enum EquipmentState
{
    Productive,
    Standby,
    Engineering,
    ScheduledDowntime,
    UnscheduledDowntime,
    NonScheduled,
}

/// <summary>Anything a tool reports. Timestamps keep their UTC offset; comparisons use the instant.</summary>
public abstract record EquipmentEvent(string EquipmentId, DateTimeOffset At);

/// <summary>The tool entered a new E10 state at <see cref="EquipmentEvent.At"/>.</summary>
public sealed record StateChanged(string EquipmentId, DateTimeOffset At, EquipmentState State)
    : EquipmentEvent(EquipmentId, At);

/// <summary>One sensor reading taken while a process run was executing a recipe.</summary>
public sealed record SensorSample(
    string EquipmentId,
    DateTimeOffset At,
    string RunId,
    string RecipeId,
    string Parameter,
    double Value,
    string Unit)
    : EquipmentEvent(EquipmentId, At);

/// <summary>Wire names for states (as tools send them), in one place so parsing and output agree.</summary>
public static class StateNames
{
    private static readonly Dictionary<string, EquipmentState> ByName = new(StringComparer.Ordinal)
    {
        ["PRODUCTIVE"] = EquipmentState.Productive,
        ["STANDBY"] = EquipmentState.Standby,
        ["ENGINEERING"] = EquipmentState.Engineering,
        ["SCHEDULED_DOWNTIME"] = EquipmentState.ScheduledDowntime,
        ["UNSCHEDULED_DOWNTIME"] = EquipmentState.UnscheduledDowntime,
        ["NON_SCHEDULED"] = EquipmentState.NonScheduled,
    };

    private static readonly Dictionary<EquipmentState, string> ByState = ByName.ToDictionary(p => p.Value, p => p.Key);

    public static IReadOnlyCollection<string> All => ByName.Keys;

    public static bool TryParse(string name, out EquipmentState state) => ByName.TryGetValue(name, out state);

    public static string ToWireName(EquipmentState state) => ByState[state];
}
