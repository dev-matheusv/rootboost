using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RootBoost.Application.Ads;
using RootBoost.Domain;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Ads;

/// <summary>
/// Lê insights da Marketing API da Meta (só leitura, permissão <c>ads_read</c>). Conferido na referência
/// "Ad Account Insights" e "AdsActionStats" em 24/10/2026:
/// <c>GET /{versão}/act_{id}/insights?level=campaign|ad&amp;time_increment=1&amp;time_range={"since","until"}</c>,
/// métricas chegam como texto numérico, conversões vêm em <c>actions[] {action_type, value}</c> e a resposta
/// pagina por <c>paging</c>. O token vai no cabeçalho Authorization, nunca na URL (URL vai pro log).
/// </summary>
public sealed class MetaAdsInsightsReader : IAdInsightsReader
{
    /// <summary>Cadastro pelo Pixel: a conversão do teste grátis do MedlyCare. Por conta dá pra trocar (Ads:Accounts:N:ConversionAction).</summary>
    public const string DefaultConversionAction = "offsite_conversion.fb_pixel_complete_registration";

    /// <summary>Acima disto (% da quota de insights) a rodada para e tenta de novo na próxima.</summary>
    public const int ThrottleStopPct = 90;

    private const string Fields = "campaign_id,campaign_name,ad_id,ad_name,spend,impressions,clicks,reach,actions,account_currency";
    private const int MaxPages = 50;

    private readonly IHttpClientFactory _factory;
    private readonly MetaOptions _opts;
    private readonly ILogger<MetaAdsInsightsReader> _log;

    public MetaAdsInsightsReader(IHttpClientFactory factory, IOptions<MetaOptions> opts, ILogger<MetaAdsInsightsReader> log)
    {
        _factory = factory;
        _opts = opts.Value;
        _log = log;
    }

    public string Platform => "meta";

    public async Task<IReadOnlyList<AdInsightRow>> ReadDailyAsync(AdAccount account, DateOnly since, DateOnly until, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.AdsAccessToken))
            throw new InvalidOperationException("Meta__AdsAccessToken não configurado");

        var conversion = account.ConversionAction ?? DefaultConversionAction;
        var rows = new List<AdInsightRow>();
        rows.AddRange(await ReadLevelAsync(account, AdLevel.Campaign, since, until, conversion, ct));
        rows.AddRange(await ReadLevelAsync(account, AdLevel.Ad, since, until, conversion, ct));
        return rows;
    }

    private async Task<List<AdInsightRow>> ReadLevelAsync(AdAccount account, AdLevel level, DateOnly since, DateOnly until,
        string conversion, CancellationToken ct)
    {
        var http = _factory.CreateClient("meta");
        var accountPath = account.AccountId.StartsWith("act_", StringComparison.Ordinal) ? account.AccountId : $"act_{account.AccountId}";
        var timeRange = Uri.EscapeDataString($"{{\"since\":\"{since:yyyy-MM-dd}\",\"until\":\"{until:yyyy-MM-dd}\"}}");
        var levelName = level == AdLevel.Campaign ? "campaign" : "ad";
        string? url = $"{_opts.ApiVersion}/{accountPath}/insights?level={levelName}&time_increment=1&time_range={timeRange}" +
                      $"&fields={Fields}&limit=500";

        var rows = new List<AdInsightRow>();
        for (var page = 0; url is not null && page < MaxPages; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.AdsAccessToken);
            using var res = await http.SendAsync(req, ct);
            var body = await res.Content.ReadAsStringAsync(ct);

            if (!res.IsSuccessStatusCode) throw new InvalidOperationException(DescribeError((int)res.StatusCode, body));
            CheckThrottle(res, account);

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var item in data.EnumerateArray())
                    rows.Add(Parse(item, level, conversion));

            url = NextPage(doc.RootElement);
        }
        return rows;
    }

    internal static AdInsightRow Parse(JsonElement item, AdLevel level, string conversion)
    {
        var campaignId = Str(item, "campaign_id");
        var objectId = level == AdLevel.Campaign ? campaignId : Str(item, "ad_id");
        var name = level == AdLevel.Campaign ? Str(item, "campaign_name") : Str(item, "ad_name");
        if (string.IsNullOrEmpty(objectId)) throw new InvalidOperationException($"linha de insights sem id no nível {level}");

        long conversions = 0;
        if (item.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            foreach (var a in actions.EnumerateArray())
                if (Str(a, "action_type") == conversion)
                    conversions += (long)Math.Round(Num(a, "value"));

        return new AdInsightRow(level, objectId, name, campaignId,
            DateOnly.ParseExact(Str(item, "date_start"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Num(item, "spend"), (long)Num(item, "impressions"), (long)Num(item, "clicks"), (long)Num(item, "reach"),
            conversions, Str(item, "account_currency") is { Length: 3 } cur ? cur : "USD");
    }

    /// <summary>
    /// Próxima página: a URL pronta em paging.next. Só segue pra graph.facebook.com (o token vai no cabeçalho e
    /// não pode ir pra outro host) e tira um access_token que venha embutido, pra ele não parar no log.
    /// </summary>
    internal static string? NextPage(JsonElement root)
    {
        if (!root.TryGetProperty("paging", out var paging) || !paging.TryGetProperty("next", out var next) ||
            next.ValueKind != JsonValueKind.String || !Uri.TryCreate(next.GetString(), UriKind.Absolute, out var uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "graph.facebook.com", StringComparison.OrdinalIgnoreCase))
            return null;

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("access_token=", StringComparison.OrdinalIgnoreCase));
        return new UriBuilder(uri) { Query = string.Join('&', query) }.Uri.ToString();
    }

    /// <summary>
    /// <c>X-FB-Ads-Insights-Throttle: {"app_id_util_pct":..,"acc_id_util_pct":..}</c>. Perto do teto a rodada para
    /// (a próxima relê a janela); insistir só piora o bloqueio.
    /// </summary>
    private void CheckThrottle(HttpResponseMessage res, AdAccount account)
    {
        if (!res.Headers.TryGetValues("X-FB-Ads-Insights-Throttle", out var values)) return;
        var pct = ThrottlePct(values.FirstOrDefault());
        if (pct >= ThrottleStopPct)
            throw new InvalidOperationException($"quota de insights da Meta em {pct:0}%: a próxima rodada tenta de novo");
        if (pct >= 75)
            _log.LogWarning("Quota de insights da Meta em {Pct:0}% na conta {Account}.", pct, account.AccountId);
    }

    internal static double ThrottlePct(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(header);
            double max = 0;
            foreach (var key in new[] { "app_id_util_pct", "acc_id_util_pct" })
                if (doc.RootElement.TryGetProperty(key, out var v) && v.TryGetDouble(out var d)) max = Math.Max(max, d);
            return max;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>Erro do Graph: <c>{"error":{"message","code","error_subcode"}}</c>. Mensagem sem token.</summary>
    internal static string DescribeError(int status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                var code = e.TryGetProperty("code", out var c) ? c.ToString() : "?";
                var sub = e.TryGetProperty("error_subcode", out var s) ? $"/{s}" : "";
                var msg = e.TryGetProperty("message", out var m) ? m.GetString() : null;
                return $"Meta {status} (código {code}{sub}): {msg}";
            }
        }
        catch (JsonException) { }
        return $"Meta {status}";
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // Métricas chegam como "numeric string"; aceita número cru também, por segurança.
    private static decimal Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0m;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        return v.ValueKind == JsonValueKind.String &&
               decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
