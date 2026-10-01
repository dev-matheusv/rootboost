using System.Globalization;
using System.Text.Json;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// Pure translation of a Stripe Checkout Session (the data.object of checkout.session.completed)
/// into our <see cref="PaymentEvent"/>. Kept free of HTTP so it can be unit tested against real
/// payloads. Field names were confirmed against a live Session object from the Stripe API, not
/// guessed: the shipping address lives under "collected_information.shipping_details" in the
/// current API version ("shipping_details" no longer exists at the top level), and the
/// idempotency key we use is "payment_intent".
/// </summary>
public static class StripeSessionParser
{
    /// <summary>
    /// Returns a PaymentEvent for a genuinely paid session, or null when the session is not paid
    /// (the caller then ignores the event rather than fulfilling anything).
    /// </summary>
    public static PaymentEvent? ToPaymentEvent(JsonElement session)
    {
        // Only a paid session may fulfill. "payment_status" is the authority, not "status".
        var paymentStatus = Str(session, "payment_status");
        if (!string.Equals(paymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
            return null;

        // Idempotency key: the PaymentIntent id. Falls back to the session id so we never end up
        // with an empty PaymentId (Order would throw and we would lose a paid order).
        var paymentId = Str(session, "payment_intent") ?? Str(session, "id");
        if (string.IsNullOrWhiteSpace(paymentId)) return null;

        // productKey/quantity travel in metadata, which we set when creating the session.
        var metadata = Obj(session, "metadata");
        var productKey = metadata is { } m ? Str(m, "productKey") ?? "" : "";
        var quantity = 1;
        if (metadata is { } m2 && int.TryParse(Str(m2, "quantity"), out var q) && q > 0) quantity = q;

        var customer = Obj(session, "customer_details");
        var email = (customer is { } c ? Str(c, "email") : null) ?? "";

        var shipTo = ReadShipping(session, customer);

        // amount_total is in the smallest currency unit (cents for USD).
        decimal amount = 0m;
        if (session.TryGetProperty("amount_total", out var at) && at.ValueKind == JsonValueKind.Number)
            amount = at.GetInt64() / 100m;

        var currency = (Str(session, "currency") ?? "usd").ToUpperInvariant();

        return new PaymentEvent(true, paymentId, productKey, quantity, email, shipTo, amount, currency);
    }

    /// <summary>
    /// Shipping comes from collected_information.shipping_details. We fall back to the billing
    /// address on customer_details so a paid order is never dropped just because the shape moved;
    /// PlaceOrderOnPayment still refuses to ship an incomplete address.
    /// </summary>
    private static ShippingAddress ReadShipping(JsonElement session, JsonElement? customer)
    {
        var collected = Obj(session, "collected_information");
        var details = collected is { } ci ? Obj(ci, "shipping_details") : null;

        var name = details is { } d ? Str(d, "name") : null;
        var addr = details is { } d2 ? Obj(d2, "address") : null;

        if (addr is null && customer is { } c)
        {
            name ??= Str(c, "name");
            addr = Obj(c, "address");
        }

        if (addr is not { } a)
            return new ShippingAddress(name ?? "", "", "", "", "", "");

        return new ShippingAddress(
            Name: name ?? "",
            Line1: Str(a, "line1") ?? "",
            City: Str(a, "city") ?? "",
            State: Str(a, "state") ?? "",
            Zip: Str(a, "postal_code") ?? "",
            CountryCode: (Str(a, "country") ?? "").ToUpperInvariant(),
            Phone: customer is { } cc ? Str(cc, "phone") : null,
            Line2: Str(a, "line2"));
    }

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static JsonElement? Obj(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v
            : null;
}
