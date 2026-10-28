using Microsoft.Extensions.Logging;
using RootBoost.Domain;

namespace RootBoost.Application.Alerts;

public interface IAlertStore
{
    Task<OpsAlert?> GetOpenAsync(string key, CancellationToken ct = default);
    Task<OpsAlert?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<OpsAlert>> ListOpenAsync(CancellationToken ct = default);
    Task AddAsync(OpsAlert alert, CancellationToken ct = default);
    Task SaveAsync(OpsAlert alert, CancellationToken ct = default);
}

/// <summary>
/// Abre, atualiza e fecha avisos de operação (RB-58). Hoje o aviso vai pro log e pra tela; a push da PWA (RB-37)
/// lê daqui depois. Falha ao gravar aviso nunca derruba quem chamou: só vai pro log.
/// </summary>
public sealed class AlertService
{
    private readonly IAlertStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<AlertService> _log;

    public AlertService(IAlertStore store, TimeProvider time, ILogger<AlertService> log)
    {
        _store = store;
        _time = time;
        _log = log;
    }

    public async Task RaiseAsync(AlertKind kind, string key, string? brandKey, string message, CancellationToken ct = default)
    {
        try
        {
            var now = _time.GetUtcNow();
            if (await _store.GetOpenAsync(key, ct) is { } open)
            {
                open.SeenAgain(message, now);
                await _store.SaveAsync(open, ct);
                return;
            }
            await _store.AddAsync(new OpsAlert(kind, key, brandKey, message, now), ct);
            _log.LogWarning("AVISO {Kind} ({Key}): {Message}", kind, key, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Não consegui gravar o aviso {Key}: {Message}", key, message);
        }
    }

    /// <summary>O problema sumiu (post publicou, sync voltou, conexão ok): fecha o aviso aberto, se houver.</summary>
    public async Task ResolveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            if (await _store.GetOpenAsync(key, ct) is not { } open) return;
            open.Resolve("auto", _time.GetUtcNow());
            await _store.SaveAsync(open, ct);
            _log.LogInformation("Aviso {Key} resolvido sozinho.", key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Não consegui fechar o aviso {Key}.", key);
        }
    }

    public static string PublicationKey(Guid publicationId) => $"pub:{publicationId:N}";
    public static string AdsKey(string platform, string accountId) => $"ads:{platform.ToLowerInvariant()}:{accountId}";
    public static string MetaKey(string brandKey, SocialChannel channel) => $"meta:{brandKey}:{channel}";
}
