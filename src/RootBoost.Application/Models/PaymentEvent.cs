using RootBoost.Domain;

namespace RootBoost.Application.Models;

/// <summary>
/// A payment normalized from whatever the processor sent (PayPal / Stripe) into one shape the
/// use cases understand. Verification and parsing happen in Infrastructure; by the time a
/// PaymentEvent exists, the signature was already checked.
/// </summary>
public sealed record PaymentEvent(
    bool IsPaid,
    string PaymentId,
    string ProductKey,
    int Quantity,
    string Email,
    ShippingAddress ShipTo,
    decimal Amount,
    string Currency);
