using GestionMagasin.Data;
using GestionMagasin.Forms;
using GestionMagasin.Services;

namespace GestionMagasin;

public partial class Form1 : Form
{
    public Form1(DatabaseSession session, AuthenticatedUser? user)
    {
        InitializeComponent();
        Text = $"Gestion-Magasin — {session.ModeLabel}";
        ClientSize = new Size(1040, 660);
        MinimumSize = new Size(740, 430);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        BackColor = CatalogPalette.Background;
        ForeColor = CatalogPalette.Text;
        Font = new Font("Segoe UI", 10);

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(34),
            BackColor = CatalogPalette.Background
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var welcome = new Panel { Dock = DockStyle.Fill, BackColor = CatalogPalette.Background };
        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = CatalogPalette.Text,
            Location = new Point(0, 4),
            Text = "Gestion-Magasin"
        };
        var modeLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = session.Mode == DatabaseMode.Reelle ? CatalogPalette.Primary : Color.FromArgb(35, 125, 99),
            Location = new Point(4, 54),
            Text = user is null
                ? $"{session.ModeLabel} · Consultation"
                : $"{session.ModeLabel} · {user.FullName} · {user.Role}"
        };
        var catalogButton = CatalogPalette.CreateButton("Ouvrir le catalogue", CatalogPalette.Primary);
        catalogButton.Size = new Size(196, 42);
        catalogButton.Location = new Point(0, 6);
        catalogButton.Click += (_, _) =>
        {
            using var catalog = new ProductsCatalogForm(session, user);
            catalog.ShowDialog(this);
        };
        welcome.Controls.AddRange([title, modeLabel]);

        var salesButton = CatalogPalette.CreateButton("Ouvrir les ventes", CatalogPalette.Primary);
        salesButton.Size = new Size(196, 42);
        salesButton.Enabled = user is not null || session.Mode == DatabaseMode.Demonstration;
        salesButton.Click += (_, _) =>
        {
            using var sales = new SalesForm(session, user);
            sales.ShowDialog(this);
        };

        var card = new Panel
        {
            Size = new Size(460, 270),
            BackColor = Color.White,
            Padding = new Padding(24),
            Location = new Point(0, 14)
        };
        var cardTitle = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = CatalogPalette.Text,
            Location = new Point(24, 23),
            Text = "Catalogue et catégories"
        };
        var cardText = new Label
        {
            AutoSize = false,
            Size = new Size(400, 74),
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(92, 103, 119),
            Location = new Point(26, 66),
            Text = "Gérez le catalogue, recherchez des articles, encaissez les ventes et consultez leur historique."
        };
        catalogButton.Location = new Point(26, 150);
        salesButton.Location = new Point(26, 204);
        card.Controls.AddRange(new Control[] { cardTitle, cardText, catalogButton, salesButton });
        page.Controls.Add(welcome, 0, 0);
        page.Controls.Add(card, 0, 1);
        Controls.Add(page);
    }
}
