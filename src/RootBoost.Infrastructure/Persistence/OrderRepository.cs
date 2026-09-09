using Microsoft.EntityFrameworkCore;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Persistence;

public sealed class OrderRepository : IOrderRepository
{
    private readonly RootBoostDbContext _db;
    public OrderRepository(RootBoostDbContext db) => _db = db;

    public Task<bool> ExistsAsync(string paymentId, CancellationToken ct = default)
        => _db.Orders.AnyAsync(o => o.PaymentId == paymentId, ct);

    public Task<Order?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default)
        => _db.Orders.FirstOrDefaultAsync(o => o.PaymentId == paymentId, ct);

    public Task<Order?> GetBySupplierOrderIdAsync(string supplierOrderId, CancellationToken ct = default)
        => _db.Orders.FirstOrDefaultAsync(o => o.SupplierOrderId == supplierOrderId, ct);

    public async Task AddAsync(Order order, CancellationToken ct = default)
    {
        await _db.Orders.AddAsync(order, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        _db.Orders.Update(order);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Order>> ListAsync(int limit = 100, CancellationToken ct = default)
        => await _db.Orders.AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
}
