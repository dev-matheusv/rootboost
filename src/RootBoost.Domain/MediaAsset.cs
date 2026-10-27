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
