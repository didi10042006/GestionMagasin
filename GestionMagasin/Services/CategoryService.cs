using GestionMagasin.Data;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed class CategoryService(SqliteConnectionFactory connectionFactory)
{
    public IReadOnlyList<CategoryCatalogItem> GetAll(string? query = null, RecordStateFilter state = RecordStateFilter.Tous)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Nom, Description, Actif
            FROM Categorie
            WHERE ($query = '' OR Nom LIKE $pattern ESCAPE '\')
              AND ($state = 0 OR ($state = 1 AND Actif = 1) OR ($state = 2 AND Actif = 0))
            ORDER BY Actif DESC, Nom COLLATE NOCASE;
            """;
        var normalizedQuery = query?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$query", normalizedQuery);
        command.Parameters.AddWithValue("$pattern", $"%{normalizedQuery.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%");
        command.Parameters.AddWithValue("$state", (int)state);
        var items = new List<CategoryCatalogItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new CategoryCatalogItem(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt32(3) == 1));
        }
        return items;
    }

    public IReadOnlyList<CategoryCatalogItem> GetActive()
    {
        return GetAll(state: RecordStateFilter.Actif);
    }

    public void Create(AuthenticatedUser? actor, string name, string? description)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        ValidateName(name);
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Categorie (Nom, Description, Actif) VALUES ($name, $description, 1);";
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$description", string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim());
        try
        {
            command.ExecuteNonQuery();
            using var identity = connection.CreateCommand();
            identity.Transaction = transaction;
            identity.CommandText = "SELECT last_insert_rowid();";
            var categoryId = Convert.ToInt32(identity.ExecuteScalar());
            AuditWriter.Record(connection, transaction, actor!.Id, "CreationCategorie", "Categorie", categoryId,
                $"Catégorie créée : {name.Trim()}.");
            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Une catégorie porte déjà ce nom, même si elle est archivée.", ex);
        }
    }

    public void Update(AuthenticatedUser? actor, int id, string name, string? description)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        ValidateName(name);
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Categorie SET Nom = $name, Description = $description WHERE Id = $id AND Actif = 1;";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$description", string.IsNullOrWhiteSpace(description) ? DBNull.Value : description.Trim());
        try
        {
            if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException("Cette catégorie est introuvable ou archivée.");
            AuditWriter.Record(connection, transaction, actor!.Id, "ModificationCategorie", "Categorie", id,
                $"Catégorie modifiée : {name.Trim()}.");
            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Une autre catégorie porte déjà ce nom.", ex);
        }
    }

    public void Archive(AuthenticatedUser? actor, int id)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE Categorie SET Actif = 0 WHERE Id = $id AND Actif = 1;";
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException("Cette catégorie est introuvable ou déjà archivée.");
        AuditWriter.Record(connection, transaction, actor!.Id, "ArchivageCategorie", "Categorie", id, "Catégorie archivée.");
        transaction.Commit();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Le nom de la catégorie est obligatoire.");
    }
}
