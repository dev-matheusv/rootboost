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
}
