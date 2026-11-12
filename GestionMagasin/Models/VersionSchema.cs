namespace GestionMagasin.Models;

public sealed class VersionSchema
{
    public int Id { get; set; }
    public int Version { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime AppliqueeLeUtc { get; set; }
}
