using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RootBoost.Application.Abstractions;
using RootBoost.Domain;

namespace RootBoost.Application.Copy;

public enum CheckStatus
{
    Pass,
    Fail,

    /// <summary>A máquina não decide sozinha: precisa de humano (ou da IA, quando entrar) antes de publicar.</summary>
    NeedsHuman,

    /// <summary>Não bloqueia; melhora a peça (ex.: trocar palavra do sistema pela do cliente).</summary>
    Suggestion
}

public sealed record CheckFinding(string Id, string Group, CheckStatus Status, bool Blocking, string Message, string? Fix = null);

public sealed record CopyCheckResult(bool Blocked, IReadOnlyList<CheckFinding> Findings);

/// <summary>Peça a conferir. Canal e campo são opcionais; com eles, entram os limites de caractere.</summary>
public sealed record CopyPiece(string Text, string? Channel = null, string? Field = null);

/// <summary>
/// Checklist automática de copy (RB-18). Função pura: recebe a peça, o conhecimento geral e o perfil da marca e
/// devolve o que passou, o que falhou e o que precisa de humano. Nada aqui chama rede ou banco, por isso é
/// testada caso a caso. A peça só chega ao humano com isso rodado; nada publica sem aprovação.
/// </summary>
public static class CopyChecklist
{
    private static readonly string[] HumanGroups = ["truth", "quality"];

    public static CopyCheckResult Check(CopyPiece piece, IKnowledgeBase knowledge, BrandProfile? brand)
    {
        var findings = new List<CheckFinding>();
        var text = piece.Text ?? string.Empty;
        var normalized = Normalize(text);
        var checklist = knowledge.Find("copy/checklist")?.Items.EnumerateArray().ToList() ?? [];

        CheckDashes(text, checklist, findings);
        CheckAiMarks(normalized, checklist, brand, findings);
        CheckLimits(piece, knowledge, findings);
        CheckPolicies(normalized, knowledge, brand, findings);
        SuggestWordSwaps(normalized, brand, findings);
        ListBrandForbidden(brand, findings);
        ListHumanItems(checklist, findings);

        var blocked = findings.Any(f => f.Blocking && f.Status == CheckStatus.Fail);
        return new CopyCheckResult(blocked, findings);
    }

    private static void CheckDashes(string text, List<JsonElement> checklist, List<CheckFinding> findings)
    {
        var item = checklist.FirstOrDefault(i => Id(i) == "style-no-dashes");
        if (item.ValueKind == JsonValueKind.Undefined) return;
        var count = new Regex(item.GetProperty("pattern").GetString()!).Matches(text).Count;
        findings.Add(count == 0
            ? new("style-no-dashes", "style", CheckStatus.Pass, true, "Sem travessão nem meia-risca")
            : new("style-no-dashes", "style", CheckStatus.Fail, true,
                $"{count} travessão ou meia-risca no texto", "Trocar por vírgula, ponto ou dois-pontos, ou reescrever a frase"));
    }

    private static void CheckAiMarks(string normalized, List<JsonElement> checklist, BrandProfile? brand, List<CheckFinding> findings)
    {
        var item = checklist.FirstOrDefault(i => Id(i) == "style-no-ai-marks");
        var terms = new List<string>();
        if (item.ValueKind != JsonValueKind.Undefined && item.TryGetProperty("terms", out var t))
            terms.AddRange(t.EnumerateArray().Select(x => x.GetString()!));
        if (brand is not null) terms.AddRange(brand.Voice.BannedTerms);

        var hits = terms.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(term => ContainsWord(normalized, Normalize(term))).ToList();

        // "não é X, é Y" repetido é a marca registrada de texto de IA.
        var notXisY = Regex.Matches(normalized, @"\bnao e [^.,;!?]{1,40}, e\b").Count;
        if (notXisY >= 2) hits.Add("\"não é X, é Y\" repetido");

        findings.Add(hits.Count == 0
            ? new("style-no-ai-marks", "style", CheckStatus.Pass, true, "Sem marcas de texto de IA")
            : new("style-no-ai-marks", "style", CheckStatus.Fail, true,
                $"Marcas de texto de IA ou termos proibidos da marca: {string.Join(", ", hits)}",
                "Reescrever com as palavras do cliente"));
    }

    private static void CheckLimits(CopyPiece piece, IKnowledgeBase knowledge, List<CheckFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(piece.Channel) || string.IsNullOrWhiteSpace(piece.Field)) return;

        var limit = knowledge.Find("channels/limits")?.Items.EnumerateArray().FirstOrDefault(l =>
            string.Equals(l.GetProperty("channel").GetString(), piece.Channel, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(l.GetProperty("field").GetString(), piece.Field, StringComparison.OrdinalIgnoreCase));

        if (limit is not { ValueKind: JsonValueKind.Object } l2)
        {
            findings.Add(new("channel-char-limits", "channel", CheckStatus.NeedsHuman, true,
                $"Sem limite confirmado no knowledge/ pra {piece.Channel}/{piece.Field}", "Conferir o limite na documentação oficial do canal"));
            return;
        }

        var maxChars = l2.GetProperty("maxChars").GetInt32();
        int? maxWords = l2.TryGetProperty("maxWords", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : null;

        // Campos de linha (gancho na tela, legenda) valem por linha; o resto vale pro texto inteiro.
        var perLine = piece.Field!.EndsWith("-line", StringComparison.OrdinalIgnoreCase);
        var units = perLine
            ? piece.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [piece.Text.Trim()];

        var over = units.Select((u, i) => (u, i))
            .Where(x => x.u.Length > maxChars || (maxWords is { } mw && x.u.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > mw))
            .ToList();

        findings.Add(over.Count == 0
            ? new("channel-char-limits", "channel", CheckStatus.Pass, true, $"Dentro do limite de {maxChars} caracteres")
            : new("channel-char-limits", "channel", CheckStatus.Fail, true,
                perLine
                    ? $"{over.Count} linha(s) acima do limite ({maxChars} caracteres{(maxWords is null ? "" : $" ou {maxWords} palavras")})"
                    : $"{piece.Text.Trim().Length} caracteres, limite é {maxChars}",
                "Encurtar sem perder o detalhe concreto"));
    }

    private static void CheckPolicies(string normalized, IKnowledgeBase knowledge, BrandProfile? brand, List<CheckFinding> findings)
    {
        var policies = knowledge.Find("channels/policies")?.Items.EnumerateArray() ?? Enumerable.Empty<JsonElement>();
        foreach (var policy in policies)
        {
            if (!policy.TryGetProperty("patterns", out var patterns) || patterns.GetArrayLength() == 0) continue;

            var hit = patterns.EnumerateArray().Select(p => p.GetString()!)
                .FirstOrDefault(p => Regex.IsMatch(normalized, p, RegexOptions.IgnoreCase));
            if (hit is null) continue;

            var id = Id(policy);
            var rule = policy.GetProperty("rule").GetString()!;
            var block = policy.TryGetProperty("severity", out var s) && s.GetString() == "block";

            // Escassez real (prova ativa no inventário) não é bloqueada: vai pra conferência humana.
            if (block && policy.TryGetProperty("unlessProof", out var unless) &&
                Enum.TryParse<ProofKind>(unless.GetString(), out var kind) &&
                brand?.Proof.Any(p => p.Active && p.Kind == kind) == true)
                block = false;

            findings.Add(new($"policy:{id}", "channel", block ? CheckStatus.Fail : CheckStatus.NeedsHuman, block,
                rule, block ? "Remover ou reescrever o trecho" : "Confirmar que é verdade e permitido no canal"));
        }
    }

    private static void SuggestWordSwaps(string normalized, BrandProfile? brand, List<CheckFinding> findings)
    {
        if (brand is null) return;
        foreach (var (from, to) in brand.Voice.WordSwaps)
            if (ContainsWord(normalized, Normalize(from)))
                findings.Add(new($"swap:{from}", "style", CheckStatus.Suggestion, false,
                    $"\"{from}\" é palavra do sistema", $"Usar \"{to}\", que é a palavra do cliente"));
    }

    private static void ListBrandForbidden(BrandProfile? brand, List<CheckFinding> findings)
    {
        if (brand is null) return;
        var forbidden = brand.Proof.Where(p => p.Active && p.Kind == ProofKind.Forbidden).Select(p => p.Statement).ToList();
        if (forbidden.Count == 0) return;
        findings.Add(new("brand-forbidden", "truth", CheckStatus.NeedsHuman, true,
            "Conferir que a peça não faz nada do que a marca nunca pode dizer: " + string.Join("; ", forbidden)));
    }

    private static void ListHumanItems(List<JsonElement> checklist, List<CheckFinding> findings)
    {
        foreach (var item in checklist)
        {
            var group = item.GetProperty("group").GetString()!;
            if (!HumanGroups.Contains(group)) continue;
            findings.Add(new(Id(item), group, CheckStatus.NeedsHuman, item.GetProperty("blocking").GetBoolean(),
                item.GetProperty("text").GetString()!));
        }
    }

    private static string Id(JsonElement e) => e.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";

    private static bool ContainsWord(string normalizedText, string normalizedTerm) =>
        normalizedTerm.Length > 0 && Regex.IsMatch(normalizedText, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(normalizedTerm)}(?![\p{{L}}\p{{N}}])");

    /// <summary>Minúsculas e sem acento, pra "Revolucione" e "só hoje" casarem com "revolucione" e "so hoje".</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
