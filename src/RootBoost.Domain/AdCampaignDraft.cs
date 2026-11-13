namespace RootBoost.Domain;

public enum AdCampaignStatus
{
    Draft,
    Approved,
    Creating,
    Created,
    Failed
}

/// <summary>Objetivo da campanha, no vocabulário de resultado (ODAX) da Meta.</summary>
public enum AdObjective
{
    Traffic,
    Leads
}

/// <summary>
/// Campanha proposta pela IA (RB-31) a partir de um vídeo pronto. Regra da casa: a IA monta, a pessoa confere e aprova,
/// e a campanha nasce PAUSADA na plataforma. Quem ativa e começa a gastar é sempre a pessoa, no Gerenciador de Anúncios.
/// Mexeu em qualquer campo depois de aprovar, volta pra rascunho.
/// </summary>
public sealed class AdCampaignDraft
{
    public const long MinDailyBudgetCents = 600;    // R$ 6,00 por dia: abaixo disso a Meta não entrega
    public const long MaxDailyBudgetCents = 50_000; // teto de segurança pra campanha criada pela plataforma (R$ 500/dia)

    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public Guid? CreativeJobId { get; private set; }
    public string Name { get; private set; } = default!;
    public AdObjective Objective { get; private set; }
    /// <summary>Orçamento diário em centavos da moeda da conta.</summary>
    public long DailyBudgetCents { get; private set; }
    /// <summary>
    /// Teste com prazo (13/11, dinheiro contado): com dias, a campanha vai com orçamento total (diário x dias) e data de
    /// fim, e a Meta não passa do total. Nulo = sem fim, orçamento diário.
    /// </summary>
    public int? TestDays { get; private set; }
    /// <summary>Quando o conjunto termina, gravado ao criar na plataforma.</summary>
    public DateTimeOffset? EndsAt { get; private set; }
    public const int MaxTestDays = 30;
    public long? TotalBudgetCents => TestDays is int d ? DailyBudgetCents * d : null;
    public string CountriesCsv { get; private set; } = "BR";
    public int AgeMin { get; private set; } = 18;
    public int AgeMax { get; private set; } = 65;
    /// <summary>Vídeo do anúncio (URL https pública, a Meta baixa pelo link).</summary>
    public string VideoUrl { get; private set; } = default!;
    public string? ThumbnailUrl { get; private set; }
    public string PrimaryText { get; private set; } = default!;
    public string Headline { get; private set; } = default!;
    public string CallToAction { get; private set; } = "LEARN_MORE";
    public string LinkUrl { get; private set; } = default!;
    /// <summary>Por que a IA propôs assim (público, orçamento, texto): fica visível pra pessoa decidir.</summary>
    public string Rationale { get; private set; } = string.Empty;

    public AdCampaignStatus Status { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Ids criados na plataforma, gravados um a um: retomar depois de falha não duplica o que já existe.</summary>
    public string? ExternalVideoId { get; private set; }
    public string? ExternalCampaignId { get; private set; }
    public string? ExternalAdSetId { get; private set; }
    public string? ExternalCreativeId { get; private set; }
    public string? ExternalAdId { get; private set; }
    public string? Error { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static readonly string[] CallsToAction = ["LEARN_MORE", "SIGN_UP", "SHOP_NOW", "CONTACT_US", "BOOK_NOW", "GET_OFFER", "SUBSCRIBE"];

    private AdCampaignDraft() { }

    public AdCampaignDraft(string brandKey, Guid? creativeJobId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        CreativeJobId = creativeJobId;
        Status = AdCampaignStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public IReadOnlyList<string> Countries => CountriesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Grava a proposta (da IA ou editada pela pessoa). Volta pra rascunho se já estava aprovada.</summary>
    public void Update(string name, AdObjective objective, long dailyBudgetCents, IEnumerable<string> countries, int ageMin, int ageMax,
        string videoUrl, string? thumbnailUrl, string primaryText, string headline, string callToAction, string linkUrl, string? rationale,
        DateTimeOffset now)
    {
        if (Status is AdCampaignStatus.Creating or AdCampaignStatus.Created) throw new InvalidOperationException("campanha já enviada pra plataforma");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("nome vazio", nameof(name));
        if (dailyBudgetCents is < MinDailyBudgetCents or > MaxDailyBudgetCents)
            throw new ArgumentException($"orçamento diário entre {MinDailyBudgetCents / 100m:0.00} e {MaxDailyBudgetCents / 100m:0.00}", nameof(dailyBudgetCents));
        var list = countries.Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length == 2 && c.All(char.IsLetter)).Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException("informe ao menos um país (código de 2 letras)", nameof(countries));
        if (ageMin < 18 || ageMax > 65 || ageMin > ageMax) throw new ArgumentException("idade entre 18 e 65", nameof(ageMin));
        if (string.IsNullOrWhiteSpace(primaryText) || primaryText.Length > 2000) throw new ArgumentException("texto principal vazio ou longo demais", nameof(primaryText));
        if (string.IsNullOrWhiteSpace(headline) || headline.Length > 255) throw new ArgumentException("título vazio ou longo demais", nameof(headline));
        if (!CallsToAction.Contains(callToAction)) throw new ArgumentException("chamada pra ação desconhecida", nameof(callToAction));

        Name = name.Trim()[..Math.Min(name.Trim().Length, 200)];
        Objective = objective;
        DailyBudgetCents = dailyBudgetCents;
        CountriesCsv = string.Join(',', list);
        AgeMin = ageMin;
        AgeMax = ageMax;
        VideoUrl = Https(videoUrl, nameof(videoUrl))!;
        ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl) ? null : Https(thumbnailUrl, nameof(thumbnailUrl));
        PrimaryText = primaryText.Trim();
        Headline = headline.Trim();
        CallToAction = callToAction;
        LinkUrl = Https(linkUrl, nameof(linkUrl))!;
        Rationale = (rationale ?? string.Empty).Trim();
        if (Status is AdCampaignStatus.Approved or AdCampaignStatus.Failed) Status = AdCampaignStatus.Draft;
        ApprovedBy = null;
        ApprovedAt = null;
        UpdatedAt = now;
    }

    /// <summary>Dias do teste (1 a 30) ou nulo pra sem fim. Mudar depois de aprovar volta pra rascunho.</summary>
    public void SetTestDays(int? days, DateTimeOffset now)
    {
        if (Status is AdCampaignStatus.Creating or AdCampaignStatus.Created) throw new InvalidOperationException("campanha já enviada pra plataforma");
        if (days is < 1 or > MaxTestDays) throw new ArgumentException($"dias do teste entre 1 e {MaxTestDays}", nameof(days));
        if (days == TestDays) return;
        TestDays = days;
        if (Status is AdCampaignStatus.Approved or AdCampaignStatus.Failed) Status = AdCampaignStatus.Draft;
        ApprovedBy = null;
        ApprovedAt = null;
        UpdatedAt = now;
    }

    public void SetEndsAt(DateTimeOffset endsAt) => EndsAt = endsAt;

    /// <summary>
    /// Sai da lista do Console (13/11): rascunho, aprovada, com falha ou já pausada. Nunca a que está sendo criada nem a
    /// criada sem pausa (pode estar gastando: essa se pausa primeiro). Remover não apaga nada na plataforma.
    /// </summary>
    public bool CanRemove => Status is AdCampaignStatus.Draft or AdCampaignStatus.Approved or AdCampaignStatus.Failed
        || (Status == AdCampaignStatus.Created && PausedAt is not null);

    /// <summary>Aprovação humana: só a partir daqui a plataforma pode criar a campanha (sempre pausada).</summary>
    public void Approve(string approvedBy, DateTimeOffset now)
    {
        if (Status != AdCampaignStatus.Draft) throw new InvalidOperationException("só rascunho pode ser aprovado");
        if (string.IsNullOrWhiteSpace(VideoUrl)) throw new InvalidOperationException("rascunho incompleto");
        Status = AdCampaignStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = now;
        Error = null;
        UpdatedAt = now;
    }

    public void StartCreating(DateTimeOffset now)
    {
        if (Status is not (AdCampaignStatus.Approved or AdCampaignStatus.Failed) || ApprovedAt is null)
            throw new InvalidOperationException("campanha precisa estar aprovada");
        Status = AdCampaignStatus.Creating;
        Attempts++;
        UpdatedAt = now;
    }

    public void SetVideo(string id) => ExternalVideoId ??= id;
    public void SetCampaign(string id) => ExternalCampaignId ??= id;
    public void SetAdSet(string id) => ExternalAdSetId ??= id;
    public void SetCreative(string id) => ExternalCreativeId ??= id;

    public void Created(string adId, DateTimeOffset now)
    {
        ExternalAdId = adId;
        Status = AdCampaignStatus.Created;
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>Pausada pela plataforma a pedido da pessoa (RB-52). Ativar de novo é no Gerenciador de Anúncios.</summary>
    public DateTimeOffset? PausedAt { get; private set; }
    public string? PausedBy { get; private set; }

    public void MarkPaused(string by, DateTimeOffset now)
    {
        if (Status != AdCampaignStatus.Created) throw new InvalidOperationException("só campanha criada pode ser pausada");
        PausedAt = now;
        PausedBy = by;
        UpdatedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = AdCampaignStatus.Failed;
        Error = error.Length > 1000 ? error[..1000] : error;
        UpdatedAt = now;
    }

    private static string? Https(string? url, string param)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("link precisa ser https", param);
        return u.ToString();
    }
}
