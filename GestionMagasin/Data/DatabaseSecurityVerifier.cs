using GestionMagasin.Services;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace GestionMagasin.Data;

public static class DatabaseSecurityVerifier
{
    public static void Verify(string directory)
    {
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "security-check.db");
        DatabaseInitializer.Initialize(databasePath);

        var factory = new SqliteConnectionFactory(databasePath);
        var passwordHasher = new PasswordHashingService();
        var users = new UserService(factory, passwordHasher);
        if (users.HasAdministrator())
        {
            throw new InvalidOperationException("Une base réelle neuve ne doit pas avoir d'administrateur prédéfini.");
        }

        var adminPasswordForCheck = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "A9!";
        var administrator = users.CreateInitialAdministrator("admin-validation", "Admin Validation", adminPasswordForCheck);
        if (!users.HasAdministrator())
        {
            throw new InvalidOperationException("La création du premier administrateur n'a pas été enregistrée.");
        }

        var authentication = new AuthenticationService(factory, passwordHasher);
        if (authentication.Authenticate("admin-validation", "mot-de-passe-incorrect") is not null)
        {
            throw new InvalidOperationException("Un mot de passe incorrect a été accepté.");
        }
        var signedInAdmin = authentication.Authenticate("admin-validation", adminPasswordForCheck)
            ?? throw new InvalidOperationException("L'authentification du premier administrateur a échoué.");
        AuthenticationService.RequireRole(signedInAdmin, UserRole.Administrateur);

        var vendorPasswordForCheck = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "V8!";
        users.CreateUser(signedInAdmin, "vendeur-validation", "Vendeur Validation", vendorPasswordForCheck, UserRole.Vendeur);
        var signedInVendor = authentication.Authenticate("vendeur-validation", vendorPasswordForCheck)
            ?? throw new InvalidOperationException("L'authentification du vendeur a échoué.");
        AuthenticationService.RequireRole(signedInVendor, UserRole.Vendeur);
        var roleDenied = false;
        try
        {
            AuthenticationService.RequireRole(signedInVendor, UserRole.Administrateur);
        }
        catch (UnauthorizedAccessException)
        {
            roleDenied = true;
        }
        if (!roleDenied)
        {
            throw new InvalidOperationException("Le contrôle des rôles n'a pas refusé l'accès administrateur au vendeur.");
        }

        using var connection = factory.OpenConnection();
        using var fkCheck = connection.CreateCommand();
        fkCheck.CommandText = "PRAGMA foreign_key_check;";
        using var reader = fkCheck.ExecuteReader();
        if (reader.Read())
        {
            throw new InvalidOperationException("La migration des rôles a laissé une clé étrangère invalide.");
        }
    }
}
