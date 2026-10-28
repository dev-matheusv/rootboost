using System.Text.Json;
using RootBoost.Domain;

namespace RootBoost.Application.Studio;

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
