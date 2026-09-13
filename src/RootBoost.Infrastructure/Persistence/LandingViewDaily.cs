namespace RootBoost.Infrastructure.Persistence;

/// <summary>
/// Contador de visitas de landing agregado por produto + idioma + dia (UTC). Não é conceito de
/// domínio (é só analytics de topo de funil), por isso vive na Infra, não no Domain. Sem PII.
/// Um pedaço de dado por (produto, idioma, dia) mantém a tabela pequena mesmo com muito tráfego.
/// </summary>
public sealed class LandingViewDaily
{
    public string ProductKey { get; set; } = default!;
    public string Lang { get; set; } = "";
    public DateTime DateUtc { get; set; }   // data (00:00 UTC), sem hora
    public long Count { get; set; }
}
