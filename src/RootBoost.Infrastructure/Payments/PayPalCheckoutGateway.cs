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

        var pay = PayPalCaptureParser.ToPaymentEvent(doc.RootElement, providerOrderId);
        if (pay is null)
            _log.LogWarning("PayPal capture {OrderId} sem evento aproveitável (status/estrutura inesperados).", providerOrderId);
        return pay;
    }
}
