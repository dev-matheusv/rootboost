namespace RootBoost.Infrastructure.Suppliers;

public sealed class CjOptions
{
    public const string Section = "Cj";

    public string Email { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "https://developers.cjdropshipping.com/api2.0/v1/";
}
