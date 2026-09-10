using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Tracking;

/// <summary>Default tracker: does nothing (logs at debug). Used until Meta CAPI is configured.</summary>
public sealed class NullConversionTracker : IConversionTracker
{
    private readonly ILogger<NullConversionTracker> _log;
    public NullConversionTracker(ILogger<NullConversionTracker> log) => _log = log;

    public Task TrackPurchaseAsync(Order order, ConversionContext? context = null, CancellationToken ct = default)
    {
        _log.LogDebug("[tracker:null] Purchase {PaymentId} {Amount} {Currency} (tracking desligado)",
            order.PaymentId, order.AmountPaid, order.Currency);
        return Task.CompletedTask;
    }
}
