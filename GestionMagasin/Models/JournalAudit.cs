namespace GestionMagasin.Models;

public sealed class JournalAudit
{
    public int Id { get; set; }
    public int? UtilisateurId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Entite { get; set; } = string.Empty;
    public int? EntiteId { get; set; }
    public string? Details { get; set; }
    public DateTime CreeLeUtc { get; set; }
}
