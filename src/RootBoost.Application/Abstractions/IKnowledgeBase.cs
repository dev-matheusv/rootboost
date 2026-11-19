using System.Text.Json;

namespace RootBoost.Application.Abstractions;

/// <summary>
/// Conhecimento geral de marketing da plataforma (pasta knowledge/ versionada no repositório). Igual pra
/// todas as marcas; o que é de uma marca fica no perfil dela. Desenho em docs/BASE-DE-CONHECIMENTO.md.
/// </summary>
public interface IKnowledgeBase
{
    /// <summary>Playbook de copy em markdown (entra no prompt da IA).</summary>
    string Playbook { get; }

    IReadOnlyList<KnowledgeDocument> Documents { get; }

    /// <summary>Documento pelo caminho relativo sem extensão, ex.: "copy/checklist".</summary>
    KnowledgeDocument? Find(string name);

    /// <summary>
    /// Pacote de conhecimento da marca (knowledge/marcas/&lt;marca&gt;/*.md, versionado): o que o produto faz, como o
    /// sistema funciona. Vazio quando a marca não tem pacote (cliente de fora fica no banco, por marca).
    /// </summary>
    string BrandNotes(string brandKey) => string.Empty;
}

/// <summary>Aprendizados versionados (knowledge/learnings): o que já foi testado, aprovado ou reprovado, por canal.</summary>
public static class Learnings
{
    /// <summary>Texto pro prompt: "testado → resultado", só dos canais pedidos, do mais novo pro mais antigo.</summary>
    public static string ForPrompt(IKnowledgeBase knowledge, params string[] channels)
    {
        var doc = knowledge.Find("learnings/learnings");
        if (doc is null) return string.Empty;
        var lines = doc.Items.EnumerateArray()
            .Where(i => channels.Length == 0 || channels.Contains(i.TryGetProperty("channel", out var c) ? c.GetString() : null))
            .OrderByDescending(i => i.TryGetProperty("date", out var d) ? d.GetString() : string.Empty, StringComparer.Ordinal)
            .Take(20)
            .Select(i => $"- {Text(i, "tested")} → {Text(i, "result")} (confiança {Text(i, "confidence")})");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? string.Empty : string.Empty;
}

/// <summary>Um arquivo JSON do knowledge/, já validado (envelope e ids únicos).</summary>
public sealed record KnowledgeDocument(
    string Name, int Version, DateOnly UpdatedAt, string Source, string? Notes, JsonElement Raw)
{
    public JsonElement Items => Raw.GetProperty("items");
}
