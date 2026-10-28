using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Push;

namespace RootBoost.Infrastructure.Push;

/// <summary>A cada 15 minutos lembra de post que sai em menos de 24 h sem aprovação (RB-37). Erro nunca derruba a API.</summary>
public sealed class ApprovalReminderScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ApprovalReminderScheduler> _log;

    public ApprovalReminderScheduler(IServiceScopeFactory scopes, ILogger<ApprovalReminderScheduler> log)
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
                var reminded = await scope.ServiceProvider.GetRequiredService<RemindPendingApprovals>().RunAsync(stoppingToken);
                if (reminded > 0) _log.LogInformation("Lembrete de aprovação enviado para {Count} post(s).", reminded);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Rodada do lembrete de aprovação falhou.");
            }
        }
    }
}
