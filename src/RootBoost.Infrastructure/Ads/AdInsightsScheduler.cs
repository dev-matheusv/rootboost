using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Ads;

namespace RootBoost.Infrastructure.Ads;

/// <summary>
/// Sincroniza as métricas de anúncio a cada 6 horas (relê os últimos 3 dias). Poucas chamadas por dia ficam
/// longe da quota da Meta. Erro numa rodada só vai pro log; a próxima rodada relê a janela.
/// </summary>
public sealed class AdInsightsScheduler : BackgroundService
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AdInsightsScheduler> _log;

    public AdInsightsScheduler(IServiceScopeFactory scopes, ILogger<AdInsightsScheduler> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRun, stoppingToken);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await WaitAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // desligando
        }
    }

    private static async Task<bool> WaitAsync(CancellationToken ct)
    {
        await Task.Delay(Interval, ct);
        return true;
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var results = await scope.ServiceProvider.GetRequiredService<SyncAdInsights>().RunAsync(SyncAdInsights.DefaultDays, ct);
            foreach (var r in results)
            {
                if (r.Ok)
                    _log.LogInformation("Anúncios {Brand} {Platform} {Account}: {Inserted} novos, {Updated} atualizados.",
                        r.BrandKey, r.Platform, r.AccountId, r.Inserted, r.Updated);
                else
                    _log.LogWarning("Anúncios {Brand} {Platform} {Account} falharam: {Error}", r.BrandKey, r.Platform, r.AccountId, r.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Rodada de sincronização de anúncios falhou.");
        }
    }
}
