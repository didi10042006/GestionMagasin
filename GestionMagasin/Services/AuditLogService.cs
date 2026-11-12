using System.Globalization;
using GestionMagasin.Data;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed record AuditLogEntry(long Id, DateTime CreatedAtUtc, string? UserName, string Action,
    string Entity, int? EntityId, string? Details);

internal static class AuditWriter
{
    public static void Record(SqliteConnection connection, SqliteTransaction? transaction, int? userId,
        string action, string entity, int? entityId, string? details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO JournalAudit (UtilisateurId, Action, Entite, EntiteId, Details, CreeLeUtc)
            VALUES ($userId, $action, $entity, $entityId, $details, $createdAt);
            """;
        command.Parameters.AddWithValue("$userId", userId is null ? DBNull.Value : userId.Value);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$entity", entity);
        command.Parameters.AddWithValue("$entityId", entityId is null ? DBNull.Value : entityId.Value);
        command.Parameters.AddWithValue("$details", details is null ? DBNull.Value : details);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
}

public sealed class AuditLogService(SqliteConnectionFactory connectionFactory)
{
    public IReadOnlyList<AuditLogEntry> GetRecent(AuthenticatedUser? actor, int limit = 500)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        if (limit is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.Id, a.CreeLeUtc, u.NomUtilisateur, a.Action, a.Entite, a.EntiteId, a.Details
            FROM JournalAudit a LEFT JOIN Utilisateur u ON u.Id = a.UtilisateurId
            ORDER BY a.Id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var entries = new List<AuditLogEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            entries.Add(new AuditLogEntry(reader.GetInt64(0), DateTime.Parse(reader.GetString(1), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        return entries;
    }
}
