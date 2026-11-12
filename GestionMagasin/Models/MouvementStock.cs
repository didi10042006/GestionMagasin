namespace GestionMagasin.Models;

public sealed class MouvementStock
{
    public int Id { get; set; }
    public int ProduitId { get; set; }
    public int? UtilisateurId { get; set; }
    public int Quantite { get; set; }
    public string TypeMouvement { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string? Motif { get; set; }
    public DateTime CreeLeUtc { get; set; }
}
