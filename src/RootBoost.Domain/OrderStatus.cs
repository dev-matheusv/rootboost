namespace RootBoost.Domain;

/// <summary>
/// Lifecycle of a fulfillment order. Moves forward only: PLACED -> PLACED_AT_SUPPLIER -> SHIPPED -> DELIVERED.
/// FAILED is terminal and means human intervention is required (paid but not fulfilled).
/// </summary>
public enum OrderStatus
{
    /// <summary>Payment confirmed and persisted, supplier order not yet created.</summary>
    Placed = 0,

    /// <summary>Order successfully created at the supplier (CJ). We hold their order id.</summary>
    PlacedAtSupplier = 1,

    /// <summary>Supplier reported a tracking number.</summary>
    Shipped = 2,

    /// <summary>Supplier/carrier reported delivery.</summary>
    Delivered = 3,

    /// <summary>Paid but fulfillment failed after retries. Needs a human. Money is at risk here.</summary>
    Failed = 9
}
