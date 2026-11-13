using RootBoost.Domain;

namespace RootBoost.Application.Ai;

public interface IAiBudgetStore
{
    Task<AiBudget?> GetAsync(CancellationToken ct = default);
    Task SaveAsync(AiBudget budget, CancellationToken ct = default);
}

/// <summary>Gasto da IA no mês corrente contra o teto.</summary>
public sealed record AiCreditStatus(
    DateTimeOffset MonthStart,
    decimal SpentUsd,
    decimal TodayUsd,
    int Calls,
    decimal? LimitUsd,
    int AlertPercent,
    decimal? Percent,
    decimal ProjectedUsd,
    bool Blocked,
    IReadOnlyList<AiUsageLine> ByPurpose,
    IReadOnlyList<AiUsageLine> ByBrand);

/// <summary>
/// Controle de crédito da IA (13/11, uso interno): soma o custo gravado de cada chamada no mês (UTC) e compara com o teto.
/// Passou do teto, a IA para com mensagem clara em vez de gastar até a conta da Anthropic zerar no meio de um vídeo.
/// </summary>
public sealed class AiCredit
{
    private readonly IAiUsageStore _usage;
    private readonly IAiBudgetStore _budgets;
    private readonly TimeProvider _time;

    public AiCredit(IAiUsageStore usage, IAiBudgetStore budgets, TimeProvider time)
    {
        _usage = usage;
        _budgets = budgets;
        _time = time;
    }

    public async Task<AiCreditStatus> StatusAsync(CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var rows = await _usage.ListSinceAsync(monthStart, ct);
        var budget = await _budgets.GetAsync(ct) ?? AiBudget.Default(now);
        var spent = rows.Sum(r => r.CostUsd);
        var today = rows.Where(r => r.CreatedAt >= new DateTimeOffset(now.Date, TimeSpan.Zero)).Sum(r => r.CostUsd);
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var elapsedDays = Math.Max((decimal)(now - monthStart).TotalDays, 1m);
        var projected = Math.Round(spent / elapsedDays * daysInMonth, 2);
        decimal? percent = budget.MonthlyLimitUsd is > 0 ? Math.Round(spent / budget.MonthlyLimitUsd.Value * 100, 1) : null;
        var blocked = budget.MonthlyLimitUsd is { } limit && spent >= limit;
        static List<AiUsageLine> By(IEnumerable<AiUsageEntry> r, Func<AiUsageEntry, string> key) =>
            r.GroupBy(key).Select(g => new AiUsageLine(g.Key, g.Sum(x => x.CostUsd), g.Count(), g.Sum(x => x.InputTokens), g.Sum(x => x.OutputTokens)))
             .OrderByDescending(l => l.CostUsd).ToList();
        return new AiCreditStatus(monthStart, Math.Round(spent, 4), Math.Round(today, 4), rows.Count, budget.MonthlyLimitUsd, budget.AlertPercent,
            percent, projected, blocked, By(rows, r => r.Purpose), By(rows, r => r.BrandKey ?? "geral"));
    }

    public async Task<AiBudget> SetAsync(decimal? monthlyLimitUsd, int alertPercent, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var budget = await _budgets.GetAsync(ct) ?? AiBudget.Default(now);
        budget.Set(monthlyLimitUsd, alertPercent, now);
        await _budgets.SaveAsync(budget, ct);
        return budget;
    }
}
