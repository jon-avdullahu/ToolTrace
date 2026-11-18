namespace ToolTrace.Core;

/// <summary>Outcome of one upload. Good lines are kept even when others are rejected.</summary>
public sealed record IngestReport(int Accepted, int Duplicates, int Rejected, IReadOnlyList<ParseError> Errors);

/// <summary>
/// Reads a tool's event log line by line: parse, validate against the server clock and the
/// spec catalogue, then store. One bad line never blocks the rest of the file.
/// </summary>
public sealed class IngestionService(IEventStore store, SpecCatalog specs, TimeProvider clock)
{
    /// <summary>Tool clocks drift; anything further ahead than this is treated as a clock fault.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Caps the error list so one broken file cannot produce a huge response.</summary>
    public const int MaxReportedErrors = 100;

    public IngestReport Ingest(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        int accepted = 0, duplicates = 0, rejected = 0, lineNumber = 0;
        var errors = new List<ParseError>();
        var now = clock.GetUtcNow();

        // ReadLine handles \n, \r\n and \r, so files from Windows and Linux tools both work.
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var result = EventParser.ParseLine(line, lineNumber);
            var error = result.IsSuccess ? Validate(result.Event, now, lineNumber) : result.Error;
            if (error is not null)
            {
                rejected++;
                if (errors.Count < MaxReportedErrors)
                {
                    errors.Add(error);
                }

                continue;
            }

            if (store.TryAdd(result.Event!))
            {
                accepted++;
            }
            else
            {
                duplicates++;
            }
        }

        return new IngestReport(accepted, duplicates, rejected, errors);
    }

    private ParseError? Validate(EquipmentEvent equipmentEvent, DateTimeOffset now, int lineNumber)
    {
        if (equipmentEvent.At > now + MaxClockSkew)
        {
            return new ParseError(lineNumber, $"Timestamp {equipmentEvent.At:O} is in the future; check the tool's clock");
        }

        // Comparing 1.2 Pa against a limit in mTorr would pass or fail for the wrong reason.
        if (equipmentEvent is SensorSample sample
            && specs.Find(sample.RecipeId, sample.Parameter) is { } limit
            && !string.Equals(limit.Unit, sample.Unit, StringComparison.Ordinal))
        {
            return new ParseError(
                lineNumber,
                $"Unit '{sample.Unit}' does not match the spec unit '{limit.Unit}' for {sample.Parameter} in recipe {sample.RecipeId}");
        }

        return null;
    }
}
