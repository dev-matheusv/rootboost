// ============================================================================
//  AUTO-FULFILLMENT ENGINE  (.NET 8 minimal API)  — the core of the platform
//  Flow:  landing page → customer pays → payment webhook hits THIS service →
//         we verify the payment → we call CJ Dropshipping API to place the order
//         with the buyer's shipping address → CJ later posts tracking back here →
//         we notify the customer. No manual step.
//
//  This is a working skeleton. Fill the TODOs (secrets + exact CJ payload from
//  https://developers.cjdropshipping.com docs) and it runs. Built to grow into
//  the multi-product platform, not just the rack.
// ============================================================================

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
// Persist orders somewhere real later (Postgres/EF). In-memory for the skeleton:
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<CjClient>();
var app = builder.Build();

// --- config (move to appsettings/user-secrets/env in real life) --------------
// TODO: fill these
var CJ_EMAIL   = Environment.GetEnvironmentVariable("CJ_EMAIL")   ?? "you@example.com";
var CJ_API_KEY = Environment.GetEnvironmentVariable("CJ_API_KEY") ?? "TODO_CJ_API_KEY";
// Map your landing-page product -> the CJ variant (VID) you'll fulfill from.
var PRODUCT_MAP = new Dictionary<string, string>
{
    ["spinrack"] = "TODO_CJ_VARIANT_ID_FOR_RACK",
    // ["chopper"] = "...", ["petfeeder"] = "...",
};

// 1) PAYMENT WEBHOOK -----------------------------------------------------------
// Point PayPal (or Stripe) webhooks here. Start with the "payment completed" event.
app.MapPost("/webhook/payment", async (HttpContext ctx, CjClient cj, OrderStore store) =>
{
    using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var raw = await reader.ReadToEndAsync();

    // TODO: VERIFY the webhook signature before trusting it.
    //   PayPal: verify via /v1/notifications/verify-webhook-signature
    //   Stripe: Stripe.EventUtility.ConstructEvent(raw, sigHeader, whSecret)
    // Skipping verification in prod = anyone can trigger free orders. Do it.

    PaymentEvent? pay = ParsePayment(raw);           // normalize PayPal/Stripe -> PaymentEvent
    if (pay is null || !pay.IsPaid) return Results.Ok(); // ignore non-payment events

    if (store.Exists(pay.PaymentId))                 // idempotency: webhooks retry
        return Results.Ok();

    if (!PRODUCT_MAP.TryGetValue(pay.ProductKey, out var cjVariantId))
        return Results.BadRequest($"No CJ mapping for '{pay.ProductKey}'");

    var cjOrderId = await cj.CreateOrderAsync(pay, cjVariantId);
    store.Save(new Order(pay.PaymentId, pay.ProductKey, cjOrderId, "PLACED", pay.Email, null));

    // (optional) send "order confirmed" email here
    return Results.Ok(new { cjOrderId });
});

// 2) CJ TRACKING WEBHOOK -------------------------------------------------------
// Register this URL in CJ so they push tracking/status updates back to you.
app.MapPost("/webhook/cj", async (HttpContext ctx, OrderStore store) =>
{
    using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var raw = await reader.ReadToEndAsync();
    // TODO: parse CJ payload (order status + trackNumber) per CJ webhook docs.
    // store.AttachTracking(cjOrderId, trackNumber);  then email the customer.
    return Results.Ok();
});

app.MapGet("/orders", (OrderStore store) => store.All());   // quick dashboard
app.MapGet("/health", () => "ok");
app.Run();

// --- CJ Dropshipping client ---------------------------------------------------
class CjClient
{
    private readonly IHttpClientFactory _f;
    private readonly string _email, _apiKey;
    private string? _token;
    const string BASE = "https://developers.cjdropshipping.com/api2.0/v1/";

    public CjClient(IHttpClientFactory f, IConfiguration cfg)
    {
        _f = f;
        _email  = Environment.GetEnvironmentVariable("CJ_EMAIL")   ?? "you@example.com";
        _apiKey = Environment.GetEnvironmentVariable("CJ_API_KEY") ?? "TODO_CJ_API_KEY";
    }

    // CJ auth: POST authentication/getAccessToken { email, apiKey } -> accessToken
    private async Task<string> GetTokenAsync()
    {
        if (_token is not null) return _token;               // TODO: cache + refresh on expiry
        var http = _f.CreateClient();
        var body = JsonSerializer.Serialize(new { email = _email, apiKey = _apiKey });
        var res = await http.PostAsync(BASE + "authentication/getAccessToken",
            new StringContent(body, Encoding.UTF8, "application/json"));
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        _token = doc.RootElement.GetProperty("data").GetProperty("accessToken").GetString();
        return _token!;
    }

    // Create the order on CJ (auth header: CJ-Access-Token).
    // TODO: match the exact body to CJ "createOrderV2" docs (products[], shipping fields, logistic).
    public async Task<string> CreateOrderAsync(PaymentEvent pay, string cjVariantId)
    {
        var token = await GetTokenAsync();
        var http = _f.CreateClient();
        http.DefaultRequestHeaders.Add("CJ-Access-Token", token);

        var payload = new
        {
            orderNumber = pay.PaymentId,                     // your reference
            shippingCountryCode = pay.CountryCode,
            shippingProvince = pay.State,
            shippingCity = pay.City,
            shippingAddress = pay.Address1,
            shippingCustomerName = pay.Name,
            shippingZip = pay.Zip,
            shippingPhone = pay.Phone,
            // logisticName = "...", // choose CJ line with a US/EU warehouse for ~1wk delivery
            products = new[] { new { vid = cjVariantId, quantity = 1 } }
        };
        var res = await http.PostAsync(BASE + "shopping/order/createOrderV2",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        // TODO: check result flag; CJ returns an orderId in data on success.
        return doc.RootElement.GetProperty("data").GetProperty("orderId").GetString() ?? "";
    }
}

// --- helpers / models ---------------------------------------------------------
// Normalize whatever PayPal/Stripe sends into one shape.
static PaymentEvent? ParsePayment(string raw)
{
    // TODO: implement per provider. Extract: paid?, paymentId, productKey (from item/SKU),
    // buyer email, and the SHIPPING address (name, address1, city, state, zip, country, phone).
    // PayPal capture: purchase_units[0].shipping.address + payer.email_address.
    return null; // <- replace
}

record PaymentEvent(bool IsPaid, string PaymentId, string ProductKey, string Email,
                    string Name, string Address1, string City, string State,
                    string Zip, string CountryCode, string Phone);

record Order(string PaymentId, string ProductKey, string CjOrderId, string Status,
             string Email, string? Tracking);

class OrderStore
{
    private readonly Dictionary<string, Order> _o = new();
    public bool Exists(string id) => _o.ContainsKey(id);
    public void Save(Order o) => _o[o.PaymentId] = o;
    public IEnumerable<Order> All() => _o.Values;
    public void AttachTracking(string paymentId, string track)
    { if (_o.TryGetValue(paymentId, out var o)) _o[paymentId] = o with { Tracking = track, Status = "SHIPPED" }; }
}
