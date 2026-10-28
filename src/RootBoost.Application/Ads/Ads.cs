using Microsoft.Extensions.Logging;
using RootBoost.Domain;

namespace RootBoost.Application.Ads;

/// <summary>Conta de anúncios ligada a uma marca (ex.: a conta Meta do MedlyCare).</summary>
/// <param name="ConversionAction">Qual ação conta como conversão (ex.: cadastro pelo Pixel). Nulo = padrão do leitor.</param>
public sealed record AdAccount(string BrandKey, string Platform, string AccountId, string? ConversionAction = null);

/// <summary>Uma linha de métrica diária como a plataforma devolve, já traduzida pro nosso formato.</summary>
public sealed record AdInsightRow(
    AdLevel Level,
    string ObjectId,
    string ObjectName,
    string? CampaignId,
    DateOnly Date,
    decimal Spend,
    long Impressions,
    long Clicks,
    long Reach,
    long Conversions,
    string Currency);

/// <summary>Lê métricas diárias de uma plataforma de anúncios. Só leitura: nada aqui mexe em campanha.</summary>
public interface IAdInsightsReader
{
    string Platform { get; }
    Task<IReadOnlyList<AdInsightRow>> ReadDailyAsync(AdAccount account, DateOnly since, DateOnly until, CancellationToken ct = default);
}

/// <summary>Contas configuradas (vêm da config, nunca do navegador).</summary>
public interface IAdAccountDirectory
{
    IReadOnlyList<AdAccount> All { get; }
    IReadOnlyList<AdAccount> ForBrand(string brandKey) => All.Where(a => a.BrandKey == brandKey).ToList();
}

public sealed record UpsertResult(int Inserted, int Updated);

public interface IAdInsightStore
{
    /// <summary>Grava ou atualiza por (plataforma, conta, nível, objeto, dia). Idempotente.</summary>
    Task<UpsertResult> UpsertAsync(AdAccount account, IReadOnlyList<AdInsightRow> rows, DateTimeOffset fetchedAt, CancellationToken ct = default);

    Task<IReadOnlyList<AdInsightDaily>> ListAsync(string brandKey, DateOnly since, DateOnly until, AdLevel level, CancellationToken ct = default);
}

public sealed record AccountSyncResult(string BrandKey, string Platform, string AccountId, bool Ok, int Inserted, int Updated, string? Error);

/// <summary>
/// Relê os últimos dias de cada conta e grava o snapshot. A plataforma ajusta números recentes
/// (atribuição atrasada), então reler e sobrescrever é o comportamento certo. Falha numa conta
/// (limite da API, token vencido) vira resultado com erro e não impede as outras.
/// </summary>
public sealed class SyncAdInsights
{
    public const int DefaultDays = 3;

    private readonly IAdAccountDirectory _accounts;
    private readonly IReadOnlyDictionary<string, IAdInsightsReader> _readers;
    private readonly IAdInsightStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger<SyncAdInsights> _log;
    private readonly RootBoost.Application.Alerts.AlertService? _alerts;

    public SyncAdInsights(IAdAccountDirectory accounts, IEnumerable<IAdInsightsReader> readers, IAdInsightStore store,
        TimeProvider time, ILogger<SyncAdInsights> log, RootBoost.Application.Alerts.AlertService? alerts = null)
    {
        _alerts = alerts;
        _accounts = accounts;
        _readers = readers.ToDictionary(r => r.Platform, StringComparer.OrdinalIgnoreCase);
        _store = store;
        _time = time;
        _log = log;
    }

    public async Task<IReadOnlyList<AccountSyncResult>> RunAsync(int days = DefaultDays, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var now = _time.GetUtcNow();
        var until = DateOnly.FromDateTime(now.UtcDateTime);
        var since = until.AddDays(-(days - 1));
        var results = new List<AccountSyncResult>();

        foreach (var account in _accounts.All)
        {
            if (!_readers.TryGetValue(account.Platform, out var reader))
            {
                results.Add(new(account.BrandKey, account.Platform, account.AccountId, false, 0, 0,
                    $"nenhum leitor configurado pra {account.Platform}"));
                continue;
            }

            try
            {
                var rows = await reader.ReadDailyAsync(account, since, until, ct);
                var upsert = await _store.UpsertAsync(account, rows, now, ct);
                results.Add(new(account.BrandKey, account.Platform, account.AccountId, true, upsert.Inserted, upsert.Updated, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Falha ao sincronizar anúncios de {Brand} ({Platform} {Account}).",
                    account.BrandKey, account.Platform, account.AccountId);
                results.Add(new(account.BrandKey, account.Platform, account.AccountId, false, 0, 0, ex.Message));
            }
        }

        if (_alerts is not null)
            foreach (var r in results)
            {
                var key = RootBoost.Application.Alerts.AlertService.AdsKey(r.Platform, r.AccountId);
                if (r.Ok) await _alerts.ResolveAsync(key, ct);
                else await _alerts.RaiseAsync(RootBoost.Domain.AlertKind.AdsSyncFailed, key, r.BrandKey,
                    $"Métricas de anúncio ({r.Platform} {r.AccountId}) não sincronizaram: {r.Error}", ct);
            }

        return results;
    }
}
