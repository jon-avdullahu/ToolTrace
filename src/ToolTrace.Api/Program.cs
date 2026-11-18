using System.Text.Json;
using System.Text.Json.Serialization;

using ToolTrace.Api;
using ToolTrace.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper)));

// TimeProvider instead of DateTime.UtcNow, so tests can pin "now".
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEventStore, InMemoryEventStore>();
builder.Services.AddSingleton(services => new SpecCatalog(
    services.GetRequiredService<IConfiguration>().GetSection("Specs").Get<SpecLimit[]>() ?? []));
builder.Services.AddSingleton<IngestionService>();

var app = builder.Build();

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { Status = "ok" }));
app.MapToolTraceEndpoints();

app.Run();

/// <summary>Entry point; public so the integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
