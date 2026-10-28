using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.Alerts;
using RootBoost.Application.Push;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public sealed class InMemoryPushDeviceStore : IPushDeviceStore
{
    public List<PushDevice> Items { get; } = new();
    public Task<PushDevice?> GetByEndpointAsync(string endpoint, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(d => d.Endpoint == endpoint));
    public Task<IReadOnlyList<PushDevice>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PushDevice>>(Items.ToList());
    public Task AddAsync(PushDevice device, CancellationToken ct = default) { Items.Add(device); return Task.CompletedTask; }
    public Task SaveAsync(PushDevice device, CancellationToken ct = default) => Task.CompletedTask;
    public Task RemoveAsync(PushDevice device, CancellationToken ct = default) { Items.Remove(device); return Task.CompletedTask; }
}

/// <summary>Responde por endereço: "gone" some, "boom" lança, o resto entrega.</summary>
public sealed class TestPushTransport : IPushTransport
{
    public List<(string Endpoint, string Payload)> Sent { get; } = new();

    public Task<PushResult> SendAsync(PushDevice device, string payload, CancellationToken ct = default)
    {
        if (device.Endpoint.Contains("gone")) return Task.FromResult(PushResult.Gone);
        if (device.Endpoint.Contains("boom")) throw new HttpRequestException("rede caiu");
        Sent.Add((device.Endpoint, payload));
        return Task.FromResult(PushResult.Sent);
    }
}

public class PushTests
{
    private readonly ManualTime _time = new();
    private readonly InMemoryPushDeviceStore _devices = new();
    private readonly TestPushTransport _transport = new();
    private static readonly PushOptions On = new() { PublicKey = "pub", PrivateKey = "priv", Subject = "mailto:dono@exemplo.com" };

    private PushNotifier Notifier(PushOptions? options = null) =>
        new(_devices, _transport, options ?? On, _time, NullLogger<PushNotifier>.Instance);

    private void Device(string endpoint) => _devices.Items.Add(new PushDevice(endpoint, "p256", "auth", "dono@exemplo.com", _time.Now));

    [Fact]
    public async Task Sends_to_every_device_and_drops_the_ones_that_are_gone()
    {
        Device("https://push.exemplo.com/ok");
        Device("https://push.exemplo.com/gone");
        var sent = await Notifier().SendAsync(new PushMessage("Título", "Corpo", "/studio", "t"));
        Assert.Equal(1, sent);
        Assert.Contains("\"url\":\"/studio\"", Assert.Single(_transport.Sent).Payload);
        Assert.DoesNotContain(_devices.Items, d => d.Endpoint.Contains("gone"));
    }

    [Fact]
    public async Task Network_errors_never_throw_and_repeated_failures_remove_the_device()
    {
        Device("https://push.exemplo.com/boom");
        for (var i = 0; i < 4; i++) Assert.Equal(0, await Notifier().SendAsync(new PushMessage("a", "b", "/", "t")));
        Assert.Single(_devices.Items);
        await Notifier().SendAsync(new PushMessage("a", "b", "/", "t"));
        Assert.Empty(_devices.Items);
    }

    [Fact]
    public async Task Without_keys_nothing_is_sent()
    {
        Device("https://push.exemplo.com/ok");
        Assert.Equal(0, await Notifier(new PushOptions()).SendAsync(new PushMessage("a", "b", "/", "t")));
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task Only_a_new_alert_goes_to_the_phone()
    {
        Device("https://push.exemplo.com/ok");
        var alerts = new AlertService(new InMemoryAlertStore(), _time, NullLogger<AlertService>.Instance, Notifier());
        await alerts.RaiseAsync(AlertKind.PublicationFailed, "pub:1", "medlycare", "Instagram recusou o vídeo");
        await alerts.RaiseAsync(AlertKind.PublicationFailed, "pub:1", "medlycare", "Instagram recusou o vídeo");
        Assert.Contains("Instagram recusou o vídeo", Assert.Single(_transport.Sent).Payload);
    }

    [Fact]
    public void Device_needs_https_endpoint_and_keys()
    {
        Assert.Throws<ArgumentException>(() => new PushDevice("http://push.exemplo.com/x", "p", "a", "e", _time.Now));
        Assert.Throws<ArgumentException>(() => new PushDevice("https://push.exemplo.com/x", "", "a", "e", _time.Now));
    }

    [Fact]
    public async Task Reminds_once_per_group_of_channels_and_again_after_rescheduling()
    {
        Device("https://push.exemplo.com/ok");
        var store = new InMemoryPublicationStore();
        var when = _time.Now.AddHours(5);
        Publication Draft(SocialChannel ch, DateTimeOffset at) =>
            new(BrandKeys.MedlyCare, ch, "https://cdn.exemplo.com/v.mp4", "R1 pacote completo\nresto", at, _time.Now);
        var ig = Draft(SocialChannel.Instagram, when);
        var fb = Draft(SocialChannel.Facebook, when);
        var later = Draft(SocialChannel.Instagram, _time.Now.AddDays(3)); // fora da janela
        var approved = Draft(SocialChannel.LinkedIn, when.AddHours(1));
        approved.Approve("dono@exemplo.com", _time.Now);
        store.Items.AddRange([ig, fb, later, approved]);
        var remind = new RemindPendingApprovals(store, Notifier(), _time);

        Assert.Equal(1, await remind.RunAsync());
        var sent = Assert.Single(_transport.Sent).Payload;
        Assert.Contains("Falta aprovar", sent);
        Assert.Contains("R1 pacote completo (Instagram, Facebook)", sent);
        Assert.Equal(0, await remind.RunAsync()); // não repete

        ig.Reschedule(when.AddHours(2), _time.Now);
        Assert.Equal(1, await remind.RunAsync()); // data nova, lembra de novo
    }

    [Fact]
    public async Task Reminder_waits_while_push_is_off()
    {
        var store = new InMemoryPublicationStore();
        var p = new Publication(BrandKeys.MedlyCare, SocialChannel.Instagram, "https://cdn.exemplo.com/v.mp4", "Post", _time.Now.AddHours(2), _time.Now);
        store.Items.Add(p);
        Assert.Equal(0, await new RemindPendingApprovals(store, Notifier(new PushOptions()), _time).RunAsync());
        Assert.Null(p.ReminderSentAt);
    }

    [Fact]
    public void Brasilia_time_in_the_text()
    {
        Assert.Equal("ter 12:00", RemindPendingApprovals.Local(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero)));
    }
}
