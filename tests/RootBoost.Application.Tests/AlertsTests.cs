using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.Ads;
using RootBoost.Application.Alerts;
using RootBoost.Application.Publishing;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public sealed class InMemoryAlertStore : IAlertStore
{
    public List<OpsAlert> Items { get; } = new();
    public Task<OpsAlert?> GetOpenAsync(string key, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(a => a.Key == key && a.IsOpen));
    public Task<OpsAlert?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(a => a.Id == id));
    public Task<IReadOnlyList<OpsAlert>> ListOpenAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OpsAlert>>(Items.Where(a => a.IsOpen).ToList());
    public Task AddAsync(OpsAlert alert, CancellationToken ct = default) { Items.Add(alert); return Task.CompletedTask; }
    public Task SaveAsync(OpsAlert alert, CancellationToken ct = default) => Task.CompletedTask;
}

public class AlertsTests
{
    private readonly ManualTime _time = new();
    private readonly InMemoryAlertStore _store = new();
    private AlertService Alerts() => new(_store, _time, NullLogger<AlertService>.Instance);

    [Fact]
    public async Task Same_problem_keeps_one_open_alert_and_resolving_closes_it()
    {
        var alerts = Alerts();
        await alerts.RaiseAsync(AlertKind.MetaConnection, "meta:medlycare:Instagram", BrandKeys.MedlyCare, "token vencido");
        _time.Now += TimeSpan.FromDays(1);
        await alerts.RaiseAsync(AlertKind.MetaConnection, "meta:medlycare:Instagram", BrandKeys.MedlyCare, "token vencido de novo");

        var open = Assert.Single(_store.Items);
        Assert.Equal(2, open.Count);
        Assert.Equal("token vencido de novo", open.Message);
        Assert.Equal(_time.Now, open.LastSeenAt);

        await alerts.ResolveAsync("meta:medlycare:Instagram");
        Assert.False(open.IsOpen);
        Assert.Equal("auto", open.ResolvedBy);

        await alerts.RaiseAsync(AlertKind.MetaConnection, "meta:medlycare:Instagram", BrandKeys.MedlyCare, "caiu outra vez");
        Assert.Equal(2, _store.Items.Count); // voltou depois de resolvido: aviso novo
    }

    [Fact]
    public async Task Post_that_exhausts_attempts_raises_an_alert_and_publishing_later_clears_it()
    {
        var pubs = new InMemoryPublicationStore();
        var publisher = new TestPublisher { FailNext = PublishDuePublications.MaxAttempts };
        var job = new PublishDuePublications(pubs, new[] { publisher }, _time, NullLogger<PublishDuePublications>.Instance, Alerts());
        var p = new Publication(BrandKeys.MedlyCare, SocialChannel.Instagram, "https://cdn.exemplo.com/a.jpg", "Agenda", _time.Now, _time.Now);
        p.Approve("dono@exemplo.com", _time.Now);
        pubs.Items.Add(p);

        for (var i = 0; i < PublishDuePublications.MaxAttempts - 1; i++) await job.RunAsync();
        Assert.Empty(_store.Items); // ainda tentando: sem aviso

        await job.RunAsync();
        var alert = Assert.Single(_store.Items);
        Assert.Equal(AlertKind.PublicationFailed, alert.Kind);
        Assert.Equal(AlertService.PublicationKey(p.Id), alert.Key);
        Assert.Contains("canal fora", alert.Message);

        p.EditCaption("Agenda nova", _time.Now);         // humano corrige e aprova de novo
        p.Approve("dono@exemplo.com", _time.Now);
        await job.RunAsync();
        Assert.Equal(PublicationStatus.Published, p.Status);
        Assert.False(alert.IsOpen);
    }

    [Fact]
    public async Task Ads_sync_failure_raises_per_account_and_success_clears()
    {
        var accounts = new AdsTests.Accounts(new AdAccount(BrandKeys.MedlyCare, "meta", "act_1"));
        var reader = new AdsTests.Reader("meta", fail: true);
        var sync = new SyncAdInsights(accounts, new[] { reader }, new AdsTests.Store(), _time,
            NullLogger<SyncAdInsights>.Instance, Alerts());

        await sync.RunAsync();
        var alert = Assert.Single(_store.Items);
        Assert.Equal(AlertKind.AdsSyncFailed, alert.Kind);
        Assert.Contains("limite da API", alert.Message);

        reader.Fail = false;
        await sync.RunAsync();
        Assert.False(alert.IsOpen);
    }
}
