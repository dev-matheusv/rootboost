namespace RootBoost.Domain;

public enum ContentIdeaStatus
{
    /// <summary>Na pauta, ainda não pedido.</summary>
    Planned,
    /// <summary>Mandado pra fila: a plataforma escreve o roteiro e manda renderizar na vez dele.</summary>
    Queued,
    /// <summary>Virou vídeo (job criado e mandado pro render).</summary>
    InProduction,
    /// <summary>Não deu pra criar (roteiro recusado, IA parada): o motivo fica em <see cref="ContentIdea.Note"/>.</summary>
    Failed,
}

/// <summary>
/// Um tema da pauta de conteúdos da marca (17/11): o pedido pro Estúdio, guardado até a pessoa mandar pra fila. Na fila,
/// a plataforma cria o vídeo, escreve o roteiro e manda renderizar, um por vez, sem a pessoa clicar em cada etapa.
/// </summary>
public sealed class ContentIdea
{
    public const int MaxRequest = 300;

    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public string Title { get; private set; } = default!;
    public string Request { get; private set; } = default!;
    public string? Group { get; private set; }
    public int Order { get; private set; }
    public ContentIdeaStatus Status { get; private set; }
    /// <summary>Na fila: aprova sozinho a voz que passar na conferência, pra gravação seguir sem esperar.</summary>
    public bool AutoVoice { get; private set; }
    public Guid? JobId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ContentIdea() { }

    public ContentIdea(string brandKey, string title, string request, string? group, int order, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Edit(title, request, group, order, now);
        Status = ContentIdeaStatus.Planned;
        CreatedAt = now;
    }

    public void Edit(string title, string request, string? group, int order, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 120) throw new ArgumentException("título de 1 a 120 caracteres", nameof(title));
        if (string.IsNullOrWhiteSpace(request) || request.Trim().Length > MaxRequest) throw new ArgumentException($"pedido de 1 a {MaxRequest} caracteres", nameof(request));
        Title = title.Trim();
        Request = request.Trim();
        Group = string.IsNullOrWhiteSpace(group) ? null : group.Trim()[..Math.Min(group.Trim().Length, 80)];
        Order = order;
        UpdatedAt = now;
    }

    public void Enqueue(bool autoVoice, DateTimeOffset now)
    {
        if (Status is ContentIdeaStatus.Queued or ContentIdeaStatus.InProduction) throw new InvalidOperationException($"\"{Title}\" já está na fila");
        Status = ContentIdeaStatus.Queued;
        AutoVoice = autoVoice;
        Note = null;
        UpdatedAt = now;
    }

    public void Produced(Guid jobId, DateTimeOffset now)
    {
        Status = ContentIdeaStatus.InProduction;
        JobId = jobId;
        Note = null;
        UpdatedAt = now;
    }

    public void Fail(string note, Guid? jobId, DateTimeOffset now)
    {
        Status = ContentIdeaStatus.Failed;
        JobId = jobId ?? JobId;
        Note = note.Length > 500 ? note[..500] : note;
        UpdatedAt = now;
    }

    /// <summary>Volta pra pauta (vídeo apagado ou pra pedir de novo).</summary>
    public void BackToPlan(DateTimeOffset now)
    {
        Status = ContentIdeaStatus.Planned;
        JobId = null;
        Note = null;
        UpdatedAt = now;
    }
}
