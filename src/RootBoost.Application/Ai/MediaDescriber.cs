using System.Text.Json;
using RootBoost.Application.Media;
using RootBoost.Domain;

namespace RootBoost.Application.Ai;

/// <summary>
/// Descreve a foto do acervo (RB-16) pra IA do roteiro saber escolher a imagem de cada cena. Só foto: vídeo fica com a
/// descrição da pessoa (custo). Foto acima de 5 MB não vai pra IA. Custo registrado como "media-describe".
/// </summary>
public sealed class MediaDescriber
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private readonly ILanguageModel _model;
    private readonly IMediaStorage _storage;
    private readonly IAiUsageStore _usage;
    private readonly TimeProvider _time;

    public MediaDescriber(ILanguageModel model, IMediaStorage storage, IAiUsageStore usage, TimeProvider time)
    {
        _model = model;
        _storage = storage;
        _usage = usage;
        _time = time;
    }

    /// <returns>False quando não dá pra descrever (vídeo, arquivo grande ou ausente); a mídia segue sem descrição.</returns>
    public async Task<bool> DescribeAsync(MediaAsset asset, string? brandName, CancellationToken ct = default)
    {
        if (asset.IsVideo || asset.SizeBytes > MaxBytes || !asset.ContentType.StartsWith("image/jpeg", StringComparison.Ordinal)) return false;
        var obj = await _storage.GetAsync(asset.StorageKey, ct);
        if (obj is null) return false;
        using var ms = new MemoryStream();
        await using (obj.Content) await obj.Content.CopyToAsync(ms, ct);

        var system = $"Você descreve fotos do acervo da marca {brandName} pra escolher qual usar em cada cena de um vídeo curto. " +
            "Diga em português, numa frase objetiva, o que aparece (pessoas, lugar, produto, ação, antes e depois). Nada de travessão. " +
            "Tags: de 3 a 8 palavras-chave curtas.";
        var result = await _model.GenerateJsonAsync(new LanguageModelRequest(system, "Descreva esta foto.", Schema, "media-describe",
            asset.BrandKey, Convert.ToBase64String(ms.ToArray())), ct);
        await _usage.AddAsync(new AiUsageEntry(Guid.NewGuid(), asset.BrandKey, "media-describe", result.Model, result.InputTokens,
            result.OutputTokens, result.CostUsd, _time.GetUtcNow()), ct);

        using var doc = JsonDocument.Parse(result.Json);
        var tags = doc.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Where(t => !string.IsNullOrWhiteSpace(t));
        asset.Describe(doc.RootElement.GetProperty("descricao").GetString(), string.Join(", ", tags));
        return true;
    }

    public static readonly JsonElement Schema = JsonDocument.Parse("""
        { "type": "object", "additionalProperties": false, "required": ["descricao", "tags"],
          "properties": { "descricao": { "type": "string" }, "tags": { "type": "array", "items": { "type": "string" } } } }
        """).RootElement.Clone();
}
