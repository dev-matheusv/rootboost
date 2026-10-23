namespace RootBoost.Domain;

/// <summary>Tipo de fato do inventário de prova. Casa com o "requiresProof" de knowledge/copy/levers.json.</summary>
public enum ProofKind
{
    Offer,
    Feature,
    Price,
    Metric,
    Testimonial,
    Credential,
    Scarcity,

    /// <summary>O que a marca NUNCA pode dizer (ex.: "nunca sai do ar"). A checklist bloqueia se aparecer.</summary>
    Forbidden
}

/// <summary>
/// Um fato do inventário de prova: o que é verdade HOJE sobre a marca. A IA só afirma o que estiver aqui,
/// ativo. Sem fonte não entra: é o que impede depoimento, número ou urgência inventados.
/// </summary>
public sealed class ProofItem
{
    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public ProofKind Kind { get; private set; }
    public string Statement { get; private set; } = default!;
    public string Source { get; private set; } = default!;
    public DateOnly VerifiedAt { get; private set; }

    /// <summary>Quando o fato precisa ser conferido de novo (ex.: "suporte com quem fez o sistema" muda se o time crescer).</summary>
    public DateOnly? ReviewBy { get; private set; }

    public bool Active { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ProofItem() { }

    public ProofItem(string brandKey, ProofKind kind, string statement, string source, DateOnly verifiedAt,
        DateOnly? reviewBy, DateOnly today, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(statement)) throw new ArgumentException("o fato precisa de texto", nameof(statement));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("fato sem fonte não entra no inventário", nameof(source));
        if (verifiedAt > today) throw new ArgumentException("data de verificação no futuro", nameof(verifiedAt));
        if (reviewBy is { } r && r < verifiedAt) throw new ArgumentException("revisão antes da verificação", nameof(reviewBy));

        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Kind = kind;
        Statement = statement.Trim();
        Source = source.Trim();
        VerifiedAt = verifiedAt;
        ReviewBy = reviewBy;
        Active = true;
        CreatedAt = createdAt;
    }

    /// <summary>Deixou de ser verdade (recurso saiu, oferta mudou). Fica no histórico, mas a IA não usa mais.</summary>
    public void Retire() => Active = false;

    /// <summary>Precisa ser reconferido: a IA ainda vê, mas o Console avisa.</summary>
    public bool NeedsReview(DateOnly today) => Active && ReviewBy is { } r && r <= today;
}

/// <summary>Frase real de cliente (ou de público do nicho), com fonte e data. Nunca resumida.</summary>
public sealed class CustomerPhrase
{
    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public string Text { get; private set; } = default!;
    public string Source { get; private set; } = default!;
    public DateOnly CollectedAt { get; private set; }

    private CustomerPhrase() { }

    public CustomerPhrase(string brandKey, string text, string source, DateOnly collectedAt)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("frase vazia", nameof(text));
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("frase sem fonte não entra", nameof(source));
        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Text = text.Trim();
        Source = source.Trim();
        CollectedAt = collectedAt;
    }
}

/// <summary>Voz da marca: quem é o público, como a marca fala, que palavras troca e quais nunca usa.</summary>
public sealed record BrandVoice(
    string Audience,
    IReadOnlyList<string> Tone,
    IReadOnlyDictionary<string, string> WordSwaps,
    IReadOnlyList<string> BannedTerms,
    IReadOnlyList<BrandObjection> Objections)
{
    public static readonly BrandVoice Empty = new("", [], new Dictionary<string, string>(), [], []);
}

/// <summary>Objeção do público e a resposta, que tem que se apoiar num fato do inventário.</summary>
public sealed record BrandObjection(string Objection, string Answer);
