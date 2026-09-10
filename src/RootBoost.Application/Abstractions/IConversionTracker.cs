using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Application.Abstractions;

/// <summary>
/// Sends conversion events to the ad platform (Meta CAPI today) from the SERVER — the reliable,
/// cookie/iOS-proof signal for optimizing paid traffic. Implementations must never throw in a way
/// that fails the order; tracking is best-effort. Deduplicated with the browser Pixel by event_id
/// (we use the order's PaymentId as the shared event_id).
/// </summary>
public interface IConversionTracker
{
    /// <summary>Report a completed purchase (fires only for successfully fulfilled orders).
    /// <paramref name="context"/> carries optional browser signals (fbp/fbc/ip/ua) for better match.</summary>
    Task TrackPurchaseAsync(Order order, ConversionContext? context = null, CancellationToken ct = default);
}
