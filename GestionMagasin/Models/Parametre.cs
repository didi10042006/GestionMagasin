namespace GestionMagasin.Models;

public sealed class Parametre
{
    public int Id { get; set; }
    public string Cle { get; set; } = string.Empty;
    public string Valeur { get; set; } = string.Empty;
    public DateTime ModifieLeUtc { get; set; }
}
