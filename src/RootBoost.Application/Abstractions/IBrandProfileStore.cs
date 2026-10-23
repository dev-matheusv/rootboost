using RootBoost.Domain;

namespace RootBoost.Application.Abstractions;

/// <summary>Perfil da marca: voz, inventário de prova e frases reais dos clientes.</summary>
public interface IBrandProfileStore
{
    Task<BrandProfile?> GetAsync(string brandKey, CancellationToken ct = default);
    Task AddProofAsync(ProofItem item, CancellationToken ct = default);
    Task<bool> RetireProofAsync(string brandKey, Guid proofId, CancellationToken ct = default);
    Task AddPhraseAsync(CustomerPhrase phrase, CancellationToken ct = default);
    Task<bool> SetVoiceAsync(string brandKey, BrandVoice voice, CancellationToken ct = default);

    /// <summary>
    /// Carga inicial de um perfil (ex.: o MedlyCare a partir do playbook). Só roda com o perfil vazio, então
    /// repetir não duplica nem sobrescreve o que já foi editado.
    /// </summary>
    Task<bool> ImportIfEmptyAsync(string brandKey, BrandVoice voice, IReadOnlyList<ProofItem> proof,
        IReadOnlyList<CustomerPhrase> phrases, CancellationToken ct = default);
}

public sealed record BrandProfile(Brand Brand, BrandVoice Voice, IReadOnlyList<ProofItem> Proof, IReadOnlyList<CustomerPhrase> Phrases);
