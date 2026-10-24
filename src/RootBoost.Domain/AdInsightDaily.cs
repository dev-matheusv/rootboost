namespace RootBoost.Domain;

public enum AdLevel
{
    Campaign,
    AdSet,
    Ad
}

/// <summary>
/// Métricas de um objeto de anúncio (campanha, conjunto ou anúncio) num dia. Uma linha por
/// (plataforma, conta, nível, objeto, dia): rodar a sincronização de novo atualiza, nunca duplica.
/// A plataforma corrige números dos últimos dias, por isso o job relê uma janela e sobrescreve.
/// </summary>
public sealed class AdInsightDaily
{
    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public string Platform { get; private set; } = default!;
    public string AccountId { get; private set; } = default!;
    public AdLevel Level { get; private set; }
    public string ObjectId { get; private set; } = default!;
    public string ObjectName { get; private set; } = default!;
    public string? CampaignId { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Spend { get; private set; }
    public long Impressions { get; private set; }
    public long Clicks { get; private set; }
    public long Reach { get; private set; }
    public long Conversions { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTimeOffset FetchedAt { get; private set; }

    private AdInsightDaily() { }

    public AdInsightDaily(string brandKey, string platform, string accountId, AdLevel level, string objectId,
        string objectName, string? campaignId, DateOnly date, string currency, DateTimeOffset fetchedAt)
    {
        if (string.IsNullOrWhiteSpace(platform)) throw new ArgumentException("plataforma obrigatória", nameof(platform));
        if (string.IsNullOrWhiteSpace(accountId)) throw new ArgumentException("conta obrigatória", nameof(accountId));
        if (string.IsNullOrWhiteSpace(objectId)) throw new ArgumentException("objeto obrigatório", nameof(objectId));

        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Platform = platform.Trim().ToLowerInvariant();
        AccountId = accountId.Trim();
        Level = level;
        ObjectId = objectId.Trim();
        ObjectName = objectName?.Trim() ?? string.Empty;
        CampaignId = campaignId;
        Date = date;
        Currency = NormalizeCurrency(currency);
        FetchedAt = fetchedAt;
    }

    /// <summary>Grava os números do dia. Valor negativo é dado corrompido: recusa em vez de somar errado.</summary>
    public void SetMetrics(string objectName, decimal spend, long impressions, long clicks, long reach, long conversions,
        string currency, DateTimeOffset fetchedAt)
    {
        if (spend < 0 || impressions < 0 || clicks < 0 || reach < 0 || conversions < 0)
            throw new ArgumentException($"métrica negativa em {ObjectId} {Date:yyyy-MM-dd}");
        if (!string.IsNullOrWhiteSpace(objectName)) ObjectName = objectName.Trim();
        Spend = spend;
        Impressions = impressions;
        Clicks = clicks;
        Reach = reach;
        Conversions = conversions;
        Currency = NormalizeCurrency(currency);
        FetchedAt = fetchedAt;
    }

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3
            ? throw new ArgumentException("moeda precisa do código de 3 letras", nameof(currency))
            : currency.Trim().ToUpperInvariant();
}
