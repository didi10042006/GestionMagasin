using Microsoft.Data.Sqlite;

namespace GestionMagasin.Data;

public static class DatabaseIsolationVerifier
{
    public static void Verify(string directory)
    {
        Directory.CreateDirectory(directory);
        var realPath = Path.Combine(directory, "real.db");
        var demoPath = Path.Combine(directory, "demo.db");
        if (string.Equals(Path.GetFullPath(realPath), Path.GetFullPath(demoPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Les chemins des bases réelle et de démonstration doivent être différents.");
        }

        DatabaseInitializer.Initialize(realPath);
        DatabaseInitializer.Initialize(demoPath);
        DemoDataSeeder.SeedIfNeeded(new SqliteConnectionFactory(demoPath));

        var realProducts = CountRows(realPath, "Produit");
        var demoProducts = CountRows(demoPath, "Produit");
        var demoCategories = CountRows(demoPath, "Categorie");
        if (realProducts != 0 || demoProducts != 8 || demoCategories == 0)
        {
            throw new InvalidOperationException(
                $"Isolation invalide : réel={realProducts} produits, démonstration={demoProducts} produits, catégories démo={demoCategories}.");
        }

        using var connection = new SqliteConnectionFactory(demoPath).OpenConnection();
        using var states = connection.CreateCommand();
        states.CommandText = """
            SELECT
                SUM(CASE WHEN QuantiteStock > SeuilAlerte THEN 1 ELSE 0 END),
                SUM(CASE WHEN QuantiteStock > 0 AND QuantiteStock <= SeuilAlerte THEN 1 ELSE 0 END),
                SUM(CASE WHEN QuantiteStock = 0 THEN 1 ELSE 0 END)
            FROM Produit;
            """;
        using var reader = states.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) == 0 || reader.GetInt32(1) == 0 || reader.GetInt32(2) == 0)
        {
            throw new InvalidOperationException("Les stocks de démonstration ne couvrent pas les trois états attendus.");
        }
    }

    private static int CountRows(string databasePath, string tableName)
    {
        var allowedTables = new HashSet<string>(["Produit", "Categorie"], StringComparer.Ordinal);
        if (!allowedTables.Contains(tableName))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }
        using var connection = new SqliteConnectionFactory(databasePath).OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
