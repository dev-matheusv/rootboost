using RootBoost.Domain;

namespace RootBoost.Application.Tests;

/// <summary>O inventário de prova é o que impede a IA de inventar. Estas regras não podem afrouxar.</summary>
public class ProofInventoryTests
{
    private static readonly DateOnly Today = new(2026, 10, 23);
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static ProofItem Fact(string source = "playbook 23/10/2026", DateOnly? verifiedAt = null, DateOnly? reviewBy = null) =>
        new(BrandKeys.MedlyCare, ProofKind.Offer, "3 meses grátis, sem cartão, sem fidelidade", source,
            verifiedAt ?? Today, reviewBy, Today, Now);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_fact_without_source_never_enters_the_inventory(string source) =>
        Assert.Throws<ArgumentException>(() => Fact(source: source));

    [Fact]
    public void A_fact_cannot_be_verified_in_the_future() =>
        Assert.Throws<ArgumentException>(() => Fact(verifiedAt: Today.AddDays(1)));

    [Fact]
    public void Review_date_cannot_be_before_verification() =>
        Assert.Throws<ArgumentException>(() => Fact(reviewBy: Today.AddDays(-1)));

    [Fact]
    public void Retired_fact_stays_in_history_but_is_no_longer_active()
    {
        var fact = Fact();
        fact.Retire();
        Assert.False(fact.Active);
        Assert.Equal("3 meses grátis, sem cartão, sem fidelidade", fact.Statement);
    }

    [Fact]
    public void Fact_past_its_review_date_asks_to_be_checked_again()
    {
        var fact = Fact(reviewBy: Today.AddDays(30));
        Assert.False(fact.NeedsReview(Today));
        Assert.True(fact.NeedsReview(Today.AddDays(30)));
        fact.Retire();
        Assert.False(fact.NeedsReview(Today.AddDays(60)));
    }

    [Fact]
    public void Customer_phrase_needs_a_source()
    {
        Assert.Throws<ArgumentException>(() => new CustomerPhrase(BrandKeys.MedlyCare, "suporte é fraco", "", Today));
        var phrase = new CustomerPhrase(BrandKeys.MedlyCare, "  suporte é fraco  ", "Reclame Aqui, out/2026", Today);
        Assert.Equal("suporte é fraco", phrase.Text);
    }

    [Fact]
    public void Brand_voice_survives_the_round_trip_through_json()
    {
        var brand = new Brand(BrandKeys.MedlyCare, "MedlyCare", "pt-BR", Now);
        var voice = new BrandVoice("profissional de clínica pequena", ["frase curta", "fundador fala como fundador"],
            new Dictionary<string, string> { ["absenteísmo"] = "falta" }, ["revolucione"],
            [new BrandObjection("e meus dados?", "migração com prévia e sem duplicar")]);

        brand.SetVoice(voice);
        var read = brand.Voice;

        Assert.Equal("profissional de clínica pequena", read.Audience);
        Assert.Equal("falta", read.WordSwaps["absenteísmo"]);
        Assert.Equal("migração com prévia e sem duplicar", read.Objections.Single().Answer);
    }

    [Fact]
    public void A_brand_without_voice_reads_as_empty() =>
        Assert.Same(BrandVoice.Empty, new Brand(BrandKeys.Store, "Loja", "en-US", Now).Voice);
}
