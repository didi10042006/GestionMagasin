using System.Security.Cryptography;
using GestionMagasin.Data;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class ReportingServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-ReportingTests", Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;
    private readonly AuthenticatedUser _admin;
    private readonly AuthenticatedUser _seller;
    private readonly ReportingService _reports;
    private readonly int _categoryId;

    public ReportingServiceTests()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "reporting.db");
        DatabaseInitializer.Initialize(databasePath);
        _factory = new SqliteConnectionFactory(databasePath);
        var users = new UserService(_factory, new PasswordHashingService());
        _admin = users.CreateInitialAdministrator("report-admin", "=HYPERLINK(\"https://invalid\",\"André, caisse\")",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "A1!");
        users.CreateUser(_admin, "report-seller", "Vendeur Démo",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "B1!", UserRole.Vendeur);
        using (var connection = _factory.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id FROM Utilisateur WHERE NomUtilisateur = $username;";
            command.Parameters.AddWithValue("$username", "report-seller");
            _seller = new AuthenticatedUser(Convert.ToInt32(command.ExecuteScalar()), "report-seller", "Vendeur Démo", UserRole.Vendeur);
        }
        var categories = new CategoryService(_factory);
        categories.Create(_admin, "Épicerie, fraîche", "Catégorie accentuée");
        _categoryId = categories.GetActive().Single().Id;
        _reports = new ReportingService(_factory);
    }

    [Fact(DisplayName = "T18 - exports CSV protègent les formules et préservent accents et séparateurs")]
    public void CsvExportsEscapeFormulaCellsAndKeepUtf8Text()
    {
        var products = new ProductService(_factory);
        products.Create(_admin, "CSV-001", "+Commande, crème \"fine\"", "Description accentuée", _categoryId, 300, 900, 5);
        var product = Assert.Single(products.Search("CSV-001"));
        var stock = new StockMovementService(_factory);
        stock.RegisterInitialStock(_admin, product.Id, 12);
        var sales = new SalesService(_factory);
        sales.Checkout(_admin, [new CartLineRequest(product.Id, 2, 900)], SalePaymentMethod.Especes, 1800, Guid.NewGuid());

        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = _reports.GetProductPeriodReport(_admin, day, day, TimeZoneInfo.Utc);
        var row = Assert.Single(report);
        Assert.Equal(2, row.UnitsSold);
        Assert.Equal(1800, row.RevenueXaf);

        var salesPath = Path.Combine(_directory, "ventes.csv");
        var stockPath = Path.Combine(_directory, "stocks.csv");
        Assert.Equal(1, _reports.ExportSalesCsv(_admin, day, day, salesPath, TimeZoneInfo.Utc));
        Assert.Equal(1, _reports.ExportStockCsv(_admin, stockPath));
        var salesBytes = File.ReadAllBytes(salesPath);
        Assert.True(salesBytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
        var salesCsv = File.ReadAllText(salesPath);
        var stockCsv = File.ReadAllText(stockPath);
        Assert.Contains("'=HYPERLINK", salesCsv);
        Assert.Contains("'+Commande", stockCsv);
        Assert.Contains("+Commande, crème", stockCsv);
        Assert.Contains("Épicerie, fraîche", stockCsv);
        Assert.Contains("\"\"fine\"\"", stockCsv);
        Assert.Throws<UnauthorizedAccessException>(() => _reports.ExportStockCsv(_seller, Path.Combine(_directory, "refuse.csv")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
