namespace GestionMagasin.Models;

public sealed class Produit
{
    public int Id { get; set; }
    public int CategorieId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Nom { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PrixAchatXaf { get; set; }
    public int PrixVenteXaf { get; set; }
    public int QuantiteStock { get; set; }
    public int SeuilAlerte { get; set; }
    public bool Actif { get; set; } = true;
    public DateTime CreeLeUtc { get; set; }
}
