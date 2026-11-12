using System.Globalization;
using GestionMagasin.Data;
using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class ReportsForm : Form
{
    private readonly AuthenticatedUser _administrator;
    private readonly ReportingService _reports;
    private readonly TimeZoneInfo _storeTimeZone;
    private readonly DateTimePicker _from = new();
    private readonly DateTimePicker _through = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();

    public ReportsForm(DatabaseSession session, AuthenticatedUser administrator)
    {
        AuthenticationService.RequireRole(administrator, UserRole.Administrateur);
        _administrator = administrator;
        var factory = new SqliteConnectionFactory(session.DatabasePath);
        _reports = new ReportingService(factory);
        _storeTimeZone = new AdministrationService(factory).GetStoreSettings().TimeZone;

        Text = "Rapports et exports";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(850, 560);
        Size = new Size(1200, 760);
        WindowState = FormWindowState.Maximized;
        BackColor = CatalogPalette.Background;
        ForeColor = CatalogPalette.Text;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3, BackColor = CatalogPalette.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Label { Text = "Rapports périodiques", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 22, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildFilters(), 0, 1);
        root.Controls.Add(BuildReportGrid(), 0, 2);
        Controls.Add(root);
        Shown += (_, _) => LoadReport();
    }

    private Control BuildFilters()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = CatalogPalette.Background };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 6, 0, 0), BackColor = CatalogPalette.Background };
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _storeTimeZone).DateTime.Date;
        foreach (var picker in new[] { _from, _through })
        {
            picker.Format = DateTimePickerFormat.Short;
            picker.Width = 130;
            picker.Value = today;
            picker.MaxDate = DateTime.Today.AddYears(20);
            picker.MinDate = new DateTime(2000, 1, 1);
        }
        controls.Controls.Add(new Label { Text = "Du", AutoSize = true, Padding = new Padding(0, 8, 4, 0) });
        controls.Controls.Add(_from);
        controls.Controls.Add(new Label { Text = "Au (inclus)", AutoSize = true, Padding = new Padding(14, 8, 4, 0) });
        controls.Controls.Add(_through);
        var refresh = CatalogPalette.CreateButton("Afficher le rapport", CatalogPalette.Primary);
        refresh.Size = new Size(170, 36);
        refresh.Click += (_, _) => LoadReport();
        controls.Controls.Add(refresh);
        var salesExport = CatalogPalette.CreateButton("Exporter les ventes CSV", Color.FromArgb(55, 86, 140));
        salesExport.Size = new Size(190, 36);
        salesExport.Click += (_, _) => ExportSales();
        controls.Controls.Add(salesExport);
        var stockExport = CatalogPalette.CreateButton("Exporter le stock CSV", Color.FromArgb(55, 86, 140));
        stockExport.Size = new Size(190, 36);
        stockExport.Click += (_, _) => ExportStock();
        controls.Controls.Add(stockExport);
        panel.Controls.Add(controls, 0, 0);
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = Color.FromArgb(92, 103, 119);
        _summary.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_summary, 0, 1);
        return panel;
    }

    private Control BuildReportGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 237, 245);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = CatalogPalette.Text;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        AddColumn("ProductCode", "Référence", 18);
        AddColumn("ProductName", "Produit", 42);
        AddColumn("UnitsSold", "Unités vendues", 18, "N0");
        AddColumn("RevenueXaf", "Montant réalisé (FCFA)", 22, "N0");
        return _grid;
    }

    private void AddColumn(string property, string title, float fill, string? format = null)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = title,
            FillWeight = fill,
            DefaultCellStyle = { Format = format ?? "" }
        });
    }

    private void LoadReport()
    {
        try
        {
            var from = DateOnly.FromDateTime(_from.Value.Date);
            var through = DateOnly.FromDateTime(_through.Value.Date);
            var rows = _reports.GetProductPeriodReport(_administrator, from, through, _storeTimeZone);
            _grid.DataSource = rows;
            var totalUnits = rows.Sum(row => row.UnitsSold);
            var totalXaf = rows.Sum(row => row.RevenueXaf);
            var culture = CultureInfo.GetCultureInfo("fr-FR");
            _summary.Text = $"Période locale : {from:dd/MM/yyyy} 00:00 inclus à {through.AddDays(1):dd/MM/yyyy} 00:00 exclu · {rows.Count} produit(s) · {totalUnits:N0} unités · {totalXaf.ToString("N0", culture)} FCFA";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExportSales()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", DefaultExt = "csv", AddExtension = true, FileName = $"ventes_{_from.Value:yyyyMMdd}_{_through.Value:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = _reports.ExportSalesCsv(_administrator, DateOnly.FromDateTime(_from.Value.Date), DateOnly.FromDateTime(_through.Value.Date), dialog.FileName, _storeTimeZone);
            MessageBox.Show(this, $"Export terminé : {count} ligne(s) de vente.", "Export CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ExportStock()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV UTF-8 (*.csv)|*.csv", DefaultExt = "csv", AddExtension = true, FileName = $"stocks_{DateTime.Today:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var count = _reports.ExportStockCsv(_administrator, dialog.FileName);
            MessageBox.Show(this, $"Export terminé : {count} référence(s) de produit.", "Export CSV", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception exception) => MessageBox.Show(this, exception.Message, "Rapports", MessageBoxButtons.OK, MessageBoxIcon.Error);
}
