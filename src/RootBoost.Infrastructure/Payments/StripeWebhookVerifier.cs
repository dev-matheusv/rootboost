using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// Verifies the Stripe-Signature header before trusting a webhook, then normalizes
/// checkout.session.completed into a PaymentEvent. Without this check anyone could POST a fake
/// "paid" event and receive free product (invariant 2 in CLAUDE.md).
/// Scheme: header is "t=&lt;unix&gt;,v1=&lt;hex hmac&gt;"; the signed payload is "{t}.{rawBody}"
/// hashed with HMAC SHA256 using the endpoint signing secret.
/// </summary>
public sealed class StripeWebhookVerifier : IPaymentVerifier
{
    /// <summary>Reject signatures older than this to blunt replay attacks.</summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    private readonly StripeOptions _opts;
    private readonly ILogger<StripeWebhookVerifier> _log;

    public StripeWebhookVerifier(IOptions<StripeOptions> opts, ILogger<StripeWebhookVerifier> log)
    {
        _opts = opts.Value;
        _log = log;
    }

    public string Provider => "stripe";

    public Task<PaymentEvent?> VerifyAndParseAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.WebhookSecret))
        {
            _log.LogError("Stripe webhook secret not configured; refusing to trust the event.");
            return Task.FromResult<PaymentEvent?>(null);
        }

        if (!TryGetHeader(headers, "Stripe-Signature", out var sigHeader) ||
            !IsSignatureValid(rawBody, sigHeader, _opts.WebhookSecret))
        {
            _log.LogWarning("Stripe webhook rejected: bad or missing signature.");
            return Task.FromResult<PaymentEvent?>(null);
        }

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!string.Equals(type, "checkout.session.completed", StringComparison.Ordinal))
                return Task.FromResult<PaymentEvent?>(null); // irrelevant event: ack and ignore

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("object", out var session))
                return Task.FromResult<PaymentEvent?>(null);

            return Task.FromResult(StripeSessionParser.ToPaymentEvent(session));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Stripe webhook parse failed.");
            return Task.FromResult<PaymentEvent?>(null);
        }
    }

    /// <summary>Exposed for tests: validates the v1 signature(s) and the timestamp tolerance.</summary>
    public static bool IsSignatureValid(string rawBody, string signatureHeader, string secret, DateTimeOffset? now = null)
    {
        string? timestamp = null;
        var candidates = new List<string>();

        foreach (var part in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var value = kv[1].Trim();
            if (key == "t") timestamp = value;
            else if (key == "v1") candidates.Add(value);
        }

        if (timestamp is null || candidates.Count == 0) return false;
        if (!long.TryParse(timestamp, out var unix)) return false;

        var sent = DateTimeOffset.FromUnixTimeSeconds(unix);
        var reference = now ?? DateTimeOffset.UtcNow;
        if ((reference - sent).Duration() > Tolerance) return false;

        var expected = ComputeHex(secret, $"{timestamp}.{rawBody}");

        // Constant time comparison so a timing side channel cannot leak the expected signature.
        foreach (var candidate in candidates)
        {
            var a = Encoding.UTF8.GetBytes(expected);
            var b = Encoding.UTF8.GetBytes(candidate);
            if (a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b)) return true;
        }
        return false;
    }

    private static string ComputeHex(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool TryGetHeader(IReadOnlyDictionary<string, string> headers, string name, out string value)
    {
        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value;
                return true;
            }
        }
        value = "";
        return false;
    }
}
