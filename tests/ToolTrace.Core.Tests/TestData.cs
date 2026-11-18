using ToolTrace.Core;

namespace ToolTrace.Core.Tests;

/// <summary>Pins "now" so tests about future timestamps do not depend on the machine clock.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}

internal static class TestData
{
    public static readonly TimeSpan Cet = TimeSpan.FromHours(1);

    /// <summary>The afternoon after the sample shift, so every sample timestamp is in the past.</summary>
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, Cet);

    public static DateTimeOffset At(int hour, int minute = 0) => new(2026, 11, 16, hour, minute, 0, Cet);

    public static SpecCatalog Specs() => new(
    [
        new SpecLimit("ETCH-OX-200", "ChamberPressure", "mTorr", 8.0, 15.0),
        new SpecLimit("ETCH-OX-200", "RfPower", "W", 450, 550),
        new SpecLimit("ETCH-OX-200", "ChuckTemperature", "degC", 18.0, 22.0),
    ]);

    public static string SamplePath(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);
}
