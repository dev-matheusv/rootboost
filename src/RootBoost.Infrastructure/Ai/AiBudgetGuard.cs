using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RootBoost.Application.Ai;
using RootBoost.Application.Alerts;
using RootBoost.Domain;
using RootBoost.Infrastructure.Persistence;

namespace RootBoost.Infrastructure.Ai;

public sealed class EfAiBudgetStore : IAiBudgetStore
{
    private readonly RootBoostDbContext _db;

    public EfAiBudgetStore(RootBoostDbContext db) => _db = db;

    public Task<AiBudget?> GetAsync(CancellationToken ct = default) =>
        _db.AiBudgets.FirstOrDefaultAsync(x => x.Key == AiBudget.GlobalKey, ct);

    public async Task SaveAsync(AiBudget budget, CancellationToken ct = default)
    {
        if (_db.Entry(budget).State == EntityState.Detached) _db.AiBudgets.Add(budget);
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Confere o teto do mês antes de cada chamada à IA (13/11). Passou do teto: recusa com mensagem clara. Passou da
/// porcentagem de aviso: abre um aviso no Console (um por mês). O gasto é relido no máximo a cada 30 s.
/// </summary>
public sealed class AiBudgetGuard : ILanguageModel
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private readonly ILanguageModel _inner;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AiCreditStatus? _status;
    private DateTimeOffset _readAt;

    public AiBudgetGuard(ILanguageModel inner, IServiceScopeFactory scopes, TimeProvider time)
    {
        _inner = inner;
        _scopes = scopes;
        _time = time;
    }

    /// <summary>Teto mudou no Console: relê na próxima chamada.</summary>
    public void Invalidate() => _status = null;

    public async Task<LanguageModelResult> GenerateJsonAsync(LanguageModelRequest request, CancellationToken ct = default)
    {
        var status = await StatusAsync(ct);
        if (status.Blocked)
            throw new AiRequestFailedException($"o teto mensal da IA (US$ {status.LimitUsd:0.00}) foi atingido: gasto de US$ {status.SpentUsd:0.00} no mês. " +
                "Aumente o teto em Visão geral, no cartão Crédito da IA");
        var result = await _inner.GenerateJsonAsync(request, ct);
        if (_status is not null) _status = _status with { SpentUsd = _status.SpentUsd + result.CostUsd };
        return result;
    }

    private async Task<AiCreditStatus> StatusAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        if (_status is not null && now - _readAt < CacheFor && !(_status.LimitUsd is { } l && _status.SpentUsd >= l)) return _status;
        await _lock.WaitAsync(ct);
        try
        {
            using var scope = _scopes.CreateScope();
            var status = await scope.ServiceProvider.GetRequiredService<AiCredit>().StatusAsync(ct);
            _status = status;
            _readAt = now;
            if (status.Percent is { } p && p >= status.AlertPercent)
            {
                var key = $"ai-budget:{status.MonthStart:yyyy-MM}";
                var message = status.Blocked
                    ? $"A IA parou: o gasto do mês (US$ {status.SpentUsd:0.00}) chegou ao teto de US$ {status.LimitUsd:0.00}."
                    : $"A IA já gastou {p:0}% do teto do mês (US$ {status.SpentUsd:0.00} de US$ {status.LimitUsd:0.00}).";
                await scope.ServiceProvider.GetRequiredService<AlertService>().RaiseAsync(AlertKind.AiBudget, key, null, message, ct);
            }
            return status;
        }
        finally
        {
            _lock.Release();
        }
    }
}
