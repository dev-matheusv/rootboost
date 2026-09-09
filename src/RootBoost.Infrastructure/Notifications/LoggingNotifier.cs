using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Notifications;

/// <summary>Dev notifier — logs instead of sending. Selected by config (Notifier=Logging).</summary>
public sealed class LoggingNotifier : INotifier
{
    private readonly ILogger<LoggingNotifier> _log;
    public LoggingNotifier(ILogger<LoggingNotifier> log) => _log = log;

    public Task NotifyOrderConfirmedAsync(Order order, CancellationToken ct = default)
    { _log.LogInformation("[email] order confirmed -> {Email} ({PaymentId})", order.CustomerEmail, order.PaymentId); return Task.CompletedTask; }

    public Task NotifyTrackingAsync(Order order, CancellationToken ct = default)
    { _log.LogInformation("[email] tracking {Tracking} -> {Email} ({PaymentId})", order.TrackingNumber, order.CustomerEmail, order.PaymentId); return Task.CompletedTask; }

    public Task AlertFulfillmentFailedAsync(Order order, CancellationToken ct = default)
    { _log.LogWarning("[alert] fulfillment FAILED for {PaymentId}: {Reason}", order.PaymentId, order.FailureReason); return Task.CompletedTask; }
}
