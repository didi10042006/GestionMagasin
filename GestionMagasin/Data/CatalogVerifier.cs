using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace GestionMagasin.Data;

public static class CatalogVerifier
{
    public static void Verify(string directory)
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "catalog-check.db");
        DatabaseInitializer.Initialize(databasePath);
        var factory = new SqliteConnectionFactory(databasePath);
        var hashing = new PasswordHashingService();
        var userService = new UserService(factory, hashing);
        var admin = userService.CreateInitialAdministrator(
            "admin-check", "Administrateur test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "A9!");
        var products = new ProductService(factory);
        var categories = new CategoryService(factory);
        categories.Create(admin, "Catégorie test", "Contrôle catalogue");
        var category = categories.GetActive().Single(item => item.Name == "Catégorie test");

        var priceWasRejected = false;
        try
        {
            products.Create(admin, "REF-TEST", "Produit de test", null, category.Id, 0, 0, 2);
        }
        catch (ArgumentException)
        {
            priceWasRejected = true;
        }
        if (!priceWasRejected) throw new InvalidOperationException("Un prix de vente nul a été accepté.");

        products.Create(admin, "REF-TEST", "Produit de test", "Description", category.Id, 50, 120, 2);
        var found = products.Search("REF-TEST", category.Id, AvailabilityFilter.EnRupture, RecordStateFilter.Actif);
        if (found.Count != 1 || found[0].Availability != "En rupture")
        {
            throw new InvalidOperationException("La recherche ou le filtrage initial du produit a échoué.");
        }

        var vendorPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "V8!";
        userService.CreateUser(admin, "vendeur-check", "Vendeur test", vendorPassword, UserRole.Vendeur);
        var auth = new AuthenticationService(factory, hashing);
        var signedInVendor = auth.Authenticate("vendeur-check", vendorPassword)
            ?? throw new InvalidOperationException("Le vendeur de contrôle n'a pas pu s'authentifier.");
        var vendorWasRejected = false;
        try
        {
            categories.Create(signedInVendor, "Interdit", null);
        }
        catch (UnauthorizedAccessException)
        {
            vendorWasRejected = true;
        }
        if (!vendorWasRejected) throw new InvalidOperationException("Un vendeur a pu modifier le catalogue.");

        var productId = found[0].Id;
        products.Archive(admin, productId);
        var archived = products.Search("REF-TEST", category.Id, state: RecordStateFilter.Archive);
        if (archived.Count != 1 || archived[0].Code != "REF-TEST")
        {
            throw new InvalidOperationException("Le produit archivé n'est plus consultable.");
        }

        var referenceWasReserved = false;
        try
        {
            products.Create(admin, "REF-TEST", "Autre produit", null, category.Id, 0, 100, 2);
        }
        catch (InvalidOperationException)
        {
            referenceWasReserved = true;
        }
        if (!referenceWasReserved) throw new InvalidOperationException("La référence archivée a pu être réutilisée.");

        using var connection = factory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var sale = connection.CreateCommand();
        sale.Transaction = transaction;
        sale.CommandText = """
            INSERT INTO Vente (Numero, UtilisateurId, DateVenteUtc, MontantTotalXaf, MontantPayeXaf,
                               MonnaieRendueXaf, ModePaiement, Statut)
            VALUES ('CHECK-VENTE', $userId, $date, 120, 120, 0, 'Espèces', 'Validee');
            SELECT last_insert_rowid();
            """;
        sale.Parameters.AddWithValue("$userId", admin.Id);
        sale.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
        var saleId = Convert.ToInt32(sale.ExecuteScalar());
        var archivedSaleWasRejected = false;
        try
        {
            using var saleLine = connection.CreateCommand();
            saleLine.Transaction = transaction;
            saleLine.CommandText = "INSERT INTO LigneVente (VenteId, ProduitId, Quantite, PrixUnitaireXaf, SousTotalXaf) VALUES ($saleId, $productId, 1, 120, 120);";
            saleLine.Parameters.AddWithValue("$saleId", saleId);
            saleLine.Parameters.AddWithValue("$productId", productId);
            saleLine.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            archivedSaleWasRejected = true;
        }
        if (!archivedSaleWasRejected) throw new InvalidOperationException("Une vente a accepté un produit archivé.");
        transaction.Rollback();
    }
}
