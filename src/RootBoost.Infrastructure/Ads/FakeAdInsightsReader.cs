using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Ads;

/// <summary>
/// Leitor de mentira (Ads:Reader=Fake): números determinísticos por (anúncio, dia), sem rede nenhuma.
/// Duas campanhas: uma de cadastro que converte e uma de remarketing que gasta sem converter,
/// pra a tela e a regra de parada terem o que mostrar em dev, nos testes e no CI.
/// </summary>
public sealed class FakeAdInsightsReader : IAdInsightsReader
{
    public const string FakePlatform = "meta";

    private static readonly (string Id, string Name, (string Id, string Name)[] Ads, bool Converts)[] Campaigns =
    {
        ("fake-cmp-cadastro", "Cadastro teste grátis | Fisioterapia",
            new[] { ("fake-ad-cadastro-1", "Reel agenda cheia"), ("fake-ad-cadastro-2", "Reel falta zero") }, true),
        ("fake-cmp-remarketing", "Remarketing | Visitantes do site",
            new[] { ("fake-ad-remarketing-1", "Carrossel recursos"), ("fake-ad-remarketing-2", "Depoimento placeholder") }, false),
    };

    public string Platform => FakePlatform;

    public Task<IReadOnlyList<AdInsightRow>> ReadDailyAsync(AdAccount account, DateOnly since, DateOnly until, CancellationToken ct = default)
    {
        var rows = new List<AdInsightRow>();
        for (var day = since; day <= until; day = day.AddDays(1))
        {
            foreach (var (campaignId, campaignName, ads, converts) in Campaigns)
            {
                var adRows = ads.Select(ad =>
                {
                    var seed = Hash($"{account.AccountId}|{ad.Id}|{day:yyyy-MM-dd}");
                    var spend = 12m + seed % 2300 / 100m;                 // R$ 12,00 a R$ 34,99 por anúncio
                    var impressions = 900 + (long)(seed % 1700);
                    var clicks = impressions * (8 + (long)(seed % 17)) / 1000; // CTR 0,8% a 2,4%
                    var reach = impressions * 82 / 100;
                    var conversions = converts ? (long)(seed / 7 % 3) : 0;
                    return new AdInsightRow(AdLevel.Ad, ad.Id, ad.Name, campaignId, day, spend, impressions, clicks, reach, conversions, "BRL");
                }).ToList();

                rows.AddRange(adRows);
                rows.Add(new AdInsightRow(AdLevel.Campaign, campaignId, campaignName, campaignId, day,
                    adRows.Sum(r => r.Spend), adRows.Sum(r => r.Impressions), adRows.Sum(r => r.Clicks),
                    adRows.Sum(r => r.Reach), adRows.Sum(r => r.Conversions), "BRL"));
            }
        }
        return Task.FromResult<IReadOnlyList<AdInsightRow>>(rows);
    }

    // FNV-1a: estável entre execuções (string.GetHashCode muda a cada processo).
    private static ulong Hash(string s)
    {
        var h = 14695981039346656037UL;
        foreach (var c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }
}
