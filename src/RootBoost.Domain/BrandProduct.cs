using System.Text.Json;

namespace RootBoost.Domain;

/// <summary>
/// O que a marca vende, montado na conversa com a IA (RB-60): o que é, pra quem, onde ver (site, app, loja), recursos,
/// diferenciais e oferta. Alimenta o roteiro (RB-45) e a gravação de tela (RB-50: <see cref="AppUrl"/>). Um por marca.
/// </summary>
public sealed class BrandProduct
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Guid Id { get; private set; }
    public string BrandKey { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Kind { get; private set; } = "outro";
    public string? Url { get; private set; }
    public string? AppUrl { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string Audience { get; private set; } = string.Empty;
    public string FeaturesJson { get; private set; } = "[]";
    public string DifferentialsJson { get; private set; } = "[]";
    public string? Offer { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static readonly string[] Kinds = ["app", "site", "loja", "servico", "outro"];

    public IReadOnlyList<string> Features => JsonSerializer.Deserialize<List<string>>(FeaturesJson, Json) ?? [];
    public IReadOnlyList<string> Differentials => JsonSerializer.Deserialize<List<string>>(DifferentialsJson, Json) ?? [];

    private BrandProduct() { }

    public BrandProduct(string brandKey, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        BrandKey = BrandKeys.Validate(brandKey);
        Name = string.Empty;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Grava o cadastro conferido pela pessoa. Link só https/http público; listas limpas e limitadas.</summary>
    public void Update(string name, string kind, string? url, string? appUrl, string summary, string audience,
        IEnumerable<string> features, IEnumerable<string> differentials, string? offer, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("nome do produto vazio", nameof(name));
        Name = Clip(name, 120)!;
        Kind = Kinds.Contains(kind?.Trim().ToLowerInvariant()) ? kind!.Trim().ToLowerInvariant() : "outro";
        Url = Link(url, nameof(url));
        AppUrl = Link(appUrl, nameof(appUrl));
        Summary = Clip(summary, 1000) ?? string.Empty;
        Audience = Clip(audience, 500) ?? string.Empty;
        FeaturesJson = List(features);
        DifferentialsJson = List(differentials);
        Offer = Clip(offer, 300);
        UpdatedAt = now;
    }

    private static string List(IEnumerable<string> items) => JsonSerializer.Serialize(
        items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => Clip(i, 200)!).Distinct().Take(20).ToList(), Json);

    private static string? Link(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        if (!v.StartsWith("http", StringComparison.OrdinalIgnoreCase)) v = "https://" + v;
        if (!Uri.TryCreate(v, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttps && u.Scheme != Uri.UriSchemeHttp) || v.Length > 500)
            throw new ArgumentException($"link inválido: {value}", field);
        return v;
    }

    private static string? Clip(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length <= max ? s.Trim() : s.Trim()[..max].Trim();
}
