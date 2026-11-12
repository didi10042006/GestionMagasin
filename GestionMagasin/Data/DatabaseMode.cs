namespace GestionMagasin.Data;

public enum DatabaseMode
{
    Reelle,
    Demonstration
}

public sealed record DatabaseSession(DatabaseMode Mode, string DatabasePath)
{
    public string ModeLabel => Mode == DatabaseMode.Reelle ? "Base réelle" : "Base de démonstration";
}
