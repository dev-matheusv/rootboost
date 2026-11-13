using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using RootBoost.Application.Ads;
using RootBoost.Domain;
using RootBoost.Infrastructure.Publishing;
using RootBoost.Infrastructure.Tracking;

namespace RootBoost.Infrastructure.Ads;

/// <summary>
/// Cria a campanha na Marketing API da Meta, tudo com <c>status=PAUSED</c> (RB-31). Campos conferidos na referência
/// oficial em 11/11/2026 (campaigns, adsets, adcreatives, ad-creative-object-story-spec, ad-creative-video-data, ads,
/// advideos, basic-targeting). O token precisa de <c>ads_management</c>; a Página e o Instagram vêm das contas de
/// publicação da marca. Cada id é gravado antes do passo seguinte, então tentar de novo não duplica.
/// </summary>
public sealed class MetaAdCampaignCreator : IAdCampaignCreator
{
    private readonly IHttpClientFactory _http;
    private readonly MetaOptions _opts;
    private readonly SocialAccounts _social;

    public MetaAdCampaignCreator(IHttpClientFactory http, IOptions<MetaOptions> opts, SocialAccounts social)
    {
        _http = http;
        _opts = opts.Value;
        _social = social;
    }

    public string Platform => "meta";

    public async Task CreatePausedAsync(AdCampaignDraft draft, AdAccount account, Func<Task> progress, DateTimeOffset now, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.AdsAccessToken)) throw new InvalidOperationException("Meta__AdsAccessToken não configurado");
        var page = _social.All.FirstOrDefault(a => a.BrandKey == draft.BrandKey && a.Channel == SocialChannel.Facebook)?.AccountId
            ?? throw new InvalidOperationException("a marca não tem Página do Facebook configurada (Social:Accounts)");
        var instagram = _social.All.FirstOrDefault(a => a.BrandKey == draft.BrandKey && a.Channel == SocialChannel.Instagram)?.AccountId;
        if (draft.Objective == AdObjective.Leads && string.IsNullOrWhiteSpace(account.PixelId))
            throw new InvalidOperationException("objetivo leads precisa do Pixel da conta (Ads:Accounts:N:PixelId)");
        var act = account.AccountId.StartsWith("act_", StringComparison.Ordinal) ? account.AccountId : $"act_{account.AccountId}";

        if (draft.ExternalVideoId is null)
        {
            draft.SetVideo(await Post($"{act}/advideos", new() { ["file_url"] = draft.VideoUrl, ["name"] = draft.Name }, ct));
            await progress();
        }
        if (draft.ExternalCampaignId is null)
        {
            draft.SetCampaign(await Post($"{act}/campaigns", new()
            {
                ["name"] = draft.Name,
                ["objective"] = draft.Objective == AdObjective.Leads ? "OUTCOME_LEADS" : "OUTCOME_TRAFFIC",
                ["status"] = "PAUSED",
                ["special_ad_categories"] = "[]",
                // Orçamento fica só no conjunto (sem compartilhar com outros conjuntos). A Meta passou a exigir o campo quando
                // a campanha não tem orçamento próprio (erro 100 no teste de 12/11); a referência de campanha manda 0 no exemplo.
                ["is_adset_budget_sharing_enabled"] = "false",
            }, ct));
            await progress();
        }
        if (draft.ExternalAdSetId is null)
        {
            var targeting = JsonSerializer.Serialize(new
            {
                geo_locations = new { countries = draft.Countries },
                age_min = draft.AgeMin,
                age_max = draft.AgeMax,
                // Desde a v23 a Meta exige dizer se o público Advantage+ está ligado (erro 100 no teste de 12/11). Ligado,
                // ela fixa idade máxima em 65 e mínima entre 18 e 25: desligado (0) respeita o público que a pessoa aprovou.
                targeting_automation = new { advantage_audience = 0 },
            });
            var form = new Dictionary<string, string>
            {
                ["name"] = draft.Name,
                ["campaign_id"] = draft.ExternalCampaignId!,
                ["daily_budget"] = draft.DailyBudgetCents.ToString(),
                ["billing_event"] = "IMPRESSIONS",
                ["bid_strategy"] = "LOWEST_COST_WITHOUT_CAP",
                ["targeting"] = targeting,
                ["status"] = "PAUSED",
            };
            if (draft.Objective == AdObjective.Leads)
            {
                form["optimization_goal"] = "OFFSITE_CONVERSIONS";
                form["promoted_object"] = JsonSerializer.Serialize(new { pixel_id = account.PixelId, custom_event_type = "LEAD" });
            }
            else
            {
                form["optimization_goal"] = "LINK_CLICKS";
                form["destination_type"] = "WEBSITE";
            }
            draft.SetAdSet(await Post($"{act}/adsets", form, ct));
            await progress();
        }
        if (draft.ExternalCreativeId is null)
        {
            var video = new Dictionary<string, object?>
            {
                ["video_id"] = draft.ExternalVideoId,
                ["message"] = draft.PrimaryText,
                ["title"] = draft.Headline,
                ["call_to_action"] = new { type = draft.CallToAction, value = new { link = draft.LinkUrl } },
            };
            if (draft.ThumbnailUrl is not null) video["image_url"] = draft.ThumbnailUrl;
            var story = new Dictionary<string, object?> { ["page_id"] = page, ["video_data"] = video };
            if (!string.IsNullOrWhiteSpace(instagram)) story["instagram_user_id"] = instagram;
            var form = new Dictionary<string, string> { ["name"] = draft.Name, ["object_story_spec"] = JsonSerializer.Serialize(story) };
            // A Meta processa o vídeo depois do upload; a doc não descreve como consultar o fim, então tenta algumas vezes.
            string? id = null;
            Exception? last = null;
            for (var i = 0; i < 6 && id is null; i++)
            {
                try { id = await Post($"{act}/adcreatives", form, ct); }
                catch (InvalidOperationException ex) { last = ex; await Task.Delay(TimeSpan.FromSeconds(20), ct); }
            }
            draft.SetCreative(id ?? throw new InvalidOperationException($"criativo não foi criado: {last?.Message}"));
            await progress();
        }
        var ad = new Dictionary<string, string>
        {
            ["name"] = draft.Name,
            ["adset_id"] = draft.ExternalAdSetId!,
            ["creative"] = JsonSerializer.Serialize(new { creative_id = draft.ExternalCreativeId }),
            ["status"] = "PAUSED",
        };
        if (draft.Objective == AdObjective.Leads) ad["conversion_domain"] = new Uri(draft.LinkUrl).Host;
        draft.Created(await Post($"{act}/ads", ad, ct), now);
    }

    private async Task<string> Post(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_opts.ApiVersion}/{path}") { Content = new FormUrlEncodedContent(form) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.AdsAccessToken);
        using var res = await _http.CreateClient("meta").SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        if (!res.IsSuccessStatusCode || doc.RootElement.TryGetProperty("error", out _))
        {
            var msg = doc.RootElement.TryGetProperty("error", out var e)
                ? $"{(e.TryGetProperty("error_user_msg", out var um) ? um.GetString() : e.TryGetProperty("message", out var m) ? m.GetString() : "erro")} (código {(e.TryGetProperty("code", out var c) ? c.ToString() : "?")})"
                : $"HTTP {(int)res.StatusCode}";
            throw new InvalidOperationException($"Meta recusou {path.Split('/').Last()}: {msg}");
        }
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString()! : throw new InvalidOperationException($"Meta não devolveu id em {path}");
    }
}
