namespace ToolTrace.Core.Tests;

public class EventParserTests
{
    [Fact]
    public void Parses_a_state_change_and_keeps_the_utc_offset()
    {
        var result = EventParser.ParseLine(
            """{"type":"state","equipment":"ETCH-01","at":"2026-11-16T06:00:00+01:00","state":"UNSCHEDULED_DOWNTIME"}""", 1);

        Assert.True(result.IsSuccess);
        var change = Assert.IsType<StateChanged>(result.Event);
        Assert.Equal("ETCH-01", change.EquipmentId);
        Assert.Equal(EquipmentState.UnscheduledDowntime, change.State);
        Assert.Equal(TimeSpan.FromHours(1), change.At.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 5, 0, 0, TimeSpan.Zero), change.At);
    }

    [Fact]
    public void Parses_a_sensor_sample()
    {
        var result = EventParser.ParseLine(
            """{"type":"sample","equipment":"ETCH-01","at":"2026-11-16T06:10:00.250Z","run":"R-1001","recipe":"ETCH-OX-200","parameter":"ChamberPressure","value":-0.5e1,"unit":"mTorr"}""", 7);

        Assert.True(result.IsSuccess);
        var sample = Assert.IsType<SensorSample>(result.Event);
        Assert.Equal(("R-1001", "ETCH-OX-200", "ChamberPressure", -5.0, "mTorr"),
            (sample.RunId, sample.RecipeId, sample.Parameter, sample.Value, sample.Unit));
        Assert.Equal(250, sample.At.Millisecond);
    }

    [Theory]
    [InlineData("not json at all", "not valid JSON")]
    [InlineData("[1,2,3]", "Expected a JSON object")]
    [InlineData("""{"equipment":"ETCH-01","at":"2026-11-16T06:00:00Z","state":"STANDBY"}""", "Missing or empty field 'type'")]
    [InlineData("""{"type":"state","equipment":"","at":"2026-11-16T06:00:00Z","state":"STANDBY"}""", "Missing or empty field 'equipment'")]
    [InlineData("""{"type":"state","equipment":"ETCH 01","at":"2026-11-16T06:00:00Z","state":"STANDBY"}""", "may only contain")]
    [InlineData("""{"type":"state","equipment":"ETCH-01","at":"2026-11-16T06:00:00","state":"STANDBY"}""", "with a UTC offset")]
    [InlineData("""{"type":"state","equipment":"ETCH-01","at":"16.11.2026 06:00 +01:00","state":"STANDBY"}""", "with a UTC offset")]
    [InlineData("""{"type":"state","equipment":"ETCH-01","at":"2026-02-30T06:00:00Z","state":"STANDBY"}""", "with a UTC offset")]
    [InlineData("""{"type":"state","equipment":"ETCH-01","at":"2026-11-16T06:00:00Z","state":"running"}""", "Unknown state 'running'")]
    [InlineData("""{"type":"state","equipment":"ETCH-01","at":"2026-11-16T06:00:00Z"}""", "Missing or empty field 'state'")]
    [InlineData("""{"type":"alarm","equipment":"ETCH-01","at":"2026-11-16T06:00:00Z"}""", "Unknown event type 'alarm'")]
    [InlineData("""{"type":"sample","equipment":"ETCH-01","at":"2026-11-16T06:00:00Z","run":"R-1","recipe":"X","parameter":"P","value":"12.5","unit":"mTorr"}""", "'value' must be a finite JSON number")]
    [InlineData("""{"type":"sample","equipment":"ETCH-01","at":"2026-11-16T06:00:00Z","run":"R-1","recipe":"X","parameter":"P","value":1e999,"unit":"mTorr"}""", "'value' must be a finite JSON number")]
    [InlineData("""{"type":"sample","equipment":"ETCH-01","at":"2026-11-16T06:00:00Z","recipe":"X","parameter":"P","value":1,"unit":"mTorr"}""", "Missing or empty field 'run'")]
    [InlineData("""{"type":"sample","equipment":"ETCH-01","at":"2026-11-16T06:00:00Z","run":"R-1","recipe":"X","parameter":"P","value":1,"unit":"millitorr-per-something"}""", "'unit' is longer than")]
    public void Rejects_bad_lines_with_a_reason_and_the_line_number(string line, string expectedReason)
    {
        var result = EventParser.ParseLine(line, 42);

        Assert.False(result.IsSuccess);
        Assert.Equal(42, result.Error.LineNumber);
        Assert.Contains(expectedReason, result.Error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_ids_longer_than_the_limit()
    {
        var id = new string('A', EventParser.MaxIdLength + 1);
        var result = EventParser.ParseLine($$"""{"type":"state","equipment":"{{id}}","at":"2026-11-16T06:00:00Z","state":"STANDBY"}""", 1);

        Assert.False(result.IsSuccess);
        Assert.Contains("longer than 64", result.Error.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_state_name_round_trips()
    {
        foreach (var state in Enum.GetValues<EquipmentState>())
        {
            Assert.True(StateNames.TryParse(StateNames.ToWireName(state), out var parsed));
            Assert.Equal(state, parsed);
        }
    }
}
