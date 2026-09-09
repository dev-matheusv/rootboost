namespace RootBoost.Application.Models;

/// <summary>Result of creating a provider checkout order (price defined server-side, from the catalog).</summary>
public sealed record CreateCheckoutResult(
    bool Success, string? ProviderOrderId, decimal Amount, string Currency, string? Error)
{
    public static CreateCheckoutResult Ok(string providerOrderId, decimal amount, string currency)
        => new(true, providerOrderId, amount, currency, null);
    public static CreateCheckoutResult Fail(string error)
        => new(false, null, 0m, "", error);
}
