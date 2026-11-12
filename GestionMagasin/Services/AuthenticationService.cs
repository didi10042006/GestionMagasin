using GestionMagasin.Data;

namespace GestionMagasin.Services;

public sealed class AuthenticationService(SqliteConnectionFactory connectionFactory, PasswordHashingService passwordHasher)
{
    public AuthenticatedUser? Authenticate(string username, string password)
    {
        using var connection = connectionFactory.OpenConnection();
        var normalizedUsername = username?.Trim() ?? string.Empty;
        var valid = false;
        AuthenticatedUser? user = null;
        if (!string.IsNullOrWhiteSpace(normalizedUsername) && !string.IsNullOrEmpty(password))
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, NomUtilisateur, NomComplet, MotDePasseHash, Role
                FROM Utilisateur
                WHERE NomUtilisateur = $username COLLATE NOCASE AND Actif = 1;
                """;
            command.Parameters.AddWithValue("$username", normalizedUsername);
            using var reader = command.ExecuteReader();
            if (reader.Read() && passwordHasher.VerifyPassword(password, reader.GetString(3)) &&
                Enum.TryParse<UserRole>(reader.GetString(4), out var role))
            {
                user = new AuthenticatedUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), role);
                valid = true;
            }
        }

        AuditWriter.Record(connection, null, valid ? user!.Id : null,
            valid ? "ConnexionReussie" : "EchecConnexion", "Authentification", valid ? user!.Id : null,
            valid ? "Connexion réussie." : "Échec d'identification.");
        return user;
    }

    public static void RequireRole(AuthenticatedUser? user, params UserRole[] allowedRoles)
    {
        if (user is null || !allowedRoles.Contains(user.Role))
        {
            throw new UnauthorizedAccessException("Votre compte ne dispose pas du rôle nécessaire pour cette opération.");
        }
    }

    public static void RequireActiveRole(SqliteConnectionFactory connectionFactory, AuthenticatedUser? user,
        params UserRole[] allowedRoles)
    {
        RequireRole(user, allowedRoles);
        if (user!.Id <= 0) throw new UnauthorizedAccessException("Votre session utilisateur est invalide.");
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Role FROM Utilisateur WHERE Id = $id AND Actif = 1;";
        command.Parameters.AddWithValue("$id", user.Id);
        var roleValue = command.ExecuteScalar();
        if (roleValue is null || !Enum.TryParse<UserRole>(Convert.ToString(roleValue), out var currentRole) || currentRole != user.Role)
            throw new UnauthorizedAccessException("Votre compte est désactivé ou votre rôle a changé. Reconnectez-vous.");
        if (!allowedRoles.Contains(currentRole))
            throw new UnauthorizedAccessException("Votre compte ne dispose pas du rôle nécessaire pour cette opération.");
    }
}
