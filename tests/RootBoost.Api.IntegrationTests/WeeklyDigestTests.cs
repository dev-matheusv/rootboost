using RootBoost.Application.Organic;
using RootBoost.Application.Reports;
using RootBoost.Domain;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Resumo da semana por push (12/11): segunda 9h em Brasília, texto curto, nada quando a semana foi vazia.</summary>
public class WeeklyDigestTests
{
    [Fact]
    public void Due_only_on_monday_between_9_and_9_15_in_brasilia()
    {
        Assert.True(WeeklyDigest.IsDue(new DateTimeOffset(2026, 11, 16, 12, 5, 0, TimeSpan.Zero)));   // seg 09:05 BRT
        Assert.False(WeeklyDigest.IsDue(new DateTimeOffset(2026, 11, 16, 12, 20, 0, TimeSpan.Zero)));  // seg 09:20
        Assert.False(WeeklyDigest.IsDue(new DateTimeOffset(2026, 11, 17, 12, 5, 0, TimeSpan.Zero)));   // ter 09:05
    }

    [Fact]
    public void Message_says_posts_and_best_video_and_skips_empty_weeks()
    {
        var now = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var brand = new Brand("medlycare", "MedlyCare", "pt", now);
        var post = new OrganicPost("medlycare", SocialChannel.Instagram, "x", now.AddDays(-2));
        post.Describe("REELS", "VIDEO", "Ficha no celular\nresto", null, null);
        post.SetMetrics(4000, 3000, 100, 5, 40, 60, 205, 15000, now);
        var best = CreativeRanking.Rank([post]).Single();

        var m = WeeklyDigest.Compose(brand, 3, best)!;
        Assert.Equal("Resumo da semana: MedlyCare", m.Title);
        Assert.Contains("3 posts saíram", m.Body);
        Assert.Contains("\"Ficha no celular\"", m.Body);
        Assert.Equal("/", m.Url);
        Assert.Null(WeeklyDigest.Compose(brand, 0, null));
    }
}
