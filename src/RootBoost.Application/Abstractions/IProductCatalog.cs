using RootBoost.Application.Models;

namespace RootBoost.Application.Abstractions;

/// <summary>Read access to the configured catalog (catalog.json). Resolves productKey -> product.</summary>
public interface IProductCatalog
{
    CatalogProduct? Find(string productKey);

    IReadOnlyList<CatalogProduct> All();
}
