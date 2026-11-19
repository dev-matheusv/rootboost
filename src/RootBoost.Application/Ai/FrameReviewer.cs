using System.Text;
using System.Text.Json;

namespace RootBoost.Application.Ai;

/// <summary>
/// Conferência pela IA (roadmap A5, 19/11): antes de entregar, a IA olha a folha com 12 quadros do vídeo pronto e aponta
/// o que uma pessoa reprovaria (texto ilegível ou duplo, imagem pixelada, tela parada, dado que parece real, legenda
/// cortando frase). Uma chamada por vídeo; o resultado vira aviso na revisão, nunca bloqueia a entrega.
/// </summary>
public sealed class FrameReviewer
{
    public static readonly string[] Kinds =
        ["texto-ilegivel", "texto-duplo", "imagem-pixelada", "tela-parada", "dado-real", "legenda-cortada", "tela-errada", "outro"];

    private readonly ILanguageModel _model;
    private readonly IAiUsageStore _usage;
    private readonly TimeProvider _time;

    public FrameReviewer(ILanguageModel model, IAiUsageStore usage, TimeProvider time)
    {
        _model = model;
        _usage = usage;
        _time = time;
    }

    public async Task<IReadOnlyList<string>> ReviewAsync(string brandKey, string theme, IReadOnlyList<string> lines,
        string sheetJpegBase64, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sheetJpegBase64)) throw new InvalidOperationException("folha de quadros vazia");
        var user = new StringBuilder();
        user.AppendLine($"Tema do vídeo: {Clip(theme, 300)}");
        user.AppendLine("Falas da narração, na ordem:");
        foreach (var l in lines.Where(l => !string.IsNullOrWhiteSpace(l)).Take(30)) user.AppendLine($"- {Clip(l, 200)}");
        user.AppendLine();
        user.AppendLine("A imagem tem 12 quadros do vídeo em ordem, da esquerda pra direita e de cima pra baixo (1 a 12).");

        var result = await _model.GenerateJsonAsync(new LanguageModelRequest(Prompt, user.ToString(), Schema, "frame-review", brandKey,
            sheetJpegBase64), ct);
        await _usage.AddAsync(new AiUsageEntry(Guid.NewGuid(), brandKey, "frame-review", result.Model, result.InputTokens,
            result.OutputTokens, result.CostUsd, _time.GetUtcNow()), ct);
        return Read(result.Json);
    }

    /// <summary>Lê a resposta: no máximo 6 problemas, cada um com quadro de 1 a 12 e tipo conhecido.</summary>
    public static IReadOnlyList<string> Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var outList = new List<string>();
        foreach (var p in doc.RootElement.GetProperty("problemas").EnumerateArray())
        {
            var frame = p.TryGetProperty("quadro", out var q) && q.TryGetInt32(out var n) && n is >= 1 and <= 12 ? n : 0;
            var kind = p.TryGetProperty("tipo", out var t) ? t.GetString() : null;
            var text = p.TryGetProperty("descricao", out var d) ? d.GetString()?.Trim() : null;
            if (string.IsNullOrWhiteSpace(text) || kind is null || !Kinds.Contains(kind)) continue;
            outList.Add($"Conferência da IA{(frame > 0 ? $", quadro {frame}" : "")}: {Clip(text, 220)}");
            if (outList.Count == 6) break;
        }
        return outList;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private const string Prompt = """
        Você é o editor que confere um Reels antes de ele ir pro cliente. Olhe os 12 quadros e aponte só o que uma
        pessoa exigente reprovaria: texto ilegível (pequeno demais, cortado, sobre fundo que some); dois textos
        disputando a tela ao mesmo tempo (título e legenda); imagem ou logo ampliado e pixelado; vários quadros seguidos
        na mesma tela sem nada mudar quando a fala promete um passo a passo; nome, CPF, telefone ou e-mail que pareça de
        pessoa real (dado de demonstração costuma ter nome genérico ou "teste"); legenda que corta a frase num ponto
        estranho ou cobre a cartela final; tela que não combina com o que a fala diz. Não comente gosto, cor ou estilo
        que estejam legíveis. Se estiver tudo bom, devolva a lista vazia. Descreva cada problema em uma frase curta, em
        português, dizendo o que está errado e o que fazer.
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse($$"""
        {
          "type": "object",
          "properties": {
            "problemas": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "quadro": { "type": "integer" },
                  "tipo": { "type": "string", "enum": [{{string.Join(", ", Kinds.Select(k => $"\"{k}\""))}}] },
                  "descricao": { "type": "string" }
                },
                "required": ["quadro", "tipo", "descricao"],
                "additionalProperties": false
              }
            }
          },
          "required": ["problemas"],
          "additionalProperties": false
        }
        """).RootElement.Clone();
}
