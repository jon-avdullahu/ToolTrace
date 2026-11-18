namespace ToolTrace.Core;

/// <summary>Allowed range for one parameter of one recipe. Both limits are inclusive.</summary>
public sealed record SpecLimit(string Recipe, string Parameter, string Unit, double Lower, double Upper);

public sealed class SpecCatalog
{
    private readonly Dictionary<(string Recipe, string Parameter), SpecLimit> _limits = [];

    /// <summary>Fails fast on a broken configuration instead of silently checking against nonsense.</summary>
    public SpecCatalog(IEnumerable<SpecLimit> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        foreach (var limit in limits)
        {
            if (string.IsNullOrWhiteSpace(limit.Recipe) || string.IsNullOrWhiteSpace(limit.Parameter) || string.IsNullOrWhiteSpace(limit.Unit))
            {
                throw new ArgumentException($"Spec limit {limit} needs a recipe, a parameter and a unit.", nameof(limits));
            }

            if (!double.IsFinite(limit.Lower) || !double.IsFinite(limit.Upper) || limit.Lower > limit.Upper)
            {
                throw new ArgumentException($"Spec limit for {limit.Recipe}/{limit.Parameter} has an invalid range {limit.Lower}..{limit.Upper}.", nameof(limits));
            }

            if (!_limits.TryAdd((limit.Recipe, limit.Parameter), limit))
            {
                throw new ArgumentException($"Spec limit for {limit.Recipe}/{limit.Parameter} is defined twice.", nameof(limits));
            }
        }
    }

    public int Count => _limits.Count;

    public SpecLimit? Find(string recipe, string parameter) =>
        _limits.TryGetValue((recipe, parameter), out var limit) ? limit : null;
}

public enum Deviation
{
    BelowLower,
    AboveUpper,
}

public sealed record SpecViolation(SensorSample Sample, SpecLimit Limit, Deviation Deviation);

public sealed record RunSpecReport(string RunId, int SamplesChecked, int SamplesWithoutSpec, IReadOnlyList<SpecViolation> Violations);

public static class SpecChecker
{
    public static RunSpecReport Check(string runId, IEnumerable<SensorSample> samples, SpecCatalog specs)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(specs);

        int checkedCount = 0, withoutSpec = 0;
        var violations = new List<SpecViolation>();
        foreach (var sample in samples.OrderBy(s => s.At))
        {
            var limit = specs.Find(sample.RecipeId, sample.Parameter);
            if (limit is null)
            {
                withoutSpec++; // reported, so an unmonitored parameter is visible rather than silently "fine"
                continue;
            }

            checkedCount++;
            if (sample.Value < limit.Lower)
            {
                violations.Add(new SpecViolation(sample, limit, Deviation.BelowLower));
            }
            else if (sample.Value > limit.Upper)
            {
                violations.Add(new SpecViolation(sample, limit, Deviation.AboveUpper));
            }
        }

        return new RunSpecReport(runId, checkedCount, withoutSpec, violations);
    }
}
