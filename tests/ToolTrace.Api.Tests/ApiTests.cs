using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ToolTrace.Api.Tests;

public class ApiTests
{
    private static StringContent Ndjson(string body) => new(body, Encoding.UTF8, "application/x-ndjson");

    private static async Task<HttpClient> ClientWithShiftLogAsync(ApiFactory factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsync("/api/events", Ndjson(ApiFactory.Sample("etch-01-shift.ndjson")));
        response.EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Health_check_responds()
    {
        await using var factory = new ApiFactory();
        var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Upload_reports_accepted_duplicate_and_rejected_lines()
    {
        await using var factory = new ApiFactory();
        var response = await factory.CreateClient().PostAsync("/api/events", Ndjson(ApiFactory.Sample("etch-01-defects.ndjson")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(2, root.GetProperty("accepted").GetInt32());
        Assert.Equal(1, root.GetProperty("duplicates").GetInt32());
        Assert.Equal(8, root.GetProperty("rejected").GetInt32());
        var first = root.GetProperty("errors")[0];
        Assert.Equal(2, first.GetProperty("lineNumber").GetInt32());
        Assert.Contains("UTC offset", first.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Utilization_endpoint_returns_the_e10_breakdown()
    {
        await using var factory = new ApiFactory();
        var client = await ClientWithShiftLogAsync(factory);

        var response = await client.GetAsync("/api/equipment/ETCH-01/utilization?from=2026-11-16T06:00:00%2B01:00&to=2026-11-16T14:00:00%2B01:00");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var minutes = root.GetProperty("minutesInState");
        Assert.Equal(315, minutes.GetProperty("PRODUCTIVE").GetDouble());
        Assert.Equal(45, minutes.GetProperty("UNSCHEDULED_DOWNTIME").GetDouble());
        Assert.Equal(78.1, root.GetProperty("availabilityPercent").GetDouble());
        Assert.Equal(65.6, root.GetProperty("utilizationPercent").GetDouble());
    }

    [Fact]
    public async Task Non_scheduled_time_is_excluded_for_the_second_tool()
    {
        await using var factory = new ApiFactory();
        var client = await ClientWithShiftLogAsync(factory);

        using var json = JsonDocument.Parse(await client.GetStringAsync(
            "/api/equipment/ETCH-02/utilization?from=2026-11-16T05:00:00Z&to=2026-11-16T13:00:00Z"));

        Assert.Equal(240, json.RootElement.GetProperty("minutesInState").GetProperty("NON_SCHEDULED").GetDouble());
        Assert.Equal(100, json.RootElement.GetProperty("availabilityPercent").GetDouble());
    }

    [Fact]
    public async Task Violations_endpoint_lists_out_of_spec_samples()
    {
        await using var factory = new ApiFactory();
        var client = await ClientWithShiftLogAsync(factory);

        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/runs/R-1001/violations"));
        var root = json.RootElement;

        Assert.Equal(8, root.GetProperty("samplesChecked").GetInt32());
        Assert.Equal(1, root.GetProperty("samplesWithoutSpec").GetInt32());
        var violations = root.GetProperty("violations").EnumerateArray().ToList();
        Assert.Equal(3, violations.Count);
        Assert.Equal("ABOVE_UPPER", violations[0].GetProperty("deviation").GetString());
        Assert.Equal(15.4, violations[0].GetProperty("value").GetDouble());
    }

    [Theory]
    [InlineData("/api/equipment/ETCH-01/utilization", HttpStatusCode.BadRequest)]
    [InlineData("/api/equipment/ETCH-01/utilization?from=2026-11-16T06:00:00&to=2026-11-16T14:00:00", HttpStatusCode.BadRequest)]
    [InlineData("/api/equipment/ETCH-01/utilization?from=2026-11-16T14:00:00Z&to=2026-11-16T06:00:00Z", HttpStatusCode.BadRequest)]
    [InlineData("/api/equipment/NOPE-99/utilization?from=2026-11-16T06:00:00Z&to=2026-11-16T14:00:00Z", HttpStatusCode.NotFound)]
    [InlineData("/api/runs/R-9999/violations", HttpStatusCode.NotFound)]
    public async Task Bad_queries_get_clear_error_responses(string url, HttpStatusCode expected)
    {
        await using var factory = new ApiFactory();
        var client = await ClientWithShiftLogAsync(factory);

        var response = await client.GetAsync(url);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Empty_upload_is_rejected()
    {
        await using var factory = new ApiFactory();
        var response = await factory.CreateClient().PostAsync("/api/events", Ndjson("  \n "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_upload_is_rejected_without_storing_anything()
    {
        await using var factory = new ApiFactory(new() { ["Ingestion:MaxBodyBytes"] = "1000" });
        var client = factory.CreateClient();
        var big = string.Concat(Enumerable.Repeat(ApiFactory.Sample("etch-01-shift.ndjson"), 5));

        var response = await client.PostAsync("/api/events", Ndjson(big));
        var lookup = await client.GetAsync("/api/runs/R-1001/violations");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task Uploading_the_same_file_twice_stores_nothing_new()
    {
        await using var factory = new ApiFactory();
        var client = await ClientWithShiftLogAsync(factory);

        var second = await client.PostAsync("/api/events", Ndjson(ApiFactory.Sample("etch-01-shift.ndjson")));
        var report = await second.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(0, report.GetProperty("accepted").GetInt32());
        Assert.Equal(19, report.GetProperty("duplicates").GetInt32());
    }

    [Fact]
    public async Task OpenApi_document_is_published()
    {
        await using var factory = new ApiFactory();
        var document = await factory.CreateClient().GetStringAsync("/openapi/v1.json");

        Assert.Contains("/api/equipment/{equipmentId}/utilization", document, StringComparison.Ordinal);
    }
}
