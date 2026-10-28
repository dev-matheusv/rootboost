using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RootBoost.Application.Alerts;
using RootBoost.Domain;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Avisos de operação (RB-58) no Postgres: um aberto por problema, lista e dispensa pelo Console.</summary>
public class AlertsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public AlertsApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Repeated_problem_is_one_alert_listed_and_dismissed_from_the_console()
    {
        var key = $"ads:meta:act_{Guid.NewGuid():N}";
        for (var i = 0; i < 2; i++)
        {
            using var scope = _factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AlertService>()
                .RaiseAsync(AlertKind.AdsSyncFailed, key, "medlycare", $"limite da API {i}");
        }

        var client = _factory.CreateClient();
        var alert = (await client.GetFromJsonAsync<JsonElement>("/admin/alerts")).EnumerateArray()
            .Single(a => a.GetProperty("key").GetString() == key);
        Assert.Equal(2, alert.GetProperty("count").GetInt32());
        Assert.Equal("AdsSyncFailed", alert.GetProperty("kind").GetString());
        Assert.Equal("limite da API 1", alert.GetProperty("message").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/admin/alerts/{alert.GetProperty("id").GetString()}/dismiss", null)).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<JsonElement>("/admin/alerts")).EnumerateArray(),
            a => a.GetProperty("key").GetString() == key);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/admin/alerts/{Guid.NewGuid()}/dismiss", null)).StatusCode);
    }

    [Fact]
    public async Task Meta_check_on_demand_explains_when_publishing_is_not_live()
    {
        var res = await _factory.CreateClient().PostAsync("/admin/alerts/check-meta", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

/// <summary>Checagem da conexão com a Meta contra respostas no formato do Graph.</summary>
public class MetaConnectionCheckTests
{
    private sealed class Store : IAlertStore
    {
        public List<OpsAlert> Items { get; } = new();
        public Task<OpsAlert?> GetOpenAsync(string key, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(a => a.Key == key && a.IsOpen));
        public Task<OpsAlert?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Items.FirstOrDefault(a => a.Id == id));
        public Task<IReadOnlyList<OpsAlert>> ListOpenAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<OpsAlert>>(Items.Where(a => a.IsOpen).ToList());
        public Task AddAsync(OpsAlert alert, CancellationToken ct = default) { Items.Add(alert); return Task.CompletedTask; }
        public Task SaveAsync(OpsAlert alert, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Instagram 1784 responde; a Página 200 devolve token vencido (código 190) até <see cref="PageOk"/>.</summary>
    private sealed class Graph : HttpMessageHandler
    {
        public bool PageOk { get; set; }
        public List<(string Path, string Query, string? Auth)> Calls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            Calls.Add((path, req.RequestUri.Query, req.Headers.Authorization?.ToString()));
            var (status, json) = path.EndsWith("/1784") ? (HttpStatusCode.OK, """{"id":"1784"}""")
                : PageOk ? (HttpStatusCode.OK, """{"id":"200"}""")
                : (HttpStatusCode.BadRequest, """{"error":{"message":"Error validating access token: Session has expired","code":190,"error_subcode":463}}""");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false) { BaseAddress = new Uri("https://graph.facebook.com/") };
    }

    [Fact]
    public async Task Expired_page_token_raises_an_alert_and_recovery_closes_it()
    {
        var accounts = new SocialAccounts(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Social:Accounts:0:Brand"] = "medlycare", ["Social:Accounts:0:Channel"] = "Instagram",
            ["Social:Accounts:0:AccountId"] = "1784", ["Social:Accounts:0:AccessToken"] = "tok-ig",
            ["Social:Accounts:1:Brand"] = "medlycare", ["Social:Accounts:1:Channel"] = "Facebook",
            ["Social:Accounts:1:AccountId"] = "200", ["Social:Accounts:1:AccessToken"] = "tok-fb",
        }).Build());
        var store = new Store();
        var graph = new Graph();
        var check = new MetaConnectionCheck(new Factory(graph), accounts,
            new AlertService(store, TimeProvider.System, NullLogger<AlertService>.Instance),
            Options.Create(new MetaOptions { ApiVersion = "v26.0" }));

        var results = await check.RunAsync();

        Assert.True(results.Single(r => r.Channel == SocialChannel.Instagram).Ok);
        Assert.False(results.Single(r => r.Channel == SocialChannel.Facebook).Ok);
        Assert.Contains(graph.Calls, c => c.Path == "/v26.0/200" && c.Query == "?fields=id" && c.Auth == "Bearer tok-fb");
        Assert.DoesNotContain(graph.Calls, c => c.Query.Contains("tok-"));
        var alert = Assert.Single(store.Items);
        Assert.Equal("meta:medlycare:Facebook", alert.Key);
        Assert.Contains("190", alert.Message);

        graph.PageOk = true;
        await check.RunAsync();
        Assert.False(alert.IsOpen);
    }
}
