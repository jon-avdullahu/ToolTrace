using System.Text;

using ToolTrace.Core;

namespace ToolTrace.Api;

public sealed record UtilizationResponse(
    string EquipmentId,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyDictionary<string, double> MinutesInState,
    double UnknownMinutes,
    double? AvailabilityPercent,
    double? UtilizationPercent);

public sealed record ViolationResponse(
    DateTimeOffset At, string Parameter, double Value, string Unit, double Lower, double Upper, Deviation Deviation);

public sealed record RunSpecResponse(
    string RunId, int SamplesChecked, int SamplesWithoutSpec, IReadOnlyList<ViolationResponse> Violations);

public static class Endpoints
{
    public const int DefaultMaxBodyBytes = 1024 * 1024;

    public static void MapToolTraceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/events", IngestAsync)
            .WithSummary("Upload a tool event log (newline-delimited JSON). Valid lines are stored even if others are rejected.");

        api.MapGet("/equipment/{equipmentId}/utilization", GetUtilization)
            .WithSummary("Time per SEMI E10 state, availability and utilisation for a window.");

        api.MapGet("/runs/{runId}/violations", GetViolations)
            .WithSummary("Sensor samples of a run that are outside the recipe's spec limits.");
    }

    private static async Task<IResult> IngestAsync(
        HttpRequest request, IngestionService ingestion, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var maxBytes = configuration.GetValue("Ingestion:MaxBodyBytes", DefaultMaxBodyBytes);
        var body = await ReadBodyAsync(request.Body, maxBytes, cancellationToken);
        if (body is null)
        {
            return Results.Problem($"The upload is larger than {maxBytes} bytes; split the file.", statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return Results.Problem("The request body is empty.", statusCode: StatusCodes.Status400BadRequest);
        }

        using var reader = new StringReader(body);
        return Results.Ok(ingestion.Ingest(reader));
    }

    private static IResult GetUtilization(string equipmentId, string? from, string? to, IEventStore store)
    {
        if (!Timestamps.TryParse(from, out var start) || !Timestamps.TryParse(to, out var end))
        {
            return Results.Problem(
                "Query parameters 'from' and 'to' are required, as ISO 8601 with a UTC offset (e.g. 2026-11-16T06:00:00+01:00).",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (end <= start)
        {
            return Results.Problem("'to' must be after 'from'.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!store.HasEquipment(equipmentId))
        {
            return Results.Problem($"No data for equipment '{equipmentId}'.", statusCode: StatusCodes.Status404NotFound);
        }

        var report = UtilizationCalculator.Calculate(equipmentId, store.GetStateChanges(equipmentId), start, end);
        return Results.Ok(new UtilizationResponse(
            report.EquipmentId,
            report.From,
            report.To,
            report.TimeInState.ToDictionary(p => StateNames.ToWireName(p.Key), p => p.Value.TotalMinutes),
            report.Unknown.TotalMinutes,
            Percent(report.Availability),
            Percent(report.Utilization)));
    }

    private static IResult GetViolations(string runId, IEventStore store, SpecCatalog specs)
    {
        if (!store.HasRun(runId))
        {
            return Results.Problem($"No samples for run '{runId}'.", statusCode: StatusCodes.Status404NotFound);
        }

        var report = SpecChecker.Check(runId, store.GetSamples(runId), specs);
        return Results.Ok(new RunSpecResponse(
            report.RunId,
            report.SamplesChecked,
            report.SamplesWithoutSpec,
            [.. report.Violations.Select(v => new ViolationResponse(
                v.Sample.At, v.Sample.Parameter, v.Sample.Value, v.Sample.Unit, v.Limit.Lower, v.Limit.Upper, v.Deviation))]));
    }

    private static double? Percent(double? ratio) => ratio is { } r ? Math.Round(r * 100, 1, MidpointRounding.AwayFromZero) : null;

    /// <summary>Reads the body up to a limit; returns null if it is larger, without buffering the excess.</summary>
    private static async Task<string?> ReadBodyAsync(Stream body, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
