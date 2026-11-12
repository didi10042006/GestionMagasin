using System.Globalization;
using GestionMagasin.Data;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed record BackupRestoreResult(string RestoredBackupPath, string SafetyBackupPath, int SchemaVersion);

/// <summary>Consistent SQLite backups, retention and verified atomic restores.</summary>
public sealed class BackupService(SqliteConnectionFactory connectionFactory)
{
    private static readonly int CurrentSchemaVersion = DatabaseMigrations.All.Max(migration => migration.Version);

    public string CreateManualBackup(AuthenticatedUser? actor, string destinationPath)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        var destination = NormalizeDestination(destinationPath);
        EnsureNotActiveDatabase(destination);
        CreateSnapshotAtomically(connectionFactory.DatabasePath, destination);
        VerifyDatabase(destination, allowUpgrade: false);
        using var connection = connectionFactory.OpenConnection();
        AuditWriter.Record(connection, null, actor!.Id, "SauvegardeBase", "BaseDeDonnees", null,
            $"Sauvegarde SQLite créée : {Path.GetFileName(destination)}.");
        return destination;
    }

    /// <summary>Creates at most one automatic snapshot for the business day and retains seven.</summary>
    public string? CreateAutomaticDailyBackup(DateOnly businessDate)
    {
        var backupDirectory = GetBackupDirectory();
        Directory.CreateDirectory(backupDirectory);
        var date = businessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var destination = Path.Combine(backupDirectory, $"auto-{date}.db");
        var lockPath = Path.Combine(backupDirectory, ".automatic-backup.lock");
        FileStream lockStream;
        try { lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
        using (lockStream)
        {
            if (File.Exists(destination)) return destination;
            CreateSnapshotAtomically(connectionFactory.DatabasePath, destination);
            VerifyDatabase(destination, allowUpgrade: false);
            using (var connection = connectionFactory.OpenConnection())
                AuditWriter.Record(connection, null, null, "SauvegardeAutomatique", "BaseDeDonnees", null,
                    $"Sauvegarde automatique du {date} créée.");
            PruneAutomaticBackups(backupDirectory, keep: 7);
            return destination;
        }
    }

    public BackupRestoreResult RestoreBackup(AuthenticatedUser? actor, string sourcePath)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Le fichier de sauvegarde sélectionné est introuvable.", sourcePath);
        var source = Path.GetFullPath(sourcePath);
        EnsureNotActiveDatabase(source);
        var activePath = connectionFactory.DatabasePath;
        var workDirectory = Path.GetDirectoryName(activePath)!;
        Directory.CreateDirectory(workDirectory);
        var token = Guid.NewGuid().ToString("N");
        var stagedPath = Path.Combine(workDirectory, $".restore-{token}.db");
        var safetyPath = Path.Combine(GetBackupDirectory(), $"safety-restore-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{token[..8]}.db");
        var activated = false;

        try
        {
            CreateSnapshotAtomically(source, stagedPath);
            var incomingVersion = VerifyDatabase(stagedPath, allowUpgrade: true);
            DatabaseInitializer.Initialize(stagedPath);
            var migratedVersion = VerifyDatabase(stagedPath, allowUpgrade: false);
            if (migratedVersion != CurrentSchemaVersion)
                throw new InvalidDataException("Le schéma de la sauvegarde n'a pas pu être migré à la version courante.");

            CreateSnapshotAtomically(activePath, safetyPath);
            VerifyDatabase(safetyPath, allowUpgrade: false);
            CheckpointAndCloseConnections(activePath);
            File.Replace(stagedPath, activePath, null, ignoreMetadataErrors: true);
            activated = true;
            SqliteConnection.ClearAllPools();
            var activeVersion = VerifyDatabase(activePath, allowUpgrade: false);
            using (var connection = connectionFactory.OpenConnection())
                AuditWriter.Record(connection, null, null, "RestaurationBase", "BaseDeDonnees", null,
                    $"Sauvegarde restaurée : {Path.GetFileName(source)}; version entrante={incomingVersion}.");
            return new BackupRestoreResult(source, safetyPath, activeVersion);
        }
        catch (Exception restoreError)
        {
            if (activated)
            {
                try { RestoreSafetyCopy(safetyPath, activePath); }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException(
                        $"La restauration a échoué et le retour automatique n'a pas abouti. La copie de sécurité est conservée ici : {safetyPath}",
                        new AggregateException(restoreError, rollbackError));
                }
            }
            throw;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            DeleteSqliteFiles(stagedPath);
        }
    }

    public string BackupDirectory => GetBackupDirectory();

    private static string NormalizeDestination(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Le chemin de sauvegarde est obligatoire.");
        var path = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private void EnsureNotActiveDatabase(string path)
    {
        if (string.Equals(path, connectionFactory.DatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Le fichier choisi doit être distinct de la base active.");
    }

    private string GetBackupDirectory() => Path.Combine(Path.GetDirectoryName(connectionFactory.DatabasePath)!, "Backups");

    private static void CreateSnapshotAtomically(string sourcePath, string destinationPath)
    {
        var fullDestination = Path.GetFullPath(destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        var temporaryPath = fullDestination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(sourcePath),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            source.Open();
            using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = temporaryPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
            }
            if (File.Exists(fullDestination)) File.Replace(temporaryPath, fullDestination, null, ignoreMetadataErrors: true);
            else File.Move(temporaryPath, fullDestination);
        }
        finally { DeleteSqliteFiles(temporaryPath); }
    }

    private static int VerifyDatabase(string path, bool allowUpgrade)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = Convert.ToString(integrity.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"La sauvegarde SQLite est intègre=false : {result}");
        }
        using (var foreignKeys = connection.CreateCommand())
        {
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            using var reader = foreignKeys.ExecuteReader();
            if (reader.Read()) throw new InvalidDataException("La sauvegarde contient des références étrangères invalides.");
        }
        int version;
        try
        {
            using var schema = connection.CreateCommand();
            schema.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM VersionSchema;";
            version = Convert.ToInt32(schema.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("Le fichier n'est pas une sauvegarde Gestion-Magasin versionnée.", exception);
        }
        if (version < 1 || version > CurrentSchemaVersion)
            throw new InvalidDataException($"Version de schéma non prise en charge : {version}; version courante : {CurrentSchemaVersion}.");
        ValidateCoreSchema(connection, version);
        if (version == CurrentSchemaVersion) ValidateCurrentSchemaObjects(connection);
        if (!allowUpgrade && version != CurrentSchemaVersion)
            throw new InvalidDataException($"La base utilise le schéma {version}; le schéma attendu est {CurrentSchemaVersion}.");
        return version;
    }

    private static void ValidateCoreSchema(SqliteConnection connection, int version)
    {
        var queries = new List<string>
        {
            "SELECT Id, NomUtilisateur, NomComplet, MotDePasseHash, Role, Actif FROM Utilisateur LIMIT 0;",
            "SELECT Id, Code, Nom, PrixVenteXaf, Actif FROM Produit LIMIT 0;",
            "SELECT Id, Numero, DateVenteUtc, MontantTotalXaf, Statut FROM Vente LIMIT 0;",
            "SELECT Id, ProduitId, Quantite, TypeMouvement FROM MouvementStock LIMIT 0;",
            "SELECT Id, VenteId, ProduitId, Quantite, PrixUnitaireXaf, SousTotalXaf FROM LigneVente LIMIT 0;",
            "SELECT Id, VenteId, UtilisateurId, Motif, AnnuleeLeUtc FROM AnnulationVente LIMIT 0;",
            "SELECT Id, UtilisateurId, Action, Entite, Details, CreeLeUtc FROM JournalAudit LIMIT 0;",
            "SELECT Id, Cle, Valeur FROM Parametre LIMIT 0;",
            "SELECT Id, Version, Description FROM VersionSchema LIMIT 0;"
        };
        if (version >= 5) queries.Add("SELECT DescriptionPaiement FROM Vente LIMIT 0;");
        if (version >= 6) queries.Add("SELECT JetonValidation, EmpreinteValidation FROM Vente LIMIT 0;");
        try
        {
            foreach (var query in queries)
            {
                using var command = connection.CreateCommand();
                command.CommandText = query;
                using var reader = command.ExecuteReader();
            }
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("La sauvegarde ne contient pas toutes les tables ou colonnes attendues.", exception);
        }
    }

    private static void ValidateCurrentSchemaObjects(SqliteConnection connection)
    {
        const string requiredTriggers = "'TR_LigneVente_ProduitActif_Insert','TR_LigneVente_ProduitActif_Update'," +
            "'TR_MouvementStock_NonNegatif_Insert','TR_MouvementStock_NonNegatif_Update','TR_MouvementStock_ProduitActif'," +
            "'TR_LigneVente_CreeSortieStock','TR_Vente_ValidationCreeSorties','TR_AnnulationVente_CreeCompensations'";
        const string requiredIndexes = "'UX_Vente_JetonValidation','UX_MouvementStock_StockInitial','UX_MouvementStock_SortieVente'," +
            "'UX_MouvementStock_AnnulationVente','UX_LigneVente_VenteId_ProduitId'";
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT
                (SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name IN ({requiredTriggers})),
                (SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name IN ({requiredIndexes}));
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(0) != 8 || reader.GetInt32(1) != 5)
            throw new InvalidDataException("La sauvegarde n'inclut pas les déclencheurs ou index de sécurité requis.");
    }

    private static void CheckpointAndCloseConnections(string databasePath)
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = databasePath, Pooling = false, Mode = SqliteOpenMode.ReadWrite }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        DeleteSqliteSidecars(databasePath);
    }

    private static void RestoreSafetyCopy(string safetyPath, string activePath)
    {
        CheckpointAndCloseConnections(activePath);
        var rollbackPath = activePath + ".rollback-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(safetyPath, rollbackPath);
            File.Replace(rollbackPath, activePath, null, ignoreMetadataErrors: true);
            SqliteConnection.ClearAllPools();
            _ = VerifyDatabase(activePath, allowUpgrade: false);
        }
        finally { DeleteSqliteFiles(rollbackPath); }
    }

    private static void DeleteSqliteSidecars(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
    }

    private static void DeleteSqliteFiles(string path)
    {
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }

    private static void PruneAutomaticBackups(string directory, int keep)
    {
        var backups = Directory.GetFiles(directory, "auto-????????.db")
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
            .ToArray();
        foreach (var obsolete in backups.Skip(keep)) File.Delete(obsolete);
    }
}
