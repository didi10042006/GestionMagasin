using System.Security.Cryptography;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Data;

public static class DemoDataSeeder
{
    private const string DemoCashierUsername = "vendeur-demo";
    private const string DemoCashierFullName = "Vendeur de démonstration";

    private sealed record DemoProduct(string Code, string Name, string Category, int Price, int Stock, int Alert);

    private static readonly DemoProduct[] Products =
    [
        new("PR001", "Riz parfumé 5 kg", "Céréales", 6500, 25, 5),
        new("PR002", "Huile végétale 1 L", "Huiles", 1500, 3, 5),
        new("PR003", "Sucre 1 kg", "Épicerie", 900, 0, 5),
        new("PR004", "Lait en poudre", "Produits laitiers", 2500, 12, 5),
        new("PR005", "Savon de toilette", "Hygiène", 750, 2, 5),
        new("PR006", "Eau minérale", "Boissons", 500, 30, 5),
        new("PR007", "Spaghetti 500 g", "Pâtes alimentaires", 800, 1, 5),
        new("PR008", "Café soluble", "Boissons", 2000, 8, 5)
    ];

    public static void SeedIfNeeded(SqliteConnectionFactory factory)
    {
        using var connection = factory.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM Parametre WHERE Cle = $key;";
            check.Parameters.AddWithValue("$key", "DemoDataSeeded");
            if (Convert.ToInt64(check.ExecuteScalar()) != 0)
            {
                transaction.Commit();
                return;
            }
        }

        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var categoryName in Products.Select(product => product.Category).Distinct())
        {
            using var addCategory = connection.CreateCommand();
            addCategory.Transaction = transaction;
            addCategory.CommandText = "INSERT OR IGNORE INTO Categorie (Nom, Description, Actif) VALUES ($name, $description, 1);";
            addCategory.Parameters.AddWithValue("$name", categoryName);
            addCategory.Parameters.AddWithValue("$description", "Catégorie de démonstration");
            addCategory.ExecuteNonQuery();

            using var getCategory = connection.CreateCommand();
            getCategory.Transaction = transaction;
            getCategory.CommandText = "SELECT Id FROM Categorie WHERE Nom = $name;";
            getCategory.Parameters.AddWithValue("$name", categoryName);
            categories[categoryName] = Convert.ToInt32(getCategory.ExecuteScalar());
        }

        foreach (var product in Products)
        {
            using var addProduct = connection.CreateCommand();
            addProduct.Transaction = transaction;
            addProduct.CommandText = """
                INSERT OR IGNORE INTO Produit
                    (CategorieId, Code, Nom, Description, PrixAchatXaf, PrixVenteXaf, QuantiteStock, SeuilAlerte, Actif, CreeLeUtc)
                VALUES
                    ($categoryId, $code, $name, $description, 0, $price, $stock, $alert, 1, $createdAt);
                """;
            addProduct.Parameters.AddWithValue("$categoryId", categories[product.Category]);
            addProduct.Parameters.AddWithValue("$code", product.Code);
            addProduct.Parameters.AddWithValue("$name", product.Name);
            addProduct.Parameters.AddWithValue("$description", "Article de démonstration");
            addProduct.Parameters.AddWithValue("$price", product.Price);
            addProduct.Parameters.AddWithValue("$stock", product.Stock);
            addProduct.Parameters.AddWithValue("$alert", product.Alert);
            addProduct.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
            addProduct.ExecuteNonQuery();

            if (product.Stock > 0)
            {
                using var addInitialStock = connection.CreateCommand();
                addInitialStock.Transaction = transaction;
                addInitialStock.CommandText = """
                    INSERT OR IGNORE INTO MouvementStock
                        (ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc)
                    SELECT Id, NULL, $quantity, 'StockInitial', 'INITIAL', 'Stock initial de démonstration', $createdAt
                    FROM Produit
                    WHERE Code = $code
                      AND NOT EXISTS (SELECT 1 FROM MouvementStock WHERE ProduitId = Produit.Id);
                    """;
                addInitialStock.Parameters.AddWithValue("$quantity", product.Stock);
                addInitialStock.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
                addInitialStock.Parameters.AddWithValue("$code", product.Code);
                addInitialStock.ExecuteNonQuery();
            }
        }

        using (var markSeeded = connection.CreateCommand())
        {
            markSeeded.Transaction = transaction;
            markSeeded.CommandText = "INSERT INTO Parametre (Cle, Valeur, ModifieLeUtc) VALUES ($key, $value, $date);";
            markSeeded.Parameters.AddWithValue("$key", "DemoDataSeeded");
            markSeeded.Parameters.AddWithValue("$value", "true");
            markSeeded.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
            markSeeded.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public static AuthenticatedUser EnsureDemoCashier(SqliteConnectionFactory factory)
    {
        using var connection = factory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT Id, NomComplet, Role, Actif FROM Utilisateur WHERE NomUtilisateur = $username COLLATE NOCASE;";
        find.Parameters.AddWithValue("$username", DemoCashierUsername);
        using (var reader = find.ExecuteReader())
        {
            if (reader.Read())
            {
                var id = reader.GetInt32(0);
                var fullName = reader.GetString(1);
                var role = reader.GetString(2);
                var active = reader.GetInt32(3) == 1;
                if (!active || role != nameof(UserRole.Vendeur))
                    throw new InvalidOperationException("Le compte interne de démonstration est désactivé ou invalide.");
                transaction.Commit();
                return new AuthenticatedUser(id, DemoCashierUsername, fullName, UserRole.Vendeur);
            }
        }

        // The demo cashier is an internal session identity. Its unguessable password is
        // discarded immediately; only a salted PBKDF2 hash is ever written to the database.
        var oneTimePassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var passwordHash = new PasswordHashingService().HashPassword(oneTimePassword);
        using var add = connection.CreateCommand();
        add.Transaction = transaction;
        add.CommandText = """
            INSERT INTO Utilisateur (NomUtilisateur, NomComplet, MotDePasseHash, Role, Actif, CreeLeUtc)
            VALUES ($username, $fullName, $passwordHash, $role, 1, $createdAt);
            SELECT last_insert_rowid();
            """;
        add.Parameters.AddWithValue("$username", DemoCashierUsername);
        add.Parameters.AddWithValue("$fullName", DemoCashierFullName);
        add.Parameters.AddWithValue("$passwordHash", passwordHash);
        add.Parameters.AddWithValue("$role", nameof(UserRole.Vendeur));
        add.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        var cashierId = Convert.ToInt32(add.ExecuteScalar());
        transaction.Commit();
        return new AuthenticatedUser(cashierId, DemoCashierUsername, DemoCashierFullName, UserRole.Vendeur);
    }
}
