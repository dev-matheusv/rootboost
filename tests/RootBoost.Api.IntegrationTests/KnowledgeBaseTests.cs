using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using RootBoost.Infrastructure.Knowledge;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Protege o knowledge/: arquivo quebrado ou regra incoerente não passa no CI. Mudar conhecimento é PR,
/// e este teste é a revisão automática desse PR.
/// </summary>
public class KnowledgeBaseTests : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "knowledge");
    private static readonly FileKnowledgeBase Kb = FileKnowledgeBase.Load(Root);
    private readonly CustomWebApplicationFactory _factory;

    public KnowledgeBaseTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static IEnumerable<JsonElement> Items(string name) => Kb.Find(name)!.Items.EnumerateArray();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString() ?? "";

    [Fact]
    public void All_knowledge_files_load_and_the_playbook_exists()
    {
        var names = Kb.Documents.Select(d => d.Name).ToList();
        Assert.Equal(new[]
        {
            "ads/testing", "channels/limits", "channels/policies", "copy/checklist",
            "copy/hooks", "copy/levers", "learnings/learnings", "video/formats"
        }, names);
        Assert.Contains("Níveis de consciência", Kb.Playbook);
    }

    [Fact]
    public void Truth_channel_and_style_items_always_block_publication()
    {
        var groups = new[] { "truth", "channel", "quality", "style" };
        foreach (var item in Items("copy/checklist"))
        {
            var group = Str(item, "group");
            Assert.Contains(group, groups);
            var blocking = item.GetProperty("blocking").GetBoolean();
            Assert.Equal(group != "quality", blocking);
        }
        var min = Kb.Find("copy/checklist")!.Raw.GetProperty("minScore").GetInt32();
        Assert.InRange(min, 1, 5);
    }

    [Fact]
    public void The_dash_rule_really_catches_dashes()
    {
        var rule = Items("copy/checklist").Single(i => Str(i, "id") == "style-no-dashes");
        var pattern = new Regex(Str(rule, "pattern"));
        Assert.Matches(pattern, "copy com travessão — aqui");
        Assert.Matches(pattern, "e meia-risca – aqui");
        Assert.DoesNotMatch(pattern, "hífen-comum não conta");
    }

    [Fact]
    public void Levers_only_require_known_proof_types_and_say_when_they_are_legitimate()
    {
        var proofTypes = new[] { "offer", "metric", "testimonial", "credential", "scarcity", "price", "feature" };
        foreach (var lever in Items("copy/levers"))
        {
            Assert.False(string.IsNullOrWhiteSpace(Str(lever, "legitimateWhen")), Str(lever, "id"));
            Assert.Contains(Str(lever, "evidence"), new[] { "alta", "média", "baixa" });
            foreach (var proof in lever.GetProperty("requiresProof").EnumerateArray())
                Assert.Contains(proof.GetString(), proofTypes);
        }
        // Os 7 de Cialdini estão todos lá.
        Assert.Equal(7, Items("copy/levers").Count(l => Str(l, "family") == "cialdini"));
    }

    [Fact]
    public void Hooks_awareness_and_formulas_are_the_tag_vocabulary()
    {
        var byKind = Items("copy/hooks").GroupBy(i => Str(i, "kind")).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(6, byKind["hook"]);
        Assert.Equal(5, byKind["awareness"]);
        Assert.True(byKind["formula"] >= 4);
        Assert.Equal(3, byKind.Count);
    }

    [Fact]
    public void Formats_positions_are_fractions_of_the_frame()
    {
        foreach (var item in Items("video/formats").Where(i => Str(i, "kind") == "format"))
            foreach (var prop in new[] { "contentMargin", "captionY", "hookY", "titleY" })
                if (item.TryGetProperty(prop, out var v))
                    Assert.InRange(v.GetDouble(), 0.0, 1.0);
    }

    [Fact]
    public void Limits_are_positive_and_say_when_they_were_checked()
    {
        foreach (var item in Items("channels/limits"))
        {
            Assert.True(item.GetProperty("maxChars").GetInt32() > 0);
            Assert.True(DateOnly.TryParse(Str(item, "checkedAt"), out _), Str(item, "id"));
        }
    }

    [Fact]
    public void Learnings_carry_only_aggregates_with_confidence()
    {
        foreach (var item in Items("learnings/learnings"))
        {
            Assert.Contains(Str(item, "confidence"), new[] { "baixa", "media", "alta" });
            Assert.True(item.GetProperty("brands").GetInt32() >= 1);
        }
    }

    [Theory]
    [InlineData("{not json", "não é JSON válido")]
    [InlineData("""{"updatedAt":"2026-10-23","source":"x","items":[]}""", "version")]
    [InlineData("""{"version":1,"updatedAt":"23/10/2026","source":"x","items":[]}""", "updatedAt")]
    [InlineData("""{"version":1,"updatedAt":"2026-10-23","items":[]}""", "source")]
    [InlineData("""{"version":1,"updatedAt":"2026-10-23","source":"x","items":[{"id":"a"},{"id":"a"}]}""", "id repetido")]
    [InlineData("""{"version":1,"updatedAt":"2026-10-23","source":"x","items":[{"name":"sem id"}]}""", "precisa de 'id'")]
    public void Broken_files_are_rejected_with_a_clear_reason(string json, string reason)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FileKnowledgeBase.Parse("teste", json));
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public async Task Api_lists_documents_and_serves_each_one()
    {
        var client = _factory.CreateClient();

        var index = await client.GetFromJsonAsync<JsonElement>("/admin/knowledge");
        Assert.Equal(8, index.GetProperty("documents").GetArrayLength());

        var checklist = await client.GetFromJsonAsync<JsonElement>("/admin/knowledge/copy/checklist");
        Assert.True(checklist.GetProperty("items").GetArrayLength() > 0);

        var playbook = await client.GetAsync("/admin/knowledge/copy/playbook");
        Assert.Equal("text/markdown", playbook.Content.Headers.ContentType!.MediaType);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/knowledge/nao/existe")).StatusCode);
    }
}
