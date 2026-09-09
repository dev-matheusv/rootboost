namespace RootBoost.Infrastructure.Payments;

public sealed class PayPalOptions
{
    public const string Section = "PayPal";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>The webhook id from the PayPal dashboard, required to verify signatures.</summary>
    public string WebhookId { get; set; } = "";
    /// <summary>Live: https://api-m.paypal.com  · Sandbox: https://api-m.sandbox.paypal.com</summary>
    public string BaseUrl { get; set; } = "https://api-m.paypal.com";
}
