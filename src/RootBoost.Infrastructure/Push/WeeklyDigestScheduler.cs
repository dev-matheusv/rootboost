using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RootBoost.Application.Reports;

namespace RootBoost.Infrastructure.Push;

/// <summary>
/// Confere a cada 15 minutos se é segunda às 9h (Brasília) e manda o resumo da semana por push. Guarda a última semana
/// enviada em memória pra não repetir na mesma janela; um deploy dentro da janela pode repetir uma vez (aceitável).
/// </summary>
public sealed class WeeklyDigestScheduler : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<WeeklyDigestScheduler> _log;
    private DateOnly? _lastSent;

    public WeeklyDigestScheduler(IServiceScopeFactory scopes, TimeProvider time, ILogger<WeeklyDigestScheduler> log)
    {
        _scopes = scopes;
        _time = time;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = _time.GetUtcNow();
            var today = DateOnly.FromDateTime(now.UtcDateTime);
            if (!WeeklyDigest.IsDue(now) || _lastSent == today) continue;
            try
            {
                using var scope = _scopes.CreateScope();
                var sent = await scope.ServiceProvider.GetRequiredService<WeeklyDigest>().RunAsync(now, stoppingToken);
                _lastSent = today;
                _log.LogInformation("Resumo da semana enviado para {Count} marca(s).", sent);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Resumo da semana falhou.");
            }
        }
    }
}
