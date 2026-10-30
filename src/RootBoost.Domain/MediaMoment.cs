namespace RootBoost.Domain;

/// <summary>Trecho de um vídeo do acervo: de onde a onde, e o que a tela mostra nesse intervalo.</summary>
public sealed record MediaMoment(double Start, double End, string Description);
