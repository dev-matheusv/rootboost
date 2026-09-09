using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Boots the real API pipeline with the deterministic dev adapters (Mock supplier, Logging
/// notifier, Test payment verifier) and an isolated SQLite file + a fulfillable test catalog.
/// Mirrors the RootFlow integration-test approach: real pipeline, fake externals.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rootboost-it-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Supplier", "Mock");
        builder.UseSetting("Notifier", "Logging");
        builder.UseSetting("Payments:Verifier", "Test");
        builder.UseSetting("ConnectionStrings:Sqlite", $"Data Source={_dbPath}");
        builder.UseSetting("Catalog:Path", Path.Combine(AppContext.BaseDirectory, "test-catalog.json"));
        // no Orders:ApiKey -> /orders is open in tests
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
