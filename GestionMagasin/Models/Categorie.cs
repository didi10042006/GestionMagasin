namespace GestionMagasin.Models;

public sealed class Categorie
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Actif { get; set; } = true;
}
