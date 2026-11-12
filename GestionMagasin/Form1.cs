using System.Globalization;
using GestionMagasin.Data;
using GestionMagasin.Forms;
using GestionMagasin.Services;

namespace GestionMagasin;

public partial class Form1 : Form
{
    private readonly DatabaseSession _session;
    private readonly AuthenticatedUser? _user;
    private readonly DashboardService _dashboard;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 5000 };
    private readonly Label _businessDate = new();
    private readonly Label _lastUpdated = new();
    private readonly Label _scope = new();
    private readonly Label _salesAmount = new();
    private readonly Label _salesCount = new();
    private readonly Label _unitsSold = new();
    private readonly Label _outOfStock = new();
    private readonly Label _lowStock = new();

    public Form1(DatabaseSession session, AuthenticatedUser? user)
    {
        InitializeComponent();
        _session = session;
        _user = user;
        _dashboard = new DashboardService(new SqliteConnectionFactory(session.DatabasePath));

        Text = $"Gestion-Magasin — {session.ModeLabel}";
        ClientSize = new Size(1200, 760);
        MinimumSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = CatalogPalette.Background;
        ForeColor = CatalogPalette.Text;
        Font = new Font("Segoe UI", 10);

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(28),
            BackColor = CatalogPalette.Background
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(BuildHeader(), 0, 0);
        page.Controls.Add(BuildToolbar(), 0, 1);
        page.Controls.Add(BuildMetricCards(), 0, 2);
        Controls.Add(page);

        _refreshTimer.Tick += (_, _) => RefreshDashboard();
        Shown += (_, _) => RefreshDashboard();
        Activated += (_, _) => RefreshDashboard();
        FormClosed += (_, _) => _refreshTimer.Stop();
        _refreshTimer.Start();
        RefreshDashboard();
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = CatalogPalette.Background
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        header.Controls.Add(new Label
        {
            Text = "Gestion-Magasin",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 25, FontStyle.Bold),
            ForeColor = CatalogPalette.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = _user is null ? $"{_session.ModeLabel} · Consultation" : $"{_session.ModeLabel} · {_user.FullName} · {_user.Role}",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = _session.Mode == DatabaseMode.Reelle ? CatalogPalette.Primary : Color.FromArgb(35, 125, 99),
            TextAlign = ContentAlignment.MiddleRight
        }, 1, 0);
        header.Controls.Add(new Label
        {
            Text = _user?.Role == UserRole.Vendeur ? "Votre activité commerciale du jour" : "Vue d'ensemble de l'activité du magasin",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(92, 103, 119),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 1);
        header.Controls.Add(_businessDate, 1, 1);
        _businessDate.Dock = DockStyle.Fill;
        _businessDate.TextAlign = ContentAlignment.MiddleRight;
        _businessDate.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        _businessDate.ForeColor = CatalogPalette.Text;
        return header;
    }

    private Control BuildToolbar()
    {
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = CatalogPalette.Background
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = CatalogPalette.Background
        };
        var catalogButton = CatalogPalette.CreateButton("Catalogue", CatalogPalette.Primary);
        catalogButton.Size = new Size(130, 38);
        catalogButton.Click += (_, _) => OpenCatalog();
        var salesButton = CatalogPalette.CreateButton("Ventes", CatalogPalette.Primary);
        salesButton.Size = new Size(115, 38);
        salesButton.Enabled = _user is not null || _session.Mode == DatabaseMode.Demonstration;
        salesButton.Click += (_, _) => OpenSales(todayOnly: false);
        var refreshButton = CatalogPalette.CreateButton("Actualiser", Color.FromArgb(229, 233, 240));
        refreshButton.ForeColor = CatalogPalette.Text;
        refreshButton.Size = new Size(120, 38);
        refreshButton.Click += (_, _) => RefreshDashboard();
        actions.Controls.AddRange([catalogButton, salesButton, refreshButton]);
        toolbar.Controls.Add(actions, 0, 0);

        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = CatalogPalette.Background };
        _scope.Text = _user?.Role == UserRole.Vendeur ? "Indicateurs de vos ventes · stocks magasin" : "Indicateurs globaux · tous les vendeurs";
        _scope.Dock = DockStyle.Fill;
        _scope.TextAlign = ContentAlignment.BottomRight;
        _scope.ForeColor = Color.FromArgb(92, 103, 119);
        _lastUpdated.Dock = DockStyle.Fill;
        _lastUpdated.TextAlign = ContentAlignment.TopRight;
        _lastUpdated.ForeColor = Color.FromArgb(92, 103, 119);
        status.Controls.Add(_scope, 0, 0);
        status.Controls.Add(_lastUpdated, 0, 1);
        toolbar.Controls.Add(status, 1, 0);
        return toolbar;
    }

    private Control BuildMetricCards()
    {
        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 2,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = CatalogPalette.Background
        };
        for (var column = 0; column < 3; column++)
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var salesLabel = _user?.Role == UserRole.Vendeur ? "Vos ventes validées" : "Ventes validées";
        cards.Controls.Add(CreateMetricCard(salesLabel, _salesAmount, "Montant encaissé aujourd'hui · cliquer pour ouvrir", CatalogPalette.Primary,
            () => OpenSales(todayOnly: true)), 0, 0);
        cards.Controls.Add(CreateMetricCard(_user?.Role == UserRole.Vendeur ? "Vos ventes" : "Nombre de ventes", _salesCount,
            "Ventes validées aujourd'hui · cliquer pour consulter", Color.FromArgb(124, 58, 237),
            () => OpenSales(todayOnly: true)), 1, 0);
        cards.Controls.Add(CreateMetricCard(_user?.Role == UserRole.Vendeur ? "Vos unités vendues" : "Unités vendues", _unitsSold,
            "Articles vendus aujourd'hui · cliquer pour consulter", Color.FromArgb(8, 126, 139),
            () => OpenSales(todayOnly: true)), 2, 0);
        cards.Controls.Add(CreateMetricCard("Références en rupture", _outOfStock,
            "Produits actifs à zéro · cliquer pour afficher", CatalogPalette.OutOfStockText,
            () => OpenCatalog(AvailabilityFilter.EnRupture)), 0, 1);
        cards.Controls.Add(CreateMetricCard("Références en stock faible", _lowStock,
            "Stock positif sous le seuil · cliquer pour afficher", CatalogPalette.LowStockText,
            () => OpenCatalog(AvailabilityFilter.StockFaible)), 1, 1);
        cards.Controls.Add(CreateInformationCard(), 2, 1);
        return cards;
    }

    private static Control CreateMetricCard(string title, Label valueLabel, string caption, Color accent, Action onClick)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Margin = new Padding(8),
            Padding = new Padding(20),
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand
        };
        var heading = new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            ForeColor = Color.FromArgb(77, 89, 106),
            Cursor = Cursors.Hand
        };
        valueLabel.Text = "—";
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = new Font("Segoe UI", 29, FontStyle.Bold);
        valueLabel.ForeColor = accent;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;
        valueLabel.Cursor = Cursors.Hand;
        var description = new Label
        {
            Text = caption,
            Dock = DockStyle.Bottom,
            Height = 42,
            ForeColor = Color.FromArgb(107, 114, 128),
            Font = new Font("Segoe UI", 9),
            Cursor = Cursors.Hand
        };
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(description);
        panel.Controls.Add(heading);
        panel.Click += (_, _) => onClick();
        heading.Click += (_, _) => onClick();
        valueLabel.Click += (_, _) => onClick();
        description.Click += (_, _) => onClick();
        return panel;
    }

    private Control CreateInformationCard()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(239, 246, 255),
            Margin = new Padding(8),
            Padding = new Padding(20)
        };
        panel.Controls.Add(new Label
        {
            Text = "Mise à jour automatique",
            Dock = DockStyle.Top,
            Height = 32,
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = CatalogPalette.Text
        });
        panel.Controls.Add(new Label
        {
            Text = "Les indicateurs sont recalculés toutes les 5 secondes et au retour sur cet écran. Sélectionnez une carte pour ouvrir la liste correspondante.",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(77, 89, 106),
            Font = new Font("Segoe UI", 10)
        });
        return panel;
    }

    private void RefreshDashboard()
    {
        if (IsDisposed) return;
        try
        {
            var metrics = _dashboard.GetDailyMetrics(_user);
            var french = CultureInfo.GetCultureInfo("fr-FR");
            _businessDate.Text = $"Date métier : {metrics.BusinessDate.ToDateTime(TimeOnly.MinValue).ToString("dddd d MMMM yyyy", french)}";
            _salesAmount.Text = $"{metrics.ValidatedSalesAmountXaf.ToString("N0", french)} FCFA";
            _salesCount.Text = metrics.ValidatedSalesCount.ToString("N0", french);
            _unitsSold.Text = metrics.UnitsSold.ToString("N0", french);
            _outOfStock.Text = metrics.OutOfStockReferences.ToString("N0", french);
            _lowStock.Text = metrics.LowStockReferences.ToString("N0", french);
            _lastUpdated.Text = $"Actualisé à {DateTime.Now.ToString("HH:mm:ss", french)} · toutes les 5 s";
        }
        catch (Exception ex)
        {
            _lastUpdated.Text = $"Actualisation impossible : {ex.Message}";
        }
    }

    private void OpenCatalog(AvailabilityFilter availability = AvailabilityFilter.Tous)
    {
        using var catalog = new ProductsCatalogForm(_session, _user, availability,
            availability == AvailabilityFilter.Tous ? RecordStateFilter.Tous : RecordStateFilter.Actif);
        catalog.ShowDialog(this);
        RefreshDashboard();
    }

    private void OpenSales(bool todayOnly)
    {
        using var sales = new SalesForm(_session, _user, todayOnly);
        sales.ShowDialog(this);
        RefreshDashboard();
    }
}
