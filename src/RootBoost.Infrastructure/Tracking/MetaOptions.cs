namespace RootBoost.Infrastructure.Tracking;

public sealed class MetaOptions
{
    public const string Section = "Meta";

    public string PixelId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>Optional: código do Test Events (Events Manager) pra depurar sem sujar os dados reais.</summary>
    public string? TestEventCode { get; set; }
}
