using System.Text.Json;
using RootBoost.Application.Media;
using RootBoost.Domain;

namespace RootBoost.Application.Ai;

/// <summary>
/// Descreve a foto do acervo (RB-16) pra IA do roteiro saber escolher a imagem de cada cena. Só foto: vídeo fica com a
/// descrição da pessoa (custo). Qualquer formato de foto vira JPEG de até 1568 px antes de ir pra IA (limite e custo da
/// imagem). Custo registrado como "media-describe".
/// </summary>
public sealed class MediaDescriber
{
    private const long MaxBytes = 40 * 1024 * 1024;
    private const int MaxSide = 1568;
    private const int RedactedMaxSide = 2048;
    private readonly ILanguageModel _model;
    private readonly IMediaStorage _storage;
    private readonly IImageConverter _images;
    private readonly IAiUsageStore _usage;
    private readonly TimeProvider _time;

    public MediaDescriber(ILanguageModel model, IMediaStorage storage, IImageConverter images, IAiUsageStore usage, TimeProvider time)
    {
        _images = images;
        _model = model;
        _storage = storage;
        _usage = usage;
        _time = time;
    }

    /// <returns>False quando não dá pra descrever (vídeo, arquivo enorme, ausente ou que não é imagem); a mídia segue sem descrição.</returns>
    public async Task<bool> DescribeAsync(MediaAsset asset, Brand? brand, CancellationToken ct = default)
    {
        if (asset.IsVideo || asset.SizeBytes > MaxBytes || !asset.ContentType.StartsWith("image/", StringComparison.Ordinal)) return false;
        var obj = await _storage.GetAsync(asset.StorageKey, ct);
        if (obj is null) return false;
        using var ms = new MemoryStream();
        await using (obj.Content) await obj.Content.CopyToAsync(ms, ct);
        var original = ms.ToArray();
        var jpeg = _images.ToJpeg(original, MaxSide);
        if (jpeg is null) return false;

        var system = $"Você descreve fotos do acervo da marca {brand?.Name} pra escolher qual usar em cada cena de um vídeo curto. " +
            "Diga em português, numa frase objetiva, o que aparece (pessoas, lugar, produto, ação, antes e depois). Nada de travessão. " +
            "Tags: de 3 a 8 palavras-chave curtas.\n" +
            "Dado pessoal: procure nome de pessoa, CPF, RG, telefone, e-mail, endereço, data de nascimento, número de prontuário, " +
            "assinatura e rosto de cliente ou paciente. Para cada um, dê a área (x, y, largura, altura em fração de 0 a 1 da imagem, " +
            "a partir do canto superior esquerdo), com folga pra cobrir o texto todo. Classifique: nenhum (não há), ficticio (só dados " +
            $"de demonstração conhecidos da marca: {brand?.FictionalMarkers ?? "nenhum informado"}), real (dado de pessoa de verdade) " +
            "ou incerto (na dúvida). Nome de empresa, logotipo e nome do profissional dono da marca não contam como dado pessoal.";
        var result = await _model.GenerateJsonAsync(new LanguageModelRequest(system, $"Descreva esta foto (arquivo {asset.FileName}).", Schema, "media-describe",
            asset.BrandKey, Convert.ToBase64String(jpeg)), ct);
        await _usage.AddAsync(new AiUsageEntry(Guid.NewGuid(), asset.BrandKey, "media-describe", result.Model, result.InputTokens,
            result.OutputTokens, result.CostUsd, _time.GetUtcNow()), ct);

        using var doc = JsonDocument.Parse(result.Json);
        var tags = doc.RootElement.GetProperty("tags").EnumerateArray().Select(t => t.GetString()).Where(t => !string.IsNullOrWhiteSpace(t));
        asset.Describe(doc.RootElement.GetProperty("descricao").GetString(), string.Join(", ", tags));

        var pd = doc.RootElement.GetProperty("dadosPessoais");
        var status = pd.GetProperty("tipo").GetString() switch
        {
            "nenhum" => PersonalDataStatus.None,
            "ficticio" => PersonalDataStatus.Fictional,
            "real" => PersonalDataStatus.Real,
            _ => PersonalDataStatus.Uncertain, // resposta estranha: na dúvida, borrar
        };
        var kinds = string.Join(", ", pd.GetProperty("quais").EnumerateArray().Select(k => k.GetString()).Where(k => !string.IsNullOrWhiteSpace(k)));
        var boxes = pd.GetProperty("areas").EnumerateArray()
            .Select(a => Clamp(new RedactionBox(a.GetProperty("x").GetDouble(), a.GetProperty("y").GetDouble(), a.GetProperty("largura").GetDouble(), a.GetProperty("altura").GetDouble())))
            .Where(b => b.IsValid).Take(30).ToList();
        asset.MarkPersonalData(status, kinds, boxes);
        await RedactAsync(asset, original, ct);
        return true;
    }

    /// <summary>
    /// Refaz a versão borrada com as áreas atuais (da IA ou ajustadas pela pessoa). Sem área num item real ou duvidoso, não
    /// há versão borrada: ele fica bloqueado até alguém marcar onde borrar.
    /// </summary>
    public async Task RedactAsync(MediaAsset asset, byte[]? original, CancellationToken ct = default)
    {
        if (!asset.HidesOriginal || asset.RedactionBoxes.Count == 0) return;
        if (original is null)
        {
            var obj = await _storage.GetAsync(asset.StorageKey, ct);
            if (obj is null) return;
            using var ms = new MemoryStream();
            await using (obj.Content) await obj.Content.CopyToAsync(ms, ct);
            original = ms.ToArray();
        }
        var redacted = _images.Redact(original, asset.RedactionBoxes, RedactedMaxSide);
        if (redacted is null) return;
        var key = asset.RedactedKeyFor();
        using var content = new MemoryStream(redacted);
        await _storage.PutAsync(key, content, redacted.Length, "image/jpeg", ct);
        asset.SetRedacted(key);
    }

    private static RedactionBox Clamp(RedactionBox b)
    {
        double x = Math.Clamp(b.X, 0, 1), y = Math.Clamp(b.Y, 0, 1);
        return new RedactionBox(x, y, Math.Clamp(b.W, 0, 1 - x), Math.Clamp(b.H, 0, 1 - y));
    }

    public static readonly JsonElement Schema = JsonDocument.Parse("""
        { "type": "object", "additionalProperties": false, "required": ["descricao", "tags", "dadosPessoais"],
          "properties": {
            "descricao": { "type": "string" },
            "tags": { "type": "array", "items": { "type": "string" } },
            "dadosPessoais": { "type": "object", "additionalProperties": false, "required": ["tipo", "quais", "areas"],
              "properties": {
                "tipo": { "type": "string", "enum": ["nenhum", "ficticio", "real", "incerto"] },
                "quais": { "type": "array", "items": { "type": "string" } },
                "areas": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["x", "y", "largura", "altura"],
                  "properties": { "x": { "type": "number" }, "y": { "type": "number" }, "largura": { "type": "number" }, "altura": { "type": "number" } } } } } } } }
        """).RootElement.Clone();
}
