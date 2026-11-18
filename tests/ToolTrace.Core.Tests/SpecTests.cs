using static ToolTrace.Core.Tests.TestData;

namespace ToolTrace.Core.Tests;

public class SpecTests
{
    private static SensorSample Sample(string parameter, double value, int minute = 0, string unit = "mTorr") =>
        new("ETCH-01", At(6, minute), "R-1", "ETCH-OX-200", parameter, value, unit);

    [Theory]
    [InlineData(7.99, Deviation.BelowLower)]
    [InlineData(15.01, Deviation.AboveUpper)]
    public void Flags_values_outside_the_limits(double value, Deviation expected)
    {
        var report = SpecChecker.Check("R-1", [Sample("ChamberPressure", value)], Specs());

        var violation = Assert.Single(report.Violations);
        Assert.Equal(expected, violation.Deviation);
        Assert.Equal(value, violation.Sample.Value);
    }

    [Theory]
    [InlineData(8.0)]
    [InlineData(12.0)]
    [InlineData(15.0)]
    public void Limits_are_inclusive(double value)
    {
        var report = SpecChecker.Check("R-1", [Sample("ChamberPressure", value)], Specs());

        Assert.Empty(report.Violations);
        Assert.Equal(1, report.SamplesChecked);
    }

    [Fact]
    public void Parameters_without_a_spec_are_counted_not_silently_passed()
    {
        var report = SpecChecker.Check("R-1", [Sample("GasFlowAr", 9999), Sample("ChamberPressure", 12)], Specs());

        Assert.Equal(1, report.SamplesWithoutSpec);
        Assert.Equal(1, report.SamplesChecked);
        Assert.Empty(report.Violations);
    }

    [Fact]
    public void Violations_are_reported_in_time_order()
    {
        var report = SpecChecker.Check("R-1", [Sample("ChamberPressure", 20, minute: 30), Sample("ChamberPressure", 1, minute: 10)], Specs());

        Assert.Equal([Deviation.BelowLower, Deviation.AboveUpper], report.Violations.Select(v => v.Deviation));
    }

    [Fact]
    public void A_broken_spec_configuration_fails_at_startup()
    {
        Assert.Throws<ArgumentException>(() => new SpecCatalog([new SpecLimit("R", "P", "W", 10, 5)]));
        Assert.Throws<ArgumentException>(() => new SpecCatalog([new SpecLimit("R", "P", "W", double.NaN, 5)]));
        Assert.Throws<ArgumentException>(() => new SpecCatalog([new SpecLimit("R", "P", "", 1, 5)]));
        Assert.Throws<ArgumentException>(() => new SpecCatalog([new SpecLimit("R", "P", "W", 1, 5), new SpecLimit("R", "P", "W", 2, 6)]));
    }
}
