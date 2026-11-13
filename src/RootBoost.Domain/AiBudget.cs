namespace RootBoost.Domain;

/// <summary>
/// Teto de gasto da IA por mês, em dólar (13/11, uso interno). Hoje há um só, pra plataforma inteira; quando o modelo de
/// venda estiver definido, vira o limite de cada plano ou marca (o RootFlow já tem planos e pacotes de crédito).
/// </summary>
public sealed class AiBudget
{
    public const string GlobalKey = "global";

    public string Key { get; private set; } = default!;
    /// <summary>Nulo: sem teto (só mostra o gasto).</summary>
    public decimal? MonthlyLimitUsd { get; private set; }
    /// <summary>Avisa quando o gasto do mês passa desta porcentagem do teto.</summary>
    public int AlertPercent { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private AiBudget() { }

    public static AiBudget Default(DateTimeOffset now) => new() { Key = GlobalKey, AlertPercent = 80, UpdatedAt = now };

    public void Set(decimal? monthlyLimitUsd, int alertPercent, DateTimeOffset now)
    {
        if (monthlyLimitUsd is < 0 or > 100_000) throw new ArgumentOutOfRangeException(nameof(monthlyLimitUsd), "teto entre 0 e 100 mil dólares");
        MonthlyLimitUsd = monthlyLimitUsd;
        AlertPercent = Math.Clamp(alertPercent, 10, 100);
        UpdatedAt = now;
    }
}
