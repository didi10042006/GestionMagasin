using GestionMagasin.Data;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class DemoSalesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-DemoSalesTests", Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;

    public DemoSalesTests()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "demo-sales.db");
        DatabaseInitializer.Initialize(databasePath);
        _factory = new SqliteConnectionFactory(databasePath);
        DemoDataSeeder.SeedIfNeeded(_factory);
    }

    [Fact(DisplayName = "Une validation en mode démonstration enregistre la vente et met à jour le stock")]
    public void DemoCashierCanCommitSaleAndPersistStockMovement()
    {
        var cashier = DemoDataSeeder.EnsureDemoCashier(_factory);
        var sameCashier = DemoDataSeeder.EnsureDemoCashier(_factory);
        Assert.Equal(cashier.Id, sameCashier.Id);
        Assert.Equal(UserRole.Vendeur, cashier.Role);

        var product = Assert.Single(new ProductService(_factory).Search("PR001"));
        Assert.Equal(25, product.Stock);
        var receipt = new SalesService(_factory).Checkout(cashier,
            [new CartLineRequest(product.Id, 1, product.SalePrice)],
            SalePaymentMethod.Especes, product.SalePrice, Guid.NewGuid());

        Assert.Equal(product.SalePrice, receipt.TotalXaf);
        Assert.Equal(0, receipt.ChangeXaf);
        Assert.Equal(24, new StockMovementService(_factory).GetAvailableQuantity(product.Id));
        using var connection = _factory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Vente WHERE Id = $saleId AND Statut = 'Validee';";
        command.Parameters.AddWithValue("$saleId", receipt.Id);
        Assert.Equal(1, Convert.ToInt32(command.ExecuteScalar()));
        Assert.Single(new StockMovementService(_factory).GetHistory(product.Id)
            .Where(movement => movement.TypeMouvement == "SortieVente" && movement.Reference == receipt.Id.ToString()));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
