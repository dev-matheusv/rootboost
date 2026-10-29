using System.Text.Json;

namespace RootBoost.Domain;

/// <summary>Formato do vídeo: dita duração, gancho e versões por canal (ver o pacote do marketing, etapa 7).</summary>
public enum CreativeKind
{
    Organic,
    Ad,
    Prospecting,
}

/// <summary>
/// Caminho do job do estúdio (RB-42): roteiro escrito (pela IA ou à mão) → humano libera pra render → o worker local
/// puxa e renderiza → revisão → vira Publications em rascunho. Falhou no worker: volta pra fila até o limite.
/// </summary>
public enum CreativeJobStatus
{
    Draft,
    ScriptReady,
    Queued,
    Rendering,
    /// <summary>A voz saiu em duas versões: a pessoa ouve e escolhe antes de gravar a tela e montar (29/10).</summary>
    VoiceReview,
    Review,
    Done,
    Failed,
}

/// <summary>Versão da voz pra ouvir: semente, onde está o áudio (privado) e palavras sem par do alinhamento.</summary>
public sealed record VoiceTake(int Seed, string StorageKey, int Unmatched);

/// <summary>Arquivo entregue pelo worker. O papel diz onde ele vai (ex.: "video-instagram", "video", "cover-instagram").</summary>
public sealed record CreativeOutput(string Role, string Url);

/// <summary>
/// Pedido de criativo de uma marca. Roteiro e falas ficam como JSON (os contratos do pacote: roteiro.json e
/// falas.json), validados na Application antes de entrar aqui. O worker que pegou o job é dono dele até o prazo
/// (lease) vencer; passou do prazo sem notícia, outro worker pode pegar (o PC pode ter desligado no meio).
/// </summary>
public sealed class CreativeJob
{
    public const int MaxAttempts = 3;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public CreativeKind Kind { get; private set; }
    public string Theme { get; private set; } = default!;
    /// <summary>Voz por estilo (ex.: "matheus2a" solta, "matheus2b" pausada). Vazio: o worker usa a padrão da marca.</summary>
    public string? VoiceKey { get; private set; }
    public string? Script { get; private set; }
    public string? Lines { get; private set; }
    public CreativeJobStatus Status { get; private set; }
    /// <summary>Semente da síntese que passou nos portões; guardada pra refazer igual.</summary>
    public int? Seed { get; private set; }
    public string WarningsJson { get; private set; } = "[]";
    public string OutputsJson { get; private set; } = "[]";
    public string? WorkerId { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public int Attempts { get; private set; }
    public int Progress { get; private set; }
    public string? ProgressNote { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>Quando o worker pegou o job da última vez e quanto levou até entregar: base da previsão de tempo.</summary>
    public DateTimeOffset? RenderStartedAt { get; private set; }
    public int? RenderSeconds { get; private set; }
    /// <summary>Versões da voz geradas pro "Revisão da voz" e se a pessoa já escolheu (aí o worker monta com ela).</summary>
    public string VoiceTakesJson { get; private set; } = "[]";
    public bool VoiceApproved { get; private set; }

    public IReadOnlyList<VoiceTake> VoiceTakes => JsonSerializer.Deserialize<List<VoiceTake>>(VoiceTakesJson, Json) ?? [];

    public IReadOnlyList<string> Warnings => JsonSerializer.Deserialize<List<string>>(WarningsJson, Json) ?? [];
    public IReadOnlyList<CreativeOutput> Outputs => JsonSerializer.Deserialize<List<CreativeOutput>>(OutputsJson, Json) ?? [];

    private CreativeJob() { }

    public CreativeJob(string brandKey, CreativeKind kind, string theme, string? voiceKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(theme)) throw new ArgumentException("tema vazio", nameof(theme));
        if (theme.Trim().Length > 300) throw new ArgumentException("tema passa de 300 caracteres", nameof(theme));
        if (voiceKey is { Length: > 64 }) throw new ArgumentException("voz passa de 64 caracteres", nameof(voiceKey));

        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Kind = kind;
        Theme = theme.Trim();
        VoiceKey = string.IsNullOrWhiteSpace(voiceKey) ? null : voiceKey.Trim();
        Status = CreativeJobStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Grava roteiro e falas (já validados). Vale até a revisão: mexer no roteiro de um vídeo pronto descarta os
    /// arquivos, porque eles não batem mais com o texto. Durante o render não pode (o worker está usando).
    /// </summary>
    public void SetScript(string script, string lines, DateTimeOffset now, IReadOnlyList<string>? warnings = null)
    {
        if (Status is CreativeJobStatus.Queued or CreativeJobStatus.Rendering or CreativeJobStatus.Done)
            throw new InvalidOperationException($"roteiro não pode mudar com o job em {Status}");
        if (string.IsNullOrWhiteSpace(script) || string.IsNullOrWhiteSpace(lines))
            throw new ArgumentException("roteiro e falas são obrigatórios");
        Script = script;
        Lines = lines;
        Status = CreativeJobStatus.ScriptReady;
        OutputsJson = "[]";
        VoiceTakesJson = "[]";
        VoiceApproved = false; // texto novo, voz nova: ouvir de novo antes de montar
        WarningsJson = SerializeWarnings(warnings);
        Error = null;
        Progress = 0;
        ProgressNote = null;
        UpdatedAt = now;
    }

    /// <summary>Humano liberou o roteiro: entra na fila do worker. Também serve pra tentar de novo um job que falhou.</summary>
    public void QueueRender(DateTimeOffset now)
    {
        if (Status is not (CreativeJobStatus.ScriptReady or CreativeJobStatus.Failed) || Script is null)
            throw new InvalidOperationException($"só roteiro pronto (ou job que falhou) vai pra fila (status atual: {Status})");
        Status = CreativeJobStatus.Queued;
        Attempts = 0;
        Error = null;
        Progress = 0;
        ProgressNote = null;
        UpdatedAt = now;
    }

    /// <summary>Worker entregou as versões da voz: o job espera a pessoa escolher (sem prazo, sem worker preso).</summary>
    public void DeliverVoice(string workerId, IReadOnlyList<VoiceTake> takes, DateTimeOffset now)
    {
        EnsureOwner(workerId);
        if (takes.Count == 0) throw new ArgumentException("nenhuma versão da voz", nameof(takes));
        VoiceTakesJson = JsonSerializer.Serialize(takes, Json);
        Status = CreativeJobStatus.VoiceReview;
        Progress = 0;
        ProgressNote = null;
        WorkerId = null;
        LeaseUntil = null;
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>A pessoa escolheu a versão pelo ouvido: volta pra fila e o worker monta com essa semente.</summary>
    public void ChooseVoice(int seed, DateTimeOffset now)
    {
        if (Status != CreativeJobStatus.VoiceReview) throw new InvalidOperationException("o job não está esperando a escolha da voz");
        if (VoiceTakes.All(t => t.Seed != seed)) throw new ArgumentException("versão de voz inexistente", nameof(seed));
        Seed = seed;
        VoiceApproved = true;
        Status = CreativeJobStatus.Queued;
        Attempts = 0;
        UpdatedAt = now;
    }

    /// <summary>Nenhuma versão agradou: gera outras duas, com sementes novas.</summary>
    public void RedoVoice(DateTimeOffset now)
    {
        if (Status != CreativeJobStatus.VoiceReview) throw new InvalidOperationException("o job não está esperando a escolha da voz");
        Seed = VoiceTakes.Max(t => t.Seed) + 1;
        VoiceTakesJson = "[]";
        VoiceApproved = false;
        Status = CreativeJobStatus.Queued;
        Attempts = 0;
        UpdatedAt = now;
    }

    public bool CanBeClaimed(DateTimeOffset now) =>
        Status == CreativeJobStatus.Queued || (Status == CreativeJobStatus.Rendering && LeaseUntil < now);

    public void Claim(string workerId, DateTimeOffset now, TimeSpan lease)
    {
        if (string.IsNullOrWhiteSpace(workerId)) throw new ArgumentException("worker sem identificação", nameof(workerId));
        if (!CanBeClaimed(now)) throw new InvalidOperationException($"job em {Status} não está disponível");
        Status = CreativeJobStatus.Rendering;
        WorkerId = workerId.Trim();
        LeaseUntil = now + lease;
        Attempts++;
        Progress = 0;
        ProgressNote = null;
        RenderStartedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Notícia do worker: avança o progresso e renova o prazo (render longo não perde o job).</summary>
    public void ReportProgress(string workerId, int percent, string? note, DateTimeOffset now, TimeSpan lease)
    {
        EnsureOwner(workerId);
        Progress = Math.Clamp(percent, 0, 100);
        ProgressNote = Truncate(note, 300);
        LeaseUntil = now + lease;
        UpdatedAt = now;
    }

    public void Deliver(string workerId, IReadOnlyList<CreativeOutput> outputs, IReadOnlyList<string>? warnings, int? seed, DateTimeOffset now)
    {
        EnsureOwner(workerId);
        if (outputs.Count == 0) throw new ArgumentException("entrega sem arquivo", nameof(outputs));
        foreach (var o in outputs)
        {
            if (string.IsNullOrWhiteSpace(o.Role)) throw new ArgumentException("arquivo sem papel", nameof(outputs));
            if (!Uri.TryCreate(o.Url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException($"link do arquivo precisa ser https: {o.Url}", nameof(outputs));
            // A capa vira cover_url do Reels, que só aceita JPEG: recusa aqui, enquanto o worker ainda pode refazer.
            if (o.Role.Trim().StartsWith("cover", StringComparison.OrdinalIgnoreCase) &&
                !(u.AbsolutePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"capa precisa ser JPEG (.jpg): {o.Url}", nameof(outputs));
        }
        OutputsJson = JsonSerializer.Serialize(outputs.Select(o => new CreativeOutput(o.Role.Trim(), o.Url)).ToList(), Json);
        // Avisos do roteiro (portões ao salvar) continuam; os do worker entram depois, sem repetir.
        WarningsJson = SerializeWarnings(ScriptWarnings.Concat(warnings ?? []).ToList());
        Seed = seed ?? Seed;
        if (RenderStartedAt is { } started) RenderSeconds = (int)Math.Max(0, (now - started).TotalSeconds);
        Status = CreativeJobStatus.Review;
        Progress = 100;
        WorkerId = null;
        LeaseUntil = null;
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>Worker não conseguiu. Com <paramref name="retry"/> volta pra fila enquanto houver tentativa; senão espera o humano.</summary>
    public void Fail(string workerId, string error, bool retry, DateTimeOffset now)
    {
        EnsureOwner(workerId);
        Error = Truncate(string.IsNullOrWhiteSpace(error) ? "falha sem mensagem" : error, 1000);
        Status = retry && Attempts < MaxAttempts ? CreativeJobStatus.Queued : CreativeJobStatus.Failed;
        WorkerId = null;
        LeaseUntil = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Vídeo reprovado na revisão: volta pro roteiro (mesmo texto) pra ajustar e mandar de novo. A voz escolhida fica,
    /// então a próxima rodada não precisa ouvir de novo se o texto não mudar.
    /// </summary>
    public void Reject(DateTimeOffset now)
    {
        if (Status is not (CreativeJobStatus.Review or CreativeJobStatus.Failed))
            throw new InvalidOperationException($"só vídeo em revisão (ou que falhou) volta pro roteiro (status atual: {Status})");
        Status = CreativeJobStatus.ScriptReady;
        OutputsJson = "[]";
        Progress = 0;
        ProgressNote = null;
        Error = null;
        UpdatedAt = now;
    }

    /// <summary>Apagar só fora do worker: com ele renderizando, o arquivo ainda está sendo feito.</summary>
    public bool CanBeDeleted => Status != CreativeJobStatus.Rendering;

    /// <summary>Os rascunhos de publicação foram criados a partir dos arquivos: o job termina aqui.</summary>
    public void MarkDone(DateTimeOffset now)
    {
        if (Status != CreativeJobStatus.Review) throw new InvalidOperationException($"só job em revisão vira post (status atual: {Status})");
        Status = CreativeJobStatus.Done;
        UpdatedAt = now;
    }

    /// <summary>Avisos gravados com o roteiro; o worker pode acrescentar os dele na entrega.</summary>
    private List<string> ScriptWarnings => JsonSerializer.Deserialize<List<string>>(WarningsJson, Json)?
        .Where(w => w.StartsWith(ScriptWarningPrefix, StringComparison.Ordinal)).ToList() ?? [];

    /// <summary>Marca os avisos que vieram do roteiro, pra entrega do worker não apagar.</summary>
    public const string ScriptWarningPrefix = "Roteiro: ";

    private static string SerializeWarnings(IEnumerable<string>? warnings) => JsonSerializer.Serialize(
        (warnings ?? []).Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => Truncate(w, 500)!).Distinct().Take(50).ToList(), Json);

    private void EnsureOwner(string workerId)
    {
        if (Status != CreativeJobStatus.Rendering || !string.Equals(WorkerId, workerId?.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException("job não está com este worker");
    }

    private static string? Truncate(string? s, int max) => s is null ? null : s.Length <= max ? s.Trim() : s[..max].Trim();
}
