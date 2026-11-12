using System.Security.Cryptography;
using GestionMagasin.Data;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class AdministrationTests : IDisposable
{
    private const string AdminPassword = "SecretAdmin!2026";
    private const string SellerPassword = "SecretSeller!2026";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-AdministrationTests", Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;
    private readonly UserService _users;
    private readonly AdministrationService _administration;
    private readonly AuditLogService _audit;
    private readonly AuthenticatedUser _admin;
    private readonly AuthenticatedUser _seller;

    public AdministrationTests()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "administration.db");
        DatabaseInitializer.Initialize(databasePath);
        _factory = new SqliteConnectionFactory(databasePath);
        _users = new UserService(_factory, new PasswordHashingService());
        _admin = _users.CreateInitialAdministrator("admin-test", "Administrateur", AdminPassword);
        _users.CreateUser(_admin, "vendeur-test", "Vendeur", SellerPassword, UserRole.Vendeur);
        var managedSeller = _users.GetUsers(_admin).Single(user => user.Username == "vendeur-test");
        _seller = new AuthenticatedUser(managedSeller.Id, managedSeller.Username, managedSeller.FullName, managedSeller.Role);
        _administration = new AdministrationService(_factory);
        _audit = new AuditLogService(_factory);
    }

    [Fact(DisplayName = "T14 - les opérations d'administration sont imposées par les services métier")]
    public void PrivilegedOperationsRequireAdminAndProtectLastActiveAdmin()
    {
        Assert.Throws<UnauthorizedAccessException>(() => _users.CreateUser(_seller, "intrus", "Intrus", SellerPassword, UserRole.Administrateur));
        Assert.Throws<UnauthorizedAccessException>(() => _users.UpdateUser(_seller, _seller.Id, "vendeur-test", "Vendeur", UserRole.Administrateur));
        Assert.Throws<UnauthorizedAccessException>(() => _users.SetActive(_seller, _admin.Id, false));
        Assert.Throws<UnauthorizedAccessException>(() => _administration.SaveStoreSettings(_seller, "Magasin", "FCFA XAF", TimeZoneInfo.Local.Id));
        Assert.Throws<UnauthorizedAccessException>(() => _audit.GetRecent(_seller));
        Assert.Throws<UnauthorizedAccessException>(() => _administration.CreateBackup(_seller, Path.Combine(_directory, "refus.db")));
        Assert.Throws<UnauthorizedAccessException>(() => _administration.SaveStoreSettings(_seller with { Role = UserRole.Administrateur },
            "Faux administrateur", "FCFA XAF", TimeZoneInfo.Local.Id));

        Assert.Throws<InvalidOperationException>(() => _users.SetActive(_admin, _admin.Id, false));
        Assert.Throws<InvalidOperationException>(() => _users.UpdateUser(_admin, _admin.Id, "admin-test", "Administrateur", UserRole.Vendeur));

        _users.CreateUser(_admin, "admin-second", "Second administrateur", "SecondAdmin!2026", UserRole.Administrateur);
        var secondAdmin = _users.GetUsers(_admin).Single(user => user.Username == "admin-second");
        var updated = _users.GetUsers(_admin).Single(user => user.Username == "vendeur-test");
        _users.UpdateUser(_admin, updated.Id, "vendeur-maj", "Vendeur modifié", UserRole.Administrateur);
        _users.SetActive(_admin, secondAdmin.Id, false);
        Assert.False(_users.GetUsers(_admin).Single(user => user.Username == "admin-second").Active);
        Assert.Equal(UserRole.Administrateur, _users.GetUsers(_admin).Single(user => user.Username == "vendeur-maj").Role);
        Assert.Throws<UnauthorizedAccessException>(() => _administration.SaveStoreSettings(
            new AuthenticatedUser(secondAdmin.Id, secondAdmin.Username, secondAdmin.FullName, secondAdmin.Role),
            "Session révoquée", "FCFA XAF", TimeZoneInfo.Local.Id));
    }

    [Fact(DisplayName = "Paramètres, audit des connexions et sauvegardes sont persistés sans mot de passe")]
    public void StoreSettingsBackupAndAuditAreRecordedWithoutSecrets()
    {
        var settings = _administration.GetStoreSettings();
        Assert.Equal("Gestion-Magasin", settings.StoreName);
        _administration.SaveStoreSettings(_admin, "Marché Étoile", "FCFA XAF", TimeZoneInfo.Local.Id);
        var saved = _administration.GetStoreSettings();
        Assert.Equal("Marché Étoile", saved.StoreName);
        Assert.Equal("FCFA XAF", saved.Currency);
        Assert.Equal(TimeZoneInfo.Local.Id, saved.TimeZoneId);
        Assert.Throws<ArgumentException>(() => _administration.SaveStoreSettings(_admin, "Magasin", "EUR", TimeZoneInfo.Local.Id));

        var auth = new AuthenticationService(_factory, new PasswordHashingService());
        Assert.NotNull(auth.Authenticate("admin-test", AdminPassword));
        Assert.Null(auth.Authenticate("admin-test", "WrongSecret!2026"));
        var categories = new CategoryService(_factory);
        categories.Create(_admin, "Audit catégorie", null);
        var categoryId = categories.GetActive().Single().Id;
        var products = new ProductService(_factory);
        products.Create(_admin, "AUDIT-01", "Produit audit", null, categoryId, 100, 250, 3);
        var product = Assert.Single(products.Search("AUDIT-01"));
        products.Update(_admin, product.Id, "Produit modifié", null, categoryId, 110, 275, 4);
        products.Archive(_admin, product.Id);
        var backupPath = Path.Combine(_directory, "sauvegarde.db");
        Assert.Equal(backupPath, _administration.CreateBackup(_admin, backupPath));
        Assert.True(File.Exists(backupPath));
        using (var backup = new SqliteConnectionFactory(backupPath).OpenConnection())
        using (var command = backup.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM Utilisateur;";
            Assert.Equal(2L, Convert.ToInt64(command.ExecuteScalar()));
        }

        var entries = _audit.GetRecent(_admin);
        Assert.Contains(entries, entry => entry.Action == "ConnexionReussie");
        Assert.Contains(entries, entry => entry.Action == "EchecConnexion");
        Assert.Contains(entries, entry => entry.Action == "ModificationParametresMagasin");
        Assert.Contains(entries, entry => entry.Action == "CreationProduit");
        Assert.Contains(entries, entry => entry.Action == "ModificationProduit");
        Assert.Contains(entries, entry => entry.Action == "ArchivageProduit");
        Assert.Contains(entries, entry => entry.Action == "SauvegardeBase");
        var details = string.Join("\n", entries.Select(entry => entry.Details));
        Assert.DoesNotContain(AdminPassword, details, StringComparison.Ordinal);
        Assert.DoesNotContain("WrongSecret!2026", details, StringComparison.Ordinal);
        Assert.DoesNotContain(SellerPassword, details, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
