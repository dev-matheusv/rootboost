using System.Text.Json;
using RootBoost.Infrastructure.Payments;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Trava o parser do checkout.session.completed contra o shape REAL do Stripe (campos conferidos
/// num objeto Session vivo da API). Protege a invariante: so sessao paga vira pedido, e o endereco
/// sai de collected_information.shipping_details.
/// </summary>
public class StripeSessionParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private const string PaidSession = """
    {
      "id": "cs_test_abc",
      "payment_status": "paid",
      "status": "complete",
      "payment_intent": "pi_live_123",
      "amount_total": 2999,
      "currency": "usd",
      "metadata": { "productKey": "posture", "quantity": "2" },
      "customer_details": { "email": "buyer@example.com", "name": "Jane Buyer", "phone": "+15551234567" },
      "collected_information": {
        "shipping_details": {
          "name": "Jane Buyer",
          "address": {
            "line1": "123 Main St", "line2": "Apt 4", "city": "Austin",
            "state": "TX", "postal_code": "78701", "country": "us"
          }
        }
      }
    }
    """;

    [Fact]
    public void Paid_session_maps_to_payment_event()
    {
        var pay = StripeSessionParser.ToPaymentEvent(Parse(PaidSession));

        Assert.NotNull(pay);
        Assert.True(pay!.IsPaid);
        Assert.Equal("pi_live_123", pay.PaymentId);      // idempotencia = PaymentIntent
        Assert.Equal("posture", pay.ProductKey);
        Assert.Equal(2, pay.Quantity);
        Assert.Equal("buyer@example.com", pay.Email);
        Assert.Equal(29.99m, pay.Amount);                 // centavos -> decimal
        Assert.Equal("USD", pay.Currency);
    }

    [Fact]
    public void Shipping_address_comes_from_collected_information()
    {
        var pay = StripeSessionParser.ToPaymentEvent(Parse(PaidSession))!;

        Assert.Equal("Jane Buyer", pay.ShipTo.Name);
        Assert.Equal("123 Main St", pay.ShipTo.Line1);
        Assert.Equal("Apt 4", pay.ShipTo.Line2);
        Assert.Equal("Austin", pay.ShipTo.City);
        Assert.Equal("TX", pay.ShipTo.State);
        Assert.Equal("78701", pay.ShipTo.Zip);
        Assert.Equal("US", pay.ShipTo.CountryCode);       // normalizado pra maiusculo
        Assert.True(pay.ShipTo.IsComplete);
    }

    [Fact]
    public void Unpaid_session_is_ignored()
    {
        var unpaid = PaidSession.Replace("\"payment_status\": \"paid\"", "\"payment_status\": \"unpaid\"");
        Assert.Null(StripeSessionParser.ToPaymentEvent(Parse(unpaid)));
    }

    [Fact]
    public void Falls_back_to_session_id_when_there_is_no_payment_intent()
    {
        var noPi = PaidSession.Replace("\"payment_intent\": \"pi_live_123\",", "");
        var pay = StripeSessionParser.ToPaymentEvent(Parse(noPi));
        Assert.Equal("cs_test_abc", pay!.PaymentId);      // nunca fica sem PaymentId
    }

    [Fact]
    public void Quantity_defaults_to_one_when_metadata_is_missing()
    {
        var noMeta = PaidSession.Replace("\"metadata\": { \"productKey\": \"posture\", \"quantity\": \"2\" },", "");
        var pay = StripeSessionParser.ToPaymentEvent(Parse(noMeta))!;
        Assert.Equal(1, pay.Quantity);
        Assert.Equal("", pay.ProductKey);                  // PlaceOrderOnPayment escala pra humano
    }
}
