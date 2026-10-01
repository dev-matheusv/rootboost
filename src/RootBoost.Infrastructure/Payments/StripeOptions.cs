namespace RootBoost.Infrastructure.Payments;

/// <summary>Stripe config. Secrets come from env vars only (Stripe__SecretKey, Stripe__WebhookSecret).</summary>
public sealed class StripeOptions
{
    public const string Section = "Stripe";

    /// <summary>Secret API key (sk_test_... / sk_live_...). NEVER commit.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Signing secret of the webhook endpoint (whsec_...). Used to verify Stripe-Signature.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Where Stripe sends the buyer after paying / cancelling.</summary>
    public string SuccessUrl { get; set; } = "";
    public string CancelUrl { get; set; } = "";

    /// <summary>Countries we ship to, comma separated ISO codes. Drives shipping_address_collection.</summary>
    public string AllowedCountries { get; set; } = "US";
}
