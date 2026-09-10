using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// Server-side PayPal checkout. The amount ALWAYS comes from the catalog here — the client never
/// sends a price. Create builds a PayPal order; Capture completes it and normalizes the result into
/// a PaymentEvent for PlaceOrderOnPayment.
/// </summary>
public sealed class PayPalCheckoutGateway : ICheckoutGateway
{
    private readonly PayPalClient _paypal;
    private readonly IProductCatalog _catalog;
    private readonly ILogger<PayPalCheckoutGateway> _log;

    public PayPalCheckoutGateway(PayPalClient paypal, IProductCatalog catalog, ILogger<PayPalCheckoutGateway> log)
    {
        _paypal = paypal;
        _catalog = catalog;
        _log = log;
    }

    public string Provider => "paypal";

    public async Task<CreateCheckoutResult> CreateOrderAsync(string productKey, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) quantity = 1;

        var product = _catalog.Find(productKey);
        if (product is null)
            return CreateCheckoutResult.Fail($"unknown product '{productKey}'");

        var amount = product.Price * quantity;
        var display = product.DisplayName ?? product.Key;

        var orderId = await _paypal.CreateOrderAsync(product.Key, display, amount, product.Currency, ct);
        return orderId is null
            ? CreateCheckoutResult.Fail("PayPal did not return an order id")
            : CreateCheckoutResult.Ok(orderId, amount, product.Currency);
    }

    public async Task<PaymentEvent?> CaptureOrderAsync(string providerOrderId, CancellationToken ct = default)
    {
        using var doc = await _paypal.CaptureOrderAsync(providerOrderId, ct);
        if (doc is null) return null;

        var root = doc.RootElement;
        if (!string.Equals(Str(root, "status"), "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            _log.LogWarning("PayPal capture for {OrderId} not COMPLETED (status={Status}).", providerOrderId, Str(root, "status"));
            return null;
        }

        var email = root.TryGetProperty("payer", out var payer) ? Str(payer, "email_address") ?? "" : "";

        if (!root.TryGetProperty("purchase_units", out var pus) || pus.ValueKind != JsonValueKind.Array || pus.GetArrayLength() == 0)
            return null;
        var pu = pus[0];

        // custom_id (nosso productKey) pode vir no purchase_unit OU no objeto da captura.
        var productKey = Str(pu, "custom_id") ?? "";

        // capture id is the payment id / idempotency key
        string paymentId = providerOrderId;
        decimal amount = 0m;
        var currency = "USD";
        if (pu.TryGetProperty("payments", out var payments) &&
            payments.TryGetProperty("captures", out var caps) &&
            caps.ValueKind == JsonValueKind.Array && caps.GetArrayLength() > 0)
        {
            var cap = caps[0];
            paymentId = Str(cap, "id") ?? providerOrderId;
            if (string.IsNullOrWhiteSpace(productKey))
                productKey = Str(cap, "custom_id") ?? "";
            if (cap.TryGetProperty("amount", out var amt))
            {
                decimal.TryParse(Str(amt, "value"), NumberStyles.Any, CultureInfo.InvariantCulture, out amount);
                currency = Str(amt, "currency_code") ?? currency;
            }
        }

        var shipTo = pu.TryGetProperty("shipping", out var shipping)
            ? ParseShipping(shipping)
            : new ShippingAddress("", "", "", "", "", "");

        return new PaymentEvent(
            IsPaid: true,
            PaymentId: paymentId,
            ProductKey: productKey,
            Quantity: 1, // PayPal order was created with a single line; multi-qty support is a later step
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
            City: Str(a, "admin_area_2") ?? "",
            State: Str(a, "admin_area_1") ?? "",
            Zip: Str(a, "postal_code") ?? "",
            CountryCode: Str(a, "country_code") ?? "",
            Phone: null,
            Line2: Str(a, "address_line_2"));
    }

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
