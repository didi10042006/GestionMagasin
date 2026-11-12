using GestionMagasin.Data;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed record ManagedUser(int Id, string Username, string FullName, UserRole Role, bool Active, DateTime CreatedAtUtc);

public sealed class UserService(SqliteConnectionFactory connectionFactory, PasswordHashingService passwordHasher)
{
    public bool HasAdministrator()
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM Utilisateur WHERE Role = $role AND Actif = 1);";
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
        check.CommandText = "SELECT EXISTS (SELECT 1 FROM Utilisateur WHERE Role = $role AND Actif = 1);";
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
        AuditWriter.Record(connection, transaction, null, "CreationPremierAdministrateur", "Utilisateur", id,
            $"Compte administrateur initial créé : {username}.");
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
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        ValidateRole(role);
        username = ValidateRequired(username, "Le nom d'utilisateur");
        fullName = ValidateRequired(fullName, "Le nom complet");
        ValidatePassword(password);
        var passwordHash = passwordHasher.HashPassword(password);

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        using var identity = connection.CreateCommand();
        identity.Transaction = transaction;
        identity.CommandText = "SELECT last_insert_rowid();";
        var id = Convert.ToInt32(identity.ExecuteScalar());
        AuditWriter.Record(connection, transaction, actor.Id, "CreationUtilisateur", "Utilisateur", id,
            $"Compte créé : {username}; rôle={role}.");
        transaction.Commit();
    }

    public IReadOnlyList<ManagedUser> GetUsers(AuthenticatedUser? actor)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, NomUtilisateur, NomComplet, Role, Actif, CreeLeUtc FROM Utilisateur ORDER BY Actif DESC, NomComplet COLLATE NOCASE;";
        var users = new List<ManagedUser>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!Enum.TryParse<UserRole>(reader.GetString(3), out var role)) continue;
            users.Add(new ManagedUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), role,
                reader.GetInt32(4) == 1, DateTime.Parse(reader.GetString(5), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        }
        return users;
    }

    public void UpdateUser(AuthenticatedUser? actor, int userId, string username, string fullName,
        UserRole role, string? newPassword = null)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        ValidateRole(role);
        username = ValidateRequired(username, "Le nom d'utilisateur");
        fullName = ValidateRequired(fullName, "Le nom complet");
        var passwordHash = string.IsNullOrWhiteSpace(newPassword) ? null : passwordHasher.HashPassword(ValidatePasswordAndReturn(newPassword));
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var current = ReadAccountState(connection, transaction, userId);
        if (actor!.Id == userId && role != actor.Role)
            throw new InvalidOperationException("Le rôle de la session courante ne peut pas être modifié. Utilisez une autre session administrateur.");
        if (current.Role == UserRole.Administrateur && current.Active && role != UserRole.Administrateur &&
            !HasOtherActiveAdministrator(connection, transaction, userId))
            throw new InvalidOperationException("Le dernier administrateur actif ne peut pas perdre son rôle.");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE Utilisateur SET NomUtilisateur = $username, NomComplet = $fullName, Role = $role,
                MotDePasseHash = COALESCE($passwordHash, MotDePasseHash) WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$username", username);
        command.Parameters.AddWithValue("$fullName", fullName);
        command.Parameters.AddWithValue("$role", role.ToString());
        command.Parameters.AddWithValue("$passwordHash", passwordHash is null ? DBNull.Value : passwordHash);
        command.Parameters.AddWithValue("$id", userId);
        if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException("Ce compte n'existe plus.");
        AuditWriter.Record(connection, transaction, actor!.Id, "ModificationUtilisateur", "Utilisateur", userId,
            $"Compte modifié : {username}; rôle={role}; mot de passe réinitialisé={(passwordHash is null ? "non" : "oui")}.");
        transaction.Commit();
    }

    public void SetActive(AuthenticatedUser? actor, int userId, bool active)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var current = ReadAccountState(connection, transaction, userId);
        if (actor!.Id == userId && !active)
            throw new InvalidOperationException("Le compte actuellement connecté ne peut pas être désactivé.");
        if (current.Active == active) return;
        if (!active && current.Role == UserRole.Administrateur && !HasOtherActiveAdministrator(connection, transaction, userId))
            throw new InvalidOperationException("Le dernier administrateur actif ne peut pas être désactivé.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Utilisateur SET Actif = $active WHERE Id = $id;";
        command.Parameters.AddWithValue("$active", active ? 1 : 0);
        command.Parameters.AddWithValue("$id", userId);
        command.ExecuteNonQuery();
        AuditWriter.Record(connection, transaction, actor!.Id, active ? "ActivationUtilisateur" : "DesactivationUtilisateur",
            "Utilisateur", userId, $"Compte {current.Username} {(active ? "activé" : "désactivé")}.");
        transaction.Commit();
    }

    private static (string Username, UserRole Role, bool Active) ReadAccountState(
        Microsoft.Data.Sqlite.SqliteConnection connection, Microsoft.Data.Sqlite.SqliteTransaction transaction, int userId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT NomUtilisateur, Role, Actif FROM Utilisateur WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", userId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Ce compte n'existe pas.");
        if (!Enum.TryParse<UserRole>(reader.GetString(1), out var role)) throw new InvalidOperationException("Le rôle du compte est invalide.");
        return (reader.GetString(0), role, reader.GetInt32(2) == 1);
    }

    private static bool HasOtherActiveAdministrator(Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction, int excludedUserId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM Utilisateur WHERE Id <> $id AND Role = $role AND Actif = 1);";
        command.Parameters.AddWithValue("$id", excludedUserId);
        command.Parameters.AddWithValue("$role", nameof(UserRole.Administrateur));
        return Convert.ToInt32(command.ExecuteScalar()) == 1;
    }

    private static void ValidateRole(UserRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role), "Le rôle doit être Administrateur ou Vendeur.");
    }

    private static string ValidatePasswordAndReturn(string password)
    {
        ValidatePassword(password);
        return password;
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
