using System.Text.Json;
using System.Text.RegularExpressions;

namespace RootBoost.Domain;

/// <summary>Clone = gravação da própria pessoa (com consentimento falado); Design = voz do banco, descrita em palavras.</summary>
public enum NarrationVoiceKind
{
    Clone,
    Design,
}

/// <summary>Fila: Queued → o worker pega (Processing) → cria a voz no DMaker e duas amostras → Ready. Falhou: volta até o limite.</summary>
public enum NarrationVoiceStatus
{
    Queued,
    Processing,
    Ready,
    Failed,
}

/// <summary>Amostra da voz com uma semente: a pessoa ouve as duas e escolhe pelo ouvido (RB-59).</summary>
public sealed record VoiceSample(int Seed, string StorageKey);

/// <summary>
/// Voz de uma marca (RB-59). A voz clonada é da marca e de mais ninguém: o worker grava a marca no perfil do DMaker e
/// recusa narrar outra marca com ela. A gravação e as amostras ficam no armazenamento privado (nunca em link público):
/// voz de pessoa é dado sensível. Consentimento: a gravação começa com a frase de autorização, e quem gravou, quando e
/// o texto aceito ficam registrados aqui.
/// </summary>
public sealed partial class NarrationVoice
{
    public const int MaxAttempts = 3;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    /// <summary>Id da voz no DMaker (ex.: "salao-laura-laura"). É o que vai no job do estúdio.</summary>
    public string Key { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public NarrationVoiceKind Kind { get; private set; }
    public string? ReferenceStorageKey { get; private set; }
    /// <summary>Texto que a pessoa leu na gravação (ajuda o clone; o DMaker transcreve se a gravação for longa).</summary>
    public string? ReferenceText { get; private set; }
    /// <summary>Descrição da voz do banco, no formato do DMaker (ex.: "female, middle-aged, low pitch").</summary>
    public string? Instruct { get; private set; }
    public string? ConsentBy { get; private set; }
    public string? ConsentText { get; private set; }
    public DateTimeOffset? ConsentAt { get; private set; }
    public NarrationVoiceStatus Status { get; private set; }
    public string SamplesJson { get; private set; } = "[]";
    public int? ChosenSeed { get; private set; }
    public string? WorkerId { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public int Attempts { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<VoiceSample> Samples => JsonSerializer.Deserialize<List<VoiceSample>>(SamplesJson, Json) ?? [];

    /// <summary>Frase que a pessoa fala no começo da gravação; fica gravada junto e registrada aqui.</summary>
    public static string ConsentFor(string person, string brandName) =>
        $"Eu, {person.Trim()}, autorizo a {brandName.Trim()} a criar e usar uma voz com inteligência artificial a partir desta gravação, " +
        "somente nos vídeos e posts da marca.";

    /// <summary>Texto de leitura (~15 s): frases variadas e naturais, bom material pro clone.</summary>
    public const string ReadingText =
        "Bom dia! Hoje eu quero te mostrar uma coisa simples que faz muita diferença no dia a dia. " +
        "Você já parou pra pensar quanto tempo perde com tarefas repetidas? Pois é. " +
        "Com organização, sobra tempo pro que realmente importa: cuidar bem de cada pessoa que chega até você.";

    private NarrationVoice() { }

    private NarrationVoice(string brandKey, string name, NarrationVoiceKind kind, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60) throw new ArgumentException("nome da voz entre 1 e 60 caracteres", nameof(name));
        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Name = name.Trim();
        Kind = kind;
        var slug = Slug(Name);
        Key = $"{BrandKey}-{(slug.Length == 0 ? "voz" : slug)}-{Id.ToString("N")[..4]}";
        if (Key.Length > 64) Key = Key[..64].TrimEnd('-');
        Status = NarrationVoiceStatus.Queued;
        CreatedAt = UpdatedAt = now;
    }

    /// <summary>Voz da própria pessoa. Sem o nome de quem consentiu e o texto aceito, não cria.</summary>
    public static NarrationVoice Clone(string brandKey, string name, string referenceStorageKey, string? referenceText,
        string consentBy, string consentText, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(referenceStorageKey)) throw new ArgumentException("gravação ausente", nameof(referenceStorageKey));
        if (string.IsNullOrWhiteSpace(consentBy) || consentBy.Trim().Length > 120) throw new ArgumentException("nome de quem autoriza é obrigatório", nameof(consentBy));
        if (string.IsNullOrWhiteSpace(consentText) || consentText.Length > 600) throw new ArgumentException("texto da autorização é obrigatório", nameof(consentText));
        return new NarrationVoice(brandKey, name, NarrationVoiceKind.Clone, now)
        {
            ReferenceStorageKey = referenceStorageKey,
            ReferenceText = string.IsNullOrWhiteSpace(referenceText) ? null : referenceText.Trim()[..Math.Min(referenceText.Trim().Length, 1000)],
            ConsentBy = consentBy.Trim(),
            ConsentText = consentText.Trim(),
            ConsentAt = now,
        };
    }

    /// <summary>Voz do banco: só gênero, idade e tom (o vocabulário que o DMaker entende), nada livre.</summary>
    public static NarrationVoice Design(string brandKey, string name, string gender, string age, string pitch, DateTimeOffset now)
    {
        string[] genders = ["female", "male"], ages = ["young adult", "middle-aged", "elderly"], pitches = ["low pitch", "moderate pitch", "high pitch"];
        if (!genders.Contains(gender) || !ages.Contains(age) || !pitches.Contains(pitch))
            throw new ArgumentException("gênero, idade ou tom fora das opções", nameof(gender));
        return new NarrationVoice(brandKey, name, NarrationVoiceKind.Design, now) { Instruct = $"{gender}, {age}, {pitch}" };
    }

    public bool CanBeClaimed(DateTimeOffset now) =>
        Status == NarrationVoiceStatus.Queued || (Status == NarrationVoiceStatus.Processing && LeaseUntil < now);

    public void Claim(string workerId, DateTimeOffset now, TimeSpan lease)
    {
        if (string.IsNullOrWhiteSpace(workerId)) throw new ArgumentException("worker sem identificação", nameof(workerId));
        if (!CanBeClaimed(now)) throw new InvalidOperationException($"voz em {Status} não está disponível");
        Status = NarrationVoiceStatus.Processing;
        WorkerId = workerId.Trim();
        LeaseUntil = now + lease;
        Attempts++;
        UpdatedAt = now;
    }

    public void Complete(string workerId, IReadOnlyList<VoiceSample> samples, DateTimeOffset now)
    {
        EnsureOwner(workerId);
        if (samples.Count == 0) throw new ArgumentException("voz sem amostra", nameof(samples));
        SamplesJson = JsonSerializer.Serialize(samples, Json);
        Status = NarrationVoiceStatus.Ready;
        Error = null;
        WorkerId = null;
        LeaseUntil = null;
        UpdatedAt = now;
    }

    public void Fail(string workerId, string error, bool retry, DateTimeOffset now)
    {
        EnsureOwner(workerId);
        Error = string.IsNullOrWhiteSpace(error) ? "falha sem mensagem" : error.Trim()[..Math.Min(error.Trim().Length, 1000)];
        Status = retry && Attempts < MaxAttempts ? NarrationVoiceStatus.Queued : NarrationVoiceStatus.Failed;
        WorkerId = null;
        LeaseUntil = null;
        UpdatedAt = now;
    }

    /// <summary>A pessoa ouviu e escolheu: essa semente passa a ser a padrão dos vídeos com esta voz.</summary>
    public void Choose(int seed, DateTimeOffset now)
    {
        if (Status != NarrationVoiceStatus.Ready) throw new InvalidOperationException("a voz ainda não está pronta");
        if (Samples.All(s => s.Seed != seed)) throw new ArgumentException("semente sem amostra", nameof(seed));
        ChosenSeed = seed;
        UpdatedAt = now;
    }

    private void EnsureOwner(string workerId)
    {
        if (Status != NarrationVoiceStatus.Processing || WorkerId != workerId?.Trim())
            throw new InvalidOperationException("esta voz não está com este worker");
    }

    private static string Slug(string name)
    {
        var plain = new string(name.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return NonAlnum().Replace(plain.ToLowerInvariant(), "-").Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlnum();
}
