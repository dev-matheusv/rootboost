namespace RootBoost.Application.Models;

/// <summary>
/// Result of creating a checkout with the provider. <paramref name="RedirectUrl"/> is set only by
/// hosted providers (Stripe Checkout), where the browser must be sent to the provider's page.
/// PayPal's in page flow leaves it null and uses <paramref name="ProviderOrderId"/> alone.
/// </summary>
public sealed record CreateCheckoutResult(
    bool Success, string? ProviderOrderId, decimal Amount, string Currency, string? Error,
    string? RedirectUrl = null)
{
    public static CreateCheckoutResult Ok(string providerOrderId, decimal amount, string currency)
        => new(true, providerOrderId, amount, currency, null);

    /// <summary>Hosted checkout: the caller must redirect the browser to <paramref name="redirectUrl"/>.</summary>
    public static CreateCheckoutResult OkHosted(string providerOrderId, decimal amount, string currency, string redirectUrl)
        => new(true, providerOrderId, amount, currency, null, redirectUrl);

    public static CreateCheckoutResult Fail(string error)
        => new(false, null, 0m, "", error);
}
