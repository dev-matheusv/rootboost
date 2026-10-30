using System.Text;
using System.Text.Json;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Studio;
using RootBoost.Domain;

namespace RootBoost.Application.Ai;

/// <summary>
/// Porta do modelo de linguagem (decisão de 28/10: Claude agora, Root AI depois, trocando só o adaptador).
/// Nada da plataforma conhece o fornecedor: pede um JSON num formato e recebe o JSON e o custo.
/// </summary>
public interface ILanguageModel
{
    Task<LanguageModelResult> GenerateJsonAsync(LanguageModelRequest request, CancellationToken ct = default);
}

/// <param name="Schema">JSON Schema da resposta (o modelo é obrigado a seguir).</param>
/// <param name="Purpose">Pra que serve a chamada (vai pro registro de uso).</param>
/// <param name="ImageJpegBase64">Foto opcional (JPEG em base64) que vai junto da mensagem, ex.: a tela na gravação.</param>
public sealed record LanguageModelRequest(string System, string User, JsonElement Schema, string Purpose, string? BrandKey,
    string? ImageJpegBase64 = null);

public sealed record LanguageModelResult(string Json, string Model, long InputTokens, long OutputTokens, decimal CostUsd);

/// <summary>A IA está desligada (Ai:Mode=Off) ou sem chave: a tela mostra isso em vez de falhar.</summary>
public sealed class AiDisabledException() : InvalidOperationException("IA desligada nesta instalação");

/// <summary>O provedor da IA recusou ou falhou na chamada; a mensagem é segura pra mostrar na tela.</summary>
public sealed class AiRequestFailedException(string message, Exception? inner = null) : InvalidOperationException(message, inner);

public enum AiMode
{
    Off,
    Fake,
    Claude,
}

/// <summary>Config da IA. Off por padrão: nada é chamado nem cobrado até ligar e pôr a chave.</summary>
public sealed class AiOptions
{
    public AiMode Mode { get; set; } = AiMode.Off;
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low, medium, high, xhigh ou max. Medium é o padrão do modelo; roteiro curto não precisa de mais.</summary>
    public string Effort { get; set; } = "medium";
    public string? ApiKey { get; set; }

    /// <summary>
    /// Modelo por finalidade, pra controlar custo sem mexer em código (ex.: Ai__Models__screen-recording=claude-haiku-5-5).
    /// Sem entrada, vale <see cref="Model"/>.
    /// </summary>
    public Dictionary<string, string> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string ModelFor(string purpose) => Models.TryGetValue(purpose, out var m) && !string.IsNullOrWhiteSpace(m) ? m : Model;
}

/// <summary>Gasto com IA num período, por finalidade e por marca (pra acompanhar o custo da plataforma).</summary>
public sealed record AiUsageSummary(decimal TotalUsd, int Calls, IReadOnlyList<AiUsageLine> ByPurpose, IReadOnlyList<AiUsageLine> ByBrand);

public sealed record AiUsageLine(string Key, decimal CostUsd, int Calls, long InputTokens, long OutputTokens);

/// <summary>Uma chamada à IA: quanto custou, pra quê e de qual marca (decisão: uso e custo gravados).</summary>
public sealed record AiUsageEntry(Guid Id, string? BrandKey, string Purpose, string Model, long InputTokens, long OutputTokens,
    decimal CostUsd, DateTimeOffset CreatedAt);

public interface IAiUsageStore
{
    Task AddAsync(AiUsageEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<AiUsageEntry>> ListSinceAsync(DateTimeOffset since, CancellationToken ct = default);
}

/// <summary>Preço por milhão de tokens (entrada, saída) em dólar, pra registrar o custo de cada chamada.</summary>
public static class AiPricing
{
    private static readonly Dictionary<string, (decimal In, decimal Out)> PerMillion = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5-5"] = (4m, 20m),
        ["claude-sonnet-5-5"] = (2m, 10m),
        ["claude-haiku-5-5"] = (0.10m, 0.50m),
        ["claude-fable-5-1"] = (10m, 50m),
    };

    public static decimal Cost(string model, long input, long output) =>
        PerMillion.TryGetValue(model, out var p) ? Math.Round((input * p.In + output * p.Out) / 1_000_000m, 6) : 0m;
}

/// <summary>
/// IA que escreve o roteiro e as falas do vídeo (RB-45) com o que a plataforma sabe da marca: público, tom, termos
/// proibidos, provas ativas (só afirma o que está ali), frases de clientes, playbook de copy e as regras do formato.
/// O resultado passa pelos mesmos portões do roteiro escrito à mão; se não passar, pede de novo uma vez com os erros.
/// </summary>
public sealed class ScriptWriter
{
    private readonly ILanguageModel _model;
    private readonly IKnowledgeBase _knowledge;
    private readonly IAiUsageStore _usage;
    private readonly TimeProvider _time;

    public ScriptWriter(ILanguageModel model, IKnowledgeBase knowledge, IAiUsageStore usage, TimeProvider time)
    {
        _model = model;
        _knowledge = knowledge;
        _usage = usage;
        _time = time;
    }

    public sealed record Written(JsonElement Script, JsonElement Lines, IReadOnlyList<string> Hooks, IReadOnlyList<string> Warnings, decimal CostUsd,
        IReadOnlyList<string>? EditIdeas = null);

    public sealed record Failed(IReadOnlyList<string> Errors);

    /// <returns>O roteiro pronto ou os erros que sobraram depois de duas tentativas.</returns>
    public async Task<(Written? Ok, Failed? Error)> WriteAsync(CreativeJob job, BrandProfile? brand, string? instructions, CancellationToken ct = default,
        BrandProduct? product = null, IReadOnlyList<MediaAsset>? library = null)
    {
        // Acervo (RB-16): fotos e vídeos com descrição que a IA pode pôr nas cenas. Sem descrição, foto não entra (a IA não vê).
        var media = (library ?? []).Where(m => !m.Finished && (m.IsVideo || !string.IsNullOrWhiteSpace(m.Description))).Take(60).ToList();
        var system = SystemPrompt(job.Kind, brand, product, media, job.Format);
        var user = UserPrompt(job, instructions);
        decimal cost = 0;
        IReadOnlyList<string> errors = [];

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var prompt = attempt == 1 ? user : $"{user}\n\nA versão anterior foi recusada pela conferência. Corrija:\n- {string.Join("\n- ", errors)}";
            var result = await _model.GenerateJsonAsync(new LanguageModelRequest(system, prompt, Schema, "studio-script", job.BrandKey), ct);
            cost += result.CostUsd;
            await _usage.AddAsync(new AiUsageEntry(Guid.NewGuid(), job.BrandKey, "studio-script", result.Model,
                result.InputTokens, result.OutputTokens, result.CostUsd, _time.GetUtcNow()), ct);

            using var doc = JsonDocument.Parse(result.Json);
            var (script, lines, hooks) = ToContracts(doc.RootElement, media.ToDictionary(m => m.Id.ToString("N"), m => m.Moments), job.Format);
            errors = ScriptContract.Validate(script, lines);
            // Mesma mídia em duas cenas deixa o vídeo repetitivo (relato de 30/10: o receituário aparecia 3 vezes).
            var repeated = script.GetProperty("blocos").EnumerateArray()
                .Select(b => b.GetProperty("trechos")[0][0].GetString() ?? "").Where(src => src.StartsWith("acervo-", StringComparison.Ordinal))
                .GroupBy(src => src).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            var plain = string.Join(" ", lines.EnumerateArray().Select(l => l.GetString() ?? "")).ToLowerInvariant();
            var forbidden = ForbiddenWords.Where(w => System.Text.RegularExpressions.Regex.IsMatch(plain, $@"{System.Text.RegularExpressions.Regex.Escape(w)}")).ToList();
            if (forbidden.Count > 0)
                errors = [.. errors, $"fala com palavra proibida ({string.Join(", ", forbidden)}); reescreva em linguagem do dia a dia"];
            if (repeated.Count > 0)
                errors = [.. errors, $"a mesma mídia do acervo aparece em mais de uma cena ({string.Join(", ", repeated)}); use cada mídia uma vez só, troque ou deixe a cena pra gravar"];
            if (errors.Count == 0)
            {
                var review = ScriptGates.Review(job.Kind, script, lines, _knowledge, brand);
                var ideas = doc.RootElement.TryGetProperty("ideiasEdicao", out var ie) && ie.ValueKind == JsonValueKind.Array
                    ? ie.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Take(3).ToList() : [];
                if (review.Errors.Count == 0) return (new Written(script, lines, hooks, review.Warnings, cost, ideas), null);
                errors = review.Errors;
            }
        }
        return (null, new Failed(errors));
    }

    /// <summary>
    /// A IA não sabe o tempo de cada gravação ainda: cada bloco ganha um trecho "a gravar" com a duração estimada
    /// da fala (o worker troca pela gravação de tela no RB-50) e guarda a descrição da cena pra quem for gravar.
    /// </summary>
    /// <summary>Palavras com cara de "IA burguesa" (Matheus, 30/10): a conferência recusa e a IA reescreve.</summary>
    public static readonly string[] ForbiddenWords = ["otimize", "otimizar", "eleve", "elevar", "potencialize", "potencializar", "solução completa", "experiência", "jornada"];

    /// <summary>Transições e movimentos que o DMaker faz e que ficam bem em reel (lista curta de propósito).</summary>
    public static readonly string[] Transitions = ["fade", "dissolve", "slideleft", "slideup", "smoothleft", "wipeleft", "circleopen", "zoomin"];
    public static readonly string[] Motions = ["zoom-in", "zoom-out", "pan-left", "pan-right"];

    /// <summary>
    /// Trecho do bloco: mídia do acervo (no momento escolhido, cortado pra caber na fala sem passar de 1,2x) ou "a gravar".
    /// Vídeo com momentos e escolha inválida cai no primeiro momento, nunca no segundo zero.
    /// </summary>
    private static object[] Trecho(string mediaId, JsonElement b, IReadOnlyDictionary<string, IReadOnlyList<MediaMoment>> mediaMoments, int n, double seconds)
    {
        if (!mediaMoments.TryGetValue(mediaId, out var moments)) return [$"gravar-bloco-{n}", 0, seconds];
        if (moments.Count == 0) return [$"acervo-{mediaId}", 0, seconds];
        var i = b.TryGetProperty("momento", out var mo) && mo.ValueKind == JsonValueKind.Number ? mo.GetInt32() : 0;
        var m = moments[i >= 0 && i < moments.Count ? i : 0];
        var end = Math.Min(m.End, m.Start + Math.Max(seconds * 1.2, 2));
        return [$"acervo-{mediaId}", Math.Round(m.Start, 1), Math.Round(end, 1)];
    }

    private static (JsonElement Script, JsonElement Lines, IReadOnlyList<string> Hooks) ToContracts(JsonElement ai, IReadOnlyDictionary<string, IReadOnlyList<MediaMoment>> mediaMoments, VideoFormat requested)
    {
        var lines = new List<string>();
        // Formato pedido manda; no automático, o que a IA decidiu.
        var personal = requested switch
        {
            VideoFormat.Personal => true,
            VideoFormat.Explainer => false,
            _ => Str(ai, "formato") == "pessoal",
        };
        var opening = ai.TryGetProperty("abertura", out var a) && a.ValueKind == JsonValueKind.Object
            ? new[] { Str(a, "titulo"), Str(a, "subtitulo") } : null;
        // Pessoal: abertura e fecho são cartões sem narração (a música segue); a fala, quando há, está nas cenas.
        if (opening is not null) lines.Add(personal ? "" : Str(a, "fala"));

        var blocks = new List<object>();
        var n = 0;
        foreach (var b in ai.GetProperty("blocos").EnumerateArray())
        {
            n++;
            var audio = personal ? Str(b, "audio") switch { "original" => "original", "narracao" => "narracao", _ => "musica" } : "narracao";
            var fala = audio == "narracao" ? Str(b, "fala") : "";
            lines.Add(fala);
            var seconds = audio == "narracao"
                ? Math.Max(3, Math.Round(fala.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / ScriptGates.WordsPerSecond, 1))
                : Math.Clamp(b.TryGetProperty("duracao", out var du) && du.ValueKind == JsonValueKind.Number ? du.GetDouble() : 4, 2, 8);
            blocks.Add(new
            {
                titulo = Str(b, "titulo"),
                sub = Str(b, "subtitulo"),
                cena = Str(b, "cena"),
                // Mídia do acervo escolhida pela IA vira o trecho ("acervo-<id>"); sem ela, o bloco fica pra gravar a tela.
                trechos = new object[] { Trecho(Str(b, "midia"), b, mediaMoments, n, seconds) },
                // Edição por bloco (transição de entrada e movimento da foto); fora da lista vira o padrão suave.
                transicao = Transitions.Contains(Str(b, "transicao")) ? Str(b, "transicao") : "fade",
                movimento = Motions.Contains(Str(b, "movimento")) ? Str(b, "movimento") : "zoom-in",
                // Som da cena: narracao (voz da marca), original (fala da pessoa no vídeo dela) ou musica.
                audio,
            });
        }
        var final = ai.GetProperty("final");
        lines.Add(personal ? "" : Str(final, "fala"));

        var script = new Dictionary<string, object?>
        {
            ["formato"] = personal ? "pessoal" : "explicativo",
            ["musica"] = new { clima = Str(ai, "musicaClima") },
            ["nome"] = Str(ai, "nome"),
            ["abertura"] = opening,
            ["fecho"] = new[] { Str(final, "titulo"), Str(final, "cta") },
            ["blocos"] = blocks,
        };
        var hooks = ai.TryGetProperty("ganchosAlternativos", out var h) && h.ValueKind == JsonValueKind.Array
            ? h.EnumerateArray().Select(x => x.GetString() ?? string.Empty).Where(x => x.Length > 0).ToList() : [];
        return (JsonSerializer.SerializeToElement(script), JsonSerializer.SerializeToElement(lines), hooks);
    }

    private static string Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : string.Empty;

    private string SystemPrompt(CreativeKind kind, BrandProfile? brand, BrandProduct? product, IReadOnlyList<MediaAsset> media, VideoFormat format = VideoFormat.Auto)
    {
        var (min, max) = ScriptGates.Range(kind);
        var sb = new StringBuilder();
        sb.AppendLine("Você escreve roteiros de vídeo curto (Reels) em português do Brasil para a marca descrita abaixo.");
        sb.AppendLine("O vídeo mostra o produto de verdade na tela (gravação de tela ou imagem do cliente) com a voz do fundador narrando.");
        sb.AppendLine();
        sb.AppendLine("Regras do roteiro:");
        sb.AppendLine($"- Duração da voz entre {min} e {max} segundos, a cerca de 2,4 palavras por segundo.");
        sb.AppendLine(kind == CreativeKind.Ad
            ? "- Anúncio: sem abertura; o título do primeiro bloco é o gancho, curto e forte."
            : "- Abertura com a dor do público (título curto e subtítulo), depois blocos de um recurso cada, de 5 a 12 segundos.");
        sb.AppendLine("- Cada bloco tem título curto no topo da tela, subtítulo, a fala daquele trecho e a cena: o que a tela mostra, passo a passo, pra quem for gravar.");
        sb.AppendLine(format switch
        {
            VideoFormat.Explainer => "- Formato explicativo (obrigatório): toda cena com narração (audio narracao) e fala.",
            VideoFormat.Personal => "- Formato pessoal (obrigatório): vídeo de resultado, viagem ou rotina, sem explicação de sistema. " +
                "Cada cena escolhe o som: musica (só música, fala vazia), original (vídeo do acervo em que a pessoa fala; fala vazia, " +
                "a legenda vem da fala dela) ou narracao (só se a pessoa pediu narração). Foto e momento de resultado pedem musica.",
            _ => "- Formato: decida pelo pedido e pelo acervo. explicativo = mostra um sistema ou ensina algo, narração em toda cena. " +
                "pessoal = viagem, resultado, antes e depois, rotina: cenas com musica, original (a pessoa falando no vídeo dela) ou " +
                "narracao só se fizer sentido. Na dúvida entre os dois, pessoal quando não houver sistema pra mostrar.",
        });
        sb.AppendLine("- Em cena musica a fala fica vazia e duracao diz quantos segundos ela fica na tela (2 a 6). Diga também o clima da música " +
            "(musicaClima, 2 a 4 palavras em inglês, ex.: calm acoustic travel), mesmo no explicativo.");
        sb.AppendLine("- Edição: escolha a transição de entrada e o movimento de cada bloco pela cena e pelo ritmo. Sem pedido da pessoa, " +
            "prefira o suave (fade, dissolve, smoothleft) e varie o movimento (zoom-in aproxima detalhe, zoom-out mostra o todo, pan " +
            "acompanha foto larga). Slide e wipe mudam de assunto; circleopen e zoomin revelam resultado. Se a pessoa pedir um estilo " +
            "de edição, siga o pedido.");
        sb.AppendLine("- Dê também 3 ideias de edição pra esse vídeo (frases curtas que a pessoa pode pedir, ex.: \"transições de deslizar entre as fotos\").");
        sb.AppendLine("- Conversa, não locução. A voz fala com quem assiste (você), como quem conhece o produto por dentro. Nada de primeira pessoa contando a própria rotina (\"eu fiz\", \"eu uso\").");
        sb.AppendLine("- Linguagem do dia a dia, como quem conta pra um colega no WhatsApp: \"pra\", \"tá\", \"a gente\", \"sabe quando...\", " +
            "\"então\", \"simples assim\". Frase curta, uma ideia por frase. Comece pela situação real de quem assiste. Nada rebuscado " +
            "(\"registrado do jeito certo\", \"consultar quando precisar\" soam formais). Se ninguém falaria assim no WhatsApp, reescreva.");
        sb.AppendLine($"- Palavras proibidas na fala, no título e no subtítulo: {string.Join(", ", ForbiddenWords)}.");
        sb.AppendLine("- Português correto: imperativo de você (teste, veja, comece), nunca \"testa\", \"vê\", \"começa\".");
        sb.AppendLine("- A fala de cada bloco descreve o que a cena daquele bloco mostra. Bloco com mídia do acervo fala do que está na mídia; não narre o que a tela não mostra.");
        sb.AppendLine("- Só cite recursos que estão na lista de recursos do produto; nada de recurso suposto.");
        sb.AppendLine("- A fala final é a frase do cartão final; o CTA vai embaixo. Final próprio deste vídeo, nada genérico.");
        sb.AppendLine("- Números por extenso na fala (\"às quatro da tarde\"). Nada de travessão nem meia-risca em nenhum texto.");
        sb.AppendLine("- Só afirme o que está nas provas da marca. Sem escassez falsa, depoimento inventado, número sem fonte, promessa de resultado ou condição de saúde da pessoa.");
        sb.AppendLine("- Dê também 3 ganchos alternativos pra abertura (ou pro primeiro bloco, no anúncio).");
        sb.AppendLine();
        sb.AppendLine("Como escrever copy (playbook da plataforma):");
        sb.AppendLine(_knowledge.Playbook);

        if (product is not null && !string.IsNullOrWhiteSpace(product.Name))
        {
            sb.AppendLine();
            sb.AppendLine($"Produto: {product.Name} ({product.Kind}). {product.Summary}");
            if (!string.IsNullOrWhiteSpace(product.Audience)) sb.AppendLine($"Pra quem: {product.Audience}");
            if (product.Features.Count > 0) sb.AppendLine($"Recursos (as cenas mostram estes, usando o produto de verdade): {string.Join("; ", product.Features)}");
            if (product.Differentials.Count > 0) sb.AppendLine($"Diferenciais: {string.Join("; ", product.Differentials)}");
            if (!string.IsNullOrWhiteSpace(product.Offer)) sb.AppendLine($"Oferta atual: {product.Offer}");
        }
        if (media.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(product?.AppUrl is { Length: > 0 }
                ? "Acervo da marca: use uma destas mídias no bloco quando ela mostrar a cena melhor que gravar a tela (campo midia = id; vazio pra gravar). Cada mídia no máximo uma vez, e nunca duas que mostram a mesma tela."
                : "Acervo da marca: cada bloco usa uma destas mídias (campo midia = id), a que melhor mostra a cena; cada mídia no máximo uma vez (com menos mídias que cenas, faça menos cenas).");
            sb.AppendLine("Não use mídia de tela vazia (lista sem itens, formulário em branco) nem a que fica quase toda borrada; " +
                "pra mostrar o sistema funcionando, prefira a gravação de tela (midia vazia) quando o produto tem app.");
            sb.AppendLine("Vídeo com momentos: escolha no campo momento o número do momento que mostra a cena da fala (o vídeo entra " +
                "só nesse trecho). Foto ou vídeo sem momentos: momento -1.");
            foreach (var m in media)
            {
                sb.AppendLine($"- {m.Id:N}: {(m.IsVideo ? "vídeo" : "foto")}: {m.Description ?? m.FileName}" +
                    (m.RedactedStorageKey is not null ? $" (com {m.PersonalDataKinds ?? "dado pessoal"} borrado)" : ""));
                var moments = m.Moments;
                for (var i = 0; i < moments.Count; i++)
                    sb.AppendLine($"    momento {i} ({moments[i].End - moments[i].Start:0.#} s): {moments[i].Description}");
            }
        }
        if (brand is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Marca: {brand.Brand.Name}");
            if (!string.IsNullOrWhiteSpace(brand.Voice.Audience)) sb.AppendLine($"Público: {brand.Voice.Audience}");
            if (brand.Voice.Tone.Count > 0) sb.AppendLine($"Tom: {string.Join(", ", brand.Voice.Tone)}");
            if (brand.Voice.BannedTerms.Count > 0) sb.AppendLine($"Nunca use estas palavras: {string.Join(", ", brand.Voice.BannedTerms)}");
            foreach (var (from, to) in brand.Voice.WordSwaps) sb.AppendLine($"Em vez de \"{from}\", diga \"{to}\".");
            var active = brand.Proof.Where(p => p.Active).ToList();
            var allowed = active.Where(p => p.Kind != ProofKind.Forbidden).ToList();
            var forbidden = active.Where(p => p.Kind == ProofKind.Forbidden).ToList();
            if (allowed.Count > 0)
            {
                sb.AppendLine("Provas (o que a marca pode afirmar):");
                foreach (var p in allowed) sb.AppendLine($"- {p.Statement}");
            }
            if (forbidden.Count > 0)
            {
                sb.AppendLine("Nunca diga:");
                foreach (var p in forbidden) sb.AppendLine($"- {p.Statement}");
            }
            if (brand.Phrases.Count > 0)
            {
                sb.AppendLine("Frases reais de clientes (use o jeito de falar, não copie):");
                foreach (var p in brand.Phrases.Take(15)) sb.AppendLine($"- {p.Text}");
            }
        }
        return sb.ToString();
    }

    private static string UserPrompt(CreativeJob job, string? instructions)
    {
        var kind = job.Kind switch { CreativeKind.Ad => "anúncio", CreativeKind.Prospecting => "vídeo de prospecção", _ => "reel orgânico" };
        var sb = new StringBuilder($"Escreva um {kind} sobre: {job.Theme}.");
        if (!string.IsNullOrWhiteSpace(instructions)) sb.Append($"\nPedido da pessoa: {instructions.Trim()}");
        return sb.ToString();
    }

    /// <summary>Formato que a IA devolve; vira roteiro.json e falas.json do contrato do pacote em <see cref="ToContracts"/>.</summary>
    public static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["nome", "abertura", "blocos", "final", "ganchosAlternativos", "ideiasEdicao", "formato", "musicaClima"],
          "properties": {
            "nome": { "type": "string", "description": "nome curto do vídeo em minúsculas com hífen, ex.: psicologia-agenda" },
            "abertura": {
              "anyOf": [
                { "type": "null" },
                { "type": "object", "additionalProperties": false, "required": ["titulo", "subtitulo", "fala"],
                  "properties": { "titulo": { "type": "string" }, "subtitulo": { "type": "string" }, "fala": { "type": "string" } } }
              ]
            },
            "blocos": {
              "type": "array",
              "items": { "type": "object", "additionalProperties": false, "required": ["titulo", "subtitulo", "fala", "cena", "midia", "transicao", "movimento", "momento", "audio", "duracao"],
                "properties": { "titulo": { "type": "string" }, "subtitulo": { "type": "string" }, "fala": { "type": "string" }, "cena": { "type": "string" },
                  "midia": { "type": "string", "description": "id da mídia do acervo pra este bloco, ou vazio" },
                  "transicao": { "type": "string", "enum": ["fade", "dissolve", "slideleft", "slideup", "smoothleft", "wipeleft", "circleopen", "zoomin"] },
                  "movimento": { "type": "string", "enum": ["zoom-in", "zoom-out", "pan-left", "pan-right"] },
                  "momento": { "type": "integer", "description": "número do momento do vídeo do acervo, ou -1" },
                  "audio": { "type": "string", "enum": ["narracao", "musica", "original"] },
                  "duracao": { "type": "number", "description": "segundos na tela quando a cena é só música" } } }
            },
            "final": { "type": "object", "additionalProperties": false, "required": ["titulo", "cta", "fala"],
              "properties": { "titulo": { "type": "string" }, "cta": { "type": "string" }, "fala": { "type": "string" } } },
            "ganchosAlternativos": { "type": "array", "items": { "type": "string" } },
            "ideiasEdicao": { "type": "array", "items": { "type": "string" } },
            "formato": { "type": "string", "enum": ["explicativo", "pessoal"] },
            "musicaClima": { "type": "string" }
          }
        }
        """).RootElement.Clone();
}
