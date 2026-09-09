namespace RootBoost.Application.Models;

/// <summary>
/// One product as configured in catalog.json. Adding a product = one of these + a landing folder.
/// No code changes. This is what makes the platform multi-product/multi-language by config.
/// </summary>
public sealed record CatalogProduct(
    string Key,
    decimal Price,
    string Currency,
    string SupplierVariantId,
    IReadOnlyList<string> Languages,
    string? LogisticName = null,
    string? DisplayName = null)
{
    public bool IsFulfillable =>
        !string.IsNullOrWhiteSpace(SupplierVariantId) &&
        !SupplierVariantId.StartsWith("TODO", StringComparison.OrdinalIgnoreCase);
}
