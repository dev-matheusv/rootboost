using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using RootBoost.Application.Ads;
using RootBoost.Domain;
using RootBoost.Infrastructure.Ads;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Api.IntegrationTests;

/// <summary>O que vai pra Marketing API ao criar a campanha (sem rede: o handler grava e responde um id).</summary>
public class MetaAdCampaignCreatorTests
{
    [Fact]
    public async Task Everything_is_paused_and_campaign_says_budget_is_not_shared()
    {
        var handler = new Recorder();
        var social = new SocialAccounts(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Social:Accounts:0:Brand"] = "medlycare", ["Social:Accounts:0:Channel"] = "Facebook",
            ["Social:Accounts:0:AccountId"] = "page1", ["Social:Accounts:0:AccessToken"] = "t",
        }).Build());
        var creator = new MetaAdCampaignCreator(new Factory(handler),
            Options.Create(new MetaOptions { AdsAccessToken = "x", ApiVersion = "v27.0" }), social);
        var now = DateTimeOffset.UtcNow;
        var d = new AdCampaignDraft(BrandKeys.MedlyCare, null, now);
        d.Update("Teste", AdObjective.Traffic, 2000, ["BR"], 25, 55, "https://cdn.example.com/v.mp4", null,
            "Texto.", "Título", "LEARN_MORE", "https://medlycare.com.br", null, now);

        await creator.CreatePausedAsync(d, new AdAccount("medlycare", "meta", "123"), () => Task.CompletedTask, now);

        var campaign = handler.Bodies.Single(b => b.Path.EndsWith("/campaigns")).Form;
        Assert.Contains("is_adset_budget_sharing_enabled=false", campaign);
        Assert.Contains("status=PAUSED", campaign);
        var adset = Uri.UnescapeDataString(handler.Bodies.Single(b => b.Path.EndsWith("/adsets")).Form.Replace("+", " "));
        Assert.Contains("\"targeting_automation\":{\"advantage_audience\":0}", adset);
        Assert.All(handler.Bodies.Where(b => !b.Path.EndsWith("/advideos") && !b.Path.EndsWith("/adcreatives")),
            b => Assert.Contains("status=PAUSED", b.Form));
    }

    [Fact]
    public async Task Test_days_send_total_budget_and_end_date_instead_of_daily()
    {
        var handler = new Recorder();
        var social = new SocialAccounts(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Social:Accounts:0:Brand"] = "medlycare", ["Social:Accounts:0:Channel"] = "Facebook",
            ["Social:Accounts:0:AccountId"] = "page1", ["Social:Accounts:0:AccessToken"] = "t",
        }).Build());
        var creator = new MetaAdCampaignCreator(new Factory(handler), Options.Create(new MetaOptions { AdsAccessToken = "x", ApiVersion = "v27.0" }), social);
        var now = new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        var d = new AdCampaignDraft(BrandKeys.MedlyCare, null, now);
        d.Update("Teste", AdObjective.Traffic, 1000, ["BR"], 25, 55, "https://cdn.example.com/v.mp4", null, "Texto.", "Título", "LEARN_MORE", "https://medlycare.com.br", null, now);
        d.SetTestDays(5, now);
        Assert.Equal(5000, d.TotalBudgetCents);

        await creator.CreatePausedAsync(d, new AdAccount("medlycare", "meta", "123"), () => Task.CompletedTask, now);

        var adset = handler.Bodies.Single(b => b.Path.EndsWith("/adsets")).Form;
        Assert.Contains("lifetime_budget=5000", adset);
        Assert.Contains($"end_time={now.AddDays(5).ToUnixTimeSeconds()}", adset);
        Assert.DoesNotContain("daily_budget", adset);
        Assert.Equal(now.AddDays(5), d.EndsAt);
        Assert.Throws<ArgumentException>(() => new AdCampaignDraft(BrandKeys.MedlyCare, null, now).SetTestDays(31, now));
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public List<(string Path, string Form)> Bodies { get; } = [];
        private int _n;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add((request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{{\"id\":\"{++_n}\"}}") };
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, false) { BaseAddress = new Uri("https://graph.facebook.com/") };
    }
}
