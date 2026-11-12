namespace GestionMagasin.Data;

public static class DatabaseOptions
{
    public static string GetRealDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMagasin");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "gestion-magasin.db");
    }

    public static string GetDemoDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMagasin", "Demo");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "gestion-magasin-demo.db");
    }
}
