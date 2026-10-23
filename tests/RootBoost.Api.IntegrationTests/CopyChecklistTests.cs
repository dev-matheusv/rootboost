using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Copy;
using RootBoost.Domain;
using RootBoost.Infrastructure.Knowledge;

namespace RootBoost.Api.IntegrationTests;

/// <summary>
/// Checklist automática contra o knowledge/ real. Cada caso é um erro que já aconteceu ou que a lei e os canais
/// punem. Se alguém afrouxar uma regra no knowledge/, estes testes avisam.
/// </summary>
public class CopyChecklistTests
{
    private static readonly IKnowledgeBase Kb = FileKnowledgeBase.Load(Path.Combine(AppContext.BaseDirectory, "knowledge"));
    private static readonly DateOnly Today = new(2026, 10, 23);
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static BrandProfile MedlyCare(bool withScarcityProof = false)
    {
        var brand = new Brand(BrandKeys.MedlyCare, "MedlyCare", "pt-BR", Now);
        var voice = new BrandVoice("dono de clínica pequena", [], new Dictionary<string, string> { ["absenteísmo"] = "falta" },
            ["potencializar sua clínica"], []);
        var proof = new List<ProofItem>
        {
            new(BrandKeys.MedlyCare, ProofKind.Forbidden, "Dizer que nunca sai do ar", "playbook", Today, null, Today, Now)
        };
        if (withScarcityProof)
            proof.Add(new(BrandKeys.MedlyCare, ProofKind.Scarcity, "10 vagas de implantação assistida em novembro", "agenda do time", Today, null, Today, Now));
        return new BrandProfile(brand, voice, proof, []);
    }

    private static CheckFinding Finding(CopyCheckResult r, string id) => r.Findings.Single(f => f.Id == id);

    private static CopyCheckResult Check(string text, string? channel = null, string? field = null, BrandProfile? brand = null) =>
        CopyChecklist.Check(new CopyPiece(text, channel, field), Kb, brand ?? MedlyCare());

    [Fact]
    public void Clean_copy_passes_the_automatic_checks()
    {
        var r = Check("Paciente que falta deixa horário vazio. Teste 3 meses grátis, sem cartão.");
        Assert.False(r.Blocked);
        Assert.Equal(CheckStatus.Pass, Finding(r, "style-no-dashes").Status);
        Assert.Equal(CheckStatus.Pass, Finding(r, "style-no-ai-marks").Status);
        Assert.DoesNotContain(r.Findings, f => f.Id.StartsWith("policy:"));
    }

    [Theory]
    [InlineData("Agenda cheia — sem papel")]
    [InlineData("Agenda cheia – sem papel")]
    public void Dashes_block_publication(string text)
    {
        var r = Check(text);
        Assert.True(r.Blocked);
        Assert.Equal(CheckStatus.Fail, Finding(r, "style-no-dashes").Status);
    }

    [Theory]
    [InlineData("Revolucione sua agenda")]
    [InlineData("DESCUBRA como organizar")]
    [InlineData("Dá pra potencializar sua clínica hoje")] // termo proibido da própria marca
    [InlineData("Não é agenda, é tranquilidade. Não é sistema, é tempo livre.")]
    public void Ai_marks_and_brand_banned_terms_block(string text)
    {
        var r = Check(text);
        Assert.True(r.Blocked);
        Assert.Equal(CheckStatus.Fail, Finding(r, "style-no-ai-marks").Status);
    }

    [Fact]
    public void A_single_not_x_but_y_is_allowed() =>
        Assert.Equal(CheckStatus.Pass, Finding(Check("Não é caderno, é agenda que avisa o paciente."), "style-no-ai-marks").Status);

    [Fact]
    public void Google_headline_over_30_chars_blocks()
    {
        var ok = Check("Agenda para fisioterapia", "google-search", "headline"); // 24 caracteres
        Assert.Equal(CheckStatus.Pass, Finding(ok, "channel-char-limits").Status);

        var tooLong = Check("Agenda online para clínicas de fisioterapia", "google-search", "headline");
        Assert.True(tooLong.Blocked);
        Assert.Equal(CheckStatus.Fail, Finding(tooLong, "channel-char-limits").Status);
    }

    [Fact]
    public void Caption_lines_are_checked_one_by_one_in_words_and_chars()
    {
        var r = Check("Paciente faltou\nde novo hoje\ne o horário ficou vazio sem aviso", "video", "caption-line");
        Assert.Equal(CheckStatus.Fail, Finding(r, "channel-char-limits").Status);
        Assert.Contains("1 linha", Finding(r, "channel-char-limits").Message);
    }

    [Fact]
    public void Unknown_channel_limit_asks_for_a_human_instead_of_guessing()
    {
        var r = Check("Texto do anúncio", "meta", "primary-text");
        var f = Finding(r, "channel-char-limits");
        Assert.Equal(CheckStatus.NeedsHuman, f.Status);
    }

    [Theory]
    [InlineData("O MedlyCare nunca sai do ar", "policy:no-absolute-availability")]
    [InlineData("Sistema 100% disponível pra sua clínica", "policy:no-absolute-availability")]
    [InlineData("Você tem dor nas costas?", "policy:no-personal-attributes")]
    [InlineData("Só hoje: 3 meses grátis", "policy:cdc-fake-scarcity")]
    [InlineData("Últimas vagas pra testar", "policy:cdc-fake-scarcity")]
    public void Policy_violations_block(string text, string policyId)
    {
        var r = Check(text);
        Assert.True(r.Blocked);
        Assert.Equal(CheckStatus.Fail, Finding(r, policyId).Status);
    }

    [Fact]
    public void Scarcity_with_real_proof_goes_to_human_review_instead_of_blocking()
    {
        var r = Check("Últimas vagas de implantação assistida em novembro", brand: MedlyCare(withScarcityProof: true));
        var f = Finding(r, "policy:cdc-fake-scarcity");
        Assert.Equal(CheckStatus.NeedsHuman, f.Status);
        Assert.False(f.Blocking);
    }

    [Fact]
    public void Result_promise_is_flagged_for_review()
    {
        var f = Finding(Check("Resultado garantido na primeira semana"), "policy:no-result-promise");
        Assert.Equal(CheckStatus.NeedsHuman, f.Status);
    }

    [Fact]
    public void Brand_word_swaps_are_suggested_without_blocking()
    {
        var r = Check("Acabe com o absenteísmo na clínica");
        var f = Finding(r, "swap:absenteísmo");
        Assert.Equal(CheckStatus.Suggestion, f.Status);
        Assert.Contains("falta", f.Fix);
    }

    [Fact]
    public void Brand_forbidden_list_and_truth_items_always_go_to_a_human()
    {
        var r = Check("Paciente que falta deixa horário vazio.");
        Assert.Equal(CheckStatus.NeedsHuman, Finding(r, "brand-forbidden").Status);
        Assert.Contains("nunca sai do ar", Finding(r, "brand-forbidden").Message);
        Assert.Equal(CheckStatus.NeedsHuman, Finding(r, "truth-in-inventory").Status);
        Assert.Equal(CheckStatus.NeedsHuman, Finding(r, "quality-clarity").Status);
    }

    [Fact]
    public void Every_policy_pattern_compiles_and_severity_is_known()
    {
        foreach (var policy in Kb.Find("channels/policies")!.Items.EnumerateArray())
        {
            Assert.Contains(policy.GetProperty("severity").GetString(), new[] { "block", "review" });
            foreach (var p in policy.GetProperty("patterns").EnumerateArray())
                _ = new System.Text.RegularExpressions.Regex(p.GetString()!);
            if (policy.TryGetProperty("unlessProof", out var u))
                Assert.True(Enum.TryParse<ProofKind>(u.GetString(), out _));
        }
    }
}

public class CopyCheckApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    public CopyCheckApiTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Api_returns_blocked_with_the_reason()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/admin/brands/medlycare/copy/check",
            new { text = "Nunca sai do ar — revolucione sua clínica", channel = "google-search", field = "headline" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("blocked").GetBoolean());
        var failed = body.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("status").GetString() == "Fail").Select(f => f.GetProperty("id").GetString()).ToList();
        Assert.Contains("style-no-dashes", failed);
        Assert.Contains("style-no-ai-marks", failed);
        Assert.Contains("policy:no-absolute-availability", failed);
        Assert.Contains("channel-char-limits", failed);
    }

    [Fact]
    public async Task Empty_text_and_unknown_brand_are_rejected()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/brands/medlycare/copy/check", new { text = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/admin/brands/nao-existe/copy/check", new { text = "oi" })).StatusCode);
    }
}
