using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ToolTrace.Api.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}

/// <summary>
/// Hosts the real application in memory (same routing, DI, configuration and JSON settings
/// as production), with only the clock replaced. Each test gets a fresh instance, so the
/// in-memory store never leaks between tests.
/// </summary>
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.FromHours(1));

    private readonly Dictionary<string, string?> _settings;

    public ApiFactory(Dictionary<string, string?>? settings = null) => _settings = settings ?? [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now)));
    }

    public static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));
}
