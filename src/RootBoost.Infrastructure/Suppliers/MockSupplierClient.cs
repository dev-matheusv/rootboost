using Microsoft.Extensions.Logging;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Suppliers;

/// <summary>
/// Dev/test supplier. Pretends to place an order and returns a fake supplier id, so the whole
/// flow (webhook -> order -> supplier -> GET /orders) is demoable without real CJ credentials.
/// Selected by config (Supplier=Mock) — never in production.
/// </summary>
public sealed class MockSupplierClient : ISupplierClient
{
    private readonly ILogger<MockSupplierClient> _log;
    public MockSupplierClient(ILogger<MockSupplierClient> log) => _log = log;

    public Task<SupplierOrderResult> CreateOrderAsync(
        string orderReference, CatalogProduct product, ShippingAddress shipTo, int quantity, CancellationToken ct = default)
    {
        var fakeId = $"MOCK-{orderReference}";
        _log.LogInformation("[MockSupplier] would place {Qty}x '{Product}' (vid {Vid}) to {Name}/{Country} -> {SupplierOrderId}",
            quantity, product.Key, product.SupplierVariantId, shipTo.Name, shipTo.CountryCode, fakeId);
        return Task.FromResult(SupplierOrderResult.Ok(fakeId));
    }
}
