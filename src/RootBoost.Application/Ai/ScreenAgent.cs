using System.Text;
using System.Text.Json;

namespace RootBoost.Application.Ai;

/// <summary>Elemento clicável ou de digitação da página, numerado pelo worker (a IA escolhe pelo id).</summary>
public sealed record ScreenElement(string Id, string Role, string Text);

/// <param name="Image">Foto da tela em JPEG (base64), já reduzida pelo worker.</param>
public sealed record ScreenState(string BrandKey, string Scene, string Url, IReadOnlyList<ScreenElement> Elements,
    IReadOnlyList<string> History, int Step, string ImageJpegBase64, string? FictionalMarkers = null);

/// <summary>
/// Próximo passo da gravação. <see cref="Action"/>: clicar, digitar, colar-imagem, rolar, esperar, login, comecar, pronto, desistir, ou
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
    public const int MaxSteps = 120; // vídeo contínuo: o processo inteiro numa sessão (13/11)
    private static readonly string[] Actions = ["clicar", "digitar", "colar-imagem", "rolar", "esperar", "login", "comecar", "pronto", "desistir"];
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
        // Círculo que não sai nem com aviso (12/11: Lista e Calendário 30 vezes, 4 minutos perdidos): para logo, com o
        // motivo, em vez de gastar passo e IA. A cena provavelmente pede uma ordem que a tela não permite.
        if (IsStuck(state.History))
            return new ScreenAction("desistir", null, null,
                "a cena pede uma sequência que a tela não permite (a gravação ficou alternando os mesmos passos). Reescreva o roteiro: ele vai usar o mapa do sistema");

        var user = new StringBuilder();
        user.AppendLine($"Cena a mostrar: {state.Scene}");
        user.AppendLine($"Página atual: {state.Url}");
        user.AppendLine($"Passo {state.Step + 1} de no máximo {MaxSteps}.");
        if (state.History.Count > 0) user.AppendLine("Passos já feitos: " + string.Join(" | ", state.History.TakeLast(8)));
        // Gravação do relatório (12/11): a cena pedia uma visão "mês" que não existe e a IA alternou Lista e Calendário
        // oito vezes até desistir. Ao ver o círculo, ela é avisada pra adaptar a cena em vez de repetir.
        if (IsLooping(state.History))
            user.AppendLine("ATENÇÃO: você está repetindo os mesmos passos sem chegar na cena. O que a cena pede pode não existir " +
                "nesse sistema. Pare de alternar: mostre o que existe de mais próximo que cumpre o objetivo da cena (ex.: a lista do " +
                "período em vez de uma visão que não existe) e siga, ou responda pronto se o essencial já apareceu. Diga no motivo o que adaptou.");
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
        // Alvo dado pelo texto em vez do id (12/11: 15 passos perdidos com "Ficha de harmonização facial" que estava na
        // tela). Casa pelo texto exato ou, se só um elemento contém o texto, por ele.
        if (action is "clicar" or "digitar" or "colar-imagem" && !state.Elements.Any(e => e.Id == target))
        {
            var asked = target;
            target = ResolveByText(state.Elements, target);
            if (target is null)
                return new ScreenAction("esperar", null, null, $"elemento {asked} não está na página; use o id da lista (ex.: e12)");
        }
        return new ScreenAction(action, string.IsNullOrWhiteSpace(target) ? null : target, string.IsNullOrEmpty(text) ? null : text, reason);
    }

    /// <summary>
    /// Os últimos passos andam em círculo: o mesmo passo 3 vezes seguidas ou dois passos se alternando (A B A B A B).
    /// </summary>
    public static bool IsLooping(IReadOnlyList<string> history)
    {
        if (history.Count >= 3 && history.TakeLast(3).Distinct().Count() == 1) return true;
        if (history.Count < 6) return false;
        var last = history.TakeLast(6).ToList();
        return last[0] != last[1] && Enumerable.Range(2, 4).All(i => last[i] == last[i % 2]);
    }

    /// <summary>Id do elemento cujo texto é o alvo (exato, sem diferenciar maiúscula) ou o único que contém o alvo.</summary>
    public static string? ResolveByText(IReadOnlyList<ScreenElement> elements, string? target)
    {
        var t = target?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        var exact = elements.Where(e => string.Equals(e.Text.Trim(), t, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count >= 1) return exact[0].Id;
        var partial = elements.Where(e => e.Text.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count == 1 ? partial[0].Id : null;
    }

    /// <summary>Nos últimos 12 passos, 10 ou mais são os mesmos dois cliques: o aviso de círculo não adiantou.</summary>
    public static bool IsStuck(IReadOnlyList<string> history)
    {
        if (history.Count < 12) return false;
        var last = history.TakeLast(12).Where(h => h.StartsWith("clicar ", StringComparison.Ordinal)).ToList();
        var top = last.GroupBy(h => h).OrderByDescending(g => g.Count()).Take(2).Sum(g => g.Count());
        return top >= 10;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private const string System = """
        Você opera um sistema web pelo navegador pra gravar um vídeo curto de demonstração. Recebe a foto da tela, a lista
        de elementos e a cena que precisa aparecer no vídeo. Devolva UM passo por vez, como uma pessoa calma mostrando o
        uso: clicar (alvo = id), digitar (alvo = id do campo, texto = o que digitar, sempre dado fictício e plausível),
        colar-imagem (alvo = id do campo: cola um print fictício, como Ctrl+V), rolar (texto = "baixo" ou "cima"), esperar (a tela está carregando), login (a tela pede usuário e senha: o sistema
        entra sozinho, nunca digite senha), comecar (a tela chegou no ponto de partida da cena: tudo antes disso sai do vídeo), pronto (a cena já foi mostrada inteira) ou desistir (não dá pra mostrar a cena).
        A gravação começa na tela inicial, sem nada aberto, mesmo que a cena diga "já aberta" ou "no mesmo atendimento".
        Ponto de partida é a tela onde a cena COMEÇA, não onde termina: se a cena descreve como chegar ("toque em Mais,
        depois em Relatórios"), esses toques fazem parte do vídeo. Responda comecar antes deles, na tela de onde eles
        partem, e então faça cada toque. Nunca vá direto pelo endereço até a tela final de uma cena que mostra o caminho.
        Faça na tela cada ação que a cena descreve, na ordem, com os valores que ela cita (datas, nomes, opções). Campo já
        preenchido com outro valor não conta: troque pelo da cena. Só responda pronto depois de a última ação aparecer.
        Primeiro prepare: navegue, abra ou crie o que a cena pressupõe, com dado fictício, e ajuste o que for preciso pra
        tela ficar como a cena descreve. Quando estiver no ponto de partida, responda comecar, uma vez só, e aí mostre a cena.
        Exceção: quando a cena começa com "Vídeo contínuo", a gravação já está valendo desde a tela inicial e não existe
        preparo nem comecar. Tudo aparece no vídeo: vá pelo caminho visível (menu, botões), faça só as ações daquela cena
        e responda pronto quando o resultado dela aparecer na tela. A próxima cena continua da tela onde você parou.
        Se a cena pede uma tela, aba, visão ou botão que não existe (nem na tela nem no mapa do sistema), não fique
        procurando nem alternando: mostre o que existe de mais próximo que cumpre o objetivo da cena e siga.
        Se o que você quer tocar aparece na foto mas não está na lista de elementos (ex.: um cartão de lista), use clicar
        com alvo = o id de qualquer elemento e texto = "x,y" com o ponto da foto: o clique cai exatamente ali. Nunca toque
        em ícone de copiar, duplicar ou excluir pra abrir um item.
        Só desista depois de tentar outro caminho, e diga no motivo o que faltou.
        Pra anexar arquivo ou inserir imagem, clique no botão: o sistema escolhe sozinho um arquivo fictício, sem janela.
        Nunca exclua, cancele, pague, envie mensagem de verdade ou saia do sistema. Só mude configuração ou preferência
        quando a cena pedir ou depender daquilo. Siga os cuidados escritos na cena. Prefira o caminho mais curto e visível.
        O motivo é uma frase curta em português.
        Dado sensível: marque dadoPessoalReal = true só se a tela mostrar CPF, RG, cartão do SUS ou convênio, telefone,
        e-mail, endereço completo ou data de nascimento que não pareçam de demonstração (compare com os dados fictícios
        conhecidos da marca). Nome de pessoa sozinho não conta. Na dúvida, true, e o motivo diz o que apareceu.
        """;

    public static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["acao", "alvo", "texto", "motivo", "dadoPessoalReal"],
          "properties": {
            "acao": { "type": "string", "enum": ["clicar", "digitar", "colar-imagem", "rolar", "esperar", "login", "comecar", "pronto", "desistir"] },
            "alvo": { "type": "string" },
            "texto": { "type": "string" },
            "motivo": { "type": "string" },
            "dadoPessoalReal": { "type": "boolean" }
          }
        }
        """).RootElement.Clone();
}
