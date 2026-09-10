using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Application.UseCases;

public enum PlaceOrderOutcome
{
    /// <summary>Order created at the supplier. The happy path.</summary>
    Fulfilled,
    /// <summary>This payment was already processed. No-op (idempotent).</summary>
    AlreadyProcessed,
    /// <summary>We got paid but could not fulfill (no mapping / bad address / supplier error).
    /// The order is persisted as Failed and a human was alerted. Money is at risk — act on it.</summary>
    NeedsHuman
}

public sealed record PlaceOrderResult(PlaceOrderOutcome Outcome, Order? Order, string? Error = null);

/// <summary>
/// The core use case. Given a VERIFIED payment, create the fulfillment order at the supplier.
/// Guarantees: (1) idempotent by paymentId, (2) never double-fulfills, (3) a paid order that
/// cannot be fulfilled is persisted and escalated to a human rather than silently dropped.
/// </summary>
public sealed class PlaceOrderOnPayment
{
    private readonly IOrderRepository _orders;
    private readonly IProductCatalog _catalog;
    private readonly ISupplierClient _supplier;
    private readonly INotifier _notifier;
    private readonly IConversionTracker _tracker;
    private readonly ILogger<PlaceOrderOnPayment> _log;

    public PlaceOrderOnPayment(
        IOrderRepository orders,
        IProductCatalog catalog,
        ISupplierClient supplier,
        INotifier notifier,
        IConversionTracker tracker,
        ILogger<PlaceOrderOnPayment> log)
    {
        _orders = orders;
        _catalog = catalog;
        _supplier = supplier;
        _notifier = notifier;
        _tracker = tracker;
        _log = log;
    }

    public async Task<PlaceOrderResult> HandleAsync(PaymentEvent pay, ConversionContext? conversionContext = null, CancellationToken ct = default)
    {
        if (!pay.IsPaid)
            return new PlaceOrderResult(PlaceOrderOutcome.AlreadyProcessed, null, "event is not a completed payment");

        // (1) Idempotency — payment webhooks retry; never process the same payment twice.
        if (await _orders.ExistsAsync(pay.PaymentId, ct))
        {
            _log.LogInformation("Payment {PaymentId} already processed; ignoring duplicate webhook.", pay.PaymentId);
            var existing = await _orders.GetByPaymentIdAsync(pay.PaymentId, ct);
            return new PlaceOrderResult(PlaceOrderOutcome.AlreadyProcessed, existing);
        }

        // Sem productKey não dá nem pra montar o pedido (invariante do domínio). Escala em vez de estourar.
        if (string.IsNullOrWhiteSpace(pay.ProductKey))
        {
            _log.LogError("Pagamento {PaymentId} chegou sem productKey; não dá pra identificar o produto. Escalando.", pay.PaymentId);
            return new PlaceOrderResult(PlaceOrderOutcome.NeedsHuman, null, "productKey ausente no pagamento");
        }

        var order = new Order(
            pay.PaymentId, pay.ProductKey, pay.Quantity, pay.Email, pay.ShipTo, pay.Amount, pay.Currency);

        // (2) Resolve the product. Paid for something we can't map = escalate, don't drop.
        var product = _catalog.Find(pay.ProductKey);
        if (product is null || !product.IsFulfillable)
        {
            var reason = product is null
                ? $"no catalog entry for productKey '{pay.ProductKey}'"
                : $"product '{pay.ProductKey}' has no supplier variant configured";
            return await FailAndEscalateAsync(order, reason, ct);
        }

        // (3) A paid order with an incomplete address can't ship — escalate.
        if (!pay.ShipTo.IsComplete)
            return await FailAndEscalateAsync(order, "shipping address is incomplete", ct);

        // Persist as Placed and confirm to the customer BEFORE touching the supplier, so the
        // buyer always gets a confirmation even if the supplier call needs a retry later.
        await _orders.AddAsync(order, ct);
        await SafeNotifyAsync(() => _notifier.NotifyOrderConfirmedAsync(order, ct), pay.PaymentId, "order-confirmed");

        // (4) Create the order at the supplier. Transient retries live in the client (Infra);
        // by here a Fail is a real failure we must escalate.
        SupplierOrderResult result;
        try
        {
            result = await _supplier.CreateOrderAsync(order.PaymentId, product, order.ShipTo, order.Quantity, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Supplier call threw for payment {PaymentId}.", pay.PaymentId);
            result = SupplierOrderResult.Fail($"supplier exception: {ex.Message}");
        }

        if (!result.Success || string.IsNullOrWhiteSpace(result.SupplierOrderId))
        {
            order.MarkFailed(result.Error ?? "supplier returned no order id");
            await _orders.UpdateAsync(order, ct);
            await SafeNotifyAsync(() => _notifier.AlertFulfillmentFailedAsync(order, ct), pay.PaymentId, "fulfillment-failed-alert");
            _log.LogError("Fulfillment FAILED for paid order {PaymentId}: {Error}", pay.PaymentId, order.FailureReason);
            return new PlaceOrderResult(PlaceOrderOutcome.NeedsHuman, order, order.FailureReason);
        }

        order.MarkPlacedAtSupplier(result.SupplierOrderId);
        await _orders.UpdateAsync(order, ct);
        _log.LogInformation("Order {PaymentId} placed at supplier as {SupplierOrderId}.", pay.PaymentId, result.SupplierOrderId);

        // Fire the server-side Purchase conversion (best-effort; deduped with the Pixel by PaymentId).
        await SafeNotifyAsync(() => _tracker.TrackPurchaseAsync(order, conversionContext, ct), pay.PaymentId, "conversion-purchase");

        return new PlaceOrderResult(PlaceOrderOutcome.Fulfilled, order);
    }

    private async Task<PlaceOrderResult> FailAndEscalateAsync(Order order, string reason, CancellationToken ct)
    {
        order.MarkFailed(reason);
        // Persist even the failed order so we have a record of a paid-but-unfulfilled purchase.
        if (!await _orders.ExistsAsync(order.PaymentId, ct))
            await _orders.AddAsync(order, ct);
        else
            await _orders.UpdateAsync(order, ct);
        await SafeNotifyAsync(() => _notifier.AlertFulfillmentFailedAsync(order, ct), order.PaymentId, "fulfillment-failed-alert");
        _log.LogError("Paid order {PaymentId} cannot be fulfilled: {Reason}", order.PaymentId, reason);
        return new PlaceOrderResult(PlaceOrderOutcome.NeedsHuman, order, reason);
    }

    // A failed notification must never fail the order — log and move on.
    private async Task SafeNotifyAsync(Func<Task> send, string paymentId, string kind)
    {
        try { await send(); }
        catch (Exception ex) { _log.LogWarning(ex, "Notification '{Kind}' failed for {PaymentId} (order still processed).", kind, paymentId); }
    }
}
