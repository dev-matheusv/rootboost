using RootBoost.Application.Models;
using RootBoost.Application.UseCases;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public class ProductPerformanceReportTests
{
    private static CatalogProduct Live(string key, decimal price = 29.99m) =>
        new(key, price, "USD", "CJ-VID-REAL", new[] { "en" }, null, $"{key} display");

    private static CatalogProduct Todo(string key, decimal price = 29.99m) =>
        new(key, price, "USD", "TODO_CJ_VID", new[] { "en" }, null, $"{key} display");

    private static Order PaidOrder(string paymentId, string key, decimal amount) =>
        new(paymentId, key, 1, "buyer@example.com", TestData.CompleteAddress(), amount, "USD");

    private static ProductPerformanceReport Sut(FakeVisitStore visits, FakeOrderRepository orders, FakeCatalog catalog)
        => new(visits, orders, catalog);

    [Fact]
    public async Task Computes_conversion_and_revenue_per_visitor()
    {
        var visits = new FakeVisitStore().Seed("carorganizer", 200);
        var orders = new FakeOrderRepository();
        orders.Store["P1"] = PaidOrder("P1", "carorganizer", 29.99m);
        orders.Store["P2"] = PaidOrder("P2", "carorganizer", 29.99m);

        var report = await Sut(visits, orders, new FakeCatalog(Live("carorganizer"))).BuildAsync();
        var car = report.Products.Single(p => p.ProductKey == "carorganizer");

        Assert.Equal(200, car.Views);
        Assert.Equal(2, car.Orders);
        Assert.Equal(59.98m, car.Revenue);
        Assert.Equal(0.01d, car.ConversionRate, 5);       // 2 / 200
        Assert.Equal(0.2999m, car.RevenuePerVisitor);      // 59.98 / 200
        Assert.True(car.Fulfillable);
        Assert.True(car.EnoughData);                       // 200 >= 100
    }

    [Fact]
    public async Task Zero_views_gives_zero_conversion_without_dividing_by_zero()
    {
        var report = await Sut(new FakeVisitStore(), new FakeOrderRepository(),
            new FakeCatalog(Todo("newproduct"))).BuildAsync();
        var p = report.Products.Single();

        Assert.Equal(0, p.Views);
        Assert.Equal(0d, p.ConversionRate);
        Assert.Equal(0m, p.RevenuePerVisitor);
        Assert.False(p.EnoughData);
        Assert.False(p.Fulfillable);                       // VID ainda é TODO
    }

    [Fact]
    public async Task Ranks_best_earner_per_visitor_first()
    {
        // A: 100 visitas, 5 vendas @ 20 = 100 receita -> 1.00/visitante
        // B: 100 visitas, 1 venda  @ 30 = 30 receita  -> 0.30/visitante
        var visits = new FakeVisitStore().Seed("a", 100).Seed("b", 100);
        var orders = new FakeOrderRepository();
        for (int i = 0; i < 5; i++) orders.Store[$"A{i}"] = PaidOrder($"A{i}", "a", 20m);
        orders.Store["B0"] = PaidOrder("B0", "b", 30m);

        var report = await Sut(visits, orders, new FakeCatalog(Live("a", 20m), Live("b", 30m))).BuildAsync();

        Assert.Equal("a", report.Products[0].ProductKey);
        Assert.Equal("b", report.Products[1].ProductKey);
        Assert.Equal(1.00m, report.Products[0].RevenuePerVisitor);
    }

    [Fact]
    public async Task Every_catalog_product_appears_even_with_no_traffic()
    {
        var visits = new FakeVisitStore().Seed("winner", 150);
        var orders = new FakeOrderRepository();
        orders.Store["W"] = PaidOrder("W", "winner", 29.99m);

        var report = await Sut(visits, orders,
            new FakeCatalog(Live("winner"), Todo("untested1"), Todo("untested2"))).BuildAsync();

        Assert.Equal(3, report.Products.Count);
        Assert.Contains(report.Products, p => p.ProductKey == "untested1" && p.Views == 0);
        Assert.Equal("winner", report.Products[0].ProductKey);   // único com tração fica no topo
    }

    [Fact]
    public async Task Small_sample_is_flagged_not_enough_data()
    {
        var visits = new FakeVisitStore().Seed("car", 30);   // < 100
        var orders = new FakeOrderRepository();
        orders.Store["X"] = PaidOrder("X", "car", 29.99m);

        var report = await Sut(visits, orders, new FakeCatalog(Live("car"))).BuildAsync();
        Assert.False(report.Products.Single().EnoughData);
    }
}
