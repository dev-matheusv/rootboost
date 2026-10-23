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
}

/// <summary>Um arquivo JSON do knowledge/, já validado (envelope e ids únicos).</summary>
public sealed record KnowledgeDocument(
    string Name, int Version, DateOnly UpdatedAt, string Source, string? Notes, JsonElement Raw)
{
    public JsonElement Items => Raw.GetProperty("items");
}
