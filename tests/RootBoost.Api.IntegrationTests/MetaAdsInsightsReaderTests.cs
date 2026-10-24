using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RootBoost.Application.Ads;
using RootBoost.Domain;
using RootBoost.Infrastructure.Ads;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Leitor real da Meta contra respostas no formato da referência (Ad Account Insights, AdsActionStats).</summary>
public class MetaAdsInsightsReaderTests
{
    private const string Token = "token-de-teste";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://graph.facebook.com/") };
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK, string? throttle = null)
    {
        var res = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (throttle is not null) res.Headers.Add("X-FB-Ads-Insights-Throttle", throttle);
        return res;
    }

    private static MetaAdsInsightsReader Reader(StubHandler handler, string token = Token) =>
        new(new Factory(handler), Options.Create(new MetaOptions { AdsAccessToken = token, ApiVersion = "v26.0" }),
            NullLogger<MetaAdsInsightsReader>.Instance);

    private static string Page(string rows, string? next = null) =>
        "{\"data\":[" + rows + "]" + (next is null ? "" : ",\"paging\":{\"next\":\"" + next + "\"}") + "}";

    private static readonly AdAccount Account = new(BrandKeys.MedlyCare, "meta", "act_123");
    private static readonly DateOnly Day = new(2026, 10, 20);

    private const string CampaignRow = """
        {"campaign_id":"111","campaign_name":"Cadastro","spend":"45.30","impressions":"3200","clicks":"64","reach":"2900",
         "account_currency":"BRL","date_start":"2026-10-20","date_stop":"2026-10-20",
         "actions":[{"action_type":"link_click","value":"64"},{"action_type":"offsite_conversion.fb_pixel_complete_registration","value":"3"}]}
        """;

    [Fact]
    public async Task Reads_campaign_and_ad_levels_with_conversions_from_actions()
    {
        var handler = new StubHandler(req => req.RequestUri!.Query.Contains("level=campaign")
            ? Json(Page(CampaignRow))
            : Json("""{"data":[{"campaign_id":"111","ad_id":"999","ad_name":"Reel","spend":"45.30","impressions":"3200","clicks":"64","reach":"2900","account_currency":"BRL","date_start":"2026-10-20","date_stop":"2026-10-20"}]}"""));

        var rows = await Reader(handler).ReadDailyAsync(Account, Day, Day);

        var campaign = rows.Single(r => r.Level == AdLevel.Campaign);
        Assert.Equal("111", campaign.ObjectId);
        Assert.Equal(45.30m, campaign.Spend);
        Assert.Equal(3200, campaign.Impressions);
        Assert.Equal(3, campaign.Conversions); // só a ação de cadastro, não o clique
        Assert.Equal("BRL", campaign.Currency);
        Assert.Equal(Day, campaign.Date);

        var ad = rows.Single(r => r.Level == AdLevel.Ad);
        Assert.Equal("999", ad.ObjectId);
        Assert.Equal("111", ad.CampaignId);
        Assert.Equal(0, ad.Conversions);

        var first = handler.Requests[0];
        Assert.Equal("Bearer", first.Headers.Authorization?.Scheme);
        Assert.DoesNotContain(Token, first.RequestUri!.ToString()); // token nunca na URL
        Assert.Contains("/v26.0/act_123/insights", first.RequestUri.AbsolutePath);
        Assert.Contains("time_increment=1", first.RequestUri.Query);
        Assert.Contains(Uri.EscapeDataString("{\"since\":\"2026-10-20\",\"until\":\"2026-10-20\"}"), first.RequestUri.Query);
    }

    [Fact]
    public async Task Follows_paging_next_but_drops_an_embedded_token_and_other_hosts()
    {
        var calls = 0;
        var handler = new StubHandler(req =>
        {
            calls++;
            if (!req.RequestUri!.Query.Contains("level=campaign")) return Json("""{"data":[]}""");
            return req.RequestUri.Query.Contains("after=p2")
                ? Json(Page(CampaignRow.Replace("\"111\"", "\"222\""), "https://evil.example.com/steal"))
                : Json(Page(CampaignRow, "https://graph.facebook.com/v26.0/act_123/insights?level=campaign&access_token=vazou&after=p2"));
        });

        var rows = await Reader(handler).ReadDailyAsync(Account, Day, Day);

        Assert.Equal(new[] { "111", "222" }, rows.Where(r => r.Level == AdLevel.Campaign).Select(r => r.ObjectId));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.ToString().Contains("vazou"));
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host != "graph.facebook.com");
    }

    [Fact]
    public async Task Graph_error_becomes_a_clear_message()
    {
        var handler = new StubHandler(_ => Json("""{"error":{"message":"User request limit reached","type":"OAuthException","code":17,"error_subcode":2446079}}""", HttpStatusCode.BadRequest));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Reader(handler).ReadDailyAsync(Account, Day, Day));
        Assert.Contains("código 17/2446079", ex.Message);
        Assert.Contains("User request limit reached", ex.Message);
    }

    [Fact]
    public async Task Stops_when_the_insights_quota_is_nearly_used()
    {
        var handler = new StubHandler(_ => Json("""{"data":[]}""", throttle: """{"app_id_util_pct":12,"acc_id_util_pct":93}"""));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Reader(handler).ReadDailyAsync(Account, Day, Day));
        Assert.Contains("93%", ex.Message);
    }

    [Fact]
    public async Task Missing_token_fails_without_calling_the_api()
    {
        var handler = new StubHandler(_ => Json("""{"data":[]}"""));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Reader(handler, token: "").ReadDailyAsync(Account, Day, Day));
        Assert.Contains("Meta__AdsAccessToken", ex.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Account_can_choose_another_conversion_action()
    {
        var handler = new StubHandler(req => req.RequestUri!.Query.Contains("level=campaign")
            ? Json(Page(CampaignRow)) : Json("""{"data":[]}"""));

        var rows = await Reader(handler).ReadDailyAsync(Account with { ConversionAction = "link_click" }, Day, Day);
        Assert.Equal(64, rows.Single().Conversions);
    }
}
