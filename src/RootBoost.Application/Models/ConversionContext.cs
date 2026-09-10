namespace RootBoost.Application.Models;

/// <summary>
/// Sinais do navegador/request que melhoram o "match quality" da conversão no Meta CAPI.
/// Todos opcionais: quando ausentes, o evento server ainda vale (email + valor), só com match menor.
/// fbp/fbc vêm dos cookies do Pixel; ip/user-agent do request; sourceUrl da página da landing.
/// </summary>
public sealed record ConversionContext(
    string? Fbp = null,
    string? Fbc = null,
    string? ClientIp = null,
    string? UserAgent = null,
    string? EventSourceUrl = null);
