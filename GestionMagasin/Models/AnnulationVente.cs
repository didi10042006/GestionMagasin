namespace GestionMagasin.Models;

public sealed class AnnulationVente
{
    public int Id { get; set; }
    public int VenteId { get; set; }
    public int UtilisateurId { get; set; }
    public string Motif { get; set; } = string.Empty;
    public DateTime AnnuleeLeUtc { get; set; }
}
