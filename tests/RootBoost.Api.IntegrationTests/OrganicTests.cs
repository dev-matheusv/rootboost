using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RootBoost.Application.Organic;
using RootBoost.Domain;
using RootBoost.Infrastructure.Organic;

namespace RootBoost.Api.IntegrationTests;

/// <summary>Métricas orgânicas e ranking de vídeos (RB-25).</summary>
public class OrganicRankingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private static OrganicPost Post(string id, long reach, long likes, long saves, long shares, long? watchMs, string product = "REELS")
    {
        var p = new OrganicPost(BrandKeys.MedlyCare, SocialChannel.Instagram, id, Now);
        p.Describe(product, product == "REELS" ? "VIDEO" : "IMAGE", id, null, null);
        p.SetMetrics(reach, reach, likes, 0, shares, saves, likes + shares + saves, watchMs, Now);
        return p;
    }

    [Fact]
    public void Saves_and_shares_outrank_likes_and_little_data_goes_last()
    {
        var ranked = CreativeRanking.Rank([
            Post("curtidas", 2000, 200, 0, 0, 8000),
            Post("salvo", 2000, 40, 60, 30, 15000),
            Post("novo", 50, 40, 30, 30, 20000),
            Post("foto", 5000, 900, 200, 100, null, "FEED"),
        ]);
        Assert.Equal(["salvo", "curtidas", "novo"], ranked.Select(r => r.Post.ExternalId)); // foto fica fora: só vídeo
        Assert.True(ranked[0].Score > ranked[1].Score);
        Assert.False(ranked[2].EnoughData);
        Assert.Contains("Pouco dado", ranked[2].Reason);
        Assert.Contains("60 salvamentos", ranked[0].Reason);
    }

    [Fact]
    public void Insights_parse_both_shapes_and_ignore_garbage()
    {
        using var doc = JsonDocument.Parse("""
            {"data":[
              {"name":"reach","period":"lifetime","values":[{"value":1234}]},
              {"name":"views","total_value":{"value":5678}},
              {"name":"saved","values":[]},
              {"name":"likes","values":[{"value":"x"}]}
            ]}
            """);
        var m = InstagramInsightsReader.ParseInsights(doc.RootElement);
        Assert.Equal(1234, m["reach"]);
        Assert.Equal(5678, m["views"]);
        Assert.False(m.ContainsKey("saved"));
        Assert.False(m.ContainsKey("likes"));
    }

    [Fact]
    public void Facebook_insights_accept_numbers_and_per_type_objects()
    {
        using var doc = JsonDocument.Parse("""
            {"data":[
              {"name":"blue_reels_play_count","period":"lifetime","values":[{"value":812}]},
              {"name":"post_video_social_actions","values":[{"value":{"COMMENT":3,"SHARE":5}}]},
              {"name":"post_video_likes_by_reaction_type","values":[{"value":{"REACTION_LIKE":20,"REACTION_LOVE":4,"x":"y"}}]},
              {"name":"post_video_avg_time_watched","values":[]}
            ]}
            """);
        var m = FacebookInsightsReader.Parse(doc.RootElement);
        Assert.Equal(812, m["blue_reels_play_count"]);
        Assert.Equal(8, m["post_video_social_actions"]);
        Assert.Equal(24, m["post_video_likes_by_reaction_type"]);
        Assert.False(m.ContainsKey("post_video_avg_time_watched"));
    }

    [Fact]
    public void Negative_metric_is_refused()
    {
        var p = new OrganicPost(BrandKeys.MedlyCare, SocialChannel.Instagram, "x", Now);
        Assert.Throws<ArgumentOutOfRangeException>(() => p.SetMetrics(1, -1, 0, 0, 0, 0, 0, null, Now));
    }
}

public class OrganicApiPostgresTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    public OrganicApiPostgresTests(PostgresWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Sync_is_idempotent_and_ranking_orders_by_score()
    {
        var client = _factory.WithWebHostBuilder(b => b.UseSetting("Organic:Reader", "Fake")).CreateClient();

        var first = await (await client.PostAsync("/admin/brands/medlycare/organic/sync", null)).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await client.PostAsync("/admin/brands/medlycare/organic/sync", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(first.EnumerateArray().Single().GetProperty("ok").GetBoolean());
        var r2 = second.EnumerateArray().Single();
        Assert.Equal(0, r2.GetProperty("inserted").GetInt32());
        Assert.Equal(4, r2.GetProperty("updated").GetInt32());

        var ranking = await client.GetFromJsonAsync<JsonElement>("/admin/brands/medlycare/organic/ranking?days=365");
        var ids = ranking.EnumerateArray().Select(r => r.GetProperty("externalId").GetString()).ToList();
        Assert.Equal(4, ids.Count);
        Assert.Equal("fake-ig-4", ids[^1]); // 70 pessoas alcançadas: pouco dado
        Assert.Equal(1, ranking[0].GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task Without_reader_sync_says_why()
    {
        var res = await _factory.CreateClient().PostAsync("/admin/brands/medlycare/organic/sync", null);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("desligada", await res.Content.ReadAsStringAsync());
    }
}
