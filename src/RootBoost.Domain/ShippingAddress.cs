namespace RootBoost.Domain;

/// <summary>
/// Value object: the buyer's shipping destination. Immutable. This is what gets sent to the
/// supplier, so it must be complete — an incomplete address means an undeliverable paid order.
/// </summary>
public sealed record ShippingAddress(
    string Name,
    string Line1,
    string City,
    string State,
    string Zip,
    string CountryCode,
    string? Phone = null,
    string? Line2 = null)
{
    /// <summary>
    /// True when every field the supplier requires to ship is present. We refuse to place an
    /// order on an incomplete address rather than have CJ reject it (or ship to nowhere).
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(Line1) &&
        !string.IsNullOrWhiteSpace(City) &&
        !string.IsNullOrWhiteSpace(Zip) &&
        !string.IsNullOrWhiteSpace(CountryCode);
}
