using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;

namespace RootBoost.Application.UseCases;

/// <summary>
/// Responde "qual produto converte mais": cruza visitas de landing (IVisitStore) com pedidos
/// pagos (IOrderRepository) e o catálogo, produto a produto, e rankeia do melhor pro pior.
/// Todo produto do catálogo aparece, mesmo com zero visitas, pra você ver a grade completa.
/// </summary>
public sealed class ProductPerformanceReport
{
    // Abaixo disto a conversão é ruído estatístico; sinalizamos EnoughData=false pra não escalar cedo.
    public const long MinViewsForSignal = 100;

    private readonly IVisitStore _visits;
    private readonly IOrderRepository _orders;
    private readonly IProductCatalog _catalog;

    public ProductPerformanceReport(IVisitStore visits, IOrderRepository orders, IProductCatalog catalog)
    {
        _visits = visits;
        _orders = orders;
        _catalog = catalog;
    }

    public async Task<PerformanceReport> BuildAsync(DateTimeOffset? since = null, CancellationToken ct = default)
    {
        var viewCounts = await _visits.GetViewCountsAsync(since, ct);
        var allOrders = await _orders.ListAsync(5000, ct);

        // Cada Order persistido é um pagamento capturado (venda). Failed = pago mas sem fulfillment,
        // ainda conta como conversão do visitante. Filtra por janela quando pedida.
        var orders = since is { } s
            ? allOrders.Where(o => o.CreatedAt >= s).ToList()
            : allOrders.ToList();

        var ordersByProduct = orders
            .GroupBy(o => o.ProductKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Universo de chaves: catálogo ∪ visitas ∪ pedidos (pega até produto legado sem catálogo).
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _catalog.All()) keys.Add(p.Key);
        foreach (var k in viewCounts.Keys) keys.Add(k);
        foreach (var k in ordersByProduct.Keys) keys.Add(k);

        var rows = new List<ProductPerformance>(keys.Count);
        foreach (var key in keys)
        {
            var product = _catalog.Find(key);
            long views = viewCounts.TryGetValue(key, out var v) ? v : 0;
            var productOrders = ordersByProduct.TryGetValue(key, out var list) ? list : new();
            int orderCount = productOrders.Count;
            decimal revenue = productOrders.Sum(o => o.AmountPaid);

            // Moeda: do catálogo; senão a do primeiro pedido; senão USD.
            string currency = product?.Currency
                ?? productOrders.FirstOrDefault()?.Currency
                ?? "USD";

            double conversion = views > 0 ? (double)orderCount / views : 0d;
            decimal revenuePerVisitor = views > 0 ? Math.Round(revenue / views, 4) : 0m;

            rows.Add(new ProductPerformance(
                ProductKey: key,
                DisplayName: product?.DisplayName,
                Views: views,
                Orders: orderCount,
                Revenue: revenue,
                Currency: currency,
                ConversionRate: conversion,
                RevenuePerVisitor: revenuePerVisitor,
                Fulfillable: product?.IsFulfillable ?? false,
                EnoughData: views >= MinViewsForSignal));
        }

        // Ranking: quem rende mais por visitante primeiro (dinheiro é o placar real), depois
        // conversão, depois volume de visitas. Produtos sem tração caem naturalmente pro fim.
        var ranked = rows
            .OrderByDescending(r => r.RevenuePerVisitor)
            .ThenByDescending(r => r.ConversionRate)
            .ThenByDescending(r => r.Views)
            .ThenBy(r => r.ProductKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PerformanceReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            Since: since,
            Products: ranked,
            Note: $"Rankeado por receita/visitante e conversão. EnoughData vira true com {MinViewsForSignal}+ visitas; " +
                  "abaixo disso a conversão é ruído, não escale ainda. Produtos com Fulfillable=false ainda estão em " +
                  "soft-launch (VID da CJ pendente): dá pra testar tráfego orgânico, mas um pedido pago cai em Failed.");
    }
}
