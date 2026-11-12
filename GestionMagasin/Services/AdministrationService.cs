using GestionMagasin.Data;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed record StoreSettings(string StoreName, string Currency, string TimeZoneId)
{
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}

public sealed class AdministrationService(SqliteConnectionFactory connectionFactory)
{
    public StoreSettings GetStoreSettings()
    {
        using var connection = connectionFactory.OpenConnection();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Cle, Valeur FROM Parametre WHERE Cle IN ('StoreName', 'Currency', 'TimeZoneId');";
        using (var reader = command.ExecuteReader())
            while (reader.Read()) values[reader.GetString(0)] = reader.GetString(1);
        var zone = values.GetValueOrDefault("TimeZoneId", TimeZoneInfo.Local.Id);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (TimeZoneNotFoundException) { zone = TimeZoneInfo.Local.Id; }
        catch (InvalidTimeZoneException) { zone = TimeZoneInfo.Local.Id; }
        return new StoreSettings(values.GetValueOrDefault("StoreName", "Gestion-Magasin"),
            values.GetValueOrDefault("Currency", "FCFA XAF"), zone);
    }

    public void SaveStoreSettings(AuthenticatedUser? actor, string storeName, string currency, string timeZoneId)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        storeName = storeName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(storeName) || storeName.Length > 120)
            throw new ArgumentException("Le nom commercial est obligatoire et limité à 120 caractères.");
        if (!string.Equals(currency, "FCFA XAF", StringComparison.Ordinal))
            throw new ArgumentException("La devise de cette application est fixée à FCFA XAF.");
        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Le fuseau horaire est obligatoire.");
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        SaveParameter(connection, transaction, "StoreName", storeName);
        SaveParameter(connection, transaction, "Currency", currency);
        SaveParameter(connection, transaction, "TimeZoneId", timeZoneId);
        AuditWriter.Record(connection, transaction, actor!.Id, "ModificationParametresMagasin", "Parametre", null,
            $"Paramètres du magasin modifiés : nom={storeName}; devise={currency}; fuseau={timeZoneId}.");
        transaction.Commit();
    }

    public string CreateBackup(AuthenticatedUser? actor, string destinationPath)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Le chemin de sauvegarde est obligatoire.");
        var fullPath = Path.GetFullPath(destinationPath);
        if (string.Equals(fullPath, connectionFactory.DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Le fichier de sauvegarde doit être distinct de la base active.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using (var source = connectionFactory.OpenConnection())
        using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = fullPath, Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString()))
        {
            destination.Open();
            source.BackupDatabase(destination);
        }

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        AuditWriter.Record(connection, transaction, actor!.Id, "SauvegardeBase", "BaseDeDonnees", null,
            $"Sauvegarde SQLite créée : {Path.GetFileName(fullPath)}.");
        transaction.Commit();
        return fullPath;
    }

    private static void SaveParameter(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Parametre (Cle, Valeur, ModifieLeUtc) VALUES ($key, $value, $modifiedAt)
            ON CONFLICT(Cle) DO UPDATE SET Valeur = excluded.Valeur, ModifieLeUtc = excluded.ModifieLeUtc;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$modifiedAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
}
