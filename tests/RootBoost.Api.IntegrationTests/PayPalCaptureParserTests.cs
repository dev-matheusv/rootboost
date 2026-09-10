using System.Text.Json;
using RootBoost.Infrastructure.Payments;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Regressão do bug: na resposta de capture do PayPal o custom_id (nosso productKey) vem DENTRO
/// da captura (payments.captures[].custom_id), não no purchase_unit. Se voltar a ler só do PU,
/// productKey fica vazio e o pedido não persiste. Estes testes travam esse comportamento.
/// </summary>
public class PayPalCaptureParserTests
{
    // custom_id só na captura (cenário que quebrou), shipping + amount presentes.
    private const string CaptureJson = """
    {
      "id": "8E4999764B878991D",
      "status": "COMPLETED",
      "payer": { "email_address": "buyer@example.com" },
      "purchase_units": [{
        "shipping": {
          "name": { "full_name": "John Doe" },
          "address": {
            "address_line_1": "123 Main St",
            "admin_area_2": "Austin",
            "admin_area_1": "TX",
            "postal_code": "78701",
            "country_code": "US"
          }
        },
        "payments": {
          "captures": [{
            "id": "3C679366HH908993F",
            "custom_id": "rack",
            "amount": { "currency_code": "USD", "value": "34.99" }
          }]
        }
      }]
    }
    """;

    [Fact]
    public void Reads_custom_id_from_the_capture_object()
    {
        using var doc = JsonDocument.Parse(CaptureJson);
        var pay = PayPalCaptureParser.ToPaymentEvent(doc.RootElement, "8E4999764B878991D");

        Assert.NotNull(pay);
        Assert.True(pay!.IsPaid);
        Assert.Equal("rack", pay.ProductKey);                 // <- o bug: vinha ""
        Assert.Equal("3C679366HH908993F", pay.PaymentId);     // capture id = idempotência
        Assert.Equal(34.99m, pay.Amount);
        Assert.Equal("USD", pay.Currency);
        Assert.Equal("buyer@example.com", pay.Email);
        Assert.Equal("John Doe", pay.ShipTo.Name);
        Assert.Equal("US", pay.ShipTo.CountryCode);
        Assert.True(pay.ShipTo.IsComplete);
    }

    [Fact]
    public void Returns_null_when_not_completed()
    {
        using var doc = JsonDocument.Parse("""{ "id":"X", "status":"PENDING", "purchase_units":[] }""");
        Assert.Null(PayPalCaptureParser.ToPaymentEvent(doc.RootElement, "X"));
    }
}
