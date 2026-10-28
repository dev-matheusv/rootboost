using System.Text.Json;
using Microsoft.Extensions.Logging;
using RootBoost.Domain;

namespace RootBoost.Application.Push;

public interface IPushDeviceStore
{
    Task<PushDevice?> GetByEndpointAsync(string endpoint, CancellationToken ct = default);
    Task<IReadOnlyList<PushDevice>> ListAsync(CancellationToken ct = default);
    Task AddAsync(PushDevice device, CancellationToken ct = default);
    Task SaveAsync(PushDevice device, CancellationToken ct = default);
    Task RemoveAsync(PushDevice device, CancellationToken ct = default);
}

public enum PushResult
{
    Sent,
    /// <summary>O serviço de push disse que a inscrição acabou (404/410): o aparelho sai da lista.</summary>
    Gone,
    Failed,
}

/// <summary>Entrega de fato (Web Push com VAPID na Infrastructure). Separado pra testar o <see cref="PushNotifier"/>.</summary>
public interface IPushTransport
{
    Task<PushResult> SendAsync(PushDevice device, string payload, CancellationToken ct = default);
}

/// <summary>
/// Chaves VAPID e o contato que o serviço de push exige ("mailto:..." ou uma URL https). Sem as três, o push fica
/// desligado e nada é enviado.
/// </summary>
public sealed class PushOptions
{
    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }
    public string? Subject { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey) && !string.IsNullOrWhiteSpace(Subject);
}

/// <summary>O que aparece no celular. A URL é a tela do Console que resolve a pendência.</summary>
public sealed record PushMessage(string Title, string Body, string Url, string Tag);

/// <summary>
/// Manda um aviso pra todos os aparelhos inscritos (RB-37). Nunca lança: aviso que não sai não pode derrubar
/// quem chamou (o post, o job, o alerta). Aparelho que o serviço diz que acabou sai da lista; o que falha
/// muitas vezes seguidas também.
/// </summary>
public sealed class PushNotifier
{
    private const int MaxFailures = 5;
    // Acento sem escapar: o JSON vai direto pro service worker (não é HTML) e o push tem limite de 4 KB.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly IPushDeviceStore _devices;
    private readonly IPushTransport _transport;
    private readonly PushOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<PushNotifier> _log;

    public PushNotifier(IPushDeviceStore devices, IPushTransport transport, PushOptions options, TimeProvider time, ILogger<PushNotifier> log)
    {
        _devices = devices;
        _transport = transport;
        _options = options;
        _time = time;
        _log = log;
    }

    public bool Enabled => _options.Enabled;

    public async Task<int> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        if (!_options.Enabled) return 0;
        var sent = 0;
        try
        {
            var payload = JsonSerializer.Serialize(message, Json);
            foreach (var device in await _devices.ListAsync(ct))
            {
                var result = await SafeSend(device, payload, ct);
                if (result == PushResult.Sent)
                {
                    device.Sent(_time.GetUtcNow());
                    await _devices.SaveAsync(device, ct);
                    sent++;
                    continue;
                }
                device.Failed();
                if (result == PushResult.Gone || device.Failures >= MaxFailures) await _devices.RemoveAsync(device, ct);
                else await _devices.SaveAsync(device, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Falha ao enviar push '{Title}'", message.Title);
        }
        return sent;
    }

    private async Task<PushResult> SafeSend(PushDevice device, string payload, CancellationToken ct)
    {
        try
        {
            return await _transport.SendAsync(device, payload, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Push não saiu pro aparelho {Id}", device.Id);
            return PushResult.Failed;
        }
    }
}
