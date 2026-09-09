namespace RootBoost.Application.Models;

/// <summary>Result of asking the supplier to create an order.</summary>
public sealed record SupplierOrderResult(bool Success, string? SupplierOrderId, string? Error)
{
    public static SupplierOrderResult Ok(string supplierOrderId) => new(true, supplierOrderId, null);
    public static SupplierOrderResult Fail(string error) => new(false, null, error);
}
