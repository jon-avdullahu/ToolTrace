namespace ToolTrace.Core;

public interface IEventStore
{
    /// <summary>Adds the event. Returns false if the identical event was already stored (tools resend).</summary>
    bool TryAdd(EquipmentEvent equipmentEvent);

    /// <summary>State changes of one tool, ordered by time; events that arrived late are in their correct place.</summary>
    IReadOnlyList<StateChanged> GetStateChanges(string equipmentId);

    IReadOnlyList<SensorSample> GetSamples(string runId);

    bool HasEquipment(string equipmentId);

    bool HasRun(string runId);
}

/// <summary>
/// Thread-safe in-memory store. Good enough for a single instance and for tests; a real
/// deployment would put a database behind the same interface.
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly Lock _lock = new();
    private readonly HashSet<EquipmentEvent> _seen = [];
    private readonly HashSet<string> _equipment = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StateChanged>> _states = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<SensorSample>> _samples = new(StringComparer.Ordinal);

    public bool TryAdd(EquipmentEvent equipmentEvent)
    {
        ArgumentNullException.ThrowIfNull(equipmentEvent);
        lock (_lock)
        {
            // Records compare by value, and DateTimeOffset compares the instant, so
            // "06:00+01:00" and "05:00Z" for the same tool and state count as one event.
            if (!_seen.Add(equipmentEvent))
            {
                return false;
            }

            _equipment.Add(equipmentEvent.EquipmentId);
            switch (equipmentEvent)
            {
                case StateChanged change:
                    var list = GetOrCreate(_states, change.EquipmentId);
                    // Insert after every change at or before this instant: keeps time order and,
                    // for equal timestamps, arrival order (the last one reported wins).
                    list.Insert(list.FindLastIndex(c => c.At <= change.At) + 1, change);
                    break;
                case SensorSample sample:
                    GetOrCreate(_samples, sample.RunId).Add(sample);
                    break;
            }

            return true;
        }
    }

    public IReadOnlyList<StateChanged> GetStateChanges(string equipmentId)
    {
        lock (_lock)
        {
            return _states.TryGetValue(equipmentId, out var list) ? [.. list] : [];
        }
    }

    public IReadOnlyList<SensorSample> GetSamples(string runId)
    {
        lock (_lock)
        {
            return _samples.TryGetValue(runId, out var list) ? [.. list] : [];
        }
    }

    public bool HasEquipment(string equipmentId)
    {
        lock (_lock)
        {
            return _equipment.Contains(equipmentId);
        }
    }

    public bool HasRun(string runId)
    {
        lock (_lock)
        {
            return _samples.ContainsKey(runId);
        }
    }

    private static List<T> GetOrCreate<T>(Dictionary<string, List<T>> map, string key)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = [];
            map[key] = list;
        }

        return list;
    }
}
