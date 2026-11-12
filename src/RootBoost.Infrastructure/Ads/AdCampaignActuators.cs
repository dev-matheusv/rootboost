using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RootBoost.Application.Ads;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Ads;

public sealed class FakeAdCampaignActuator : IAdCampaignActuator
{
    public string Platform => FakeAdInsightsReader.FakePlatform;
    public List<string> Paused { get; } = new();
    public Task PauseAsync(string campaignId, CancellationToken ct = default) { Paused.Add(campaignId); return Task.CompletedTask; }
}

/// <summary>Pausa a campanha na Meta: <c>POST /{campaign_id}</c> com <c>status=PAUSED</c> (referência ad-campaign-group, 11/11/2026).</summary>
public sealed class MetaAdCampaignActuator : IAdCampaignActuator
{
    private readonly IHttpClientFactory _http;
    private readonly MetaOptions _opts;

    public MetaAdCampaignActuator(IHttpClientFactory http, IOptions<MetaOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
    }

    public string Platform => "meta";

    public async Task PauseAsync(string campaignId, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiVersion}/{campaignId}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = "PAUSED" }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.AdsAccessToken);
        using var res = await _http.CreateClient("meta").SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (res.IsSuccessStatusCode && !body.Contains("\"error\"", StringComparison.Ordinal)) return;
        string msg;
        try { using var d = JsonDocument.Parse(body); msg = d.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body; }
        catch (Exception) { msg = $"HTTP {(int)res.StatusCode}"; }
        throw new InvalidOperationException($"Meta recusou pausar a campanha: {msg}");
    }
}
