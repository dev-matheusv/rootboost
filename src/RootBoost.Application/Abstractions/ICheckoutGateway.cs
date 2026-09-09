using RootBoost.Application.Models;

namespace RootBoost.Application.Abstractions;

/// <summary>
/// Server-side checkout with the payment provider. Exists to enforce the price on the SERVER:
/// the client only sends productKey + quantity; the amount is resolved from the catalog here.
/// This closes the "price in the browser" fraud vector. One impl per provider (PayPal today).
/// </summary>
public interface ICheckoutGateway
{
    string Provider { get; }

    /// <summary>Create a provider order priced from the catalog. Returns the provider order id.</summary>
    Task<CreateCheckoutResult> CreateOrderAsync(string productKey, int quantity, CancellationToken ct = default);

    /// <summary>
    /// Capture an approved provider order and normalize it to a PaymentEvent (paymentId, shipping,
    /// email, amount) ready for PlaceOrderOnPayment. Returns null if the capture did not complete.
    /// </summary>
    Task<PaymentEvent?> CaptureOrderAsync(string providerOrderId, CancellationToken ct = default);
}
