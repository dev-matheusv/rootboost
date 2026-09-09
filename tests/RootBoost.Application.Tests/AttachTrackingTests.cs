using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.UseCases;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public class AttachTrackingTests
{
    private static async Task<(FakeOrderRepository repo, Order order)> SeedPlacedOrder(string supplierId = "CJ-777")
    {
        var repo = new FakeOrderRepository();
        var order = new Order("PAY-T", "rack", 1, "jane@example.com", TestData.CompleteAddress(), 34.99m, "USD");
        order.MarkPlacedAtSupplier(supplierId);
        await repo.AddAsync(order);
        return (repo, order);
    }

    [Fact]
    public async Task Attaches_tracking_and_notifies_customer()
    {
        var (repo, _) = await SeedPlacedOrder();
        var notifier = new FakeNotifier();
        var sut = new AttachTracking(repo, notifier, NullLogger<AttachTracking>.Instance);

        var result = await sut.HandleAsync("CJ-777", "TRACK-abc");

        Assert.Equal(AttachTrackingOutcome.Attached, result.Outcome);
        Assert.Equal("TRACK-abc", repo.Store["PAY-T"].TrackingNumber);
        Assert.Equal(OrderStatus.Shipped, repo.Store["PAY-T"].Status);
        Assert.Equal(1, notifier.Tracking);
    }

    [Fact]
    public async Task Same_tracking_twice_does_not_notify_twice()
    {
        var (repo, _) = await SeedPlacedOrder();
        var notifier = new FakeNotifier();
        var sut = new AttachTracking(repo, notifier, NullLogger<AttachTracking>.Instance);

        await sut.HandleAsync("CJ-777", "TRACK-abc");
        await sut.HandleAsync("CJ-777", "TRACK-abc");

        Assert.Equal(1, notifier.Tracking);
    }

    [Fact]
    public async Task Unknown_supplier_order_is_reported_not_found()
    {
        var (repo, _) = await SeedPlacedOrder();
        var sut = new AttachTracking(repo, new FakeNotifier(), NullLogger<AttachTracking>.Instance);

        var result = await sut.HandleAsync("CJ-DOES-NOT-EXIST", "TRACK-x");

        Assert.Equal(AttachTrackingOutcome.OrderNotFound, result.Outcome);
    }
}
