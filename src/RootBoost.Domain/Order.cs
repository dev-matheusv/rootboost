namespace RootBoost.Domain;

/// <summary>
/// A paid order flowing through fulfillment. The aggregate root.
/// Identity is the payment id (PayPal capture id / Stripe session id) — that is also our
/// idempotency key, because payment webhooks retry and we must never double-fulfill.
/// </summary>
public sealed class Order
{
    // Properties use a private setter so EF Core maps them reliably; only the constructor and the
    // domain methods below ever mutate them, so the entity stays effectively immutable to callers.

    /// <summary>Payment id from the processor. Unique, stable, and our idempotency key.</summary>
    public string PaymentId { get; private set; }

    /// <summary>Catalog key of the product sold (e.g. "rack"). Resolves to a supplier variant.</summary>
    public string ProductKey { get; private set; }

    public int Quantity { get; private set; }

    public string CustomerEmail { get; private set; }

    public ShippingAddress ShipTo { get; private set; }

    /// <summary>Amount actually captured — we keep the decimal the processor reported so we can
    /// reconcile against the catalog price.</summary>
    public decimal AmountPaid { get; private set; }

    public string Currency { get; private set; }

    public OrderStatus Status { get; private set; }

    /// <summary>Supplier (CJ) order id, once created. Null until PlacedAtSupplier.</summary>
    public string? SupplierOrderId { get; private set; }

    /// <summary>Carrier tracking number, once the supplier reports it. Null until Shipped.</summary>
    public string? TrackingNumber { get; private set; }

    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public Order(
        string paymentId,
        string productKey,
        int quantity,
        string customerEmail,
        ShippingAddress shipTo,
        decimal amountPaid,
        string currency,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(paymentId)) throw new ArgumentException("paymentId required", nameof(paymentId));
        if (string.IsNullOrWhiteSpace(productKey)) throw new ArgumentException("productKey required", nameof(productKey));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));

        PaymentId = paymentId;
        ProductKey = productKey;
        Quantity = quantity;
        CustomerEmail = customerEmail;
        ShipTo = shipTo;
        AmountPaid = amountPaid;
        Currency = currency;
        Status = OrderStatus.Placed;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The supplier accepted the order. Records their id and advances status.</summary>
    public void MarkPlacedAtSupplier(string supplierOrderId)
    {
        if (string.IsNullOrWhiteSpace(supplierOrderId)) throw new ArgumentException("supplierOrderId required", nameof(supplierOrderId));
        SupplierOrderId = supplierOrderId;
        Status = OrderStatus.PlacedAtSupplier;
        Touch();
    }

    /// <summary>Tracking arrived from the supplier webhook. Idempotent — same number twice is a no-op.</summary>
    public void AttachTracking(string trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber)) throw new ArgumentException("trackingNumber required", nameof(trackingNumber));
        if (TrackingNumber == trackingNumber && Status == OrderStatus.Shipped) return;
        TrackingNumber = trackingNumber;
        Status = OrderStatus.Shipped;
        Touch();
    }

    public void MarkDelivered()
    {
        Status = OrderStatus.Delivered;
        Touch();
    }

    /// <summary>Fulfillment failed after retries. Terminal state that a human must resolve.</summary>
    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        Status = OrderStatus.Failed;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    // EF Core needs a parameterless ctor to materialize entities. Kept private so the domain
    // can only be constructed through the real constructor.
    private Order()
    {
        PaymentId = default!;
        ProductKey = default!;
        CustomerEmail = default!;
        ShipTo = default!;
        Currency = default!;
    }
}
