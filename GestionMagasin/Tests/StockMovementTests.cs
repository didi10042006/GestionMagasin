using GestionMagasin.Data;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class StockMovementTests : IDisposable
{
    private readonly string _databasePath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly AuthenticatedUser _administrator;
    private readonly int _categoryId;
    private readonly ProductService _products;
    private readonly StockMovementService _stock;

    public StockMovementTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _databasePath = Path.Combine(directory, "test.db");
        DatabaseInitializer.Initialize(_databasePath);
        _connectionFactory = new SqliteConnectionFactory(_databasePath);
        var hashing = new PasswordHashingService();
        _administrator = new UserService(_connectionFactory, hashing).CreateInitialAdministrator(
            "admin-test", "Admin test", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "X1!");
        var categories = new CategoryService(_connectionFactory);
        categories.Create(_administrator, "Épicerie", "Catégorie des tests");
        _categoryId = categories.GetActive().Single().Id;
        _products = new ProductService(_connectionFactory);
        _stock = new StockMovementService(_connectionFactory);
    }

    [Fact(DisplayName = "T01 - produit avec 10 unités conserve un mouvement initial")]
    public void T01_ProductCreationWithInitialStockCreatesSingleMovement()
    {
        _products.Create(_administrator, "T01-RIZ", "Riz", null, _categoryId, 0, 2000, 5);
        var product = Assert.Single(_products.Search("T01-RIZ"));

        _stock.RegisterInitialStock(_administrator, product.Id, 10);

        Assert.Equal(10, _stock.GetAvailableQuantity(product.Id));
        var initialMovement = Assert.Single(_stock.GetHistory(product.Id));
        Assert.Equal("StockInitial", initialMovement.TypeMouvement);
        Assert.Equal(10, initialMovement.Quantite);
        Assert.Throws<InvalidOperationException>(() => _stock.RegisterInitialStock(_administrator, product.Id, 10));
    }

    [Fact(DisplayName = "T02 - une référence déjà utilisée est refusée avec un message clair")]
    public void T02_DuplicateProductReferenceIsRejected()
    {
        _products.Create(_administrator, "T02-HUILE", "Huile", null, _categoryId, 0, 1500, 5);

        var error = Assert.Throws<InvalidOperationException>(() =>
            _products.Create(_administrator, "T02-HUILE", "Huile bis", null, _categoryId, 0, 1600, 5));

        Assert.Contains("référence existe déjà", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "T06 - réapprovisionnement à 5 unités affiche stock faible au seuil 5")]
    public void T06_RestockAtThresholdProducesLowStockStatus()
    {
        _products.Create(_administrator, "T06-SUCRE", "Sucre", null, _categoryId, 0, 900, 5);
        var product = Assert.Single(_products.Search("T06-SUCRE"));

        _stock.Restock(_administrator, product.Id, 5, "Test réception T06");

        Assert.Equal(5, _stock.GetAvailableQuantity(product.Id));
        var result = Assert.Single(_products.Search("T06-SUCRE", _categoryId,
            AvailabilityFilter.StockFaible, RecordStateFilter.Actif));
        Assert.Equal("Stock faible", result.Availability);
        Assert.Equal(5, Assert.Single(_stock.GetHistory(product.Id)).Quantite);
    }

    [Fact(DisplayName = "Sortie de vente atomique, refus du négatif et compensation unique")]
    public void SaleOutputCannotOversellAndCancellationRestoresStockOnce()
    {
        _products.Create(_administrator, "SALE-RIZ", "Riz", null, _categoryId, 0, 2000, 5);
        var product = Assert.Single(_products.Search("SALE-RIZ"));
        _stock.RegisterInitialStock(_administrator, product.Id, 7);

        using (var connection = _connectionFactory.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            var saleId = InsertSale(connection, transaction, "VENTE-1", 6000);
            InsertSaleLine(connection, transaction, saleId, product.Id, 3);
            _stock.RecordValidatedSale(_administrator, connection, transaction, saleId, product.Id, 3);
            transaction.Commit();
        }
        Assert.Equal(4, _stock.GetAvailableQuantity(product.Id));

        using (var connection = _connectionFactory.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            var saleId = InsertSale(connection, transaction, "VENTE-2", 10000);
            Assert.Throws<SqliteException>(() => InsertSaleLine(connection, transaction, saleId, product.Id, 5));
            transaction.Rollback();
        }
        Assert.Equal(4, _stock.GetAvailableQuantity(product.Id));

        using (var connection = _connectionFactory.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            using (var markCancelled = connection.CreateCommand())
            {
                markCancelled.Transaction = transaction;
                markCancelled.CommandText = "UPDATE Vente SET Statut = 'Annulee' WHERE Numero = $number;";
                markCancelled.Parameters.AddWithValue("$number", "VENTE-1");
                markCancelled.ExecuteNonQuery();
            }
            var saleId = FindSaleId(connection, transaction, "VENTE-1");
            using (var cancellation = connection.CreateCommand())
            {
                cancellation.Transaction = transaction;
                cancellation.CommandText = "INSERT INTO AnnulationVente (VenteId, UtilisateurId, Motif, AnnuleeLeUtc) VALUES ($saleId, $userId, $reason, $date);";
                cancellation.Parameters.AddWithValue("$saleId", saleId);
                cancellation.Parameters.AddWithValue("$userId", _administrator.Id);
                cancellation.Parameters.AddWithValue("$reason", "Erreur de caisse");
                cancellation.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
                cancellation.ExecuteNonQuery();
            }
            _stock.RecordSaleCancellation(_administrator, connection, transaction, saleId, product.Id, 3);
            transaction.Commit();
        }
        Assert.Equal(7, _stock.GetAvailableQuantity(product.Id));
        Assert.Equal(3, _stock.GetHistory(product.Id)
            .Where(movement => movement.TypeMouvement == "AnnulationVente")
            .Sum(movement => movement.Quantite));
    }

    [Fact(DisplayName = "La correction écrit les quantités avant, comptée et l'écart dans l'audit")]
    public void InventoryCorrectionStoresSignedDeltaAndAuditDetails()
    {
        _products.Create(_administrator, "INV-RIZ", "Riz inventaire", null, _categoryId, 0, 2000, 5);
        var product = Assert.Single(_products.Search("INV-RIZ"));
        _stock.RegisterInitialStock(_administrator, product.Id, 10);

        var correction = _stock.CorrectInventory(_administrator, product.Id, 8, "Comptage physique");

        Assert.Equal(new InventoryCorrectionResult(10, 8, -2), correction);
        Assert.Equal(8, _stock.GetAvailableQuantity(product.Id));
        var movement = Assert.Single(_stock.GetHistory(product.Id).Where(item => item.TypeMouvement == "CorrectionInventaire"));
        Assert.Equal(-2, movement.Quantite);
        using var connection = _connectionFactory.OpenConnection();
        using var audit = connection.CreateCommand();
        audit.CommandText = "SELECT Details FROM JournalAudit WHERE EntiteId = $productId AND Action = 'CorrectionInventaire';";
        audit.Parameters.AddWithValue("$productId", product.Id);
        var details = Assert.IsType<string>(audit.ExecuteScalar());
        Assert.Contains("Ancienne quantité=10", details);
        Assert.Contains("Quantité comptée=8", details);
        Assert.Contains("Écart=-2", details);
    }

    private int InsertSale(SqliteConnection connection, SqliteTransaction transaction, string number, int amount)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Vente (Numero, UtilisateurId, DateVenteUtc, MontantTotalXaf, MontantPayeXaf,
                               MonnaieRendueXaf, ModePaiement, Statut)
            VALUES ($number, $userId, $date, $amount, $amount, 0, 'Espèces', 'Validee');
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$number", number);
        command.Parameters.AddWithValue("$userId", _administrator.Id);
        command.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$amount", amount);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void InsertSaleLine(SqliteConnection connection, SqliteTransaction transaction,
        int saleId, int productId, int quantity)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO LigneVente (VenteId, ProduitId, Quantite, PrixUnitaireXaf, SousTotalXaf) VALUES ($saleId, $productId, $quantity, 2000, $subtotal);";
        command.Parameters.AddWithValue("$saleId", saleId);
        command.Parameters.AddWithValue("$productId", productId);
        command.Parameters.AddWithValue("$quantity", quantity);
        command.Parameters.AddWithValue("$subtotal", checked(2000 * quantity));
        command.ExecuteNonQuery();
    }

    private static int FindSaleId(SqliteConnection connection, SqliteTransaction transaction, string number)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Vente WHERE Numero = $number;";
        command.Parameters.AddWithValue("$number", number);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var directory = Path.GetDirectoryName(_databasePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
