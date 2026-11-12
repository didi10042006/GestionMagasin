namespace GestionMagasin.Models;

public sealed class Vente
{
    public int Id { get; set; }
    public string Numero { get; set; } = string.Empty;
    public int UtilisateurId { get; set; }
    public DateTime DateVenteUtc { get; set; }
    public int MontantTotalXaf { get; set; }
    public int MontantPayeXaf { get; set; }
    public int MonnaieRendueXaf { get; set; }
    public string ModePaiement { get; set; } = "Espèces";
    public string Statut { get; set; } = "Validee";
}
