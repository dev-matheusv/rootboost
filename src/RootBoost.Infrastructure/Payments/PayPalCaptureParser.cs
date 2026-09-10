using System.Globalization;
using System.Text.Json;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Payments;

/// <summary>
/// Converte a resposta de capture do PayPal (v2 checkout order) num PaymentEvent. Função pura,
/// testável. Ponto sensível coberto por teste: o `custom_id` (nosso productKey) pode vir no
/// purchase_unit OU dentro do objeto da captura (payments.captures[]).
/// </summary>
public static class PayPalCaptureParser
{
    public static PaymentEvent? ToPaymentEvent(JsonElement root, string providerOrderId)
    {
        if (!string.Equals(Str(root, "status"), "COMPLETED", StringComparison.OrdinalIgnoreCase))
            return null;

        var email = root.TryGetProperty("payer", out var payer) ? Str(payer, "email_address") ?? "" : "";

        if (!root.TryGetProperty("purchase_units", out var pus) || pus.ValueKind != JsonValueKind.Array || pus.GetArrayLength() == 0)
            return null;
        var pu = pus[0];

        // custom_id pode estar no purchase_unit OU na captura.
        var productKey = Str(pu, "custom_id") ?? "";

        var paymentId = providerOrderId;
        var amount = 0m;
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
            IsPaid: true, PaymentId: paymentId, ProductKey: productKey, Quantity: 1,
            Email: email, ShipTo: shipTo, Amount: amount, Currency: currency);
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
