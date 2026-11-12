using System.Security.Cryptography;
using GestionMagasin.Data;
using GestionMagasin.Services;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GestionMagasin-BackupTests", Guid.NewGuid().ToString("N"));
    private readonly string _activePath;
    private readonly SqliteConnectionFactory _activeFactory;
    private readonly UserService _activeUsers;
    private readonly AuthenticatedUser _activeAdmin;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _activePath = Path.Combine(_directory, "active.db");
        DatabaseInitializer.Initialize(_activePath);
        _activeFactory = new SqliteConnectionFactory(_activePath);
        _activeUsers = new UserService(_activeFactory, new PasswordHashingService());
        _activeAdmin = _activeUsers.CreateInitialAdministrator("active-admin", "Admin actif", NewPassword());
        new AdministrationService(_activeFactory).SaveStoreSettings(_activeAdmin, "Base active", "FCFA XAF", TimeZoneInfo.Local.Id);
    }

    [Fact(DisplayName = "T15 - une restauration valide est atomique et conserve une sauvegarde de sécurité")]
    public void RestoreValidBackupAndRejectCorruptBackupWithoutChangingActiveDatabase()
    {
        var sourcePath = Path.Combine(_directory, "source.db");
        DatabaseInitializer.Initialize(sourcePath);
        var sourceFactory = new SqliteConnectionFactory(sourcePath);
        var sourceUsers = new UserService(sourceFactory, new PasswordHashingService());
        var sourceAdmin = sourceUsers.CreateInitialAdministrator("source-admin", "Admin de sauvegarde", NewPassword());
        new AdministrationService(sourceFactory).SaveStoreSettings(sourceAdmin, "Base restaurée", "FCFA XAF", TimeZoneInfo.Local.Id);
        var backupPath = Path.Combine(_directory, "backup-to-restore.db");
        new BackupService(sourceFactory).CreateManualBackup(sourceAdmin, backupPath);

        var backupService = new BackupService(_activeFactory);
        var auditFailurePath = Path.Combine(_directory, "audit-failure.db");
        File.Copy(backupPath, auditFailurePath);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
               { DataSource = auditFailurePath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER FailRestoreAudit BEFORE INSERT ON JournalAudit BEGIN SELECT RAISE(ABORT, 'validation rollback'); END;";
            command.ExecuteNonQuery();
        }
        Assert.ThrowsAny<Exception>(() => backupService.RestoreBackup(_activeAdmin, auditFailurePath));
        Assert.Equal("Base active", new AdministrationService(_activeFactory).GetStoreSettings().StoreName);
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(_activePath)!, "Backups"), "safety-restore-*.db"));

        var result = backupService.RestoreBackup(_activeAdmin, backupPath);
        Assert.Equal(6, result.SchemaVersion);
        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.Equal("Base restaurée", new AdministrationService(_activeFactory).GetStoreSettings().StoreName);
        Assert.Equal("Base active", new AdministrationService(new SqliteConnectionFactory(result.SafetyBackupPath)).GetStoreSettings().StoreName);

        var corruptPath = Path.Combine(_directory, "corrompue.db");
        File.WriteAllText(corruptPath, "ceci n'est pas une base SQLite");
        Assert.ThrowsAny<Exception>(() => backupService.RestoreBackup(_activeAdmin, corruptPath));
        Assert.Equal("Base restaurée", new AdministrationService(_activeFactory).GetStoreSettings().StoreName);

        var futureVersionPath = Path.Combine(_directory, "version-future.db");
        File.Copy(backupPath, futureVersionPath);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
               { DataSource = futureVersionPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO VersionSchema (Version, Description, AppliqueeLeUtc) VALUES (7, 'future', '2026-01-01T00:00:00Z');";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => backupService.RestoreBackup(_activeAdmin, futureVersionPath));
        Assert.Equal("Base restaurée", new AdministrationService(_activeFactory).GetStoreSettings().StoreName);
    }

    [Fact(DisplayName = "La sauvegarde automatique est quotidienne et conserve les sept dernières")]
    public void DailyAutomaticBackupIsIdempotentAndRetainsSevenCopies()
    {
        var backupService = new BackupService(_activeFactory);
        var today = DateOnly.FromDateTime(DateTime.Now);
        for (var daysAgo = 8; daysAgo >= 0; daysAgo--)
        {
            var savedPath = backupService.CreateAutomaticDailyBackup(today.AddDays(-daysAgo));
            Assert.NotNull(savedPath);
        }

        Assert.Equal(7, Directory.GetFiles(backupService.BackupDirectory, "auto-????????.db").Length);
        var todayBackup = backupService.CreateAutomaticDailyBackup(today);
        Assert.Equal(todayBackup, backupService.CreateAutomaticDailyBackup(today));
    }

    private static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "A1!";

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
