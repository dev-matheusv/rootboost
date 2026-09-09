using System.Globalization;
using System.Text.Json;
using RootBoost.Application.Abstractions;
using RootBoost.Application.Models;

namespace RootBoost.Infrastructure.Catalog;

/// <summary>
/// Loads catalog.json once at startup into an immutable lookup. Registered as a singleton.
/// Adding a product is a config edit here — no code change, per the platform's core design.
/// </summary>
public sealed class JsonCatalog : IProductCatalog
{
    private readonly Dictionary<string, CatalogProduct> _byKey;

    private JsonCatalog(Dictionary<string, CatalogProduct> byKey) => _byKey = byKey;

    public static JsonCatalog LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static JsonCatalog LoadFromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, CatalogProduct>(StringComparer.OrdinalIgnoreCase);

        if (doc.RootElement.TryGetProperty("products", out var products))
        {
            foreach (var entry in products.EnumerateObject())
            {
                var key = entry.Name;
                var p = entry.Value;

                var price = decimal.Parse(GetString(p, "price") ?? "0", CultureInfo.InvariantCulture);
                var currency = GetString(p, "currency") ?? "USD";
                var variant = GetString(p, "supplierVariantId") ?? "";
                var display = GetString(p, "displayName");
                var logistic = GetString(p, "logisticName");

                var langs = new List<string>();
                if (p.TryGetProperty("langs", out var l) && l.ValueKind == JsonValueKind.Array)
                    foreach (var item in l.EnumerateArray())
                        if (item.GetString() is { } s) langs.Add(s);

                map[key] = new CatalogProduct(key, price, currency, variant, langs, logistic, display);
            }
        }

        return new JsonCatalog(map);
    }

    public CatalogProduct? Find(string productKey)
        => productKey is not null && _byKey.TryGetValue(productKey, out var p) ? p : null;

    public IReadOnlyList<CatalogProduct> All() => _byKey.Values.ToList();

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String ? v.GetString() : null;
}
