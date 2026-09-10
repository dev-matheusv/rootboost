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

// Railway/Render/Fly inject the port via $PORT. Bind to it when present (local dev uses the default).
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// All adapters wired here; behavior is config-driven (see Infrastructure.DependencyInjection).
builder.Services.AddInfrastructure(builder.Configuration, AppContext.BaseDirectory);

// CORS so the (separately hosted) landing pages can call the checkout endpoints.
// Set Cors:AllowedOrigins (comma-separated) in prod; empty = allow any (dev convenience).
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    var origins = (builder.Configuration["Cors:AllowedOrigins"] ?? "")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (origins.Length == 0) p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    else p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

app.UseCors();

// Dev convenience: when LANDING_DIR is set, serve that folder's static landing at the API root,
// so a local end-to-end test runs from a single origin (no CORS). Not used in production.
var landingDir = Environment.GetEnvironmentVariable("LANDING_DIR");
if (!string.IsNullOrWhiteSpace(landingDir) && Directory.Exists(landingDir))
{
    var provider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(landingDir);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
}

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

    var result = await useCase.HandleAsync(pay, ct: ct);
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

// --- PayPal server-side checkout ----------------------------------------------
// Price is resolved from the catalog HERE, not sent by the browser. The landing calls:
//   createOrder  -> POST /paypal/create-order { productKey, quantity }  -> { id }
//   onApprove    -> POST /paypal/capture-order { orderId }              -> fulfills
app.MapPost("/paypal/create-order", async (CreateOrderRequest req, ICheckoutGateway gateway, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.ProductKey))
        return Results.BadRequest(new { error = "productKey required" });

    var result = await gateway.CreateOrderAsync(req.ProductKey, req.Quantity <= 0 ? 1 : req.Quantity, ct);
    return result.Success
        ? Results.Ok(new { id = result.ProviderOrderId })
        : Results.BadRequest(new { error = result.Error });
});

app.MapPost("/paypal/capture-order", async (HttpContext http, CaptureOrderRequest req, ICheckoutGateway gateway, PlaceOrderOnPayment useCase, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.OrderId))
        return Results.BadRequest(new { error = "orderId required" });

    var pay = await gateway.CaptureOrderAsync(req.OrderId, ct);
    if (pay is null)
        return Results.BadRequest(new { error = "capture did not complete" });

    // Sinais pro Meta CAPI: fbp/fbc/sourceUrl vêm do navegador (corpo); IP/UA do request (X-Forwarded-For atrás de proxy).
    var clientIp = http.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
    if (string.IsNullOrWhiteSpace(clientIp)) clientIp = http.Connection.RemoteIpAddress?.ToString();
    var conv = new ConversionContext(
        Fbp: req.Fbp, Fbc: req.Fbc, ClientIp: clientIp,
        UserAgent: http.Request.Headers.UserAgent.ToString(), EventSourceUrl: req.SourceUrl);

    var result = await useCase.HandleAsync(pay, conv, ct);
    // The payment already succeeded; report the fulfillment outcome. The customer's money is
    // captured regardless — a NeedsHuman outcome means we alerted an operator to finish it.
    return Results.Ok(new { outcome = result.Outcome.ToString(), paymentId = pay.PaymentId });
});

// --- Traffic autopilot (dry-run): mostra as decisões que a automação tomaria ------
app.MapGet("/admin/traffic/plan", async (HttpContext ctx, RootBoost.Application.Traffic.TrafficAutopilot autopilot, IConfiguration cfg, CancellationToken ct) =>
{
    var required = cfg["Orders:ApiKey"];
    if (!string.IsNullOrWhiteSpace(required) &&
        (!ctx.Request.Headers.TryGetValue("X-Api-Key", out var got) || got != required))
        return Results.Unauthorized();

    var report = await autopilot.RunAsync(ct);
    return Results.Ok(report);
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

record CreateOrderRequest(string ProductKey, int Quantity);
record CaptureOrderRequest(string OrderId, string? Fbp = null, string? Fbc = null, string? SourceUrl = null);

// Exposed for integration tests (WebApplicationFactory needs a public entry point type).
public partial class Program { }
