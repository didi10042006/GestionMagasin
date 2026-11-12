using System.Globalization;
using GestionMagasin.Data;
using GestionMagasin.Services;

namespace GestionMagasin.Services;

public sealed record DailyDashboardMetrics(
    DateOnly BusinessDate,
    long ValidatedSalesAmountXaf,
    long ValidatedSalesCount,
    long UnitsSold,
    long OutOfStockReferences,
    long LowStockReferences);

/// <summary>Reads a fresh database snapshot for the dashboard's current local business day.</summary>
public sealed class DashboardService(SqliteConnectionFactory connectionFactory)
{
    public DailyDashboardMetrics GetDailyMetrics(AuthenticatedUser? user, DateTime? localNow = null, TimeZoneInfo? businessTimeZone = null)
    {
        if (user is not null && !Enum.IsDefined(user.Role))
            throw new ArgumentException("Le rôle de l'utilisateur est invalide.", nameof(user));

        var timeZone = businessTimeZone ?? TimeZoneInfo.Local;
        var businessNow = localNow ?? TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime;
        var businessDate = DateOnly.FromDateTime(businessNow);
        var localStart = DateTime.SpecifyKind(businessDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(businessDate.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone).ToString("O", CultureInfo.InvariantCulture);
        var throughUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone).ToString("O", CultureInfo.InvariantCulture);
        var scopedUserId = user?.Role == UserRole.Vendeur ? user.Id : (int?)null;

        using var connection = connectionFactory.OpenConnection();
        using var sales = connection.CreateCommand();
        sales.CommandText = """
            SELECT
                (SELECT COALESCE(SUM(v.MontantTotalXaf), 0)
                 FROM Vente v
                 WHERE v.Statut = 'Validee' AND v.DateVenteUtc >= $fromUtc AND v.DateVenteUtc < $throughUtc
                   AND ($userId IS NULL OR v.UtilisateurId = $userId)),
                (SELECT COUNT(*)
                 FROM Vente v
                 WHERE v.Statut = 'Validee' AND v.DateVenteUtc >= $fromUtc AND v.DateVenteUtc < $throughUtc
                   AND ($userId IS NULL OR v.UtilisateurId = $userId)),
                (SELECT COALESCE(SUM(l.Quantite), 0)
                 FROM LigneVente l
                 INNER JOIN Vente v ON v.Id = l.VenteId
                 WHERE v.Statut = 'Validee' AND v.DateVenteUtc >= $fromUtc AND v.DateVenteUtc < $throughUtc
                   AND ($userId IS NULL OR v.UtilisateurId = $userId));
            """;
        sales.Parameters.AddWithValue("$fromUtc", fromUtc);
        sales.Parameters.AddWithValue("$throughUtc", throughUtc);
        sales.Parameters.AddWithValue("$userId", scopedUserId is null ? DBNull.Value : scopedUserId.Value);
        long amount;
        long count;
        long units;
        using (var reader = sales.ExecuteReader())
        {
            if (!reader.Read()) throw new InvalidOperationException("Impossible de lire les indicateurs de vente du jour.");
            amount = reader.GetInt64(0);
            count = reader.GetInt64(1);
            units = reader.GetInt64(2);
        }

        using var stock = connection.CreateCommand();
        stock.CommandText = """
            WITH StockCourant AS (
                SELECT p.Id, p.SeuilAlerte, COALESCE(SUM(m.Quantite), 0) AS Quantite
                FROM Produit p
                LEFT JOIN MouvementStock m ON m.ProduitId = p.Id
                WHERE p.Actif = 1
                GROUP BY p.Id
            )
            SELECT
                COALESCE(SUM(CASE WHEN Quantite = 0 THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN Quantite > 0 AND Quantite <= SeuilAlerte THEN 1 ELSE 0 END), 0)
            FROM StockCourant;
            """;
        using var stockReader = stock.ExecuteReader();
        if (!stockReader.Read()) throw new InvalidOperationException("Impossible de lire les indicateurs de stock.");
        return new DailyDashboardMetrics(businessDate, amount, count, units,
            stockReader.GetInt64(0), stockReader.GetInt64(1));
    }
}
