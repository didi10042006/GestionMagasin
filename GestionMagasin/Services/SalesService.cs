using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GestionMagasin.Data;
using GestionMagasin.Models;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public enum SalePaymentMethod
{
    Especes,
    MobileMoney,
    Carte
}

public sealed record CartLineRequest(int ProductId, int Quantity, int ExpectedUnitPriceXaf);

public sealed class InsufficientStockException(string productCode, string productName, int available, int requested)
    : InvalidOperationException($"Stock insuffisant pour {productName} ({productCode}) : {available} disponible(s), {requested} demandé(s).")
{
    public string ProductCode { get; } = productCode;
    public string ProductName { get; } = productName;
    public int Available { get; } = available;
    public int Requested { get; } = requested;
}

public sealed class SalePriceChangedException(IReadOnlyList<SaleLineSnapshot> refreshedLines, int refreshedTotalXaf)
    : InvalidOperationException("Le prix d'un ou plusieurs produits a changé. Le panier a été actualisé; vérifiez le nouveau total et validez à nouveau.")
{
    public IReadOnlyList<SaleLineSnapshot> RefreshedLines { get; } = refreshedLines;
    public int RefreshedTotalXaf { get; } = refreshedTotalXaf;
}

public sealed record SaleLineSnapshot(
    int ProductId,
    string ProductCode,
    string ProductName,
    int Quantity,
    int UnitPriceXaf,
    int SubtotalXaf);

public sealed record SaleReceipt(
    int Id,
    string Number,
    DateTime SoldAtUtc,
    string CashierName,
    int TotalXaf,
    int AmountPaidXaf,
    int ChangeXaf,
    SalePaymentMethod PaymentMethod,
    string? PaymentDescription,
    IReadOnlyList<SaleLineSnapshot> Lines);

public sealed record SaleHistoryEntry(
    int Id,
    string Number,
    DateTime SoldAtUtc,
    string CashierName,
    int TotalXaf,
    int AmountPaidXaf,
    int ChangeXaf,
    string PaymentMethod,
    string? PaymentDescription,
    string Status,
    string? CancellationReason,
    string? CancelledBy,
    DateTime? CancelledAtUtc);

public sealed record SaleDetail(SaleHistoryEntry Sale, IReadOnlyList<SaleLineSnapshot> Lines);

/// <summary>
/// Owns checkout and cancellation transactions. Sale lines snapshot their unit price;
/// SQLite stock triggers write the signed stock outputs in the same transaction.
/// </summary>
public sealed class SalesService(SqliteConnectionFactory connectionFactory)
{
    private const int MaximumQuantity = 1_000_000;

    public SaleReceipt Checkout(AuthenticatedUser? actor, IEnumerable<CartLineRequest> cart,
        SalePaymentMethod paymentMethod, int amountPaidXaf, Guid validationToken, string? paymentDescription = null)
    {
        AuthenticationService.RequireRole(actor, UserRole.Administrateur, UserRole.Vendeur);
        ArgumentNullException.ThrowIfNull(cart);
        if (!Enum.IsDefined(paymentMethod)) throw new ArgumentOutOfRangeException(nameof(paymentMethod));
        if (validationToken == Guid.Empty) throw new ArgumentException("Le jeton de validation est obligatoire.", nameof(validationToken));
        if (amountPaidXaf < 0) throw new ArgumentOutOfRangeException(nameof(amountPaidXaf), "Le montant payé ne peut pas être négatif.");
        paymentDescription = NormalizePaymentDescription(paymentMethod, paymentDescription);

        var linesByProduct = new Dictionary<int, CartLineRequest>();
        foreach (var line in cart)
        {
            if (line.ProductId <= 0) throw new ArgumentException("Le panier contient un produit invalide.", nameof(cart));
            if (line.Quantity <= 0) throw new ArgumentException("Chaque quantité du panier doit être strictement positive.", nameof(cart));
            if (line.ExpectedUnitPriceXaf <= 0) throw new ArgumentException("Le prix attendu du panier doit être strictement positif.", nameof(cart));
            if (linesByProduct.TryGetValue(line.ProductId, out var existing))
            {
                if (existing.ExpectedUnitPriceXaf != line.ExpectedUnitPriceXaf)
                    throw new ArgumentException("Un même produit ne peut pas avoir plusieurs prix attendus dans un panier.", nameof(cart));
                linesByProduct[line.ProductId] = existing with { Quantity = checked(existing.Quantity + line.Quantity) };
            }
            else
            {
                linesByProduct.Add(line.ProductId, line);
            }
            if (linesByProduct[line.ProductId].Quantity > MaximumQuantity)
                throw new ArgumentException("La quantité demandée dépasse la limite autorisée.", nameof(cart));
        }
        if (linesByProduct.Count == 0) throw new InvalidOperationException("Le panier est vide.");

        var orderedRequests = linesByProduct.Values.OrderBy(line => line.ProductId).ToArray();
        var requestFingerprint = ComputeRequestFingerprint(actor!.Id, orderedRequests, paymentMethod, amountPaidXaf, paymentDescription);
        var validationTokenText = validationToken.ToString("N");

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActiveUser(connection, transaction, actor.Id);

        var existingSale = FindSaleByValidationToken(connection, transaction, validationTokenText);
        if (existingSale is not null)
        {
            if (existingSale.Value.UserId != actor.Id)
                throw new UnauthorizedAccessException("Ce jeton de validation appartient à un autre utilisateur.");
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(existingSale.Value.Fingerprint), Convert.FromHexString(requestFingerprint)))
                throw new InvalidOperationException("Ce jeton de validation a déjà été utilisé pour un autre contenu de vente.");
            transaction.Commit();
            var previous = GetById(existingSale.Value.SaleId)
                ?? throw new InvalidOperationException("La vente déjà validée par ce jeton est introuvable.");
            return ToReceipt(previous, paymentMethod);
        }

        var snapshots = new List<SaleLineSnapshot>(orderedRequests.Length);
        long totalLong = 0;
        var hasPriceChange = false;
        foreach (var line in orderedRequests)
        {
            var product = GetSellableProduct(connection, transaction, line.ProductId);
            if (product.Stock < line.Quantity)
                throw new InsufficientStockException(product.Code, product.Name, product.Stock, line.Quantity);
            if (product.UnitPriceXaf != line.ExpectedUnitPriceXaf) hasPriceChange = true;
            var subtotal = checked(product.UnitPriceXaf * line.Quantity);
            totalLong = checked(totalLong + subtotal);
            if (totalLong > int.MaxValue) throw new InvalidOperationException("Le total dépasse le montant maximal accepté en FCFA.");
            snapshots.Add(new SaleLineSnapshot(line.ProductId, product.Code, product.Name, line.Quantity, product.UnitPriceXaf, subtotal));
        }

        if (hasPriceChange) throw new SalePriceChangedException(snapshots, (int)totalLong);

        var total = (int)totalLong;
        if (total <= 0) throw new InvalidOperationException("Le total de la vente doit être strictement positif.");
        var change = paymentMethod == SalePaymentMethod.Especes
            ? ValidateCashPayment(amountPaidXaf, total)
            : ValidateExactPayment(amountPaidXaf, total);

        var soldAt = DateTime.UtcNow;
        var number = CreateSaleNumber(soldAt);
        int saleId;
        using (var sale = connection.CreateCommand())
        {
            sale.Transaction = transaction;
            sale.CommandText = """
                INSERT INTO Vente (Numero, UtilisateurId, DateVenteUtc, MontantTotalXaf, MontantPayeXaf,
                                   MonnaieRendueXaf, ModePaiement, DescriptionPaiement, JetonValidation,
                                   EmpreinteValidation, Statut)
                VALUES ($number, $userId, $soldAt, $total, $paid, $change, $paymentMethod,
                        $paymentDescription, $validationToken, $requestFingerprint, 'Validee');
                SELECT last_insert_rowid();
                """;
            sale.Parameters.AddWithValue("$number", number);
            sale.Parameters.AddWithValue("$userId", actor.Id);
            sale.Parameters.AddWithValue("$soldAt", soldAt.ToString("O", CultureInfo.InvariantCulture));
            sale.Parameters.AddWithValue("$total", total);
            sale.Parameters.AddWithValue("$paid", amountPaidXaf);
            sale.Parameters.AddWithValue("$change", change);
            sale.Parameters.AddWithValue("$paymentMethod", PaymentMethodLabel(paymentMethod));
            sale.Parameters.AddWithValue("$paymentDescription", paymentDescription is null ? DBNull.Value : paymentDescription);
            sale.Parameters.AddWithValue("$validationToken", validationTokenText);
            sale.Parameters.AddWithValue("$requestFingerprint", requestFingerprint);
            saleId = Convert.ToInt32(sale.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        foreach (var line in snapshots)
        {
            using var insertLine = connection.CreateCommand();
            insertLine.Transaction = transaction;
            insertLine.CommandText = """
                INSERT INTO LigneVente (VenteId, ProduitId, Quantite, PrixUnitaireXaf, SousTotalXaf)
                VALUES ($saleId, $productId, $quantity, $unitPrice, $subtotal);
                """;
            insertLine.Parameters.AddWithValue("$saleId", saleId);
            insertLine.Parameters.AddWithValue("$productId", line.ProductId);
            insertLine.Parameters.AddWithValue("$quantity", line.Quantity);
            insertLine.Parameters.AddWithValue("$unitPrice", line.UnitPriceXaf);
            insertLine.Parameters.AddWithValue("$subtotal", line.SubtotalXaf);
            insertLine.ExecuteNonQuery();
        }

        transaction.Commit();
        return new SaleReceipt(saleId, number, soldAt, actor.FullName, total, amountPaidXaf, change,
            paymentMethod, paymentDescription, snapshots);
    }

    public void Cancel(AuthenticatedUser? actor, int saleId, string reason)
    {
        AuthenticationService.RequireRole(actor, UserRole.Administrateur);
        if (saleId <= 0) throw new ArgumentOutOfRangeException(nameof(saleId));
        reason = NormalizeRequired(reason, "Le motif d'annulation est obligatoire.");

        using var connection = connectionFactory.OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureActiveUser(connection, transaction, actor!.Id);
        using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE Vente SET Statut = 'Annulee' WHERE Id = $saleId AND Statut = 'Validee';";
            update.Parameters.AddWithValue("$saleId", saleId);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Cette vente n'existe pas ou a déjà été annulée.");
        }

        var cancelledAt = DateTime.UtcNow;
        using (var cancellation = connection.CreateCommand())
        {
            cancellation.Transaction = transaction;
            cancellation.CommandText = """
                INSERT INTO AnnulationVente (VenteId, UtilisateurId, Motif, AnnuleeLeUtc)
                VALUES ($saleId, $userId, $reason, $cancelledAt);
                """;
            cancellation.Parameters.AddWithValue("$saleId", saleId);
            cancellation.Parameters.AddWithValue("$userId", actor.Id);
            cancellation.Parameters.AddWithValue("$reason", reason);
            cancellation.Parameters.AddWithValue("$cancelledAt", cancelledAt.ToString("O", CultureInfo.InvariantCulture));
            cancellation.ExecuteNonQuery();
        }

        using (var audit = connection.CreateCommand())
        {
            audit.Transaction = transaction;
            audit.CommandText = """
                INSERT INTO JournalAudit (UtilisateurId, Action, Entite, EntiteId, Details, CreeLeUtc)
                VALUES ($userId, 'AnnulationVente', 'Vente', $saleId, $details, $createdAt);
                """;
            audit.Parameters.AddWithValue("$userId", actor.Id);
            audit.Parameters.AddWithValue("$saleId", saleId);
            audit.Parameters.AddWithValue("$details", $"Annulation de la vente {saleId}. Motif={reason}");
            audit.Parameters.AddWithValue("$createdAt", cancelledAt.ToString("O", CultureInfo.InvariantCulture));
            audit.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<SaleHistoryEntry> GetHistory(DateTime? fromUtc = null, DateTime? throughUtc = null,
        string? search = null, bool includeCancelled = true)
    {
        if (fromUtc is not null && throughUtc is not null && fromUtc > throughUtc)
            throw new ArgumentException("La date de début doit précéder la date de fin.");

        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.Id, v.Numero, v.DateVenteUtc, u.NomComplet, v.MontantTotalXaf, v.MontantPayeXaf,
                   v.MonnaieRendueXaf, v.ModePaiement, v.DescriptionPaiement, v.Statut, a.Motif, ua.NomComplet, a.AnnuleeLeUtc
            FROM Vente v
            INNER JOIN Utilisateur u ON u.Id = v.UtilisateurId
            LEFT JOIN AnnulationVente a ON a.VenteId = v.Id
            LEFT JOIN Utilisateur ua ON ua.Id = a.UtilisateurId
            WHERE ($fromUtc IS NULL OR v.DateVenteUtc >= $fromUtc)
              AND ($throughUtc IS NULL OR v.DateVenteUtc < $throughUtc)
              AND ($search = '' OR v.Numero LIKE $pattern ESCAPE '\' OR u.NomComplet LIKE $pattern ESCAPE '\')
              AND ($includeCancelled = 1 OR v.Statut <> 'Annulee')
            ORDER BY v.DateVenteUtc DESC, v.Id DESC;
            """;
        command.Parameters.AddWithValue("$fromUtc", fromUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$throughUtc", throughUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        var normalizedSearch = search?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("$search", normalizedSearch);
        command.Parameters.AddWithValue("$pattern", $"%{EscapeLike(normalizedSearch)}%");
        command.Parameters.AddWithValue("$includeCancelled", includeCancelled ? 1 : 0);
        return ReadHistory(command);
    }

    public SaleDetail? GetById(int saleId)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.Id, v.Numero, v.DateVenteUtc, u.NomComplet, v.MontantTotalXaf, v.MontantPayeXaf,
                   v.MonnaieRendueXaf, v.ModePaiement, v.DescriptionPaiement, v.Statut, a.Motif, ua.NomComplet, a.AnnuleeLeUtc
            FROM Vente v
            INNER JOIN Utilisateur u ON u.Id = v.UtilisateurId
            LEFT JOIN AnnulationVente a ON a.VenteId = v.Id
            LEFT JOIN Utilisateur ua ON ua.Id = a.UtilisateurId
            WHERE v.Id = $saleId;
            """;
        command.Parameters.AddWithValue("$saleId", saleId);
        var history = ReadHistory(command).SingleOrDefault();
        if (history is null) return null;

        using var linesCommand = connection.CreateCommand();
        linesCommand.CommandText = """
            SELECT l.ProduitId, p.Code, p.Nom, l.Quantite, l.PrixUnitaireXaf, l.SousTotalXaf
            FROM LigneVente l INNER JOIN Produit p ON p.Id = l.ProduitId
            WHERE l.VenteId = $saleId ORDER BY l.Id;
            """;
        linesCommand.Parameters.AddWithValue("$saleId", saleId);
        var lines = new List<SaleLineSnapshot>();
        using (var reader = linesCommand.ExecuteReader())
        {
            while (reader.Read())
                lines.Add(new SaleLineSnapshot(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5)));
        }
        return new SaleDetail(history, lines);
    }

    public static string PaymentMethodLabel(SalePaymentMethod method) => method switch
    {
        SalePaymentMethod.Especes => "Espèces",
        SalePaymentMethod.MobileMoney => "Mobile Money",
        SalePaymentMethod.Carte => "Carte",
        _ => throw new ArgumentOutOfRangeException(nameof(method))
    };

    private static int ValidateCashPayment(int amountPaidXaf, int totalXaf)
    {
        if (amountPaidXaf < totalXaf)
            throw new InvalidOperationException($"Montant insuffisant : il manque {totalXaf - amountPaidXaf:N0} FCFA.");
        return amountPaidXaf - totalXaf;
    }

    private static int ValidateExactPayment(int amountPaidXaf, int totalXaf)
    {
        if (amountPaidXaf != totalXaf)
            throw new InvalidOperationException("Pour ce mode de paiement, le montant payé doit être égal au total de la vente.");
        return 0;
    }

    private static string? NormalizePaymentDescription(SalePaymentMethod method, string? description)
    {
        var normalized = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (normalized?.Length > 300)
            throw new ArgumentException("La description du paiement ne peut pas dépasser 300 caractères.", nameof(description));
        if (method == SalePaymentMethod.Carte && normalized is null)
            throw new ArgumentException("Une description du paiement par carte est obligatoire.", nameof(description));
        if (method == SalePaymentMethod.Especes && normalized is not null)
            throw new ArgumentException("La description de paiement s'applique uniquement à Mobile Money ou à la carte.", nameof(description));
        return normalized;
    }

    private static (string Code, string Name, int UnitPriceXaf, int Stock) GetSellableProduct(
        SqliteConnection connection, SqliteTransaction transaction, int productId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.Code, p.Nom, p.PrixVenteXaf,
                   COALESCE((SELECT SUM(m.Quantite) FROM MouvementStock m WHERE m.ProduitId = p.Id), 0)
            FROM Produit p
            WHERE p.Id = $productId AND p.Actif = 1 AND p.PrixVenteXaf > 0;
            """;
        command.Parameters.AddWithValue("$productId", productId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException($"Le produit {productId} est introuvable, archivé ou sans prix de vente valide.");
        return (reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private static void EnsureActiveUser(SqliteConnection connection, SqliteTransaction transaction, int userId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM Utilisateur WHERE Id = $userId AND Actif = 1);";
        command.Parameters.AddWithValue("$userId", userId);
        if (Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) != 1)
            throw new UnauthorizedAccessException("Le compte utilisateur est désactivé.");
    }

    private static (int SaleId, int UserId, string Fingerprint)? FindSaleByValidationToken(
        SqliteConnection connection, SqliteTransaction transaction, string validationToken)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id, UtilisateurId, EmpreinteValidation FROM Vente WHERE JetonValidation = $token;";
        command.Parameters.AddWithValue("$token", validationToken);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? (reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2))
            : null;
    }

    private static string ComputeRequestFingerprint(int userId, IReadOnlyList<CartLineRequest> lines,
        SalePaymentMethod paymentMethod, int amountPaidXaf, string? paymentDescription)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            UserId = userId,
            Lines = lines.Select(line => new { line.ProductId, line.Quantity, line.ExpectedUnitPriceXaf }).ToArray(),
            PaymentMethod = (int)paymentMethod,
            AmountPaidXaf = amountPaidXaf,
            PaymentDescription = paymentDescription
        });
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static SaleReceipt ToReceipt(SaleDetail detail, SalePaymentMethod method) => new(
        detail.Sale.Id,
        detail.Sale.Number,
        detail.Sale.SoldAtUtc,
        detail.Sale.CashierName,
        detail.Sale.TotalXaf,
        detail.Sale.AmountPaidXaf,
        detail.Sale.ChangeXaf,
        method,
        detail.Sale.PaymentDescription,
        detail.Lines);

    private static List<SaleHistoryEntry> ReadHistory(SqliteCommand command)
    {
        var entries = new List<SaleHistoryEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new SaleHistoryEntry(reader.GetInt32(0), reader.GetString(1), ParseDate(reader.GetString(2)),
                reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : ParseDate(reader.GetString(12))));
        }
        return entries;
    }

    private static DateTime ParseDate(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static string NormalizeRequired(string value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();
    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    private static string CreateSaleNumber(DateTime utcNow) => $"V-{utcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}".ToUpperInvariant();
}
