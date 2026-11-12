using GestionMagasin.Models;

namespace GestionMagasin.Services;

public enum AvailabilityFilter
{
    Tous,
    Disponible,
    StockFaible,
    EnRupture
}

public enum RecordStateFilter
{
    Tous,
    Actif,
    Archive
}

public sealed record ProductCatalogItem(Produit Product, string CategoryName, string Availability)
{
    public int Id => Product.Id;
    public string Code => Product.Code;
    public string Name => Product.Nom;
    public int SalePrice => Product.PrixVenteXaf;
    public int Stock => Product.QuantiteStock;
    public int AlertThreshold => Product.SeuilAlerte;
    public string State => Product.Actif ? "Actif" : "Archivé";
}

public sealed record CategoryCatalogItem(int Id, string Name, string? Description, bool Active)
{
    public string State => Active ? "Active" : "Archivée";
}
