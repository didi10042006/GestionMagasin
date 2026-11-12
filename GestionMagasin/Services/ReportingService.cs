using System.Globalization;
using System.Text;
using GestionMagasin.Data;
using Microsoft.Data.Sqlite;

namespace GestionMagasin.Services;

public sealed record ProductPeriodReportRow(
    int ProductId,
    string ProductCode,
    string ProductName,
    long UnitsSold,
    long RevenueXaf);

/// <summary>Period reporting and administrator-only CSV exports.</summary>
public sealed class ReportingService(SqliteConnectionFactory connectionFactory)
{
    public IReadOnlyList<ProductPeriodReportRow> GetProductPeriodReport(
        AuthenticatedUser? actor, DateOnly from, DateOnly throughInclusive, TimeZoneInfo? storeTimeZone = null)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        var (fromUtc, throughUtc) = GetUtcBounds(from, throughInclusive, storeTimeZone ?? TimeZoneInfo.Local);
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Id, p.Code, p.Nom, COALESCE(SUM(l.Quantite), 0), COALESCE(SUM(l.SousTotalXaf), 0)
            FROM Vente v
            INNER JOIN LigneVente l ON l.VenteId = v.Id
            INNER JOIN Produit p ON p.Id = l.ProduitId
            WHERE v.Statut = 'Validee' AND v.DateVenteUtc >= $fromUtc AND v.DateVenteUtc < $throughUtc
            GROUP BY p.Id, p.Code, p.Nom
            ORDER BY p.Nom COLLATE NOCASE, p.Code COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$fromUtc", fromUtc);
        command.Parameters.AddWithValue("$throughUtc", throughUtc);
        var rows = new List<ProductPeriodReportRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new ProductPeriodReportRow(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetInt64(4)));
        return rows;
    }

    public int ExportSalesCsv(AuthenticatedUser? actor, DateOnly from, DateOnly throughInclusive,
        string filePath, TimeZoneInfo? storeTimeZone = null)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        var (fromUtc, throughUtc) = GetUtcBounds(from, throughInclusive, storeTimeZone ?? TimeZoneInfo.Local);
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.Numero, v.DateVenteUtc, u.NomComplet, v.Statut, v.ModePaiement, v.DescriptionPaiement,
                   p.Code, p.Nom, l.Quantite, l.PrixUnitaireXaf, l.SousTotalXaf, v.MontantTotalXaf
            FROM Vente v
            INNER JOIN Utilisateur u ON u.Id = v.UtilisateurId
            INNER JOIN LigneVente l ON l.VenteId = v.Id
            INNER JOIN Produit p ON p.Id = l.ProduitId
            WHERE v.DateVenteUtc >= $fromUtc AND v.DateVenteUtc < $throughUtc
            ORDER BY v.DateVenteUtc, v.Id, l.Id;
            """;
        command.Parameters.AddWithValue("$fromUtc", fromUtc);
        command.Parameters.AddWithValue("$throughUtc", throughUtc);
        var csv = new StringBuilder();
        AppendCsvRow(csv, "Numéro vente", "Date UTC", "Vendeur", "Statut", "Mode paiement", "Détail paiement",
            "Référence produit", "Produit", "Quantité", "Prix unitaire XAF", "Sous-total XAF", "Total vente XAF");
        var count = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                AppendCsvRow(csv,
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.IsDBNull(5) ? "" : reader.GetString(5), reader.GetString(6), reader.GetString(7),
                    Number(reader.GetInt32(8)), Number(reader.GetInt32(9)), Number(reader.GetInt32(10)), Number(reader.GetInt32(11)));
                count++;
            }
        }
        WriteCsv(filePath, csv);
        AuditWriter.Record(connection, null, actor!.Id, "ExportVentesCsv", "Export", null,
            $"Export CSV des ventes : {count} ligne(s); fichier={Path.GetFileName(filePath)}.");
        return count;
    }

    public int ExportStockCsv(AuthenticatedUser? actor, string filePath)
    {
        AuthenticationService.RequireActiveRole(connectionFactory, actor, UserRole.Administrateur);
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.Code, p.Nom, c.Nom, p.Actif, p.PrixVenteXaf, p.SeuilAlerte,
                   COALESCE(SUM(m.Quantite), 0) AS Quantite
            FROM Produit p
            INNER JOIN Categorie c ON c.Id = p.CategorieId
            LEFT JOIN MouvementStock m ON m.ProduitId = p.Id
            GROUP BY p.Id, p.Code, p.Nom, c.Nom, p.Actif, p.PrixVenteXaf, p.SeuilAlerte
            ORDER BY p.Nom COLLATE NOCASE, p.Code COLLATE NOCASE;
            """;
        var csv = new StringBuilder();
        AppendCsvRow(csv, "Référence", "Produit", "Catégorie", "État", "Prix vente XAF", "Seuil alerte", "Stock", "Disponibilité");
        var count = 0;
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var quantity = reader.GetInt64(6);
                var threshold = reader.GetInt32(5);
                var availability = quantity == 0 ? "En rupture" : quantity <= threshold ? "Stock faible" : "Disponible";
                AppendCsvRow(csv, reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt32(3) == 1 ? "Actif" : "Archivé", Number(reader.GetInt32(4)),
                    Number(threshold), Number(quantity), availability);
                count++;
            }
        }
        WriteCsv(filePath, csv);
        AuditWriter.Record(connection, null, actor!.Id, "ExportStocksCsv", "Export", null,
            $"Export CSV du stock : {count} référence(s); fichier={Path.GetFileName(filePath)}.");
        return count;
    }

    private static (string FromUtc, string ThroughUtc) GetUtcBounds(DateOnly from, DateOnly throughInclusive, TimeZoneInfo timeZone)
    {
        if (from > throughInclusive) throw new ArgumentException("La date de début doit précéder ou être égale à la date de fin.");
        ArgumentNullException.ThrowIfNull(timeZone);
        var localStart = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var nextLocalStart = DateTime.SpecifyKind(throughInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone).ToString("O", CultureInfo.InvariantCulture);
        var throughUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalStart, timeZone).ToString("O", CultureInfo.InvariantCulture);
        return (fromUtc, throughUtc);
    }

    private static void WriteCsv(string filePath, StringBuilder csv)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Le chemin d'export est obligatoire.", nameof(filePath));
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static void AppendCsvRow(StringBuilder output, params string[] cells)
    {
        output.AppendJoin(',', cells.Select(EscapeCell)).Append("\r\n");
    }

    private static string EscapeCell(string value)
    {
        if (StartsWithFormula(value)) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool StartsWithFormula(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || character == '\uFEFF') continue;
            return character is '=' or '+' or '-' or '@';
        }
        return false;
    }

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
