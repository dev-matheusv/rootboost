namespace RootBoost.Application.Abstractions;

/// <summary>
/// Registra visitas de landing por produto (sinal de topo de funil) e devolve as contagens.
/// Sem PII: só contamos quantas vezes cada landing foi vista, por produto e idioma, por dia.
/// É o denominador que falta pra calcular conversão (pedidos / visitas). A Infra implementa
/// com um contador diário no SQLite (barato, sobrevive a restart).
/// </summary>
public interface IVisitStore
{
    /// <summary>Soma 1 visita pra este produto/idioma no dia de hoje (UTC). Idempotência não
    /// se aplica: cada carregamento de página é uma visita.</summary>
    Task RecordViewAsync(string productKey, string? lang, CancellationToken ct = default);

    /// <summary>Total de visitas por productKey desde <paramref name="since"/> (null = tudo).</summary>
    Task<IReadOnlyDictionary<string, long>> GetViewCountsAsync(DateTimeOffset? since = null, CancellationToken ct = default);
}
