namespace RootBoost.Domain;

public enum SocialChannel
{
    Instagram,
    Facebook,
    LinkedIn
}

public enum PublicationStatus
{
    Draft,
    Approved,
    Publishing,
    Published,
    Failed
}

/// <summary>
/// Um post agendado num canal. Regra da casa: nada publica sem aprovação humana, e a aprovação vale pra uma
/// versão exata da legenda. Mexeu na legenda depois de aprovar, volta pra rascunho.
/// </summary>
public sealed class Publication
{
    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public SocialChannel Channel { get; private set; }

    /// <summary>Vídeo ou imagem já pronto (o canal precisa de URL pública).</summary>
    public string MediaUrl { get; private set; } = default!;

    public string Caption { get; private set; } = default!;
    public int CaptionVersion { get; private set; }
    public DateTimeOffset ScheduledFor { get; private set; }
    public PublicationStatus Status { get; private set; }

    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public int? ApprovedCaptionVersion { get; private set; }

    /// <summary>Id do container ou do post no canal. Gravado ANTES de publicar: é o que impede publicar duas vezes.</summary>
    public string? ExternalId { get; private set; }

    public string? PublishedUrl { get; private set; }
    public string? Error { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset? PublishingStartedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Publication() { }

    public Publication(string brandKey, SocialChannel channel, string mediaUrl, string caption,
        DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl) || !Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("a mídia precisa de URL https pública", nameof(mediaUrl));
        if (string.IsNullOrWhiteSpace(caption)) throw new ArgumentException("legenda vazia", nameof(caption));

        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Channel = channel;
        MediaUrl = mediaUrl;
        Caption = caption.Trim();
        CaptionVersion = 1;
        ScheduledFor = scheduledFor.ToUniversalTime();
        Status = PublicationStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void EditCaption(string caption, DateTimeOffset now)
    {
        EnsureEditable();
        if (string.IsNullOrWhiteSpace(caption)) throw new ArgumentException("legenda vazia", nameof(caption));
        if (caption.Trim() == Caption) return;
        Caption = caption.Trim();
        CaptionVersion++;
        ResetApproval();
        UpdatedAt = now;
    }

    public void Reschedule(DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        EnsureEditable();
        ScheduledFor = scheduledFor.ToUniversalTime();
        UpdatedAt = now;
    }

    /// <summary>Aprovação humana da versão atual da legenda. A checklist automática roda antes (quem chama garante).</summary>
    public void Approve(string approvedBy, DateTimeOffset now)
    {
        if (Status != PublicationStatus.Draft)
            throw new InvalidOperationException($"só rascunho pode ser aprovado (status atual: {Status})");
        if (string.IsNullOrWhiteSpace(approvedBy)) throw new ArgumentException("quem aprovou é obrigatório", nameof(approvedBy));
        Status = PublicationStatus.Approved;
        ApprovedBy = approvedBy.Trim();
        ApprovedAt = now;
        ApprovedCaptionVersion = CaptionVersion;
        Error = null;
        UpdatedAt = now;
    }

    public bool IsDue(DateTimeOffset now) => Status == PublicationStatus.Approved && ScheduledFor <= now;

    /// <summary>Ficou travada em Publishing (o processo caiu no meio)? Pode ser retomada com segurança.</summary>
    public bool IsStuck(DateTimeOffset now, TimeSpan after) =>
        Status == PublicationStatus.Publishing && PublishingStartedAt is { } s && now - s >= after;

    public void StartPublishing(DateTimeOffset now)
    {
        if (!IsDue(now) && Status != PublicationStatus.Publishing)
            throw new InvalidOperationException("só publicação aprovada e no horário pode começar");
        if (ApprovedCaptionVersion != CaptionVersion)
            throw new InvalidOperationException("a legenda mudou depois da aprovação");
        Status = PublicationStatus.Publishing;
        PublishingStartedAt = now;
        Attempts++;
        UpdatedAt = now;
    }

    public void RecordExternalId(string externalId, DateTimeOffset now)
    {
        if (Status != PublicationStatus.Publishing) throw new InvalidOperationException("fora de publicação");
        ExternalId = externalId;
        UpdatedAt = now;
    }

    public void MarkPublished(string externalId, string? url, DateTimeOffset now)
    {
        Status = PublicationStatus.Published;
        ExternalId = externalId;
        PublishedUrl = url;
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>Falhou: volta pra fila até o limite de tentativas; passou disso, fica Failed pro humano ver.</summary>
    public void MarkFailed(string error, int maxAttempts, DateTimeOffset now)
    {
        Error = error;
        Status = Attempts >= maxAttempts ? PublicationStatus.Failed : PublicationStatus.Approved;
        UpdatedAt = now;
    }

    private void EnsureEditable()
    {
        if (Status is PublicationStatus.Publishing or PublicationStatus.Published)
            throw new InvalidOperationException($"publicação em {Status} não pode ser alterada");
    }

    private void ResetApproval()
    {
        Status = PublicationStatus.Draft;
        ApprovedBy = null;
        ApprovedAt = null;
        ApprovedCaptionVersion = null;
    }
}
