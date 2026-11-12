using System.Security.Cryptography;
using GestionMagasin.Data;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class DashboardServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly SqliteConnectionFactory _factory;
    private readonly AuthenticatedUser _admin;
    private readonly AuthenticatedUser _seller;
    private readonly ProductService _products;
    private readonly StockMovementService _stock;
    private readonly SalesService _sales;
    private readonly DashboardService _dashboard;
    private readonly int _categoryId;

    public DashboardServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-DashboardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "dashboard.db");
        DatabaseInitializer.Initialize(path);
        _factory = new SqliteConnectionFactory(path);
        var userService = new UserService(_factory, new PasswordHashingService());
        _admin = userService.CreateInitialAdministrator("dashboard-admin", "Administrateur dashboard",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "A1!");
        userService.CreateUser(_admin, "dashboard-seller", "Vendeur dashboard",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "B1!", UserRole.Vendeur);
        using (var connection = _factory.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id FROM Utilisateur WHERE NomUtilisateur = 'dashboard-seller';";
            _seller = new AuthenticatedUser(Convert.ToInt32(command.ExecuteScalar()),
                "dashboard-seller", "Vendeur dashboard", UserRole.Vendeur);
        }
        var categories = new CategoryService(_factory);
        categories.Create(_admin, "Dashboard", "Produits de vérification des indicateurs");
        _categoryId = categories.GetActive().Single().Id;
        _products = new ProductService(_factory);
        _stock = new StockMovementService(_factory);
        _sales = new SalesService(_factory);
        _dashboard = new DashboardService(_factory);
    }

    [Fact(DisplayName = "Les indicateurs se recalculent après une vente et respectent le rôle")]
    public void DailyMetricsRefreshWithSalesAndScopeVendorTotals()
    {
        var sharedProductId = CreateProduct("DASH-VENTE", 2000, 10, 5);
        CreateProduct("DASH-FAIBLE", 900, 2, 3);
        CreateProduct("DASH-RUPTURE", 500, 0, 2);
        var beforeAdmin = _dashboard.GetDailyMetrics(_admin);
        var beforeSeller = _dashboard.GetDailyMetrics(_seller);

        Assert.Equal(0, beforeAdmin.ValidatedSalesAmountXaf);
        Assert.Equal(0, beforeAdmin.ValidatedSalesCount);
        Assert.Equal(0, beforeSeller.UnitsSold);
        Assert.Equal(1, beforeAdmin.OutOfStockReferences);
        Assert.Equal(1, beforeAdmin.LowStockReferences);

        _sales.Checkout(_admin, [new CartLineRequest(sharedProductId, 1, 2000)],
            SalePaymentMethod.Especes, 2000, Guid.NewGuid());
        _sales.Checkout(_seller, [new CartLineRequest(sharedProductId, 2, 2000)],
            SalePaymentMethod.Especes, 4000, Guid.NewGuid());

        var afterAdmin = _dashboard.GetDailyMetrics(_admin);
        var afterSeller = _dashboard.GetDailyMetrics(_seller);

        Assert.Equal(beforeAdmin.BusinessDate, afterAdmin.BusinessDate);
        Assert.Equal(6000, afterAdmin.ValidatedSalesAmountXaf);
        Assert.Equal(2, afterAdmin.ValidatedSalesCount);
        Assert.Equal(3, afterAdmin.UnitsSold);
        Assert.Equal(4000, afterSeller.ValidatedSalesAmountXaf);
        Assert.Equal(1, afterSeller.ValidatedSalesCount);
        Assert.Equal(2, afterSeller.UnitsSold);
        Assert.Equal(1, afterSeller.OutOfStockReferences);
        Assert.Equal(1, afterSeller.LowStockReferences);
        Assert.Equal(7, _stock.GetAvailableQuantity(sharedProductId));
    }

    private int CreateProduct(string code, int price, int initialStock, int alertThreshold)
    {
        _products.Create(_admin, code, code, null, _categoryId, 0, price, alertThreshold);
        var product = Assert.Single(_products.Search(code));
        if (initialStock > 0) _stock.RegisterInitialStock(_admin, product.Id, initialStock);
        return product.Id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
