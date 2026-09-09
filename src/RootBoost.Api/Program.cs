using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Application.UseCases;
using RootBoost.Domain;
using RootBoost.Infrastructure;
using RootBoost.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// All adapters wired here; behavior is config-driven (see Infrastructure.DependencyInjection).
builder.Services.AddInfrastructure(builder.Configuration, AppContext.BaseDirectory);

var app = builder.Build();

// Create the SQLite schema on startup. Fine for SQLite MVP; swap to migrations before real scale.
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<RootBoostDbContext>().Database.EnsureCreated();

app.MapGet("/health", () => Results.Ok("ok"));

// --- Orders dashboard (protected by an API key header) -----------------------
app.MapGet("/orders", async (HttpContext ctx, IOrderRepository repo, IConfiguration cfg, CancellationToken ct) =>
{
    var required = cfg["Orders:ApiKey"];
    if (!string.IsNullOrWhiteSpace(required) &&
        (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var got) || got != required))
        return Results.Unauthorized();

    var orders = await repo.ListAsync(200, ct);
    return Results.Ok(orders.Select(OrderDto.From));
});

// --- Payment webhook (PayPal in prod, Test verifier in dev) -------------------
app.MapPost("/webhook/payment", async (HttpContext ctx, IPaymentVerifier verifier, PlaceOrderOnPayment useCase, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("PaymentWebhook");
    var raw = await ReadBodyAsync(ctx);
    var headers = HeaderMap(ctx);

    PaymentEvent? pay;
    try { pay = await verifier.VerifyAndParseAsync(raw, headers, ct); }
    catch (Exception ex) { log.LogError(ex, "Payment verifier threw."); return Results.Ok(); }

    if (pay is null) return Results.Ok(); // unverified or irrelevant event -> ack and ignore

    var result = await useCase.HandleAsync(pay, ct);
    // Always 200 so the processor stops retrying; the outcome is our record to act on.
    return Results.Ok(new { outcome = result.Outcome.ToString(), paymentId = pay.PaymentId, error = result.Error });
});

// --- Stripe webhook (behind a feature flag until there's an LLC + Stripe) ------
app.MapPost("/webhook/stripe", (IConfiguration cfg) =>
    string.Equals(cfg["Features:Stripe"], "true", StringComparison.OrdinalIgnoreCase)
        ? Results.Problem("Stripe handler not implemented yet.", statusCode: 501)
        : Results.NotFound());

// --- CJ tracking webhook ------------------------------------------------------
// TODO: confirm the exact CJ webhook payload (field names for order id + tracking number) in the
// CJ docs and adjust the extraction below. We parse defensively and ignore anything we can't read.
app.MapPost("/webhook/cj", async (HttpContext ctx, AttachTracking useCase, ILoggerFactory lf, CancellationToken ct) =>
{
    var log = lf.CreateLogger("CjWebhook");
    var raw = await ReadBodyAsync(ctx);
    try
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        // CJ payloads vary by event; try a few likely field names without inventing a contract.
        var supplierOrderId = FirstString(root, "orderId", "cjOrderId", "orderNum");
        var tracking = FirstString(root, "trackNumber", "trackingNumber", "logisticNo");
        if (supplierOrderId is null) { log.LogWarning("CJ webhook without recognizable order id: {Body}", Trunc(raw)); return Results.Ok(); }
        await useCase.HandleAsync(supplierOrderId, tracking, ct);
    }
    catch (Exception ex) { log.LogError(ex, "CJ webhook parse failed: {Body}", Trunc(raw)); }
    return Results.Ok();
});

app.Run();

// --- helpers ------------------------------------------------------------------
static async Task<string> ReadBodyAsync(HttpContext ctx)
{
    using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    return await reader.ReadToEndAsync();
}

static Dictionary<string, string> HeaderMap(HttpContext ctx)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var h in ctx.Request.Headers)
        map[h.Key] = h.Value.ToString();
    return map;
}

static string? FirstString(JsonElement root, params string[] names)
{
    foreach (var n in names)
        if (root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
            return s;
    return null;
}

static string Trunc(string s) => s.Length <= 400 ? s : s[..400];

// Clean projection for the /orders response (enum as string, address flattened).
record OrderDto(string PaymentId, string ProductKey, int Quantity, string Status, string Email,
                string? SupplierOrderId, string? Tracking, decimal Amount, string Currency,
                string ShipName, string ShipCountry, DateTimeOffset CreatedAt, string? FailureReason)
{
    public static OrderDto From(Order o) => new(
        o.PaymentId, o.ProductKey, o.Quantity, o.Status.ToString(), o.CustomerEmail,
        o.SupplierOrderId, o.TrackingNumber, o.AmountPaid, o.Currency,
        o.ShipTo.Name, o.ShipTo.CountryCode, o.CreatedAt, o.FailureReason);
}

// Exposed for integration tests (WebApplicationFactory needs a public entry point type).
public partial class Program { }
