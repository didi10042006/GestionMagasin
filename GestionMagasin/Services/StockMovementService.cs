using GestionMagasin.Data;
using GestionMagasin.Models;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace GestionMagasin.Services;

public sealed record InventoryCorrectionResult(int PreviousQuantity, int CountedQuantity, int Delta)
{
    public int NewQuantity => CountedQuantity;
}

/// <summary>
/// Stock is the signed sum of MouvementStock. Sales and cancellations must call the
/// transaction-scoped methods before committing the sale transaction.
/// </summary>
public sealed class StockMovementService(SqliteConnectionFactory connectionFactory)
{
    public int GetAvailableQuantity(int productId)
    {
        using var connection = connectionFactory.OpenConnection();
        return GetBalance(connection, null, productId);
    }

    public IReadOnlyList<MouvementStock> GetHistory(int productId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc
            FROM MouvementStock
            WHERE ProduitId = $productId
            ORDER BY Id DESC;
            """;
        command.Parameters.AddWithValue("$productId", productId);
        var history = new List<MouvementStock>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            history.Add(new MouvementStock
            {
                Id = reader.GetInt32(0),
                ProduitId = reader.GetInt32(1),
                UtilisateurId = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Quantite = reader.GetInt32(3),
                TypeMouvement = reader.GetString(4),
                Reference = reader.IsDBNull(5) ? null : reader.GetString(5),
                Motif = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreeLeUtc = DateTime.Parse(reader.GetString(7), null, DateTimeStyles.RoundtripKind)
            });
        }
        return history;
    }

    public void RegisterInitialStock(AuthenticatedUser? actor, int productId, int quantity, string? reason = null)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Le stock initial doit être strictement supérieur à zéro.");

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActiveProduct(connection, transaction, productId);
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT EXISTS (SELECT 1 FROM MouvementStock WHERE ProduitId = $productId);";
            check.Parameters.AddWithValue("$productId", productId);
            if (Convert.ToInt32(check.ExecuteScalar()) == 1)
            {
                throw new InvalidOperationException("Le stock initial a déjà été enregistré pour ce produit.");
            }
        }

        var initialReason = string.IsNullOrWhiteSpace(reason) ? "Stock initial" : reason.Trim();
        InsertMovement(connection, transaction, productId, actor!.Id, quantity, "StockInitial", "INITIAL", initialReason);
        AuditWriter.Record(connection, transaction, actor.Id, "StockInitial", "Produit", productId,
            $"Stock initial enregistré : quantité={quantity}; motif={initialReason}.");
        transaction.Commit();
    }

    public void Restock(AuthenticatedUser? actor, int productId, int quantity, string reason)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "La quantité de réapprovisionnement doit être strictement supérieure à zéro.");
        reason = NormalizeReason(reason, "Le motif du réapprovisionnement est obligatoire.");

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActiveProduct(connection, transaction, productId);
        InsertMovement(connection, transaction, productId, actor!.Id, quantity, "Reapprovisionnement", null, reason);
        AuditWriter.Record(connection, transaction, actor.Id, "Reapprovisionnement", "Produit", productId,
            $"Réapprovisionnement : quantité={quantity}; motif={reason}.");
        transaction.Commit();
    }

    /// <summary>
    /// Records the signed stock output for a validated sale. Call inside the same transaction
    /// that creates the sale and its lines, so an insufficient-stock exception rolls everything back.
    /// </summary>
    public void RecordValidatedSale(AuthenticatedUser? actor, SqliteConnection connection,
        SqliteTransaction transaction, int saleId, int productId, int quantity)
    {
        AuthenticationService.RequireRole(actor, UserRole.Administrateur, UserRole.Vendeur);
        ValidateTransaction(connection, transaction);
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "La quantité vendue doit être strictement supérieure à zéro.");

        var saleUserId = GetSaleUser(connection, transaction, saleId, "Validee");
        if (saleUserId != actor!.Id) throw new UnauthorizedAccessException("La vente ne correspond pas à l'utilisateur connecté.");
        EnsureActiveProduct(connection, transaction, productId);
        EnsureSaleLineQuantity(connection, transaction, saleId, productId, quantity);
        var existingOutput = GetRecordedQuantity(connection, transaction, productId, saleId, "SortieVente");
        if (existingOutput is not null)
        {
            if (existingOutput != -quantity) throw new InvalidOperationException("La sortie déjà créée ne correspond pas à la ligne validée.");
            return;
        }
        EnsureSufficientStock(connection, transaction, productId, quantity);

        InsertMovement(connection, transaction, productId, actor.Id, -quantity, "SortieVente", saleId.ToString(CultureInfo.InvariantCulture), "Vente validée");
    }

    /// <summary>
    /// Records the positive compensation for an annulled sale. Call in the same transaction
    /// that records AnnulationVente and changes Vente.Statut to Annulee.
    /// </summary>
    public void RecordSaleCancellation(AuthenticatedUser? actor, SqliteConnection connection,
        SqliteTransaction transaction, int saleId, int productId, int quantity)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        ValidateTransaction(connection, transaction);
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "La quantité annulée doit être strictement supérieure à zéro.");

        var cancellationUserId = GetCancellationUser(connection, transaction, saleId);
        if (cancellationUserId != actor!.Id) throw new UnauthorizedAccessException("L'annulation ne correspond pas à l'utilisateur connecté.");
        EnsureSaleLineQuantity(connection, transaction, saleId, productId, quantity);
        var existingCompensation = GetRecordedQuantity(connection, transaction, productId, saleId, "AnnulationVente");
        if (existingCompensation is not null)
        {
            if (existingCompensation != quantity) throw new InvalidOperationException("La compensation déjà créée ne correspond pas à la ligne annulée.");
            return;
        }

        InsertMovement(connection, transaction, productId, actor.Id, quantity, "AnnulationVente",
            saleId.ToString(CultureInfo.InvariantCulture), "Compensation de vente annulée");
    }

    public InventoryCorrectionResult CorrectInventory(AuthenticatedUser? actor, int productId, int countedQuantity, string reason)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        if (countedQuantity < 0) throw new ArgumentOutOfRangeException(nameof(countedQuantity), "La quantité comptée ne peut pas être négative.");
        reason = NormalizeReason(reason, "Le motif de correction est obligatoire.");

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActiveProduct(connection, transaction, productId);
        var previousQuantity = GetBalance(connection, transaction, productId);
        if (previousQuantity < 0)
        {
            throw new InvalidOperationException("Le solde historique est négatif. Une correction positive est nécessaire avant toute autre opération.");
        }

        var delta = checked(countedQuantity - previousQuantity);
        if (delta != 0)
        {
            InsertMovement(connection, transaction, productId, actor!.Id, delta, "CorrectionInventaire", null, reason);
        }

        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = """
            INSERT INTO JournalAudit (UtilisateurId, Action, Entite, EntiteId, Details, CreeLeUtc)
            VALUES ($userId, 'CorrectionInventaire', 'Produit', $productId, $details, $createdAt);
            """;
        audit.Parameters.AddWithValue("$userId", actor!.Id);
        audit.Parameters.AddWithValue("$productId", productId);
        audit.Parameters.AddWithValue("$details",
            $"Ancienne quantité={previousQuantity}; Quantité comptée={countedQuantity}; Écart={delta}; Motif={reason}");
        audit.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        audit.ExecuteNonQuery();

        transaction.Commit();
        return new InventoryCorrectionResult(previousQuantity, countedQuantity, delta);
    }

    private static int GetBalance(SqliteConnection connection, SqliteTransaction? transaction, int productId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(SUM(Quantite), 0) FROM MouvementStock WHERE ProduitId = $productId;";
        command.Parameters.AddWithValue("$productId", productId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void EnsureActiveProduct(SqliteConnection connection, SqliteTransaction transaction, int productId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM Produit WHERE Id = $productId AND Actif = 1);";
        command.Parameters.AddWithValue("$productId", productId);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException("Le produit n'existe pas ou est archivé.");
        }
    }

    private static void EnsureSufficientStock(SqliteConnection connection, SqliteTransaction transaction, int productId, int quantity)
    {
        var available = GetBalance(connection, transaction, productId);
        if (available < quantity)
        {
            throw new InvalidOperationException($"Stock insuffisant : disponible {available}, demandé {quantity}. Le stock négatif est interdit.");
        }
    }

    private static int GetSaleUser(SqliteConnection connection, SqliteTransaction transaction, int saleId, string expectedStatus)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT UtilisateurId FROM Vente WHERE Id = $saleId AND Statut = $status;";
        command.Parameters.AddWithValue("$saleId", saleId);
        command.Parameters.AddWithValue("$status", expectedStatus);
        var value = command.ExecuteScalar();
        if (value is null) throw new InvalidOperationException("La vente n'existe pas ou n'a pas le statut attendu.");
        return Convert.ToInt32(value);
    }

    private static int GetCancellationUser(SqliteConnection connection, SqliteTransaction transaction, int saleId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT a.UtilisateurId
            FROM AnnulationVente a
            INNER JOIN Vente v ON v.Id = a.VenteId
            WHERE a.VenteId = $saleId AND v.Statut = 'Annulee';
            """;
        command.Parameters.AddWithValue("$saleId", saleId);
        var value = command.ExecuteScalar();
        if (value is null) throw new InvalidOperationException("L'annulation n'existe pas ou la vente n'est pas annulée.");
        return Convert.ToInt32(value);
    }

    private static void EnsureSaleLineQuantity(SqliteConnection connection, SqliteTransaction transaction,
        int saleId, int productId, int requestedQuantity)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(SUM(Quantite), 0) FROM LigneVente WHERE VenteId = $saleId AND ProduitId = $productId;";
        command.Parameters.AddWithValue("$saleId", saleId);
        command.Parameters.AddWithValue("$productId", productId);
        if (Convert.ToInt32(command.ExecuteScalar()) < requestedQuantity)
        {
            throw new InvalidOperationException("La quantité du mouvement dépasse celle de la ligne de vente.");
        }
    }

    private static int? GetRecordedQuantity(SqliteConnection connection, SqliteTransaction transaction,
        int productId, int saleId, string movementType)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Quantite FROM MouvementStock WHERE ProduitId = $productId AND Reference = $reference AND TypeMouvement = $type;";
        command.Parameters.AddWithValue("$productId", productId);
        command.Parameters.AddWithValue("$reference", saleId.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$type", movementType);
        var value = command.ExecuteScalar();
        return value is null ? null : Convert.ToInt32(value);
    }

    private static void InsertMovement(SqliteConnection connection, SqliteTransaction transaction, int productId,
        int? userId, int signedQuantity, string movementType, string? reference, string? reason)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO MouvementStock (ProduitId, UtilisateurId, Quantite, TypeMouvement, Reference, Motif, CreeLeUtc)
            VALUES ($productId, $userId, $quantity, $type, $reference, $reason, $createdAt);
            """;
        command.Parameters.AddWithValue("$productId", productId);
        command.Parameters.AddWithValue("$userId", userId is null ? DBNull.Value : userId.Value);
        command.Parameters.AddWithValue("$quantity", signedQuantity);
        command.Parameters.AddWithValue("$type", movementType);
        command.Parameters.AddWithValue("$reference", reference is null ? DBNull.Value : reference);
        command.Parameters.AddWithValue("$reason", reason is null ? DBNull.Value : reason);
        command.Parameters.AddWithValue("$createdAt", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static string NormalizeReason(string? reason, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException(errorMessage);
        return reason.Trim();
    }

    private static void ValidateTransaction(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException("La transaction doit appartenir à la connexion fournie.", nameof(transaction));
        }
    }
}
