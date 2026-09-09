using RootBoost.Domain;

namespace RootBoost.Application.Abstractions;

/// <summary>Persistence port for orders. EF Core implements it in Infrastructure.</summary>
public interface IOrderRepository
{
    /// <summary>Idempotency check: has this payment already been processed?</summary>
    Task<bool> ExistsAsync(string paymentId, CancellationToken ct = default);

    Task<Order?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default);

    /// <summary>Look up an order by the supplier's order id (used by the tracking webhook).</summary>
    Task<Order?> GetBySupplierOrderIdAsync(string supplierOrderId, CancellationToken ct = default);

    Task AddAsync(Order order, CancellationToken ct = default);

    Task UpdateAsync(Order order, CancellationToken ct = default);

    Task<IReadOnlyList<Order>> ListAsync(int limit = 100, CancellationToken ct = default);
}
