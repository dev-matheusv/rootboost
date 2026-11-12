using RootBoost.Application.Ads;
using RootBoost.Application.Reports;
using RootBoost.Domain;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Resumo da semana (RB-34): contagens certas e próximos passos que fazem sentido.</summary>
public class WeeklyReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private static Publication Post(int daysFromNow, bool published = false)
    {
        var at = Now.AddDays(daysFromNow);
        var p = new Publication(BrandKeys.MedlyCare, SocialChannel.Instagram, "https://cdn.example.com/v.mp4", "Legenda", at, at.AddDays(-2));
        if (published) { p.Approve("dono@x", at.AddDays(-1)); p.MarkPublishedManually("https://instagram.com/p/x", at); }
        return p;
    }

    [Fact]
    public void Empty_week_asks_for_content()
    {
        var r = WeeklyReport.Build(BrandKeys.MedlyCare, Now, 7, [], [], [], []);
        Assert.Equal(0, r.Published);
        Assert.Contains(r.Highlights, l => l.Text.Contains("Nenhum post") && l.Kind == "warning");
        Assert.Contains(r.NextSteps, l => l.Text.Contains("peça um vídeo"));
    }

    [Fact]
    public void Counts_published_and_upcoming_drafts_to_approve()
    {
        var r = WeeklyReport.Build(BrandKeys.MedlyCare, Now, 7, [Post(-2, true), Post(-1, true), Post(3), Post(10)], [], [], []);
        Assert.Equal(2, r.Published);
        Assert.Equal(1, r.Scheduled); // o de daqui a 10 dias fica fora dos próximos 7
        Assert.Equal(1, r.AwaitingApproval);
        Assert.Contains(r.NextSteps, l => l.Text.Contains("Aprovar 1 post"));
    }

    [Fact]
    public void Campaign_advice_becomes_a_next_step()
    {
        var c = new AdCampaignDraft(BrandKeys.MedlyCare, null, Now);
        c.Update("Teste ficha", AdObjective.Traffic, 2000, ["BR"], 25, 55, "https://cdn.example.com/v.mp4", null, "Texto.", "Título", "LEARN_MORE",
            "https://medlycare.com.br", null, Now);
        var advice = new CampaignAdvice(AdviceKind.Pause, "Gastou sem resultado.", null, new AdMetrics(200, 5000, 20, 4000, 0));
        var r = WeeklyReport.Build(BrandKeys.MedlyCare, Now, 7, [Post(2)], [], [], [(c, advice)]);
        Assert.Contains(r.NextSteps, l => l.Text.Contains("Teste ficha") && l.Kind == "bad");
    }

    private static OrganicPost Organic(string id, int daysAgo, long reach, long saves, Guid? pub = null)
    {
        var p = new OrganicPost(BrandKeys.MedlyCare, SocialChannel.Instagram, id, Now.AddDays(-daysAgo));
        p.Describe("REELS", "VIDEO", $"Vídeo {id}\nresto da legenda", null, null);
        p.SetMetrics(reach * 2, reach, reach / 20, 2, saves / 2, saves, reach / 20 + saves, 9000, Now);
        p.LinkPublication(pub);
        return p;
    }

    [Fact]
    public void Organic_and_paid_are_separate_and_the_best_video_becomes_an_ad_suggestion()
    {
        var pub = Post(-30, true);
        var posts = new[] { Organic("a", 30, 3000, 300, pub.Id), Organic("b", 2, 1500, 20), Organic("c", 3, 800, 5), Organic("d", 40, 900, 4) };
        var r = WeeklyReport.Build(BrandKeys.MedlyCare, Now, 7, [pub], [], [], [], posts, []);
        Assert.Contains(r.Organic, l => l.Text.StartsWith("2 post(s) no Instagram"));
        Assert.Contains(r.Organic, l => l.Text.Contains("Melhor da semana: \"Vídeo b\""));
        Assert.Contains(r.Paid, l => l.Text.Contains("Nenhum anúncio rodou"));
        Assert.Contains(r.NextSteps, l => l.Text.StartsWith("Anunciar \"Vídeo a\""));

        // Já anunciado: não sugere de novo.
        var again = WeeklyReport.Build(BrandKeys.MedlyCare, Now, 7, [pub], [], [], [], posts, [pub.MediaUrl]);
        Assert.DoesNotContain(again.NextSteps, l => l.Text.StartsWith("Anunciar"));
    }

    [Fact]
    public void Lessons_list_best_and_worst_without_little_data_posts()
    {
        var ranked = RootBoost.Application.Organic.CreativeRanking.Rank([Organic("a", 1, 3000, 300), Organic("b", 2, 1500, 20), Organic("z", 1, 40, 30)]);
        var text = RootBoost.Application.Organic.ContentLessons.From(ranked, best: 1, worst: 1)!;
        Assert.Contains("\"Vídeo a\"", text);
        Assert.Contains("menos engajaram", text);
        Assert.DoesNotContain("Vídeo z", text);
        Assert.Null(RootBoost.Application.Organic.ContentLessons.From([]));
    }
}
