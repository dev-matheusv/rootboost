using RootBoost.Application.Models;

namespace RootBoost.Application.Abstractions;

/// <summary>
/// Verifies the authenticity of an incoming payment webhook and normalizes it to a PaymentEvent.
/// One implementation per processor (PayPal, Stripe). Returns null if the signature is invalid or
/// the event is not a completed payment — the endpoint then does nothing. NEVER trust an
/// unverified webhook: without this, anyone can POST a fake "paid" event and get free product.
/// </summary>
public interface IPaymentVerifier
{
    /// <summary>Identifier of the processor this verifier handles, e.g. "paypal" / "stripe".</summary>
    string Provider { get; }

    /// <summary>
    /// Verify the raw request body against the provided headers. Returns a normalized PaymentEvent
    /// on a genuine completed payment, or null to ignore (bad signature / irrelevant event type).
    /// </summary>
    Task<PaymentEvent?> VerifyAndParseAsync(
        string rawBody,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken ct = default);
}
