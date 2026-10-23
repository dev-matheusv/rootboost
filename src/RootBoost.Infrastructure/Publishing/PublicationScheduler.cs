using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Publishing;

namespace RootBoost.Infrastructure.Publishing;

/// <summary>Roda o job de publicação a cada minuto. Erro numa rodada nunca derruba a API.</summary>
public sealed class PublicationScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<PublicationScheduler> _log;

    public PublicationScheduler(IServiceScopeFactory scopes, ILogger<PublicationScheduler> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<PublishDuePublications>().RunAsync(stoppingToken);
                if (result.Published + result.Failed + result.Retrying > 0)
                    _log.LogInformation("Publicação: {Published} publicadas, {Retrying} em nova tentativa, {Failed} falharam.",
                        result.Published, result.Retrying, result.Failed);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Rodada do agendador de publicação falhou.");
            }
        }
    }
}
