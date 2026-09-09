using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Tracking;

/// <summary>
/// Sends a server-side Purchase to the Meta Conversions API. Uses the order's PaymentId as the
/// event_id so it deduplicates with the browser Pixel event of the same id. PII (email) is SHA-256
/// hashed per Meta's requirement. Best-effort: throwing is caught by the caller, never fails the order.
/// Docs: https://developers.facebook.com/docs/marketing-api/conversions-api
/// </summary>
public sealed class MetaConversionTracker : IConversionTracker
{
    private readonly IHttpClientFactory _factory;
    private readonly MetaOptions _opts;
    private readonly ILogger<MetaConversionTracker> _log;

    public MetaConversionTracker(IHttpClientFactory factory, IOptions<MetaOptions> opts, ILogger<MetaConversionTracker> log)
    {
        _factory = factory;
        _opts = opts.Value;
        _log = log;
    }

    public async Task TrackPurchaseAsync(Order order, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.PixelId) || string.IsNullOrWhiteSpace(_opts.AccessToken))
        {
            _log.LogWarning("Meta CAPI sem PixelId/AccessToken; pulando Purchase {PaymentId}.", order.PaymentId);
            return;
        }

        var evt = new Dictionary<string, object?>
        {
            ["event_name"] = "Purchase",
            ["event_time"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["event_id"] = order.PaymentId,                 // dedup com o Pixel do navegador
            ["action_source"] = "website",
            ["user_data"] = new Dictionary<string, object?>
            {
                ["em"] = new[] { Sha256(order.CustomerEmail) }
            },
            ["custom_data"] = new Dictionary<string, object?>
            {
                ["currency"] = order.Currency,
                ["value"] = order.AmountPaid,
                ["content_ids"] = new[] { order.ProductKey },
                ["content_type"] = "product"
            }
        };

        var body = new Dictionary<string, object?> { ["data"] = new[] { evt } };
        if (!string.IsNullOrWhiteSpace(_opts.TestEventCode))
            body["test_event_code"] = _opts.TestEventCode;

        var http = _factory.CreateClient("meta");
        var url = $"{_opts.ApiVersion}/{_opts.PixelId}/events?access_token={Uri.EscapeDataString(_opts.AccessToken)}";
        using var res = await http.PostAsJsonAsync(url, body, ct);
        if (!res.IsSuccessStatusCode)
        {
            var err = await res.Content.ReadAsStringAsync(ct);
            _log.LogError("Meta CAPI Purchase falhou ({Status}) p/ {PaymentId}: {Err}", (int)res.StatusCode, order.PaymentId, err);
            return;
        }
        _log.LogInformation("Meta CAPI Purchase enviado: {PaymentId} {Amount} {Currency}", order.PaymentId, order.AmountPaid, order.Currency);
    }

    /// <summary>SHA-256 hex (lowercase, trimmed) — formato exigido pelo Meta pra dados de usuário.</summary>
    private static string Sha256(string value)
    {
        var normalized = (value ?? "").Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
