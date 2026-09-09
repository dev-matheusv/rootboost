using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;

namespace RootBoost.Application.UseCases;

public enum AttachTrackingOutcome { Attached, OrderNotFound, Ignored }

public sealed record AttachTrackingResult(AttachTrackingOutcome Outcome);

/// <summary>
/// Handles a tracking/status update from the supplier webhook: find the order by supplier id,
/// attach the tracking number, notify the customer. Idempotent — the supplier retries too.
/// </summary>
public sealed class AttachTracking
{
    private readonly IOrderRepository _orders;
    private readonly INotifier _notifier;
    private readonly ILogger<AttachTracking> _log;

    public AttachTracking(IOrderRepository orders, INotifier notifier, ILogger<AttachTracking> log)
    {
        _orders = orders;
        _notifier = notifier;
        _log = log;
    }

    public async Task<AttachTrackingResult> HandleAsync(string supplierOrderId, string? trackingNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supplierOrderId) || string.IsNullOrWhiteSpace(trackingNumber))
            return new AttachTrackingResult(AttachTrackingOutcome.Ignored);

        var order = await _orders.GetBySupplierOrderIdAsync(supplierOrderId, ct);
        if (order is null)
        {
            _log.LogWarning("Tracking update for unknown supplier order {SupplierOrderId}.", supplierOrderId);
            return new AttachTrackingResult(AttachTrackingOutcome.OrderNotFound);
        }

        var alreadyHadTracking = order.TrackingNumber == trackingNumber;
        order.AttachTracking(trackingNumber);
        await _orders.UpdateAsync(order, ct);

        if (!alreadyHadTracking)
        {
            try { await _notifier.NotifyTrackingAsync(order, ct); }
            catch (Exception ex) { _log.LogWarning(ex, "Tracking notification failed for {PaymentId}.", order.PaymentId); }
        }

        return new AttachTrackingResult(AttachTrackingOutcome.Attached);
    }
}
