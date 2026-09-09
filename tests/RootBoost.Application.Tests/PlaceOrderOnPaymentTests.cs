using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.UseCases;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public class PlaceOrderOnPaymentTests
{
    private static PlaceOrderOnPayment Build(
        FakeOrderRepository repo, FakeCatalog catalog, FakeSupplier supplier, FakeNotifier notifier,
        FakeConversionTracker? tracker = null)
        => new(repo, catalog, supplier, notifier, tracker ?? new FakeConversionTracker(), NullLogger<PlaceOrderOnPayment>.Instance);

    [Fact]
    public async Task Paid_order_with_valid_product_is_placed_at_supplier()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier { SupplierOrderId = "CJ-999" };
        var notifier = new FakeNotifier();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct()), supplier, notifier);

        var result = await sut.HandleAsync(TestData.PaidRack());

        Assert.Equal(PlaceOrderOutcome.Fulfilled, result.Outcome);
        Assert.Equal(1, supplier.Calls);
        Assert.Equal(OrderStatus.PlacedAtSupplier, repo.Store["PAY-1"].Status);
        Assert.Equal("CJ-999", repo.Store["PAY-1"].SupplierOrderId);
        Assert.Equal(1, notifier.Confirmed);
    }

    [Fact]
    public async Task Duplicate_payment_is_idempotent_and_does_not_call_supplier_twice()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct()), supplier, new FakeNotifier());

        await sut.HandleAsync(TestData.PaidRack("PAY-DUP"));
        var second = await sut.HandleAsync(TestData.PaidRack("PAY-DUP"));

        Assert.Equal(PlaceOrderOutcome.AlreadyProcessed, second.Outcome);
        Assert.Equal(1, supplier.Calls); // not called on the duplicate
    }

    [Fact]
    public async Task ProductKey_maps_to_the_configured_supplier_variant()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct("CJ-VID-ABC")), supplier, new FakeNotifier());

        await sut.HandleAsync(TestData.PaidRack());

        Assert.Equal("CJ-VID-ABC", supplier.LastProduct!.SupplierVariantId);
    }

    [Fact]
    public async Task Unknown_product_escalates_to_human_and_never_calls_supplier()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier();
        var notifier = new FakeNotifier();
        var sut = Build(repo, new FakeCatalog(/* empty */), supplier, notifier);

        var result = await sut.HandleAsync(TestData.PaidRack("PAY-NOMAP"));

        Assert.Equal(PlaceOrderOutcome.NeedsHuman, result.Outcome);
        Assert.Equal(0, supplier.Calls);
        Assert.Equal(OrderStatus.Failed, repo.Store["PAY-NOMAP"].Status);
        Assert.Equal(1, notifier.Alerts);
    }

    [Fact]
    public async Task Product_with_placeholder_variant_is_not_fulfillable()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct("TODO_variant")), supplier, new FakeNotifier());

        var result = await sut.HandleAsync(TestData.PaidRack());

        Assert.Equal(PlaceOrderOutcome.NeedsHuman, result.Outcome);
        Assert.Equal(0, supplier.Calls);
    }

    [Fact]
    public async Task Supplier_failure_persists_order_as_failed_and_alerts_human()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier { ShouldSucceed = false };
        var notifier = new FakeNotifier();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct()), supplier, notifier);

        var result = await sut.HandleAsync(TestData.PaidRack("PAY-FAIL"));

        Assert.Equal(PlaceOrderOutcome.NeedsHuman, result.Outcome);
        Assert.Equal(OrderStatus.Failed, repo.Store["PAY-FAIL"].Status);
        Assert.Equal(1, notifier.Alerts);
        Assert.Equal(1, notifier.Confirmed); // customer was still confirmed before the supplier call
    }

    [Fact]
    public async Task Incomplete_address_escalates_without_calling_supplier()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct()), supplier, new FakeNotifier());

        var badAddress = new ShippingAddress("Jane", "", "", "", "", "US");
        var pay = TestData.PaidRack("PAY-BADADDR") with { ShipTo = badAddress };

        var result = await sut.HandleAsync(pay);

        Assert.Equal(PlaceOrderOutcome.NeedsHuman, result.Outcome);
        Assert.Equal(0, supplier.Calls);
    }

    [Fact]
    public async Task Fulfilled_order_fires_a_purchase_conversion()
    {
        var repo = new FakeOrderRepository();
        var tracker = new FakeConversionTracker();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct()), new FakeSupplier(), new FakeNotifier(), tracker);

        await sut.HandleAsync(TestData.PaidRack("PAY-CONV"));

        Assert.Equal(1, tracker.Purchases);
        Assert.Equal("PAY-CONV", tracker.LastOrder!.PaymentId);
    }

    [Fact]
    public async Task Failed_order_does_not_fire_a_purchase_conversion()
    {
        var repo = new FakeOrderRepository();
        var tracker = new FakeConversionTracker();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct()), new FakeSupplier { ShouldSucceed = false }, new FakeNotifier(), tracker);

        await sut.HandleAsync(TestData.PaidRack("PAY-CONV-FAIL"));

        Assert.Equal(0, tracker.Purchases);
    }

    [Fact]
    public async Task Non_payment_event_is_ignored()
    {
        var repo = new FakeOrderRepository();
        var supplier = new FakeSupplier();
        var sut = Build(repo, new FakeCatalog(TestData.RackProduct()), supplier, new FakeNotifier());

        var notPaid = TestData.PaidRack() with { IsPaid = false };
        var result = await sut.HandleAsync(notPaid);

        Assert.Equal(PlaceOrderOutcome.AlreadyProcessed, result.Outcome);
        Assert.Equal(0, supplier.Calls);
        Assert.Empty(repo.Store);
    }
}
