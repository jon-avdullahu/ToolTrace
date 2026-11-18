using static ToolTrace.Core.Tests.TestData;

namespace ToolTrace.Core.Tests;

public class EventStoreTests
{
    [Fact]
    public void Late_events_are_stored_in_time_order()
    {
        var store = new InMemoryEventStore();
        store.TryAdd(new StateChanged("ETCH-01", At(9), EquipmentState.Standby));
        store.TryAdd(new StateChanged("ETCH-01", At(6), EquipmentState.Productive));
        store.TryAdd(new StateChanged("ETCH-01", At(7, 30), EquipmentState.Engineering));

        Assert.Equal([At(6), At(7, 30), At(9)], store.GetStateChanges("ETCH-01").Select(c => c.At));
    }

    [Fact]
    public void Returns_copies_so_callers_cannot_change_stored_data()
    {
        var store = new InMemoryEventStore();
        store.TryAdd(new StateChanged("ETCH-01", At(6), EquipmentState.Productive));

        var first = store.GetStateChanges("ETCH-01");
        store.TryAdd(new StateChanged("ETCH-01", At(7), EquipmentState.Standby));

        Assert.Single(first);
        Assert.Equal(2, store.GetStateChanges("ETCH-01").Count);
    }

    [Fact]
    public void Is_safe_under_concurrent_writes()
    {
        var store = new InMemoryEventStore();

        Parallel.For(0, 2_000, i => store.TryAdd(new StateChanged("ETCH-01", At(6).AddSeconds(i % 1_000), EquipmentState.Productive)));

        var changes = store.GetStateChanges("ETCH-01");
        Assert.Equal(1_000, changes.Count); // every second event is a duplicate of another
        Assert.True(changes.Zip(changes.Skip(1)).All(pair => pair.First.At <= pair.Second.At));
    }

    [Fact]
    public void Knows_which_equipment_and_runs_it_has_seen()
    {
        var store = new InMemoryEventStore();
        store.TryAdd(new SensorSample("ETCH-02", At(6), "R-7", "ETCH-OX-200", "RfPower", 500, "W"));

        Assert.True(store.HasEquipment("ETCH-02"));
        Assert.True(store.HasRun("R-7"));
        Assert.False(store.HasEquipment("ETCH-01"));
        Assert.Empty(store.GetSamples("R-unknown"));
    }
}
