using Microsoft.Data.Sqlite;

namespace GestionMagasin.Data;

public static class DatabaseInitializer
{
    public static void Initialize(string databasePath)
    {
        var factory = new SqliteConnectionFactory(databasePath);
        using var connection = factory.OpenConnection();
        using (var createHistory = connection.CreateCommand())
        {
            createHistory.CommandText = """
                CREATE TABLE IF NOT EXISTS VersionSchema (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Version INTEGER NOT NULL UNIQUE,
                    Description TEXT NOT NULL,
                    AppliqueeLeUtc TEXT NOT NULL
                );
                """;
            createHistory.ExecuteNonQuery();
        }

        foreach (var migration in DatabaseMigrations.All.OrderBy(migration => migration.Version))
        {
            if (migration.RequiresForeignKeysDisabled)
            {
                using var disableForeignKeys = connection.CreateCommand();
                disableForeignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
                disableForeignKeys.ExecuteNonQuery();
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                using var check = connection.CreateCommand();
                check.Transaction = transaction;
                check.CommandText = "SELECT COUNT(*) FROM VersionSchema WHERE Version = $version;";
                check.Parameters.AddWithValue("$version", migration.Version);
                if (Convert.ToInt64(check.ExecuteScalar()) == 0)
                {
                    using var apply = connection.CreateCommand();
                    apply.Transaction = transaction;
                    apply.CommandText = migration.Sql;
                    apply.ExecuteNonQuery();

                    using var record = connection.CreateCommand();
                    record.Transaction = transaction;
                    record.CommandText = """
                        INSERT INTO VersionSchema (Version, Description, AppliqueeLeUtc)
                        VALUES ($version, $description, $appliedAt);
                        """;
                    record.Parameters.AddWithValue("$version", migration.Version);
                    record.Parameters.AddWithValue("$description", migration.Description);
                    record.Parameters.AddWithValue("$appliedAt", DateTime.UtcNow.ToString("O"));
                    record.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
            finally
            {
                if (migration.RequiresForeignKeysDisabled)
                {
                    using var enableForeignKeys = connection.CreateCommand();
                    enableForeignKeys.CommandText = "PRAGMA foreign_keys = ON;";
                    enableForeignKeys.ExecuteNonQuery();
                }
            }
        }
    }
}
