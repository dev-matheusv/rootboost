using System.Text;
using System.Text.Json;

namespace RootBoost.Application.Ai;

/// <summary>Elemento clicável ou de digitação da página, numerado pelo worker (a IA escolhe pelo id).</summary>
public sealed record ScreenElement(string Id, string Role, string Text);

/// <param name="Image">Foto da tela em JPEG (base64), já reduzida pelo worker.</param>
public sealed record ScreenState(string BrandKey, string Scene, string Url, IReadOnlyList<ScreenElement> Elements,
    IReadOnlyList<string> History, int Step, string ImageJpegBase64, string? FictionalMarkers = null);

/// <summary>
/// Próximo passo da gravação. <see cref="Action"/>: clicar, digitar, rolar, esperar, login, pronto, desistir, ou
/// "dado-pessoal" (a tela mostrou dado de pessoa real: a gravação para e não tenta de novo).
/// </summary>
public sealed record ScreenAction(string Action, string? Target, string? Text, string Reason);

/// <summary>
/// A IA que executa a cena do roteiro no sistema do cliente (RB-50, desenho em docs/GRAVACAO.md): olha a foto da tela e a
/// lista de elementos e devolve UM passo. A senha nunca passa por aqui (passo "login" o worker faz sozinho). Passo que
/// aponta pra elemento que não existe vira "esperar", e a partir do limite de passos a resposta é "desistir".
/// </summary>
public sealed class ScreenAgent
{
    public const int MaxSteps = 25;
    private static readonly string[] Actions = ["clicar", "digitar", "rolar", "esperar", "login", "pronto", "desistir"];
    private readonly ILanguageModel _model;
    private readonly IAiUsageStore _usage;
    private readonly TimeProvider _time;

    public ScreenAgent(ILanguageModel model, IAiUsageStore usage, TimeProvider time)
    {
        _model = model;
        _usage = usage;
        _time = time;
    }

    public async Task<ScreenAction> NextAsync(ScreenState state, CancellationToken ct = default)
    {
        if (state.Step >= MaxSteps) return new ScreenAction("desistir", null, null, $"passou de {MaxSteps} passos");

        var user = new StringBuilder();
        user.AppendLine($"Cena a mostrar: {state.Scene}");
        user.AppendLine($"Página atual: {state.Url}");
        user.AppendLine($"Passo {state.Step + 1} de no máximo {MaxSteps}.");
        if (state.History.Count > 0) user.AppendLine("Passos já feitos: " + string.Join(" | ", state.History.TakeLast(8)));
        user.AppendLine($"Dados de demonstração conhecidos da marca (fictícios): {state.FictionalMarkers ?? "nenhum informado"}");
        user.AppendLine("Elementos da página (id: tipo: texto):");
        foreach (var e in state.Elements.Take(150)) user.AppendLine($"{e.Id}: {e.Role}: {Clip(e.Text, 80)}");

        var result = await _model.GenerateJsonAsync(new LanguageModelRequest(System, user.ToString(), Schema, "screen-recording",
            state.BrandKey, state.ImageJpegBase64), ct);
        await _usage.AddAsync(new AiUsageEntry(Guid.NewGuid(), state.BrandKey, "screen-recording", result.Model,
            result.InputTokens, result.OutputTokens, result.CostUsd, _time.GetUtcNow()), ct);

        using var doc = JsonDocument.Parse(result.Json);
        var r = doc.RootElement;
        var action = r.GetProperty("acao").GetString() ?? "esperar";
        var target = r.TryGetProperty("alvo", out var t) ? t.GetString() : null;
        var text = r.TryGetProperty("texto", out var x) ? x.GetString() : null;
        var reason = r.TryGetProperty("motivo", out var m) ? m.GetString() ?? string.Empty : string.Empty;

        // Dado de pessoa real na tela: nada desta gravação pode virar vídeo (proteção de dado pessoal, 29/10).
        if (r.TryGetProperty("dadoPessoalReal", out var pd) && pd.ValueKind == JsonValueKind.True)
            return new ScreenAction("dado-pessoal", null, null, string.IsNullOrWhiteSpace(reason) ? "a tela mostrou dado pessoal real" : reason);

        if (!Actions.Contains(action)) return new ScreenAction("esperar", null, null, "passo desconhecido");
        if (action is "clicar" or "digitar" && !state.Elements.Any(e => e.Id == target))
            return new ScreenAction("esperar", null, null, $"elemento {target} não está na página");
        return new ScreenAction(action, string.IsNullOrWhiteSpace(target) ? null : target, string.IsNullOrEmpty(text) ? null : text, reason);
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private const string System = """
        Você opera um sistema web pelo navegador pra gravar um vídeo curto de demonstração. Recebe a foto da tela, a lista
        de elementos e a cena que precisa aparecer no vídeo. Devolva UM passo por vez, como uma pessoa calma mostrando o
        uso: clicar (alvo = id), digitar (alvo = id do campo, texto = o que digitar, sempre dado fictício e plausível),
        rolar (texto = "baixo" ou "cima"), esperar (a tela está carregando), login (a tela pede usuário e senha: o sistema
        entra sozinho, nunca digite senha), pronto (a cena já foi mostrada inteira) ou desistir (não dá pra mostrar a cena).
        Nunca exclua, cancele, pague, envie mensagem de verdade, mude configuração ou saia do sistema. Prefira o caminho
        mais curto e visível. O motivo é uma frase curta em português.
        Dado pessoal: marque dadoPessoalReal = true se a tela mostrar nome, CPF, telefone, e-mail, endereço ou dado de saúde
        de uma pessoa que não pareça de demonstração (compare com os dados fictícios conhecidos da marca). Na dúvida, true,
        e o motivo diz o que apareceu.
        """;

    public static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["acao", "alvo", "texto", "motivo", "dadoPessoalReal"],
          "properties": {
            "acao": { "type": "string", "enum": ["clicar", "digitar", "rolar", "esperar", "login", "pronto", "desistir"] },
            "alvo": { "type": "string" },
            "texto": { "type": "string" },
            "motivo": { "type": "string" },
            "dadoPessoalReal": { "type": "boolean" }
          }
        }
        """).RootElement.Clone();
}
