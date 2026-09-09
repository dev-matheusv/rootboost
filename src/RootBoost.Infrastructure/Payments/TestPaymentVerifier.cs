using System.Globalization;
using System.Text.Json;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// DEV-ONLY verifier. No signature check — parses a simple hand-written JSON so the full flow is
/// demoable locally without real PayPal. Selected by config (Payments:Verifier=Test). Never in prod.
/// Expected body:
/// { "paymentId":"PAY-1","productKey":"rack","quantity":1,"email":"a@b.com",
///   "name":"Jane","line1":"123 St","city":"Austin","state":"TX","zip":"78701",
///   "countryCode":"US","phone":"+1...","amount":34.99,"currency":"USD" }
/// </summary>
public sealed class TestPaymentVerifier : IPaymentVerifier
{
    public string Provider => "test";

    public Task<PaymentEvent?> VerifyAndParseAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var r = doc.RootElement;

        string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int I(string k, int def) => r.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : def;
        decimal D(string k)
        {
            if (!r.TryGetProperty(k, out var v)) return 0m;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
            return decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
        }

        var evt = new PaymentEvent(
            IsPaid: true,
            PaymentId: S("paymentId"),
            ProductKey: S("productKey"),
            Quantity: I("quantity", 1),
            Email: S("email"),
            ShipTo: new ShippingAddress(S("name"), S("line1"), S("city"), S("state"), S("zip"), S("countryCode"),
                                        string.IsNullOrEmpty(S("phone")) ? null : S("phone")),
            Amount: D("amount"),
            Currency: string.IsNullOrEmpty(S("currency")) ? "USD" : S("currency"));

        return Task.FromResult<PaymentEvent?>(evt);
    }
}
