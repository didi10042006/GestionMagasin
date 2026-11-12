using System.Diagnostics;
using System.Security.Cryptography;
using GestionMagasin.Data;
using GestionMagasin.Services;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class InstalledPersistenceTests
{
    [Fact(DisplayName = "T13 - l'application publiée redémarre sans perdre les données commerciales")]
    public void PublishedExecutableCanReopenDatabaseWithoutLosingData()
    {
        var executablePath = Environment.GetEnvironmentVariable("GESTIONMAGASIN_INSTALLED_EXE");
        // Ce test d'intégration est activé par scripts/Test-InstalledPersistence.ps1 après installation.
        // Les tests unitaires standards restent exécutables sans installer le produit.
        if (string.IsNullOrWhiteSpace(executablePath)) return;
        Assert.True(File.Exists(executablePath), $"Exécutable installé introuvable : {executablePath}");

        var directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-T13", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "commercial.db");
        try
        {
            RunInstalledInitializer(executablePath, databasePath);

            var factory = new SqliteConnectionFactory(databasePath);
            DatabaseInitializer.Initialize(databasePath);
            var users = new UserService(factory, new PasswordHashingService());
            var admin = users.CreateInitialAdministrator("t13-admin", "Administrateur T13", NewPassword());
            var administration = new AdministrationService(factory);
            administration.SaveStoreSettings(admin, "Magasin T13", "FCFA XAF", TimeZoneInfo.Local.Id);

            var categories = new CategoryService(factory);
            categories.Create(admin, "T13 Épicerie", "Données de validation persistante");
            var categoryId = Assert.Single(categories.GetActive()).Id;
            var products = new ProductService(factory);
            products.Create(admin, "T13-PERSIST", "Produit persistant", null, categoryId, 400, 850, 2);
            var productId = Assert.Single(products.Search("T13-PERSIST")).Product.Id;
            new StockMovementService(factory).RegisterInitialStock(admin, productId, 17);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            RunInstalledInitializer(executablePath, databasePath);

            var reopened = new SqliteConnectionFactory(databasePath);
            Assert.True(new UserService(reopened, new PasswordHashingService()).HasAdministrator());
            Assert.Equal("Magasin T13", new AdministrationService(reopened).GetStoreSettings().StoreName);
            var reopenedProduct = Assert.Single(new ProductService(reopened).Search("T13-PERSIST"));
            Assert.Equal(17, reopenedProduct.Stock);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void RunInstalledInitializer(string executablePath, string databasePath)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--initialize-database");
        startInfo.ArgumentList.Add(databasePath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Impossible de démarrer l'application installée.");
        Assert.True(process.WaitForExit(60000), "L'application installée n'a pas terminé l'initialisation sous 60 secondes.");
        Assert.Equal(0, process.ExitCode);
    }

    private static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "A1!";
}
