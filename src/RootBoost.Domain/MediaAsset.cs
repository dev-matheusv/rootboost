namespace RootBoost.Domain;

/// <summary>
/// Arquivo de mídia de uma marca (imagem ou vídeo) guardado no armazenamento de objetos. O link público é
/// estável (servido pela API), então um post agendado pra daqui a semanas continua achando o arquivo.
/// </summary>
public sealed class MediaAsset
{
    public static readonly IReadOnlyDictionary<string, string> AllowedTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".mp4"] = "video/mp4",
        [".mov"] = "video/quicktime",
    };

    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public string FileName { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    /// <summary>Chave no armazenamento: marca/ano/id + extensão. Nunca vem do usuário.</summary>
    public string StorageKey { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>O que aparece no arquivo (acervo da marca, RB-16): a IA descreve a foto e a pessoa pode corrigir.</summary>
    public string? Description { get; private set; }
    /// <summary>Palavras-chave separadas por vírgula, pra IA escolher a mídia certa de cada cena.</summary>
    public string? Tags { get; private set; }

    public void Describe(string? description, string? tags)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()[..Math.Min(description.Trim().Length, 500)];
        Tags = string.IsNullOrWhiteSpace(tags) ? null : tags.Trim()[..Math.Min(tags.Trim().Length, 300)];
    }

    /// <summary>
    /// Vídeo já finalizado (reel pronto, com título e legenda): fica no acervo pra post, mas nunca vira trecho de outro
    /// vídeo (sairia com título e legenda em dobro). Entrega do worker já nasce assim.
    /// </summary>
    public bool Finished { get; private set; }
    /// <summary>Onde está o assunto da foto (fração da imagem): o worker aproxima ali pra encher a tela do 9:16.</summary>
    public string? FocusJson { get; private set; }

    public void MarkFinished(bool finished) => Finished = finished;

    /// <summary>
    /// Momentos do vídeo (índice de momentos, 30/10): trechos com início, fim e o que a tela mostra. A IA do roteiro
    /// escolhe um por cena em vez de pegar o vídeo do segundo zero (que mostrava a tela parada).
    /// </summary>
    public string MomentsJson { get; private set; } = "[]";

    public IReadOnlyList<MediaMoment> Moments =>
        System.Text.Json.JsonSerializer.Deserialize<List<MediaMoment>>(MomentsJson, MomentJson) ?? [];

    private static readonly System.Text.Json.JsonSerializerOptions MomentJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void SetMoments(IReadOnlyList<MediaMoment> moments)
    {
        if (moments.Count > 40) throw new ArgumentException("no máximo 40 momentos por vídeo", nameof(moments));
        foreach (var m in moments)
            if (m.Start < 0 || m.End <= m.Start || string.IsNullOrWhiteSpace(m.Description) || m.Description.Length > 300)
                throw new ArgumentException($"momento inválido: {m.Start}-{m.End}", nameof(moments));
        MomentsJson = System.Text.Json.JsonSerializer.Serialize(moments.OrderBy(m => m.Start).ToList(), MomentJson);
    }

    public RedactionBox? Focus => string.IsNullOrWhiteSpace(FocusJson) ? null : RedactionBox.Read($"[{FocusJson}]").FirstOrDefault();

    public void SetFocus(RedactionBox? focus) => FocusJson = focus is { IsValid: true } ? RedactionBox.Write([focus])[1..^1] : null;

    /// <summary>Dado pessoal visto pela IA na foto (nome, CPF, rosto de paciente...) e onde está.</summary>
    public PersonalDataStatus PersonalData { get; private set; } = PersonalDataStatus.Unchecked;
    public string? PersonalDataKinds { get; private set; }
    public string RedactionBoxesJson { get; private set; } = "[]";
    /// <summary>Versão borrada (JPEG) no armazenamento; é ela que vai pra peça e pro link público.</summary>
    public string? RedactedStorageKey { get; private set; }

    public IReadOnlyList<RedactionBox> RedactionBoxes => RedactionBox.Read(RedactionBoxesJson);

    /// <summary>Real ou na dúvida: o original não sai pro público nem pra peça, só a versão borrada.</summary>
    public bool HidesOriginal => PersonalData is PersonalDataStatus.Real or PersonalDataStatus.Uncertain;

    public void MarkPersonalData(PersonalDataStatus status, string? kinds, IReadOnlyList<RedactionBox> boxes)
    {
        PersonalData = status;
        PersonalDataKinds = string.IsNullOrWhiteSpace(kinds) ? null : kinds.Trim()[..Math.Min(kinds.Trim().Length, 200)];
        SetRedactionBoxes(boxes);
    }

    public void SetRedactionBoxes(IReadOnlyList<RedactionBox> boxes)
    {
        if (boxes.Count > 30 || boxes.Any(b => !b.IsValid)) throw new ArgumentException("áreas de borrado inválidas", nameof(boxes));
        RedactionBoxesJson = RedactionBox.Write(boxes);
        RedactedStorageKey = null; // áreas mudaram: a versão borrada precisa ser refeita
    }

    public void SetRedacted(string storageKey) => RedactedStorageKey = storageKey;

    public string RedactedKeyFor() => $"{StorageKey[..StorageKey.LastIndexOf('.')]}-borrada.jpg";

    /// <summary>Pode entrar num roteiro desta marca? Real em marca "só fictício" nunca; real ou dúvida só com a versão borrada.</summary>
    public string? BlockedReason(PersonalDataPolicy policy) => PersonalData switch
    {
        PersonalDataStatus.Real when policy == PersonalDataPolicy.FictionalOnly => "tem dado pessoal real, e esta marca só usa dado fictício",
        PersonalDataStatus.Real or PersonalDataStatus.Uncertain when RedactedStorageKey is null => "tem dado pessoal sem borrar",
        // Foto ainda não checada (ex.: descrita antes desta proteção): na dúvida, fica fora até passar pela IA.
        PersonalDataStatus.Unchecked when !IsVideo => "ainda não foi checada (clique em Descrever com a IA)",
        _ => null,
    };

    private MediaAsset() { }

    public MediaAsset(string brandKey, string fileName, long sizeBytes, DateTimeOffset now)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (!AllowedTypes.TryGetValue(ext, out var contentType))
            throw new ArgumentException($"tipo de arquivo não aceito ({ext}); use {string.Join(", ", AllowedTypes.Keys)}", nameof(fileName));
        if (sizeBytes <= 0) throw new ArgumentException("arquivo vazio", nameof(sizeBytes));

        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        FileName = SafeName(fileName!);
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StorageKey = $"{BrandKey}/{now:yyyy}/{Id:N}{ext.ToLowerInvariant()}";
        CreatedAt = now;
    }

    public bool IsVideo => ContentType.StartsWith("video/", StringComparison.Ordinal);

    /// <summary>Nome limpo pro link: letras, números, ponto, hífen e sublinhado (o resto vira hífen).</summary>
    public static string SafeName(string fileName)
    {
        var name = Path.GetFileName(fileName).Trim();
        var chars = name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? char.ToLowerInvariant(c) : '-').ToArray();
        var safe = new string(chars).Trim('-');
        if (safe.Length > 120) safe = safe[^120..];
        return safe.Length == 0 ? "arquivo" : safe;
    }
}
