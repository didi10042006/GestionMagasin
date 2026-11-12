namespace GestionMagasin.Models;

public sealed class LigneVente
{
    public int Id { get; set; }
    public int VenteId { get; set; }
    public int ProduitId { get; set; }
    public int Quantite { get; set; }
    public int PrixUnitaireXaf { get; set; }
    public int SousTotalXaf { get; set; }
}
