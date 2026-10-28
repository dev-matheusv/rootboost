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
}

/// <summary>Uma chamada à IA: quanto custou, pra quê e de qual marca (decisão: uso e custo gravados).</summary>
public sealed record AiUsageEntry(Guid Id, string? BrandKey, string Purpose, string Model, long InputTokens, long OutputTokens,
    decimal CostUsd, DateTimeOffset CreatedAt);

public interface IAiUsageStore
{
    Task AddAsync(AiUsageEntry entry, CancellationToken ct = default);
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

    public sealed record Written(JsonElement Script, JsonElement Lines, IReadOnlyList<string> Hooks, IReadOnlyList<string> Warnings, decimal CostUsd);

    public sealed record Failed(IReadOnlyList<string> Errors);

    /// <returns>O roteiro pronto ou os erros que sobraram depois de duas tentativas.</returns>
    public async Task<(Written? Ok, Failed? Error)> WriteAsync(CreativeJob job, BrandProfile? brand, string? instructions, CancellationToken ct = default,
        BrandProduct? product = null)
    {
        var system = SystemPrompt(job.Kind, brand, product);
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
            var (script, lines, hooks) = ToContracts(doc.RootElement);
            errors = ScriptContract.Validate(script, lines);
            if (errors.Count == 0)
            {
                var review = ScriptGates.Review(job.Kind, script, lines, _knowledge, brand);
                if (review.Errors.Count == 0) return (new Written(script, lines, hooks, review.Warnings, cost), null);
                errors = review.Errors;
            }
        }
        return (null, new Failed(errors));
    }

    /// <summary>
    /// A IA não sabe o tempo de cada gravação ainda: cada bloco ganha um trecho "a gravar" com a duração estimada
    /// da fala (o worker troca pela gravação de tela no RB-50) e guarda a descrição da cena pra quem for gravar.
    /// </summary>
    private static (JsonElement Script, JsonElement Lines, IReadOnlyList<string> Hooks) ToContracts(JsonElement ai)
    {
        var lines = new List<string>();
        var opening = ai.TryGetProperty("abertura", out var a) && a.ValueKind == JsonValueKind.Object
            ? new[] { Str(a, "titulo"), Str(a, "subtitulo") } : null;
        if (opening is not null) lines.Add(Str(a, "fala"));

        var blocks = new List<object>();
        var n = 0;
        foreach (var b in ai.GetProperty("blocos").EnumerateArray())
        {
            n++;
            var fala = Str(b, "fala");
            lines.Add(fala);
            var seconds = Math.Max(3, Math.Round(fala.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length / ScriptGates.WordsPerSecond, 1));
            blocks.Add(new
            {
                titulo = Str(b, "titulo"),
                sub = Str(b, "subtitulo"),
                cena = Str(b, "cena"),
                trechos = new object[] { new object[] { $"gravar-bloco-{n}", 0, seconds } },
            });
        }
        var final = ai.GetProperty("final");
        lines.Add(Str(final, "fala"));

        var script = new Dictionary<string, object?>
        {
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

    private string SystemPrompt(CreativeKind kind, BrandProfile? brand, BrandProduct? product)
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
        sb.AppendLine("- Conversa, não locução. A voz fala como quem fez o produto, não como cliente.");
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
          "required": ["nome", "abertura", "blocos", "final", "ganchosAlternativos"],
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
              "items": { "type": "object", "additionalProperties": false, "required": ["titulo", "subtitulo", "fala", "cena"],
                "properties": { "titulo": { "type": "string" }, "subtitulo": { "type": "string" }, "fala": { "type": "string" }, "cena": { "type": "string" } } }
            },
            "final": { "type": "object", "additionalProperties": false, "required": ["titulo", "cta", "fala"],
              "properties": { "titulo": { "type": "string" }, "cta": { "type": "string" }, "fala": { "type": "string" } } },
            "ganchosAlternativos": { "type": "array", "items": { "type": "string" } }
          }
        }
        """).RootElement.Clone();
}
