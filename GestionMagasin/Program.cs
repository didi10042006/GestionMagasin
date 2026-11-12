using GestionMagasin.Data;
using GestionMagasin.Forms;
using GestionMagasin.Services;
using Microsoft.Data.Sqlite;

namespace GestionMagasin;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var initializeIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, "--initialize-database", StringComparison.OrdinalIgnoreCase));
        var verifyIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, "--verify-database-isolation", StringComparison.OrdinalIgnoreCase));
        var verifySecurityIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, "--verify-security", StringComparison.OrdinalIgnoreCase));
        var verifyCatalogIndex = Array.FindIndex(args, argument =>
            string.Equals(argument, "--verify-catalog", StringComparison.OrdinalIgnoreCase));
        if (verifyIndex >= 0)
        {
            var directory = verifyIndex + 1 < args.Length
                ? args[verifyIndex + 1]
                : Path.Combine(Path.GetTempPath(), "GestionMagasin-IsolationCheck");
            DatabaseIsolationVerifier.Verify(directory);
            Console.WriteLine("Isolation validée : base réelle vide; base de démonstration avec 8 produits et ses propres catégories.");
            return;
        }
        if (verifySecurityIndex >= 0)
        {
            var directory = verifySecurityIndex + 1 < args.Length
                ? args[verifySecurityIndex + 1]
                : Path.Combine(Path.GetTempPath(), "GestionMagasin-SecurityCheck");
            DatabaseSecurityVerifier.Verify(directory);
            Console.WriteLine("Sécurité validée : aucun compte par défaut, création initiale, hachage PBKDF2, connexion et autorisation par rôle.");
            return;
        }
        if (verifyCatalogIndex >= 0)
        {
            var directory = verifyCatalogIndex + 1 < args.Length
                ? args[verifyCatalogIndex + 1]
                : Path.Combine(Path.GetTempPath(), "GestionMagasin-CatalogCheck");
            CatalogVerifier.Verify(directory);
            Console.WriteLine("Catalogue validé : recherche/filtrage, prix strict, archivage consultable, référence réservée, écriture administrateur et blocage de vente.");
            return;
        }

        var databasePath = initializeIndex >= 0 && initializeIndex + 1 < args.Length
            ? args[initializeIndex + 1]
            : string.Empty;
        if (initializeIndex >= 0)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                databasePath = DatabaseOptions.GetRealDatabasePath();
            }
            DatabaseInitializer.Initialize(databasePath);
            var factory = new SqliteConnectionFactory(databasePath);
            using var connection = factory.OpenConnection();
            using var verify = connection.CreateCommand();
            verify.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name <> $internal;";
            verify.Parameters.AddWithValue("$type", "table");
            verify.Parameters.AddWithValue("$internal", "sqlite_sequence");
            var tableCount = Convert.ToInt32(verify.ExecuteScalar());
            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_keys;";
            var foreignKeysEnabled = Convert.ToInt32(foreignKeys.ExecuteScalar()) == 1;
            Console.WriteLine($"Base créée : {Path.GetFullPath(databasePath)}");
            Console.WriteLine($"Tables détectées : {tableCount}; clés étrangères activées : {foreignKeysEnabled}.");
            return;
        }

        // La base réelle existe dès la première ouverture, même si l'utilisateur choisit ensuite la démo.
        DatabaseInitializer.Initialize(DatabaseOptions.GetRealDatabasePath());
        using var modeForm = new DatabaseModeForm();
        if (modeForm.ShowDialog() != DialogResult.OK || modeForm.SelectedMode is not { } selectedMode)
        {
            return;
        }

        var selectedPath = selectedMode == DatabaseMode.Reelle
            ? DatabaseOptions.GetRealDatabasePath()
            : DatabaseOptions.GetDemoDatabasePath();
        DatabaseInitializer.Initialize(selectedPath);
        AuthenticatedUser? currentUser = null;
        var connectionFactory = new SqliteConnectionFactory(selectedPath);
        if (selectedMode == DatabaseMode.Demonstration)
        {
            DemoDataSeeder.SeedIfNeeded(connectionFactory);
        }
        else
        {
            var passwordHasher = new PasswordHashingService();
            var userService = new UserService(connectionFactory, passwordHasher);
            if (!userService.HasAdministrator())
            {
                using var setupForm = new FirstAdminSetupForm(userService);
                if (setupForm.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                currentUser = setupForm.CreatedUser;
            }
            else
            {
                var authService = new AuthenticationService(connectionFactory, passwordHasher);
                var customLogoPath = new AdministrationService(connectionFactory).GetStoreSettings().LogoPath;
                using var loginForm = new LoginForm(authService, customLogoPath);
                if (loginForm.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
                currentUser = loginForm.AuthenticatedUser;
            }
        }
        try
        {
            var settings = new AdministrationService(connectionFactory).GetStoreSettings();
            var businessDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, settings.TimeZone).DateTime);
            _ = new BackupService(connectionFactory).CreateAutomaticDailyBackup(businessDate);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"La sauvegarde automatique du jour n'a pas pu être créée : {ex.Message}",
                "Sauvegarde", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Application.Run(new Form1(new DatabaseSession(selectedMode, selectedPath), currentUser));
    }
}
