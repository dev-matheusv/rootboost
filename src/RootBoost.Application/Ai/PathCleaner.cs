using System.Text;
using System.Text.Json;

namespace RootBoost.Application.Ai;

/// <summary>Uma ação feita no ensaio, como o worker anotou.</summary>
public sealed record RehearsalAction(int Index, int Scene, string Action, string? Text, string? Typed, bool Changed, string? Reason);

/// <summary>
/// Caminho limpo (13/11, método do marketing): o ensaio tem idas e voltas (tocar Limpar duas vezes, abrir a lista três
/// vezes, rolar sem sair do lugar). A IA escolhe, por cena, só as ações que uma pessoa faria pra chegar no resultado;
/// a gravação de verdade toca esse caminho sem pensar no meio. Só escolhe ações que existem: nunca inventa passo.
/// </summary>
public sealed class PathCleaner
{
    private readonly ILanguageModel _model;
    private readonly IAiUsageStore _usage;
    private readonly TimeProvider _time;

    public PathCleaner(ILanguageModel model, IAiUsageStore usage, TimeProvider time)
    {
        _model = model;
        _usage = usage;
        _time = time;
    }

    public async Task<IReadOnlyList<IReadOnlyList<int>>> CleanAsync(string brandKey, IReadOnlyList<string> scenes,
        IReadOnlyList<RehearsalAction> actions, CancellationToken ct = default)
    {
        if (scenes.Count == 0 || actions.Count == 0) throw new InvalidOperationException("ensaio sem cenas ou sem ações");
        var user = new StringBuilder();
        user.AppendLine("Cenas do vídeo, na ordem:");
        for (var i = 0; i < scenes.Count; i++) user.AppendLine($"Cena {i + 1}: {Clip(scenes[i], 600)}");
        user.AppendLine();
        user.AppendLine("Ações do ensaio (índice | cena em que aconteceu | ação | alvo | texto digitado | mudou a tela? | motivo):");
        foreach (var a in actions)
            user.AppendLine($"{a.Index} | {a.Scene + 1} | {a.Action} | {Clip(a.Text ?? "", 80)} | {Clip(a.Typed ?? "", 60)} | {(a.Changed ? "sim" : "não")} | {Clip(a.Reason ?? "", 120)}");

        var result = await _model.GenerateJsonAsync(new LanguageModelRequest(Prompt, user.ToString(), Schema, "clean-path", brandKey), ct);
        await _usage.AddAsync(new AiUsageEntry(Guid.NewGuid(), brandKey, "clean-path", result.Model, result.InputTokens, result.OutputTokens,
            result.CostUsd, _time.GetUtcNow()), ct);
        return Validate(result.Json, scenes.Count, actions);
    }

    /// <summary>Confere a resposta: índice que existe, cada um uma vez só, em ordem crescente no vídeo inteiro.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> Validate(string json, int sceneCount, IReadOnlyList<RehearsalAction> actions)
    {
        using var doc = JsonDocument.Parse(json);
        var valid = actions.Select(a => a.Index).ToHashSet();
        var used = new HashSet<int>();
        var last = -1;
        var outScenes = new List<IReadOnlyList<int>>();
        var cenas = doc.RootElement.GetProperty("cenas").EnumerateArray().ToList();
        if (cenas.Count != sceneCount) throw new InvalidOperationException($"o caminho limpo veio com {cenas.Count} cenas, esperava {sceneCount}");
        foreach (var c in cenas)
        {
            var list = new List<int>();
            foreach (var v in c.GetProperty("acoes").EnumerateArray())
            {
                var i = v.GetInt32();
                if (!valid.Contains(i) || !used.Add(i) || i <= last) continue;
                list.Add(i);
                last = i;
            }
            outScenes.Add(list);
        }
        if (outScenes.All(s => s.Count == 0)) throw new InvalidOperationException("o caminho limpo veio vazio");
        return outScenes;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private const string Prompt = """
        Você limpa o caminho de um vídeo tutorial de sistema. Um robô ensaiou as cenas e anotou cada ação, com idas e
        voltas. Escolha, para cada cena, só as ações que uma pessoa mostrando o sistema faria pra chegar no resultado
        daquela cena, na ordem: sem repetir, sem abrir e fechar à toa, sem toque que não mudou a tela, sem "Limpar" ou
        ajuste que não faz parte do que a cena ensina. Mantenha o caminho até a tela (menu, botões) quando a cena mostra o
        caminho. Mantenha o que a ação seguinte precisa (abrir o calendário antes de tocar no dia, abrir a lista antes de
        escolher). Rolagem só se ela mudou a tela. Use só índices da lista, em ordem crescente no vídeo todo; cada índice
        no máximo uma vez. Uma ação feita no ensaio durante a cena seguinte pode ir pra cena certa.
        Responda em JSON: {"cenas": [{"acoes": [índices]}]}, uma entrada por cena, na mesma ordem.
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        { "type": "object", "additionalProperties": false, "required": ["cenas"],
          "properties": { "cenas": { "type": "array", "items": { "type": "object", "additionalProperties": false, "required": ["acoes"],
            "properties": { "acoes": { "type": "array", "items": { "type": "integer" } } } } } } }
        """).RootElement;
}
