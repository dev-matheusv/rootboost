using System.Text.Json;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Application.Studio;

/// <summary>Vozes das marcas (RB-59): mesma fila com dono e prazo do estúdio, pega uma por vez pelo worker.</summary>
public interface INarrationVoiceStore
{
    Task AddAsync(NarrationVoice voice, CancellationToken ct = default);
    Task<NarrationVoice?> GetAsync(Guid id, CancellationToken ct = default);
    Task<NarrationVoice?> FindByKeyAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<NarrationVoice>> ListByBrandAsync(string brandKey, CancellationToken ct = default);
    Task<NarrationVoice?> ClaimNextAsync(string workerId, DateTimeOffset now, TimeSpan lease, CancellationToken ct = default);
    Task RemoveAsync(NarrationVoice voice, CancellationToken ct = default);
    Task SaveAsync(NarrationVoice voice, CancellationToken ct = default);
}

/// <summary>Sinais de vida dos workers e o retrato da fila (posição e tempo médio) pro Console.</summary>
public interface IStudioStatusStore
{
    Task BeatAsync(string workerId, string? activity, DateTimeOffset now, CancellationToken ct = default);
    Task<IReadOnlyList<WorkerHeartbeat>> ListWorkersAsync(CancellationToken ct = default);
    /// <summary>Ids dos jobs na fila, na ordem em que o worker vai pegar (todas as marcas).</summary>
    Task<IReadOnlyList<Guid>> QueueAsync(CancellationToken ct = default);
    /// <summary>Média dos últimos renders entregues, em segundos (nulo sem histórico).</summary>
    Task<int?> AverageRenderSecondsAsync(CancellationToken ct = default);
}

/// <summary>Volta a marca de demonstração ao começo: apaga posts, vídeos, acervo e vozes dela (nunca de marca real).</summary>
public interface IDemoReset
{
    /// <returns>Chaves do armazenamento que eram da marca (o chamador apaga os arquivos).</returns>
    Task<IReadOnlyList<string>> ResetAsync(string brandKey, CancellationToken ct = default);
}

public interface ICreativeJobStore
{
    Task AddAsync(CreativeJob job, CancellationToken ct = default);
    Task<CreativeJob?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<CreativeJob>> ListByBrandAsync(string brandKey, CancellationToken ct = default);

    /// <summary>
    /// Pega o próximo job disponível (na fila ou com prazo vencido) e marca como deste worker, de forma atômica:
    /// dois workers ao mesmo tempo nunca levam o mesmo job. Null: nada pra fazer.
    /// </summary>
    Task<CreativeJob?> ClaimNextAsync(string workerId, DateTimeOffset now, TimeSpan lease, CancellationToken ct = default);

    Task SaveAsync(CreativeJob job, CancellationToken ct = default);
    Task RemoveAsync(CreativeJob job, CancellationToken ct = default);
}

/// <summary>Configuração do estúdio. Sem <see cref="WorkerKey"/> as rotas do worker ficam desligadas.</summary>
public sealed class StudioOptions
{
    public string? WorkerKey { get; set; }
    /// <summary>Quanto tempo o worker fica dono do job sem dar notícia (cada progresso renova).</summary>
    public int LeaseMinutes { get; set; } = 15;
    public TimeSpan Lease => TimeSpan.FromMinutes(Math.Clamp(LeaseMinutes, 1, 240));
}

/// <summary>
/// Contratos do pacote do marketing (seção 4), conferidos antes de qualquer render:
/// <code>
/// roteiro.json { "nome", "abertura": [título, subtítulo] | null, "fecho"?: [título, cta],
///                "blocos": [{ "titulo", "sub", "trechos": [[fonte, início_s, fim_s, velocidade?]] }] }
/// falas.json   ["fala da abertura?", "fala do bloco 1", ..., "fala final"]   (blocos + 1, +1 com abertura)
/// </code>
/// Também recusa travessão e meia-risca no que vira tela ou voz (regra de copy que vale sempre).
/// </summary>
public static class ScriptContract
{
    private const int MaxLines = 60;

    public static IReadOnlyList<string> Validate(JsonElement script, JsonElement lines)
    {
        var errors = new List<string>();
        if (script.ValueKind != JsonValueKind.Object)
        {
            errors.Add("roteiro precisa ser um objeto JSON");
            return errors;
        }

        if (!script.TryGetProperty("nome", out var nome) || nome.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(nome.GetString()))
            errors.Add("roteiro sem \"nome\"");

        var hasOpening = false;
        if (script.TryGetProperty("abertura", out var opening) && opening.ValueKind != JsonValueKind.Null)
        {
            hasOpening = true;
            CheckPair(opening, "abertura", errors);
        }
        if (script.TryGetProperty("fecho", out var closing) && closing.ValueKind != JsonValueKind.Null)
            CheckPair(closing, "fecho", errors);

        var blocks = 0;
        if (!script.TryGetProperty("blocos", out var blocos) || blocos.ValueKind != JsonValueKind.Array || blocos.GetArrayLength() == 0)
            errors.Add("roteiro sem \"blocos\" (precisa de pelo menos um)");
        else
        {
            foreach (var b in blocos.EnumerateArray())
            {
                blocks++;
                var where = $"bloco {blocks}";
                if (b.ValueKind != JsonValueKind.Object) { errors.Add($"{where} precisa ser um objeto"); continue; }
                CheckText(b, "titulo", where, required: true, errors);
                CheckText(b, "sub", where, required: false, errors);
                if (!b.TryGetProperty("trechos", out var trechos) || trechos.ValueKind != JsonValueKind.Array || trechos.GetArrayLength() == 0)
                {
                    errors.Add($"{where} sem \"trechos\"");
                    continue;
                }
                var n = 0;
                foreach (var t in trechos.EnumerateArray()) CheckClip(t, $"{where}, trecho {++n}", errors);
            }
        }

        if (lines.ValueKind != JsonValueKind.Array)
        {
            errors.Add("falas precisa ser uma lista de frases");
            return errors;
        }
        var count = 0;
        foreach (var l in lines.EnumerateArray())
        {
            count++;
            if (l.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(l.GetString())) errors.Add($"fala {count} vazia");
            else if (HasDash(l.GetString()!)) errors.Add($"fala {count} tem travessão ou meia-risca");
        }
        if (count > MaxLines) errors.Add($"falas demais ({count}, máximo {MaxLines})");

        if (blocks > 0)
        {
            var expected = blocks + 1 + (hasOpening ? 1 : 0);
            if (count != expected)
                errors.Add($"falas não batem com o roteiro: {count} frases, esperado {expected} " +
                    $"({blocks} blocos + fala final{(hasOpening ? " + abertura" : string.Empty)})");
        }
        return errors;
    }

    private static void CheckPair(JsonElement e, string name, List<string> errors)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != 2 || e.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
        {
            errors.Add($"\"{name}\" precisa ser [título, texto]");
            return;
        }
        if (string.IsNullOrWhiteSpace(e[0].GetString())) errors.Add($"\"{name}\" sem título");
        if (e.EnumerateArray().Any(x => HasDash(x.GetString()!))) errors.Add($"\"{name}\" tem travessão ou meia-risca");
    }

    private static void CheckText(JsonElement obj, string prop, string where, bool required, List<string> errors)
    {
        if (!obj.TryGetProperty(prop, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            if (required) errors.Add($"{where} sem \"{prop}\"");
            return;
        }
        if (v.ValueKind != JsonValueKind.String) { errors.Add($"{where}: \"{prop}\" precisa ser texto"); return; }
        if (required && string.IsNullOrWhiteSpace(v.GetString())) errors.Add($"{where} sem \"{prop}\"");
        if (HasDash(v.GetString()!)) errors.Add($"{where}: \"{prop}\" tem travessão ou meia-risca");
    }

    /// <summary>[fonte, início, fim, velocidade?]: fonte é o nome do bruto; fim depois do início; velocidade positiva.</summary>
    private static void CheckClip(JsonElement t, string where, List<string> errors)
    {
        if (t.ValueKind != JsonValueKind.Array || t.GetArrayLength() is < 3 or > 4)
        {
            errors.Add($"{where} precisa ser [fonte, início, fim, velocidade?]");
            return;
        }
        if (t[0].ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(t[0].GetString())) errors.Add($"{where} sem fonte");
        // TryGetDouble lança em elemento que não é número: confere o tipo antes.
        if (t[1].ValueKind != JsonValueKind.Number || t[2].ValueKind != JsonValueKind.Number ||
            !t[1].TryGetDouble(out var start) || !t[2].TryGetDouble(out var end))
        {
            errors.Add($"{where}: início e fim precisam ser números (segundos)");
            return;
        }
        if (start < 0 || end <= start) errors.Add($"{where}: fim ({end}) precisa ser depois do início ({start})");
        if (t.GetArrayLength() == 4 && (t[3].ValueKind != JsonValueKind.Number || !t[3].TryGetDouble(out var speed) || speed <= 0))
            errors.Add($"{where}: velocidade precisa ser maior que zero");
    }

    private static bool HasDash(string s) => s.Contains('—') || s.Contains('–');
}

/// <summary>Resultado dos portões do roteiro: erros bloqueiam o salvamento, avisos vão pra revisão.</summary>
public sealed record ScriptReview(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Portões do roteiro (RB-44, etapa 9 do pacote), antes de gastar render:
/// checklist de copy da marca nas falas e nos textos de tela (o que bloquearia a legenda bloqueia aqui também),
/// duração estimada pela faixa do formato (~2,4 palavras por segundo) e número escrito em algarismo
/// (a voz tropeça; o roteiro pede por extenso).
/// </summary>
public static class ScriptGates
{
    public const double WordsPerSecond = 2.4;

    /// <summary>Faixa de duração por formato, em segundos (pacote do marketing, etapa 1).</summary>
    public static (int Min, int Max) Range(CreativeKind kind) => kind switch
    {
        CreativeKind.Ad => (13, 16),
        CreativeKind.Prospecting => (60, 120),
        _ => (25, 40),
    };

    public static ScriptReview Review(CreativeKind kind, JsonElement script, JsonElement lines,
        RootBoost.Application.Abstractions.IKnowledgeBase knowledge, BrandProfile? brand)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var falas = lines.EnumerateArray().Select(l => l.GetString() ?? string.Empty).ToList();

        var screenText = ScreenTexts(script);
        var check = RootBoost.Application.Copy.CopyChecklist.Check(
            new RootBoost.Application.Copy.CopyPiece(string.Join("\n", falas.Concat(screenText))), knowledge, brand);
        foreach (var f in check.Findings.Where(f => f.Status == RootBoost.Application.Copy.CheckStatus.Fail))
            (f.Blocking ? errors : warnings).Add(f.Fix is null ? f.Message : $"{f.Message} ({f.Fix})");

        var words = falas.Sum(f => f.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        var seconds = (int)Math.Round(words / WordsPerSecond);
        var (min, max) = Range(kind);
        if (seconds < min || seconds > max)
            warnings.Add($"duração estimada de {seconds} s fora da faixa do formato ({min} a {max} s)");

        for (var i = 0; i < falas.Count; i++)
            if (falas[i].Any(char.IsDigit))
                warnings.Add($"fala {i + 1} tem número em algarismo: escreva por extenso pra voz não tropeçar");

        return new ScriptReview(errors, warnings);
    }

    private static IEnumerable<string> ScreenTexts(JsonElement script)
    {
        foreach (var name in new[] { "abertura", "fecho" })
            if (script.TryGetProperty(name, out var pair) && pair.ValueKind == JsonValueKind.Array)
                foreach (var x in pair.EnumerateArray())
                    if (x.ValueKind == JsonValueKind.String) yield return x.GetString()!;
        if (script.TryGetProperty("blocos", out var blocos) && blocos.ValueKind == JsonValueKind.Array)
            foreach (var b in blocos.EnumerateArray())
                foreach (var prop in new[] { "titulo", "sub" })
                    if (b.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String) yield return v.GetString()!;
    }
}
