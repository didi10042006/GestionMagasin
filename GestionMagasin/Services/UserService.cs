using GestionMagasin.Data;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed class UserService(SqliteConnectionFactory connectionFactory, PasswordHashingService passwordHasher)
{
    public bool HasAdministrator()
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM Utilisateur WHERE Role = $role);";
        command.Parameters.AddWithValue("$role", nameof(UserRole.Administrateur));
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    public AuthenticatedUser CreateInitialAdministrator(string username, string fullName, string password)
    {
        username = ValidateRequired(username, "Le nom d'utilisateur");
        fullName = ValidateRequired(fullName, "Le nom complet");
        ValidatePassword(password);
        var passwordHash = passwordHasher.HashPassword(password);

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT EXISTS (SELECT 1 FROM Utilisateur WHERE Role = $role);";
        check.Parameters.AddWithValue("$role", nameof(UserRole.Administrateur));
        if (Convert.ToInt32(check.ExecuteScalar()) == 1)
        {
            throw new InvalidOperationException("Un administrateur existe déjà. La création initiale n'est plus disponible.");
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO Utilisateur (NomUtilisateur, NomComplet, MotDePasseHash, Role, Actif, CreeLeUtc)
            VALUES ($username, $fullName, $passwordHash, $role, 1, $createdAt);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$username", username);
        insert.Parameters.AddWithValue("$fullName", fullName);
        insert.Parameters.AddWithValue("$passwordHash", passwordHash);
        insert.Parameters.AddWithValue("$role", nameof(UserRole.Administrateur));
        insert.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        var id = Convert.ToInt32(insert.ExecuteScalar());
        transaction.Commit();
        return new AuthenticatedUser(id, username, fullName, UserRole.Administrateur);
    }

    public void CreateUser(
        AuthenticatedUser actor,
        string username,
        string fullName,
        string password,
        UserRole role)
    {
        AuthenticationService.RequireRole(actor, UserRole.Administrateur);
        username = ValidateRequired(username, "Le nom d'utilisateur");
        fullName = ValidateRequired(fullName, "Le nom complet");
        ValidatePassword(password);
        var passwordHash = passwordHasher.HashPassword(password);

        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Utilisateur (NomUtilisateur, NomComplet, MotDePasseHash, Role, Actif, CreeLeUtc)
            VALUES ($username, $fullName, $passwordHash, $role, 1, $createdAt);
            """;
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$fullName", fullName);
        command.Parameters.AddWithValue("$passwordHash", passwordHash);
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static string ValidateRequired(string value, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException($"{label} est obligatoire.");
        }
        return normalized;
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            throw new ArgumentException("Le mot de passe doit contenir au moins 12 caractères.");
        }
    }
}
