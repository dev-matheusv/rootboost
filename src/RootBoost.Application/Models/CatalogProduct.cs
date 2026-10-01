namespace RootBoost.Application.Models;

/// <summary>
/// One product as configured in catalog.json. Adding a product = one of these + a landing folder.
/// No code changes. This is what makes the platform multi-product/multi-language by config.
/// </summary>
/// <param name="LandingPath">
/// Folder of this product's landing page, which is NOT the same as <paramref name="Key"/>
/// (e.g. key "veggiechopper" lives at /kitchen/). Used to build return URLs after payment.
/// </param>
public sealed record CatalogProduct(
    string Key,
    decimal Price,
    string Currency,
    string SupplierVariantId,
    IReadOnlyList<string> Languages,
    string? LogisticName = null,
    string? DisplayName = null,
    string? LandingPath = null)
{
    public bool IsFulfillable =>
        !string.IsNullOrWhiteSpace(SupplierVariantId) &&
        !SupplierVariantId.StartsWith("TODO", StringComparison.OrdinalIgnoreCase);

    /// <summary>Landing folder, falling back to the catalog key when not configured.</summary>
    public string PathOrKey => string.IsNullOrWhiteSpace(LandingPath) ? Key : LandingPath!;
}
