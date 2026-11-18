using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ToolTrace.Core;

public sealed record ParseError(int LineNumber, string Reason);

public readonly record struct ParseResult(EquipmentEvent? Event, ParseError? Error)
{
    [MemberNotNullWhen(true, nameof(Event))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Event is not null;
}

/// <summary>
/// Parses one line of a tool's event log (newline-delimited JSON):
/// <code>
/// {"type":"state","equipment":"ETCH-01","at":"2026-11-16T06:00:00+01:00","state":"PRODUCTIVE"}
/// {"type":"sample","equipment":"ETCH-01","at":"...","run":"R-1001","recipe":"ETCH-OX-200","parameter":"ChamberPressure","value":12.1,"unit":"mTorr"}
/// </code>
/// Every rejection names the problem, so whoever owns the tool integration can fix it.
/// </summary>
public static class EventParser
{
    public const int MaxIdLength = 64;
    public const int MaxUnitLength = 16;

    public static ParseResult ParseLine(string line, int lineNumber)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Fail(lineNumber, "Line is not valid JSON");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail(lineNumber, "Expected a JSON object");
            }

            if (!TryGetText(root, "type", MaxIdLength, out var type, out var error)
                || !TryGetId(root, "equipment", out var equipment, out error)
                || !TryGetTimestamp(root, out var at, out error))
            {
                return Fail(lineNumber, error);
            }

            return type switch
            {
                "state" => ParseState(root, equipment, at, lineNumber),
                "sample" => ParseSample(root, equipment, at, lineNumber),
                _ => Fail(lineNumber, $"Unknown event type '{type}'"),
            };
        }
    }

    private static ParseResult ParseState(JsonElement root, string equipment, DateTimeOffset at, int lineNumber)
    {
        if (!TryGetText(root, "state", MaxIdLength, out var name, out var error))
        {
            return Fail(lineNumber, error);
        }

        if (!StateNames.TryParse(name, out var state))
        {
            return Fail(lineNumber, $"Unknown state '{name}'; expected one of {string.Join(", ", StateNames.All)}");
        }

        return new ParseResult(new StateChanged(equipment, at, state), null);
    }

    private static ParseResult ParseSample(JsonElement root, string equipment, DateTimeOffset at, int lineNumber)
    {
        if (!TryGetId(root, "run", out var run, out var error)
            || !TryGetId(root, "recipe", out var recipe, out error)
            || !TryGetId(root, "parameter", out var parameter, out error)
            || !TryGetText(root, "unit", MaxUnitLength, out var unit, out error))
        {
            return Fail(lineNumber, error);
        }

        if (!root.TryGetProperty("value", out var valueElement)
            || valueElement.ValueKind != JsonValueKind.Number
            || !valueElement.TryGetDouble(out var value)
            || !double.IsFinite(value))
        {
            return Fail(lineNumber, "Field 'value' must be a finite JSON number");
        }

        return new ParseResult(new SensorSample(equipment, at, run, recipe, parameter, value, unit), null);
    }

    private static bool TryGetText(
        JsonElement root, string name, int maxLength,
        [NotNullWhen(true)] out string? value, [NotNullWhen(false)] out string? error)
    {
        value = null;
        error = null;
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(element.GetString()))
        {
            error = $"Missing or empty field '{name}'";
            return false;
        }

        value = element.GetString()!;
        if (value.Length > maxLength)
        {
            error = $"Field '{name}' is longer than {maxLength} characters";
            return false;
        }

        return true;
    }

    /// <summary>IDs end up in URLs, logs and file names, so only a safe character set is allowed.</summary>
    private static bool TryGetId(
        JsonElement root, string name,
        [NotNullWhen(true)] out string? value, [NotNullWhen(false)] out string? error)
    {
        if (!TryGetText(root, name, MaxIdLength, out value, out error))
        {
            return false;
        }

        if (!value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            error = $"Field '{name}' may only contain letters, digits, '-', '_' and '.'";
            value = null;
            return false;
        }

        return true;
    }

    private static bool TryGetTimestamp(JsonElement root, out DateTimeOffset at, [NotNullWhen(false)] out string? error)
    {
        at = default;
        if (!TryGetText(root, "at", MaxIdLength, out var text, out error))
        {
            return false;
        }

        if (!Timestamps.TryParse(text, out at))
        {
            error = $"Timestamp '{text}' must be ISO 8601 with a UTC offset, e.g. 2026-11-16T06:00:00+01:00";
            return false;
        }

        return true;
    }

    private static ParseResult Fail(int lineNumber, string reason) => new(null, new ParseError(lineNumber, reason));
}
