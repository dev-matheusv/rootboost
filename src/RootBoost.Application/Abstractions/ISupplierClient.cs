using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Application.Abstractions;

/// <summary>The dropshipping supplier (CJ today, could be Zendrop tomorrow). Infra implements it.</summary>
public interface ISupplierClient
{
    /// <summary>
    /// Create the order at the supplier with the buyer's shipping address.
    /// Must not throw for expected failures (out of stock, bad variant) — return Fail instead,
    /// so the use case can mark the order Failed and alert a human rather than crash.
    /// </summary>
    Task<SupplierOrderResult> CreateOrderAsync(
        string orderReference,
        CatalogProduct product,
        ShippingAddress shipTo,
        int quantity,
        CancellationToken ct = default);
}
