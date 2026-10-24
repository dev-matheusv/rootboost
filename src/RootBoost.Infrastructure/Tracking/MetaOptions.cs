namespace RootBoost.Infrastructure.Tracking;

public sealed class MetaOptions
{
    public const string Section = "Meta";

    public string PixelId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string ApiVersion { get; set; } = "v26.0";

    /// <summary>Token do usuário do sistema com <c>ads_read</c>, só pra ler insights (Meta__AdsAccessToken).</summary>
    public string AdsAccessToken { get; set; } = "";
    /// <summary>Optional: código do Test Events (Events Manager) pra depurar sem sujar os dados reais.</summary>
    public string? TestEventCode { get; set; }
}
