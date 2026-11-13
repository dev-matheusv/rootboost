using RootBoost.Application.Organic;
using RootBoost.Domain;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Ranking com o pago ao lado (13/11): o vídeo que virou anúncio soma os números dos anúncios dele.</summary>
public class VideoAdsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Post_that_became_an_ad_gets_the_ad_numbers_and_others_do_not()
    {
        const string url = "https://cdn.example.com/ficha.mp4";
        var pub = new Publication(BrandKeys.MedlyCare, SocialChannel.Instagram, url, "Ficha", Now.AddDays(-10), Now.AddDays(-12));
        var post = new OrganicPost(BrandKeys.MedlyCare, SocialChannel.Instagram, "ig1", Now.AddDays(-10));
        post.LinkPublication(pub.Id);
        var other = new OrganicPost(BrandKeys.MedlyCare, SocialChannel.Instagram, "ig2", Now.AddDays(-5));

        var draft = new AdCampaignDraft(BrandKeys.MedlyCare, null, Now);
        draft.Update("Teste", AdObjective.Traffic, 1000, ["BR"], 25, 55, url, null, "Texto.", "Título", "LEARN_MORE", "https://medlycare.com.br", null, Now);
        draft.Created("ad1", Now);
        AdInsightDaily Row(string ad, int day, decimal spend, long imp, long clicks)
        {
            var r = new AdInsightDaily(BrandKeys.MedlyCare, "meta", "act_1", AdLevel.Ad, ad, ad, "c1", DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-day), "BRL", Now);
            r.SetMetrics(ad, spend, imp, clicks, imp, 0, "BRL", Now);
            return r;
        }

        var result = VideoAds.ForPosts([post, other], [pub], [draft], [Row("ad1", 1, 6m, 900, 12), Row("ad1", 2, 4m, 600, 8), Row("adX", 1, 9m, 999, 9)]);

        Assert.False(result.ContainsKey("ig2"));
        var m = result["ig1"];
        Assert.Equal(10m, m.Spend);
        Assert.Equal(1500, m.Impressions);
        Assert.Contains("CTR", VideoAds.Describe(m));
    }
}
