namespace GestionMagasin.Models;

public sealed class Utilisateur
{
    public int Id { get; set; }
    public string NomUtilisateur { get; set; } = string.Empty;
    public string NomComplet { get; set; } = string.Empty;
    public string MotDePasseHash { get; set; } = string.Empty;
    public string Role { get; set; } = "Vendeur";
    public bool Actif { get; set; } = true;
    public DateTime CreeLeUtc { get; set; }
}
