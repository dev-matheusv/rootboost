using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// Verifies a PayPal webhook and normalizes PAYMENT.CAPTURE.COMPLETED into a PaymentEvent.
/// The capture resource does NOT reliably carry the shipping address, so we take the linked
/// order id from the event and fetch the v2 order for shipping + payer email + item quantity.
/// Returns null (ignore) for bad signatures or non-payment events.
/// </summary>
public sealed class PayPalWebhookVerifier : IPaymentVerifier
{
    private readonly PayPalClient _paypal;
    private readonly ILogger<PayPalWebhookVerifier> _log;

    public PayPalWebhookVerifier(PayPalClient paypal, ILogger<PayPalWebhookVerifier> log)
    {
        _paypal = paypal;
        _log = log;
    }

    public string Provider => "paypal";

    public async Task<PaymentEvent?> VerifyAndParseAsync(
        string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        if (!await _paypal.VerifyWebhookAsync(rawBody, headers, ct))
        {
            _log.LogWarning("PayPal webhook failed signature verification; ignoring.");
            return null;
        }

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        var eventType = Str(root, "event_type");
        if (!string.Equals(eventType, "PAYMENT.CAPTURE.COMPLETED", StringComparison.OrdinalIgnoreCase))
            return null; // only completed captures fulfill

        if (!root.TryGetProperty("resource", out var resource))
            return null;

        var paymentId = Str(resource, "id") ?? "";
        var amount = 0m;
        var currency = "USD";
        if (resource.TryGetProperty("amount", out var amt))
        {
            decimal.TryParse(Str(amt, "value"), NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
            currency = Str(amt, "currency_code") ?? currency;
        }

        var productKey = Str(resource, "custom_id");
        var orderId = Nested(resource, "supplementary_data", "related_ids", "order_id");

        // Enrich with the order (shipping address, email, quantity, custom_id fallback).
        string email = "";
        int quantity = 1;
        ShippingAddress? shipTo = null;

        if (!string.IsNullOrWhiteSpace(orderId))
        {
            using var orderDoc = await _paypal.GetOrderAsync(orderId!, ct);
            if (orderDoc is not null)
            {
                var oroot = orderDoc.RootElement;
                if (oroot.TryGetProperty("payer", out var payer))
                    email = Str(payer, "email_address") ?? "";

                if (oroot.TryGetProperty("purchase_units", out var pus) &&
                    pus.ValueKind == JsonValueKind.Array && pus.GetArrayLength() > 0)
                {
                    var pu = pus[0];
                    productKey ??= Str(pu, "custom_id");

                    if (pu.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        var q = 0;
                        foreach (var it in items.EnumerateArray())
                            if (int.TryParse(Str(it, "quantity"), out var qi)) q += qi;
                        if (q > 0) quantity = q;
                    }

                    if (pu.TryGetProperty("shipping", out var shipping))
                        shipTo = ParseShipping(shipping);
                }
            }
        }

        // If we couldn't build a shipping address, hand over an empty one — the use case treats an
        // incomplete address as NeedsHuman rather than shipping to nowhere.
        shipTo ??= new ShippingAddress(Name: "", Line1: "", City: "", State: "", Zip: "", CountryCode: "");

        return new PaymentEvent(
            IsPaid: true,
            PaymentId: paymentId,
            ProductKey: productKey ?? "",
            Quantity: quantity,
            Email: email,
            ShipTo: shipTo,
            Amount: amount,
            Currency: currency);
    }

    private static ShippingAddress ParseShipping(JsonElement shipping)
    {
        var name = shipping.TryGetProperty("name", out var n) ? Str(n, "full_name") ?? "" : "";
        if (!shipping.TryGetProperty("address", out var a))
            return new ShippingAddress(name, "", "", "", "", "");

        return new ShippingAddress(
            Name: name,
            Line1: Str(a, "address_line_1") ?? "",
            City: Str(a, "admin_area_2") ?? "",         // PayPal: admin_area_2 = city
            State: Str(a, "admin_area_1") ?? "",         // PayPal: admin_area_1 = state/province
            Zip: Str(a, "postal_code") ?? "",
            CountryCode: Str(a, "country_code") ?? "",
            Phone: null,
            Line2: Str(a, "address_line_2"));
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Nested(JsonElement e, params string[] path)
    {
        var cur = e;
        foreach (var p in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out var next))
                return null;
            cur = next;
        }
        return cur.ValueKind == JsonValueKind.String ? cur.GetString() : null;
    }
}
