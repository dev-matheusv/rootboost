using System.Text.Json;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Application.Tests;

public class ScriptContractTests
{
    // Formato real aprovado (combo-8 psicologia): abertura + 3 blocos = 5 falas.
    private const string Organic = """
        { "nome": "combo-8-psicologia", "abertura": ["Psicologia sem planilha", "Agenda, evolução e confirmação juntas"],
          "blocos": [
            { "titulo": "Sessão toda semana", "sub": "Marca uma vez, o sistema repete", "trechos": [["psico1", 1, 4.3, 2], ["psico1", 5.9, 25.4, 2]] },
            { "titulo": "Evolução em minutos", "sub": "Texto pronto com nome e data", "trechos": [["psico2", 0.8, 14, 1.2]] },
            { "titulo": "Confirmação pelo WhatsApp", "sub": "Você sabe quem vem amanhã", "trechos": [["confirmacoes", 3, 9]] } ] }
        """;

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;
    private static JsonElement Lines(int n) => J(JsonSerializer.Serialize(Enumerable.Range(1, n).Select(i => $"Fala {i}.")));

    [Fact]
    public void Organic_with_opening_needs_blocks_plus_two_lines()
    {
        Assert.Empty(ScriptContract.Validate(J(Organic), Lines(5)));
        var errors = ScriptContract.Validate(J(Organic), Lines(4));
        Assert.Contains(errors, e => e.Contains("4 frases, esperado 5"));
    }

    [Fact]
    public void Ad_without_opening_needs_blocks_plus_one_line()
    {
        const string ad = """
            { "nome": "anuncio", "abertura": null, "fecho": ["Teste 3 meses grátis", "Sem cartão. medlycare.com.br"],
              "blocos": [ { "titulo": "Paciente que falta é prejuízo", "sub": "", "trechos": [["confirmacoes", 3, 9, 1.4]] },
                          { "titulo": "Confirmação pelo WhatsApp", "sub": "Um clique", "trechos": [["confirmacoes", 11.2, 16.5, 1.3]] } ] }
            """;
        Assert.Empty(ScriptContract.Validate(J(ad), Lines(3)));
        Assert.NotEmpty(ScriptContract.Validate(J(ad), Lines(4)));
    }

    [Fact]
    public void Broken_clips_and_dashes_are_listed()
    {
        const string bad = """
            { "nome": "x", "blocos": [ { "titulo": "Bloco — um", "trechos": [["", 5, 2], ["a", "b", 3], ["a", 1, 2, 0]] } ] }
            """;
        var errors = ScriptContract.Validate(J(bad), J("""["Uma fala – com meia-risca", "Final."]"""));
        Assert.Contains(errors, e => e.Contains("\"titulo\" tem travessão"));
        Assert.Contains(errors, e => e.Contains("trecho 1 sem fonte"));
        Assert.Contains(errors, e => e.Contains("trecho 1: fim (2) precisa ser depois do início (5)"));
        Assert.Contains(errors, e => e.Contains("trecho 2: início e fim precisam ser números"));
        Assert.Contains(errors, e => e.Contains("trecho 3: velocidade precisa ser maior que zero"));
        Assert.Contains(errors, e => e.Contains("fala 1 tem travessão"));
    }

    [Fact]
    public void Wrong_shapes_do_not_throw()
    {
        Assert.NotEmpty(ScriptContract.Validate(J("[]"), J("[]")));
        Assert.NotEmpty(ScriptContract.Validate(J("""{ "nome": "x", "blocos": [] }"""), J("{}")));
    }
}

public class CreativeJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(15);

    private static CreativeJob Queued()
    {
        var job = new CreativeJob(BrandKeys.MedlyCare, CreativeKind.Organic, "Psicologia", "matheus2b", Now);
        job.SetScript("""{"nome":"x"}""", """["a","b"]""", Now);
        job.QueueRender(Now);
        return job;
    }

    [Fact]
    public void Worker_owns_the_job_until_the_lease_expires()
    {
        var job = Queued();
        job.Claim("pc-casa", Now, Lease);
        Assert.False(job.CanBeClaimed(Now.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() => job.ReportProgress("outro-pc", 50, null, Now, Lease));

        job.ReportProgress("pc-casa", 40, "voz pronta", Now.AddMinutes(10), Lease); // renova o prazo
        Assert.False(job.CanBeClaimed(Now.AddMinutes(20)));
        Assert.True(job.CanBeClaimed(Now.AddMinutes(26)));

        job.Claim("outro-pc", Now.AddMinutes(26), Lease); // PC desligou: outro pega
        Assert.Throws<InvalidOperationException>(() => job.Deliver("pc-casa", [new("video", "https://x/v.mp4")], null, 7, Now));
        Assert.Equal(2, job.Attempts);
    }

    [Fact]
    public void Delivery_goes_to_review_and_requires_https_files()
    {
        var job = Queued();
        job.Claim("pc", Now, Lease);
        Assert.Throws<ArgumentException>(() => job.Deliver("pc", [new("video", "http://x/v.mp4")], null, 1, Now));
        Assert.Throws<ArgumentException>(() => job.Deliver("pc", [], null, 1, Now));

        job.Deliver("pc", [new("video-instagram", "https://x/ig.mp4"), new("video", "https://x/v.mp4")], ["fator 2,1 no bloco 2"], 42, Now);
        Assert.Equal(CreativeJobStatus.Review, job.Status);
        Assert.Equal(42, job.Seed);
        Assert.Equal(2, job.Outputs.Count);
        Assert.Equal("fator 2,1 no bloco 2", Assert.Single(job.Warnings));
        Assert.Null(job.WorkerId);
    }

    [Fact]
    public void Failure_retries_until_the_limit_then_waits_for_a_human()
    {
        var job = Queued();
        for (var i = 1; i < CreativeJob.MaxAttempts; i++)
        {
            job.Claim("pc", Now, Lease);
            job.Fail("pc", "palavra sem par", retry: true, Now);
            Assert.Equal(CreativeJobStatus.Queued, job.Status);
        }
        job.Claim("pc", Now, Lease);
        job.Fail("pc", "palavra sem par", retry: true, Now);
        Assert.Equal(CreativeJobStatus.Failed, job.Status);
        Assert.Equal("palavra sem par", job.Error);

        job.QueueRender(Now); // humano manda de novo
        Assert.Equal(0, job.Attempts);
    }

    [Fact]
    public void Script_cannot_change_while_rendering_and_editing_after_review_drops_files()
    {
        var job = Queued();
        Assert.Throws<InvalidOperationException>(() => job.SetScript("{}", "[]", Now));
        job.Claim("pc", Now, Lease);
        job.Deliver("pc", [new("video", "https://x/v.mp4")], null, 1, Now);
        job.SetScript("""{"nome":"y"}""", """["a","b"]""", Now);
        Assert.Equal(CreativeJobStatus.ScriptReady, job.Status);
        Assert.Empty(job.Outputs);
    }
}
