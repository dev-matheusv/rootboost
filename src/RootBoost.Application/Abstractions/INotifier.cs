using RootBoost.Domain;

namespace RootBoost.Application.Abstractions;

/// <summary>Customer notifications (email today). Never let a notification failure fail the order.</summary>
public interface INotifier
{
    Task NotifyOrderConfirmedAsync(Order order, CancellationToken ct = default);

    Task NotifyTrackingAsync(Order order, CancellationToken ct = default);

    /// <summary>Alert the operator that a paid order failed to fulfill and needs a human.</summary>
    Task AlertFulfillmentFailedAsync(Order order, CancellationToken ct = default);
}
