using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// Thin PayPal REST client: OAuth token (client_credentials, cached), webhook signature
/// verification, and order lookup. Registered singleton so the OAuth token is cached.
/// Contracts confirmed against PayPal REST docs (v1 verify-webhook-signature, v2 checkout orders).
/// </summary>
public sealed class PayPalClient
{
    private readonly IHttpClientFactory _factory;
    private readonly PayPalOptions _opts;
    private readonly ILogger<PayPalClient> _log;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalClient(IHttpClientFactory factory, IOptions<PayPalOptions> opts, ILogger<PayPalClient> log)
    {
        _factory = factory;
        _opts = opts.Value;
        _log = log;
    }

    public string WebhookId => _opts.WebhookId;

    /// <summary>Verify a webhook against PayPal. Returns true only on verification_status == SUCCESS.</summary>
    public async Task<bool> VerifyWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opts.WebhookId))
        {
            _log.LogError("PayPal WebhookId not configured — cannot verify webhook signature. Rejecting.");
            return false;
        }

        string? H(string k) => headers.TryGetValue(k, out var v) ? v : null;

        JsonElement webhookEvent;
        using (var doc = JsonDocument.Parse(rawBody))
            webhookEvent = doc.RootElement.Clone();

        var verifyBody = new
        {
            auth_algo = H("paypal-auth-algo"),
            cert_url = H("paypal-cert-url"),
            transmission_id = H("paypal-transmission-id"),
            transmission_sig = H("paypal-transmission-sig"),
            transmission_time = H("paypal-transmission-time"),
            webhook_id = _opts.WebhookId,
            webhook_event = webhookEvent
        };

        var http = await AuthorizedClientAsync(ct);
        using var res = await http.PostAsJsonAsync("v1/notifications/verify-webhook-signature", verifyBody, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("PayPal verify-webhook-signature HTTP {Status}: {Body}", (int)res.StatusCode, json);
            return false;
        }

        using var vdoc = JsonDocument.Parse(json);
        var status = vdoc.RootElement.TryGetProperty("verification_status", out var s) ? s.GetString() : null;
        return string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Create a v2 checkout order with the amount defined HERE (server-side). custom_id carries the
    /// productKey so capture/webhook can resolve what to fulfill. Returns the PayPal order id, or null.
    /// </summary>
    public async Task<string?> CreateOrderAsync(
        string productKey, string displayName, decimal amount, string currency, CancellationToken ct)
    {
        var http = await AuthorizedClientAsync(ct);
        var body = new
        {
            intent = "CAPTURE",
            purchase_units = new[]
            {
                new
                {
                    custom_id = productKey,
                    description = displayName,
                    amount = new { currency_code = currency, value = amount.ToString("0.00", CultureInfo.InvariantCulture) }
                }
            },
            application_context = new { shipping_preference = "GET_FROM_FILE", user_action = "PAY_NOW" }
        };

        using var res = await http.PostAsJsonAsync("v2/checkout/orders", body, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("PayPal create order HTTP {Status}: {Body}", (int)res.StatusCode, json);
            return null;
        }
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    /// <summary>Capture an approved v2 checkout order. Returns the full capture response, or null.</summary>
    public async Task<JsonDocument?> CaptureOrderAsync(string orderId, CancellationToken ct)
    {
        var http = await AuthorizedClientAsync(ct);
        // Capture takes an empty body; PayPal requires the header even so.
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var res = await http.PostAsync($"v2/checkout/orders/{orderId}/capture", content, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("PayPal capture order {OrderId} HTTP {Status}: {Body}", orderId, (int)res.StatusCode, json);
            return null;
        }
        return JsonDocument.Parse(json);
    }

    /// <summary>Fetch a v2 checkout order (for shipping address / payer / items).</summary>
    public async Task<JsonDocument?> GetOrderAsync(string orderId, CancellationToken ct)
    {
        var http = await AuthorizedClientAsync(ct);
        using var res = await http.GetAsync($"v2/checkout/orders/{orderId}", ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            _log.LogError("PayPal get order {OrderId} HTTP {Status}: {Body}", orderId, (int)res.StatusCode, json);
            return null;
        }
        return JsonDocument.Parse(json);
    }

    private async Task<HttpClient> AuthorizedClientAsync(CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);
        var http = _factory.CreateClient("paypal");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1))
            return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt.AddMinutes(-1))
                return _token;

            var http = _factory.CreateClient("paypal");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opts.ClientId}:{_opts.ClientSecret}"));
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            req.Content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            using var res = await http.SendAsync(req, ct);
            var json = await res.Content.ReadAsStringAsync(ct);
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);
            _token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3000;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _token;
        }
        finally { _tokenLock.Release(); }
    }
}
