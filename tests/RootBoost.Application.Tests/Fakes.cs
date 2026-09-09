using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

/// <summary>In-memory order repo for tests.</summary>
public sealed class FakeOrderRepository : IOrderRepository
{
    public readonly Dictionary<string, Order> Store = new();

    public Task<bool> ExistsAsync(string paymentId, CancellationToken ct = default)
        => Task.FromResult(Store.ContainsKey(paymentId));

    public Task<Order?> GetByPaymentIdAsync(string paymentId, CancellationToken ct = default)
        => Task.FromResult(Store.TryGetValue(paymentId, out var o) ? o : null);

    public Task<Order?> GetBySupplierOrderIdAsync(string supplierOrderId, CancellationToken ct = default)
        => Task.FromResult(Store.Values.FirstOrDefault(o => o.SupplierOrderId == supplierOrderId));

    public Task AddAsync(Order order, CancellationToken ct = default) { Store[order.PaymentId] = order; return Task.CompletedTask; }

    public Task UpdateAsync(Order order, CancellationToken ct = default) { Store[order.PaymentId] = order; return Task.CompletedTask; }

    public Task<IReadOnlyList<Order>> ListAsync(int limit = 100, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Order>>(Store.Values.Take(limit).ToList());
}

public sealed class FakeCatalog : IProductCatalog
{
    private readonly Dictionary<string, CatalogProduct> _byKey;
    public FakeCatalog(params CatalogProduct[] products)
        => _byKey = products.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);

    public CatalogProduct? Find(string productKey) => _byKey.TryGetValue(productKey, out var p) ? p : null;
    public IReadOnlyList<CatalogProduct> All() => _byKey.Values.ToList();
}

public sealed class FakeSupplier : ISupplierClient
{
    public int Calls;
    public bool ShouldSucceed = true;
    public string SupplierOrderId = "CJ-123";
    public string? LastReference;
    public CatalogProduct? LastProduct;

    public Task<SupplierOrderResult> CreateOrderAsync(
        string orderReference, CatalogProduct product, ShippingAddress shipTo, int quantity, CancellationToken ct = default)
    {
        Calls++;
        LastReference = orderReference;
        LastProduct = product;
        return Task.FromResult(ShouldSucceed
            ? SupplierOrderResult.Ok(SupplierOrderId)
            : SupplierOrderResult.Fail("out of stock"));
    }
}

public sealed class FakeNotifier : INotifier
{
    public int Confirmed, Tracking, Alerts;
    public Task NotifyOrderConfirmedAsync(Order order, CancellationToken ct = default) { Confirmed++; return Task.CompletedTask; }
    public Task NotifyTrackingAsync(Order order, CancellationToken ct = default) { Tracking++; return Task.CompletedTask; }
    public Task AlertFulfillmentFailedAsync(Order order, CancellationToken ct = default) { Alerts++; return Task.CompletedTask; }
}

public sealed class FakeConversionTracker : IConversionTracker
{
    public int Purchases;
    public Order? LastOrder;
    public Task TrackPurchaseAsync(Order order, CancellationToken ct = default)
    { Purchases++; LastOrder = order; return Task.CompletedTask; }
}

public static class TestData
{
    public static ShippingAddress CompleteAddress() =>
        new("Jane Buyer", "123 Main St", "Austin", "TX", "78701", "US", "+15551234567");

    public static CatalogProduct RackProduct(string variant = "CJ-VID-RACK") =>
        new("rack", 34.99m, "USD", variant, new[] { "en", "es", "pt" });

    public static PaymentEvent PaidRack(string paymentId = "PAY-1") =>
        new(true, paymentId, "rack", 1, "jane@example.com", CompleteAddress(), 34.99m, "USD");
}
