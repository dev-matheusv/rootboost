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

    /// <summary>Reels: capa própria (JPEG https, até 8 MB, ideal 9:16). Tem prioridade sobre <see cref="ThumbOffsetMs"/>.</summary>
    public string? CoverUrl { get; private set; }

    /// <summary>Reels: quadro do vídeo usado como capa, em milissegundos (0 = primeiro quadro). Ignorado se houver capa.</summary>
    public int? ThumbOffsetMs { get; private set; }

    /// <summary>Reels: aparece também no feed (true) ou só na aba Reels (false).</summary>
    public bool ShareToFeed { get; private set; } = true;
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
    /// <summary>Quando o lembrete de "sai em breve e ainda não foi aprovado" foi enviado (RB-37). Reagendar zera.</summary>
    public DateTimeOffset? ReminderSentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Publication() { }

    public Publication(string brandKey, SocialChannel channel, string mediaUrl, string caption,
        DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        EnsureHttps(mediaUrl);
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

    /// <summary>Troca o vídeo ou a imagem. Conteúdo novo: a aprovação anterior não vale mais.</summary>
    public void ChangeMedia(string mediaUrl, DateTimeOffset now)
    {
        EnsureEditable();
        EnsureHttps(mediaUrl);
        if (mediaUrl == MediaUrl) return;
        MediaUrl = mediaUrl;
        CaptionVersion++;
        ResetApproval();
        UpdatedAt = now;
    }

    /// <summary>
    /// Capa e destino do Reels. Faz parte do que foi aprovado: mudou, volta pra rascunho e o container antigo é descartado.
    /// </summary>
    public void SetReelOptions(string? coverUrl, int? thumbOffsetMs, bool shareToFeed, DateTimeOffset now)
    {
        EnsureEditable();
        coverUrl = string.IsNullOrWhiteSpace(coverUrl) ? null : coverUrl.Trim();
        if (coverUrl is not null)
        {
            EnsureHttps(coverUrl);
            var path = new Uri(coverUrl).AbsolutePath;
            if (!path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("a capa do Reels precisa ser JPEG (.jpg)", nameof(coverUrl));
        }
        if (thumbOffsetMs is < 0) throw new ArgumentException("o quadro da capa não pode ser negativo", nameof(thumbOffsetMs));
        if (coverUrl == CoverUrl && thumbOffsetMs == ThumbOffsetMs && shareToFeed == ShareToFeed) return;
        CoverUrl = coverUrl;
        ThumbOffsetMs = thumbOffsetMs;
        ShareToFeed = shareToFeed;
        CaptionVersion++;
        ResetApproval();
        UpdatedAt = now;
    }

    /// <summary>Só o que ainda não foi pro ar (nem está indo) pode ser apagado.</summary>
    public bool CanDelete => Status is PublicationStatus.Draft or PublicationStatus.Approved or PublicationStatus.Failed;

    public void Reschedule(DateTimeOffset scheduledFor, DateTimeOffset now)
    {
        EnsureEditable();
        ScheduledFor = scheduledFor.ToUniversalTime();
        ReminderSentAt = null; // data nova: se continuar sem aprovação, lembra de novo
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

    /// <summary>Desfaz a aprovação (botão "Desfazer" do Console): volta pra rascunho enquanto não foi pro ar.</summary>
    public void Unapprove(DateTimeOffset now)
    {
        if (Status != PublicationStatus.Approved)
            throw new InvalidOperationException($"só aprovado pode voltar pra rascunho (status atual: {Status})");
        ResetApproval();
        UpdatedAt = now;
    }

    public void MarkReminded(DateTimeOffset now) => ReminderSentAt = now;

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

    /// <summary>Canal manual (LinkedIn): o humano publicou fora e registra. Só depois de aprovado.</summary>
    public void MarkPublishedManually(string? url, DateTimeOffset now)
    {
        if (Status != PublicationStatus.Approved)
            throw new InvalidOperationException("só post aprovado pode ser marcado como publicado");
        if (url is { Length: > 0 } && (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("link do post precisa ser https", nameof(url));
        MarkPublished("manual", string.IsNullOrWhiteSpace(url) ? null : url, now);
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

    /// <summary>
    /// Tentar de novo um post que falhou: volta pra aprovado (a aprovação da legenda continua valendo) e sai na próxima
    /// rodada do job. Mantém o id já criado no canal, pra reaproveitar o vídeo enviado em vez de subir outro.
    /// </summary>
    public void Retry(DateTimeOffset now)
    {
        if (Status != PublicationStatus.Failed) throw new InvalidOperationException($"só post que falhou pode tentar de novo (status atual: {Status})");
        if (ApprovedBy is null) throw new InvalidOperationException("post sem aprovação: aprove antes");
        Status = PublicationStatus.Approved;
        Attempts = 0;
        Error = null;
        UpdatedAt = now;
    }

    private void EnsureEditable()
    {
        if (Status is PublicationStatus.Publishing or PublicationStatus.Published)
            throw new InvalidOperationException($"publicação em {Status} não pode ser alterada");
    }

    /// <summary>
    /// Conteúdo mudou: volta pra rascunho e descarta o container já preparado no canal (ele carrega a legenda e a
    /// mídia antigas; reaproveitar publicaria o conteúdo errado). Tentativas e erro zeram: é um conteúdo novo.
    /// </summary>
    private void ResetApproval()
    {
        Status = PublicationStatus.Draft;
        ApprovedBy = null;
        ApprovedAt = null;
        ApprovedCaptionVersion = null;
        ExternalId = null;
        PublishingStartedAt = null;
        Attempts = 0;
        Error = null;
    }

    private static void EnsureHttps(string mediaUrl)
    {
        if (string.IsNullOrWhiteSpace(mediaUrl) || !Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("a mídia precisa de URL https pública", nameof(mediaUrl));
    }
}
