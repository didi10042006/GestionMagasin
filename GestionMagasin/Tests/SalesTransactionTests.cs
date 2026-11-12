using System.Security.Cryptography;
using GestionMagasin.Data;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class SalesTransactionTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteConnectionFactory _factory;
    private readonly AuthenticatedUser _admin;
    private readonly int _categoryId;
    private readonly ProductService _products;
    private readonly SalesService _sales;
    private readonly StockMovementService _stock;

    public SalesTransactionTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-SalesTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "sales-test.db");
        DatabaseInitializer.Initialize(databasePath);
        _factory = new SqliteConnectionFactory(databasePath);
        var users = new UserService(_factory, new PasswordHashingService());
        _admin = users.CreateInitialAdministrator("sales-test-admin", "Admin ventes",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "A1!");
        var categories = new CategoryService(_factory);
        categories.Create(_admin, "Ventes test", "Tests automatisés du module de vente");
        _categoryId = categories.GetActive().Single().Id;
        _products = new ProductService(_factory);
        _sales = new SalesService(_factory);
        _stock = new StockMovementService(_factory);
    }

    [Fact(DisplayName = "T03 - un prix modifié actualise le total et exige une nouvelle validation")]
    public void T03_PriceChangeRequiresSecondValidationWithoutPartialSale()
    {
        var productId = CreateProduct("T03-RIZ", 2000, 10);
        var token = Guid.NewGuid();
        var staleCart = new[] { new CartLineRequest(productId, 2, 2000) };
        SetSalePrice(productId, 2500);

        var changed = Assert.Throws<SalePriceChangedException>(() =>
            _sales.Checkout(_admin, staleCart, SalePaymentMethod.Especes, 5000, token));

        Assert.Equal(5000, changed.RefreshedTotalXaf);
        Assert.Equal(2500, Assert.Single(changed.RefreshedLines).UnitPriceXaf);
        Assert.Equal(0, CountSales());
        Assert.Equal(10, _stock.GetAvailableQuantity(productId));

        var confirmed = _sales.Checkout(_admin, [new CartLineRequest(productId, 2, 2500)],
            SalePaymentMethod.Especes, 5000, Guid.NewGuid());
        Assert.Equal(5000, confirmed.TotalXaf);
        Assert.Equal(8, _stock.GetAvailableQuantity(productId));
    }

    [Fact(DisplayName = "T04 - une ligne insuffisante annule tout le panier et nomme le produit")]
    public void T04_InsufficientLineRollsBackTheWholeSaleAndNamesProduct()
    {
        var firstProduct = CreateProduct("T04-RIZ", 2000, 10);
        var insufficientProduct = CreateProduct("T04-HUILE", 1500, 1);
        var movementCountBefore = CountMovements();

        var exception = Assert.Throws<InsufficientStockException>(() => _sales.Checkout(_admin,
        [
            new CartLineRequest(firstProduct, 2, 2000),
            new CartLineRequest(insufficientProduct, 2, 1500)
        ], SalePaymentMethod.Especes, 7000, Guid.NewGuid()));

        Assert.Contains("T04-HUILE", exception.Message);
        Assert.Equal(0, CountSales());
        Assert.Equal(movementCountBefore, CountMovements());
        Assert.Equal(10, _stock.GetAvailableQuantity(firstProduct));
        Assert.Equal(1, _stock.GetAvailableQuantity(insufficientProduct));
    }

    [Fact(DisplayName = "T05 - un jeton répété retourne la même vente sans second débit")]
    public void T05_ValidationTokenMakesARepeatedRequestIdempotent()
    {
        var productId = CreateProduct("T05-SUCRE", 900, 12);
        var token = Guid.NewGuid();
        var cart = new[] { new CartLineRequest(productId, 3, 900) };

        var first = _sales.Checkout(_admin, cart, SalePaymentMethod.Especes, 3000, token);
        var retry = _sales.Checkout(_admin, cart, SalePaymentMethod.Especes, 3000, token);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(first.Number, retry.Number);
        Assert.Equal(1, CountSales());
        Assert.Equal(9, _stock.GetAvailableQuantity(productId));
    }

    [Fact(DisplayName = "T07 - le jeton ne peut pas valider un panier différent")]
    public void T07_ValidationTokenCannotBeReusedForDifferentPayload()
    {
        var productId = CreateProduct("T07-LAIT", 2500, 8);
        var token = Guid.NewGuid();
        _sales.Checkout(_admin, [new CartLineRequest(productId, 1, 2500)], SalePaymentMethod.Especes, 2500, token);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _sales.Checkout(_admin, [new CartLineRequest(productId, 2, 2500)], SalePaymentMethod.Especes, 5000, token));

        Assert.Contains("autre contenu", exception.Message);
        Assert.Equal(1, CountSales());
        Assert.Equal(7, _stock.GetAvailableQuantity(productId));
    }

    [Fact(DisplayName = "T08 - les sorties négatives sont créées avec les lignes de vente")]
    public void T08_SaleLinesAndNegativeStockMovementsAreCommittedTogether()
    {
        var productId = CreateProduct("T08-EAU", 500, 10);

        var receipt = _sales.Checkout(_admin, [new CartLineRequest(productId, 4, 500)],
            SalePaymentMethod.Especes, 2500, Guid.NewGuid());

        Assert.Equal(2000, receipt.TotalXaf);
        Assert.Equal(500, receipt.ChangeXaf);
        Assert.Equal(6, _stock.GetAvailableQuantity(productId));
        var movement = Assert.Single(_stock.GetHistory(productId)
            .Where(item => item.TypeMouvement == "SortieVente"));
        Assert.Equal(-4, movement.Quantite);
        Assert.Equal(receipt.Id.ToString(), movement.Reference);
    }

    [Fact(DisplayName = "T10 - deux validations concurrentes avec le même jeton ne créent qu'une vente")]
    public async Task T10_ConcurrentRetriesWithTheSameTokenCreateOnlyOneSale()
    {
        var productId = CreateProduct("T10-CAFE", 2000, 10);
        var token = Guid.NewGuid();
        var cart = new[] { new CartLineRequest(productId, 2, 2000) };

        var receipts = await Task.WhenAll(
            Task.Run(() => _sales.Checkout(_admin, cart, SalePaymentMethod.Especes, 4000, token)),
            Task.Run(() => _sales.Checkout(_admin, cart, SalePaymentMethod.Especes, 4000, token)));

        Assert.Equal(receipts[0].Id, receipts[1].Id);
        Assert.Equal(1, CountSales());
        Assert.Equal(8, _stock.GetAvailableQuantity(productId));
    }

    [Fact(DisplayName = "T12 - un paiement espèces insuffisant n'écrit ni vente ni mouvement")]
    public void T12_InsufficientCashDoesNotWriteSaleOrStockMovement()
    {
        var productId = CreateProduct("T12-SPAGHETTI", 800, 10);
        var movementCountBefore = CountMovements();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _sales.Checkout(_admin, [new CartLineRequest(productId, 2, 800)], SalePaymentMethod.Especes, 1000, Guid.NewGuid()));

        Assert.Contains("Montant insuffisant", exception.Message);
        Assert.Equal(0, CountSales());
        Assert.Equal(movementCountBefore, CountMovements());
        Assert.Equal(10, _stock.GetAvailableQuantity(productId));
    }

    [Fact(DisplayName = "T16 - les quantités répétées du même produit sont fusionnées avant l'écriture")]
    public void T16_DuplicateProductLinesBecomeOneSaleLineAndOneStockMovement()
    {
        var productId = CreateProduct("T16-HUILE", 1500, 12);

        var receipt = _sales.Checkout(_admin,
        [
            new CartLineRequest(productId, 2, 1500),
            new CartLineRequest(productId, 3, 1500)
        ], SalePaymentMethod.Especes, 8000, Guid.NewGuid());

        Assert.Equal(7500, receipt.TotalXaf);
        Assert.Equal(500, receipt.ChangeXaf);
        Assert.Equal(7, _stock.GetAvailableQuantity(productId));
        Assert.Single(receipt.Lines);
        Assert.Single(_stock.GetHistory(productId).Where(item => item.TypeMouvement == "SortieVente"));
        Assert.Equal(1, CountSaleLines(receipt.Id));
    }

    [Fact(DisplayName = "T09 - annulation admin à motif obligatoire, vente conservée, compensation unique")]
    public void T09_CancellationIsAdminOnlyKeepsOriginalAndCannotBeRepeated()
    {
        var productId = CreateProduct("T09-RIZ", 2000, 10);
        var receipt = _sales.Checkout(_admin, [new CartLineRequest(productId, 3, 2000)],
            SalePaymentMethod.Especes, 6000, Guid.NewGuid());
        var seller = new AuthenticatedUser(0, "vendeur-test", "Vendeur test", UserRole.Vendeur);

        Assert.Throws<UnauthorizedAccessException>(() => _sales.Cancel(seller, receipt.Id, "Erreur caisse"));
        Assert.Throws<ArgumentException>(() => _sales.Cancel(_admin, receipt.Id, "   "));
        Assert.Equal(7, _stock.GetAvailableQuantity(productId));

        _sales.Cancel(_admin, receipt.Id, "Erreur de caisse confirmée");

        var original = Assert.IsType<SaleDetail>(_sales.GetById(receipt.Id));
        Assert.Equal("Annulee", original.Sale.Status);
        Assert.Single(original.Lines);
        Assert.Equal("Erreur de caisse confirmée", original.Sale.CancellationReason);
        Assert.Equal(10, _stock.GetAvailableQuantity(productId));
        Assert.Equal(-3, _stock.GetHistory(productId).Single(item => item.TypeMouvement == "SortieVente").Quantite);
        Assert.Equal(3, _stock.GetHistory(productId).Single(item => item.TypeMouvement == "AnnulationVente").Quantite);
        Assert.Equal(1, ExecuteScalar("SELECT COUNT(*) FROM Vente;"));
        Assert.Equal(1, ExecuteScalar("SELECT COUNT(*) FROM LigneVente WHERE VenteId = " + receipt.Id + ";"));
        Assert.Equal(1, ExecuteScalar("SELECT COUNT(*) FROM AnnulationVente WHERE VenteId = " + receipt.Id + ";"));
        Assert.Equal(1, ExecuteScalar("SELECT COUNT(*) FROM JournalAudit WHERE Entite = 'Vente' AND EntiteId = " + receipt.Id + " AND Action = 'AnnulationVente';"));

        var secondAttempt = Assert.Throws<InvalidOperationException>(() => _sales.Cancel(_admin, receipt.Id, "Seconde tentative"));
        Assert.Contains("déjà été annulée", secondAttempt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, _stock.GetAvailableQuantity(productId));
        Assert.Equal(1, ExecuteScalar("SELECT COUNT(*) FROM AnnulationVente WHERE VenteId = " + receipt.Id + ";"));
        Assert.Equal(1, _stock.GetHistory(productId).Count(item => item.TypeMouvement == "AnnulationVente"));
    }

    [Fact(DisplayName = "L'historique filtre par numéro, vendeur, statut et période")]
    public void HistoryFiltersByNumberSellerStatusAndPeriod()
    {
        var productId = CreateProduct("HIST-RIZ", 2000, 10);
        var hasher = new PasswordHashingService();
        new UserService(_factory, hasher).CreateUser(_admin, "sales-vendor", "Vendeur historique",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "B1!", UserRole.Vendeur);
        var sellerId = ExecuteScalar("SELECT Id FROM Utilisateur WHERE NomUtilisateur = 'sales-vendor';");
        var seller = new AuthenticatedUser(sellerId, "sales-vendor", "Vendeur historique", UserRole.Vendeur);

        var adminSale = _sales.Checkout(_admin, [new CartLineRequest(productId, 1, 2000)], SalePaymentMethod.Especes, 2000, Guid.NewGuid());
        var sellerSale = _sales.Checkout(seller, [new CartLineRequest(productId, 1, 2000)], SalePaymentMethod.Especes, 2000, Guid.NewGuid());
        _sales.Cancel(_admin, adminSale.Id, "Test filtre statut");
        var from = DateTime.UtcNow.AddDays(-1);
        var through = DateTime.UtcNow.AddDays(1);

        var cancelledAdminSale = Assert.Single(_sales.GetHistory(from, through, adminSale.Number, _admin.Id, "Annulee"));
        var validatedSellerSale = Assert.Single(_sales.GetHistory(from, through, sellerSale.Number, sellerId, "Validee"));

        Assert.Equal(adminSale.Id, cancelledAdminSale.Id);
        Assert.Equal(sellerSale.Id, validatedSellerSale.Id);
        Assert.Empty(_sales.GetHistory(from, through, sellerSale.Number, _admin.Id, "Validee"));
    }

    private int CreateProduct(string code, int price, int quantity)
    {
        _products.Create(_admin, code, code, null, _categoryId, 0, price, 3);
        var product = Assert.Single(_products.Search(code));
        _stock.RegisterInitialStock(_admin, product.Id, quantity);
        return product.Id;
    }

    private void SetSalePrice(int productId, int price)
    {
        using var connection = _factory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Produit SET PrixVenteXaf = $price WHERE Id = $productId;";
        command.Parameters.AddWithValue("$price", price);
        command.Parameters.AddWithValue("$productId", productId);
        command.ExecuteNonQuery();
    }

    private int CountSales() => ExecuteScalar("SELECT COUNT(*) FROM Vente;");
    private int CountMovements() => ExecuteScalar("SELECT COUNT(*) FROM MouvementStock;");
    private int CountSaleLines(int saleId)
    {
        using var connection = _factory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM LigneVente WHERE VenteId = $saleId;";
        command.Parameters.AddWithValue("$saleId", saleId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private int ExecuteScalar(string sql)
    {
        using var connection = _factory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
