using System.Net;
using RootBoost.Application.Push;
using RootBoost.Domain;
using WebPush;

namespace RootBoost.Infrastructure.Push;

/// <summary>Web Push com VAPID (pacote WebPush). O navegador cuida da criptografia do lado dele; aqui só assinamos e mandamos.</summary>
public sealed class WebPushTransport : IPushTransport
{
    private readonly WebPushClient _client;
    private readonly PushOptions _options;

    public WebPushTransport(HttpClient http, PushOptions options)
    {
        _client = new WebPushClient(http);
        _options = options;
    }

    public async Task<PushResult> SendAsync(PushDevice device, string payload, CancellationToken ct = default)
    {
        var vapid = new VapidDetails(_options.Subject, _options.PublicKey, _options.PrivateKey);
        try
        {
            await _client.SendNotificationAsync(new PushSubscription(device.Endpoint, device.P256dh, device.Auth), payload, vapid, ct);
            return PushResult.Sent;
        }
        catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
        {
            return PushResult.Gone;
        }
        catch (WebPushException)
        {
            return PushResult.Failed;
        }
    }
}
