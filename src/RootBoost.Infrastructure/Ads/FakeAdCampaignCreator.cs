using RootBoost.Application.Ads;
using RootBoost.Domain;

namespace RootBoost.Infrastructure.Ads;

/// <summary>Criação de mentira, determinística: ids derivados do rascunho. Padrão no dev e no CI (nada sai pra Meta).</summary>
public sealed class FakeAdCampaignCreator : IAdCampaignCreator
{
    public string Platform => FakeAdInsightsReader.FakePlatform;

    public async Task CreatePausedAsync(AdCampaignDraft draft, AdAccount account, Func<Task> progress, DateTimeOffset now, CancellationToken ct = default)
    {
        var tag = draft.Id.ToString("N")[..8];
        if (draft.ExternalVideoId is null) { draft.SetVideo($"fake-video-{tag}"); await progress(); }
        if (draft.ExternalCampaignId is null) { draft.SetCampaign($"fake-campaign-{tag}"); await progress(); }
        if (draft.ExternalAdSetId is null) { draft.SetAdSet($"fake-adset-{tag}"); await progress(); }
        if (draft.ExternalCreativeId is null) { draft.SetCreative($"fake-creative-{tag}"); await progress(); }
        // Texto "falhar" no título simula erro da plataforma no último passo (exercita a retomada nos testes).
        if (draft.Headline.Contains("falhar", StringComparison.OrdinalIgnoreCase) && draft.Attempts < 2)
            throw new InvalidOperationException("Meta recusou o anúncio (simulado)");
        draft.Created($"fake-ad-{tag}", now);
    }
}
