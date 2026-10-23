using RootBoost.Domain;

namespace RootBoost.Application.Abstractions;

/// <summary>Marcas cadastradas na plataforma.</summary>
public interface IBrandStore
{
    Task<IReadOnlyList<Brand>> ListAsync(CancellationToken ct = default);
}
