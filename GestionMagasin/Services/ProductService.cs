using GestionMagasin.Data;
using GestionMagasin.Models;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed class ProductService(SqliteConnectionFactory connectionFactory)
{
    public IReadOnlyList<ProductCatalogItem> Search(
        string? query = null,
        int? categoryId = null,
        AvailabilityFilter availability = AvailabilityFilter.Tous,
        RecordStateFilter state = RecordStateFilter.Tous)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH ProduitAvecStock AS (
                SELECT p.*, COALESCE(SUM(m.Quantite), 0) AS StockActuel
                FROM Produit p
                LEFT JOIN MouvementStock m ON m.ProduitId = p.Id
                GROUP BY p.Id
            )
            SELECT p.Id, p.CategorieId, c.Nom, p.Code, p.Nom, p.Description,
                   p.PrixAchatXaf, p.PrixVenteXaf, p.StockActuel, p.SeuilAlerte,
                   p.Actif, p.CreeLeUtc
            FROM ProduitAvecStock p
            INNER JOIN Categorie c ON c.Id = p.CategorieId
            WHERE ($query = '' OR p.Code LIKE $pattern ESCAPE '\' OR p.Nom LIKE $pattern ESCAPE '\')
              AND ($categoryId = 0 OR p.CategorieId = $categoryId)
              AND ($state = 0 OR ($state = 1 AND p.Actif = 1) OR ($state = 2 AND p.Actif = 0))
              AND ($availability = 0
                   OR ($availability = 1 AND p.StockActuel > p.SeuilAlerte)
                   OR ($availability = 2 AND p.StockActuel > 0 AND p.StockActuel <= p.SeuilAlerte)
                   OR ($availability = 3 AND p.StockActuel = 0))
            ORDER BY p.Actif DESC, p.Nom COLLATE NOCASE, p.Code COLLATE NOCASE;
            """;
        var normalizedQuery = query?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(normalizedQuery)}%");
        command.Parameters.AddWithValue("$categoryId", categoryId ?? 0);
        command.Parameters.AddWithValue("$state", (int)state);
        command.Parameters.AddWithValue("$availability", (int)availability);

        var results = new List<ProductCatalogItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var product = new Produit
            {
                Id = reader.GetInt32(0),
                CategorieId = reader.GetInt32(1),
                Code = reader.GetString(3),
                Nom = reader.GetString(4),
                Description = reader.IsDBNull(5) ? null : reader.GetString(5),
                PrixAchatXaf = reader.GetInt32(6),
                PrixVenteXaf = reader.GetInt32(7),
                QuantiteStock = reader.GetInt32(8),
                SeuilAlerte = reader.GetInt32(9),
                Actif = reader.GetInt32(10) == 1,
                CreeLeUtc = DateTime.Parse(reader.GetString(11), null, System.Globalization.DateTimeStyles.RoundtripKind)
            };
            results.Add(new ProductCatalogItem(product, reader.GetString(2), AvailabilityLabel(product)));
        }
        return results;
    }

    public Produit? GetById(int id)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Id, p.CategorieId, p.Code, p.Nom, p.Description, p.PrixAchatXaf,
                   p.PrixVenteXaf, COALESCE((SELECT SUM(m.Quantite) FROM MouvementStock m WHERE m.ProduitId = p.Id), 0),
                   p.SeuilAlerte, p.Actif, p.CreeLeUtc
            FROM Produit p WHERE p.Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new Produit
        {
            Id = reader.GetInt32(0),
            CategorieId = reader.GetInt32(1),
            Code = reader.GetString(2),
            Nom = reader.GetString(3),
            Description = reader.IsDBNull(4) ? null : reader.GetString(4),
            PrixAchatXaf = reader.GetInt32(5),
            PrixVenteXaf = reader.GetInt32(6),
            QuantiteStock = reader.GetInt32(7),
            SeuilAlerte = reader.GetInt32(8),
            Actif = reader.GetInt32(9) == 1,
            CreeLeUtc = DateTime.Parse(reader.GetString(10), null, System.Globalization.DateTimeStyles.RoundtripKind)
        };
    }

    public void Create(AuthenticatedUser? actor, string code, string name, string? description,
        int categoryId, int purchasePrice, int salePrice, int alertThreshold)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        Validate(code, name, categoryId, purchasePrice, salePrice, alertThreshold);
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActiveCategory(connection, categoryId, transaction);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Produit
                (CategorieId, Code, Nom, Description, PrixAchatXaf, PrixVenteXaf,
                 QuantiteStock, SeuilAlerte, Actif, CreeLeUtc)
            VALUES
                ($categoryId, $code, $name, $description, $purchasePrice, $salePrice,
                 0, $alertThreshold, 1, $createdAt);
            """;
        AddProductParameters(command, name, description, categoryId, purchasePrice, salePrice, alertThreshold);
        command.Parameters.AddWithValue("$code", code.Trim());
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        try
        {
            command.ExecuteNonQuery();
            using var identity = connection.CreateCommand();
            identity.Transaction = transaction;
            identity.CommandText = "SELECT last_insert_rowid();";
            var productId = Convert.ToInt32(identity.ExecuteScalar());
            AuditWriter.Record(connection, transaction, actor!.Id, "CreationProduit", "Produit", productId,
                $"Produit créé : {code.Trim()} — {name.Trim()}; prix vente={salePrice} XAF.");
            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Cette référence existe déjà. Chaque référence est unique et reste réservée après archivage.", ex);
        }
    }

    public void Update(AuthenticatedUser? actor, int id, string name, string? description,
        int categoryId, int purchasePrice, int salePrice, int alertThreshold)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        Validate("Référence conservée", name, categoryId, purchasePrice, salePrice, alertThreshold);
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var find = connection.CreateCommand();
        find.Transaction = transaction;
        find.CommandText = "SELECT Actif, CategorieId FROM Produit WHERE Id = $id;";
        find.Parameters.AddWithValue("$id", id);
        int oldCategoryId;
        using (var findReader = find.ExecuteReader())
        {
            if (!findReader.Read()) throw new InvalidOperationException("Ce produit n'existe plus.");
            if (findReader.GetInt32(0) == 0)
            {
                throw new InvalidOperationException("Un produit archivé est consultable, mais ne peut plus être modifié.");
            }
            oldCategoryId = findReader.GetInt32(1);
        }
        EnsureCategoryForUpdate(connection, categoryId, oldCategoryId, transaction);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Produit
            SET Nom = $name, Description = $description, CategorieId = $categoryId,
                PrixAchatXaf = $purchasePrice, PrixVenteXaf = $salePrice,
                SeuilAlerte = $alertThreshold
            WHERE Id = $id AND Actif = 1;
            """;
        AddProductParameters(command, name, description, categoryId, purchasePrice, salePrice, alertThreshold);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        AuditWriter.Record(connection, transaction, actor!.Id, "ModificationProduit", "Produit", id,
            $"Produit modifié : {name.Trim()}; prix vente={salePrice} XAF; seuil={alertThreshold}.");
        transaction.Commit();
    }

    public void Archive(AuthenticatedUser? actor, int id)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Produit SET Actif = 0 WHERE Id = $id AND Actif = 1;";
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException("Ce produit est introuvable ou déjà archivé.");
        }
        AuditWriter.Record(connection, transaction, actor!.Id, "ArchivageProduit", "Produit", id, "Produit archivé.");
        transaction.Commit();
    }

    public static string AvailabilityLabel(Produit product)
    {
        if (product.QuantiteStock == 0) return "En rupture";
        return product.QuantiteStock <= product.SeuilAlerte ? "Stock faible" : "Disponible";
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static void Validate(string code, string name, int categoryId, int purchasePrice, int salePrice, int alertThreshold)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("La référence est obligatoire.");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Le nom du produit est obligatoire.");
        if (categoryId <= 0) throw new ArgumentException("Sélectionnez une catégorie.");
        if (purchasePrice < 0) throw new ArgumentException("Le prix d'achat ne peut pas être négatif.");
        if (salePrice <= 0) throw new ArgumentException("Le prix de vente doit être un entier strictement positif en FCFA.");
        if (alertThreshold < 0) throw new ArgumentException("Le seuil d'alerte ne peut pas être négatif.");
    }

    private static void EnsureActiveCategory(SqliteConnection connection, int categoryId, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM Categorie WHERE Id = $id AND Actif = 1);";
        command.Parameters.AddWithValue("$id", categoryId);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException("La catégorie sélectionnée n'existe pas ou est archivée.");
        }
    }

    private static void EnsureCategoryForUpdate(SqliteConnection connection, int categoryId, int existingCategoryId, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM Categorie WHERE Id = $id AND (Actif = 1 OR Id = $existingId));";
        command.Parameters.AddWithValue("$id", categoryId);
        command.Parameters.AddWithValue("$existingId", existingCategoryId);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException("La catégorie sélectionnée n'existe pas ou est archivée.");
        }
    }

    private static void AddProductParameters(SqliteCommand command, string name, string? description,
        int categoryId, int purchasePrice, int salePrice, int alertThreshold)
    {
        command.Parameters.AddWithValue("$categoryId", categoryId);
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$description", string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim());
        command.Parameters.AddWithValue("$purchasePrice", purchasePrice);
        command.Parameters.AddWithValue("$salePrice", salePrice);
        command.Parameters.AddWithValue("$alertThreshold", alertThreshold);
    }
}
