using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.Models;
using RootBoost.Application.UseCases;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public class BrandTests
{
    private static CatalogProduct Product(string key, string brand) =>
        new(key, 29.99m, "USD", $"VID-{key}", new[] { "pt" }, BrandKey: brand);

    [Fact]
    public async Task Paid_order_is_born_in_the_brand_of_its_product()
    {
        var repo = new FakeOrderRepository();
        var sut = new PlaceOrderOnPayment(repo, new FakeCatalog(Product("rack", BrandKeys.MedlyCare)),
            new FakeSupplier(), new FakeNotifier(), new FakeConversionTracker(), NullLogger<PlaceOrderOnPayment>.Instance);

        await sut.HandleAsync(TestData.PaidRack("PAY-BRAND"));

        Assert.Equal(BrandKeys.MedlyCare, repo.Store["PAY-BRAND"].BrandKey);
    }

    [Fact]
    public async Task Order_for_an_unknown_product_still_lands_in_the_store_and_is_escalated()
    {
        var repo = new FakeOrderRepository();
        var sut = new PlaceOrderOnPayment(repo, new FakeCatalog(), new FakeSupplier(), new FakeNotifier(),
            new FakeConversionTracker(), NullLogger<PlaceOrderOnPayment>.Instance);

        var result = await sut.HandleAsync(TestData.PaidRack("PAY-ORPHAN"));

        Assert.Equal(PlaceOrderOutcome.NeedsHuman, result.Outcome);
        Assert.Equal(BrandKeys.Store, repo.Store["PAY-ORPHAN"].BrandKey);
    }

    [Fact]
    public void Orders_created_before_brands_default_to_the_store()
    {
        var order = new Order("OLD", "rack", 1, "a@b.c",
            new ShippingAddress("A", "1 St", "Austin", "TX", "78701", "US"), 10m, "USD");
        Assert.Equal(BrandKeys.Store, order.BrandKey);
    }

    [Theory]
    [InlineData("MedlyCare", "medlycare")]
    [InlineData(" loja-2 ", "loja-2")]
    public void Brand_keys_are_normalized(string raw, string expected) =>
        Assert.Equal(expected, BrandKeys.Validate(raw));

    [Theory]
    [InlineData("")]
    [InlineData("com espaco")]
    [InlineData("-comeca-com-hifen")]
    [InlineData("acento-é")]
    public void Invalid_brand_keys_are_rejected(string raw) =>
        Assert.Throws<ArgumentException>(() => BrandKeys.Validate(raw));

    [Fact]
    public async Task Performance_report_filters_by_brand()
    {
        var orders = new FakeOrderRepository();
        var address = new ShippingAddress("A", "1 St", "Austin", "TX", "78701", "US");
        orders.Store["S1"] = new Order("S1", "loja-produto", 1, "a@b.c", address, 30m, "USD", brandKey: BrandKeys.Store);
        orders.Store["M1"] = new Order("M1", "medly-plano", 1, "a@b.c", address with { }, 99m, "BRL", brandKey: BrandKeys.MedlyCare);

        var report = new ProductPerformanceReport(new FakeVisitStore(), orders,
            new FakeCatalog(Product("loja-produto", BrandKeys.Store), Product("medly-plano", BrandKeys.MedlyCare)));

        var medly = await report.BuildAsync(brandKey: BrandKeys.MedlyCare);
        Assert.Equal(new[] { "medly-plano" }, medly.Products.Select(p => p.ProductKey));

        var all = await report.BuildAsync();
        Assert.Equal(2, all.Products.Count);
    }
}
