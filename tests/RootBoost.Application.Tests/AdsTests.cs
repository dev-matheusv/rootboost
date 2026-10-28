using Microsoft.Extensions.Logging.Abstractions;
using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public class AdsTests
{
    private static readonly DateOnly Day1 = new(2026, 10, 20);
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static AdInsightDaily Row(string campaign, DateOnly date, decimal spend, long impressions, long clicks, long conversions, string currency = "BRL")
    {
        var r = new AdInsightDaily(BrandKeys.MedlyCare, "meta", "act_1", AdLevel.Campaign, campaign, $"Campanha {campaign}", campaign, date, currency, Now);
        r.SetMetrics($"Campanha {campaign}", spend, impressions, clicks, impressions, conversions, currency, Now);
        return r;
    }

    [Fact]
    public void Summary_computes_rates_from_totals_not_averages_of_days()
    {
        var rows = new[] { Row("a", Day1, 10m, 1000, 10, 1), Row("a", Day1.AddDays(1), 30m, 3000, 60, 1) };
        var s = AdsSummary.Build(BrandKeys.MedlyCare, Day1, Day1.AddDays(1), rows, new AdsStopRule());

        Assert.Equal(40m, s.Totals.Spend);
        Assert.Equal(70m / 4000m, s.Totals.Ctr);   // 1,75%, não a média de 1% e 2%
        Assert.Equal(20m, s.Totals.Cpa);
        Assert.Equal(2, s.Daily.Count);
        Assert.Empty(s.Alerts);
    }

    [Fact]
    public void Stop_rule_flags_campaign_that_spent_the_limit_without_converting()
    {
        var rows = new[]
        {
            Row("converte", Day1, 150m, 5000, 80, 2),
            Row("gasta", Day1, 120m, 4000, 40, 0),
            Row("gasta", Day1.AddDays(1), 90m, 3000, 30, 0),
            Row("pouco", Day1, 50m, 1000, 5, 0),
        };
        var s = AdsSummary.Build(BrandKeys.MedlyCare, Day1, Day1.AddDays(1), rows, new AdsStopRule { SpendWithoutConversion = 200m });

        var alert = Assert.Single(s.Alerts);
        Assert.Equal("stopRule", alert.Kind);
        Assert.Equal("gasta", alert.ObjectId);
        Assert.Equal("gasta", s.Campaigns[0].CampaignId); // maior gasto primeiro
    }

    [Fact]
    public void Mixed_currencies_are_flagged_instead_of_silently_summed()
    {
        var rows = new[] { Row("a", Day1, 10m, 100, 1, 1, "BRL"), Row("b", Day1, 10m, 100, 1, 1, "USD") };
        var s = AdsSummary.Build(BrandKeys.MedlyCare, Day1, Day1, rows, new AdsStopRule());
        Assert.Equal(new[] { "BRL", "USD" }, s.Currencies);
        Assert.Contains(s.Alerts, a => a.Kind == "mixedCurrency");
    }

    [Fact]
    public void Rates_without_denominator_are_null_not_zero()
    {
        var m = new AdMetrics(0m, 0, 0, 0, 0);
        Assert.Null(m.Ctr);
        Assert.Null(m.Cpc);
        Assert.Null(m.Cpa);
    }

    [Fact]
    public void Negative_metric_is_rejected()
    {
        var r = new AdInsightDaily(BrandKeys.MedlyCare, "meta", "act_1", AdLevel.Ad, "x", "x", "c", Day1, "BRL", Now);
        Assert.Throws<ArgumentException>(() => r.SetMetrics("x", -1m, 0, 0, 0, 0, "BRL", Now));
    }

    internal sealed class Accounts(params AdAccount[] all) : IAdAccountDirectory
    {
        public IReadOnlyList<AdAccount> All { get; } = all;
    }

    internal sealed class Reader(string platform, bool fail) : IAdInsightsReader
    {
        public string Platform => platform;
        public bool Fail { get; set; } = fail;
        public (DateOnly Since, DateOnly Until)? LastWindow { get; private set; }

        public Task<IReadOnlyList<AdInsightRow>> ReadDailyAsync(AdAccount account, DateOnly since, DateOnly until, CancellationToken ct = default)
        {
            LastWindow = (since, until);
            if (Fail) throw new HttpRequestException("limite da API");
            return Task.FromResult<IReadOnlyList<AdInsightRow>>(new[]
            {
                new AdInsightRow(AdLevel.Campaign, "c1", "C1", "c1", until, 10m, 100, 2, 90, 1, "BRL"),
            });
        }
    }

    internal sealed class Store : IAdInsightStore
    {
        public int Calls { get; private set; }
        public Task<UpsertResult> UpsertAsync(AdAccount account, IReadOnlyList<AdInsightRow> rows, DateTimeOffset fetchedAt, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new UpsertResult(rows.Count, 0));
        }
        public Task<IReadOnlyList<AdInsightDaily>> ListAsync(string brandKey, DateOnly since, DateOnly until, AdLevel level, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AdInsightDaily>>(Array.Empty<AdInsightDaily>());
    }

    [Fact]
    public async Task One_failing_account_does_not_stop_the_others()
    {
        var store = new Store();
        var sync = new SyncAdInsights(
            new Accounts(new AdAccount(BrandKeys.MedlyCare, "meta", "act_ok"), new AdAccount(BrandKeys.Store, "google", "g_1")),
            new IAdInsightsReader[] { new Reader("meta", fail: false), new Reader("google", fail: true) },
            store, new ManualTime { Now = Now }, NullLogger<SyncAdInsights>.Instance);

        var results = await sync.RunAsync();

        Assert.True(results.Single(r => r.AccountId == "act_ok").Ok);
        var failed = results.Single(r => r.AccountId == "g_1");
        Assert.False(failed.Ok);
        Assert.Equal("limite da API", failed.Error);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task Sync_rereads_the_last_days_ending_today()
    {
        var reader = new Reader("meta", fail: false);
        var sync = new SyncAdInsights(new Accounts(new AdAccount(BrandKeys.MedlyCare, "meta", "act_ok")),
            new[] { reader }, new Store(), new ManualTime { Now = Now }, NullLogger<SyncAdInsights>.Instance);

        await sync.RunAsync(3);

        Assert.Equal((new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22)), reader.LastWindow);
    }

    [Fact]
    public async Task Account_without_reader_reports_a_clear_error()
    {
        var sync = new SyncAdInsights(new Accounts(new AdAccount(BrandKeys.MedlyCare, "tiktok", "t_1")),
            Array.Empty<IAdInsightsReader>(), new Store(), new ManualTime { Now = Now }, NullLogger<SyncAdInsights>.Instance);

        var result = Assert.Single(await sync.RunAsync());
        Assert.Contains("tiktok", result.Error);
    }
}
