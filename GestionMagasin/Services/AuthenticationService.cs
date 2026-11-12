using GestionMagasin.Data;

namespace GestionMagasin.Services;

public sealed class AuthenticationService(SqliteConnectionFactory connectionFactory, PasswordHashingService passwordHasher)
{
    public AuthenticatedUser? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return null;
        }

        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, NomUtilisateur, NomComplet, MotDePasseHash, Role
            FROM Utilisateur
            WHERE NomUtilisateur = $username COLLATE NOCASE AND Actif = 1;
            """;
        command.Parameters.AddWithValue("$username", username.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var storedHash = reader.GetString(3);
        if (!passwordHasher.VerifyPassword(password, storedHash))
        {
            return null;
        }

        if (!Enum.TryParse<UserRole>(reader.GetString(4), out var role))
        {
            return null;
        }

        return new AuthenticatedUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), role);
    }

    public static void RequireRole(AuthenticatedUser? user, params UserRole[] allowedRoles)
    {
        if (user is null || !allowedRoles.Contains(user.Role))
        {
            throw new UnauthorizedAccessException("Votre compte ne dispose pas du rôle nécessaire pour cette opération.");
        }
    }
}
