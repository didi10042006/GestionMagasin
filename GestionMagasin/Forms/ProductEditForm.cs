using GestionMagasin.Data;
using GestionMagasin.Models;
using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class ProductEditForm : Form
{
    private readonly ProductService _productService;
    private readonly CategoryService _categoryService;
    private readonly AuthenticatedUser _actor;
    private readonly Produit? _existing;
    private readonly TextBox _code = new();
    private readonly TextBox _name = new();
    private readonly TextBox _description = new();
    private readonly Panel _contentPanel = new();
    private readonly ComboBox _category = new();
    private readonly NumericUpDown _purchasePrice = new();
    private readonly NumericUpDown _salePrice = new();
    private readonly NumericUpDown _alertThreshold = new();
    private readonly Label _error = new();

    public ProductEditForm(ProductService productService, CategoryService categoryService,
        AuthenticatedUser actor, Produit? existing = null)
    {
        _productService = productService;
        _categoryService = categoryService;
        _actor = actor;
        _existing = existing;
        Text = existing is null ? "Nouveau produit" : $"Modifier — {existing.Code}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(650, 630);
        BackColor = CatalogPalette.Background;
        Font = new Font("Segoe UI", 9.5f);
        Shown += (_, _) => DpiFormLayout.FitDialogToWorkingArea(this);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CatalogPalette.Background
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.AutoScroll = true;
        _contentPanel.BackColor = CatalogPalette.Background;
        var footer = new Panel { Dock = DockStyle.Fill, BackColor = CatalogPalette.Background };

        var heading = new Label
        {
            Text = existing is null ? "Créer un produit" : "Modifier le produit",
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = CatalogPalette.Text,
            AutoSize = true,
            Location = new Point(28, 22)
        };
        var referenceInfo = new Label
        {
            Text = existing is null
                ? "La référence est unique et restera réservée après archivage."
                : "La référence ne peut pas être modifiée après création.",
            ForeColor = Color.FromArgb(90, 100, 115),
            AutoSize = true,
            Location = new Point(31, 60)
        };
        _contentPanel.Controls.Add(heading);
        _contentPanel.Controls.Add(referenceInfo);

        AddField("Référence *", _code, 100);
        AddField("Nom du produit *", _name, 164);
        AddField("Catégorie *", _category, 228);
        AddField("Description", _description, 292);
        AddField("Prix d'achat (FCFA)", _purchasePrice, 356);
        AddField("Prix de vente (FCFA) — obligatoire et supérieur à 0", _salePrice, 420);
        AddField("Seuil d'alerte (unités)", _alertThreshold, 484);

        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        var categories = _categoryService.GetActive().ToList();
        if (existing is not null && categories.All(item => item.Id != existing.CategorieId))
        {
            var existingCategory = _categoryService.GetAll().FirstOrDefault(item => item.Id == existing.CategorieId);
            if (existingCategory is not null) categories.Add(existingCategory);
        }
        _category.DataSource = categories;
        _category.DisplayMember = nameof(CategoryCatalogItem.Name);
        _category.ValueMember = nameof(CategoryCatalogItem.Id);

        _code.ReadOnly = existing is not null;
        _code.Text = existing?.Code ?? string.Empty;
        _name.Text = existing?.Nom ?? string.Empty;
        _description.Text = existing?.Description ?? string.Empty;
        _purchasePrice.Value = existing?.PrixAchatXaf ?? 0;
        _salePrice.Value = existing?.PrixVenteXaf ?? 0;
        _alertThreshold.Value = existing?.SeuilAlerte ?? 5;
        if (existing is not null) _category.SelectedValue = existing.CategorieId;

        _error.Location = new Point(30, 2);
        _error.Size = new Size(588, 26);
        _error.ForeColor = Color.Firebrick;
        footer.Controls.Add(_error);

        var save = CatalogPalette.CreateButton("Enregistrer", CatalogPalette.Primary);
        save.Location = new Point(422, 34);
        save.Size = new Size(196, 36);
        save.Click += SaveProduct;
        var cancel = CatalogPalette.CreateButton("Annuler", Color.FromArgb(229, 233, 240));
        cancel.ForeColor = CatalogPalette.Text;
        cancel.Location = new Point(312, 34);
        cancel.Size = new Size(100, 36);
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.AddRange([save, cancel]);
        layout.Controls.Add(_contentPanel, 0, 0);
        layout.Controls.Add(footer, 0, 1);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void AddField(string label, Control input, int top)
    {
        _contentPanel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Location = new Point(31, top),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = CatalogPalette.Text
        });
        input.Location = new Point(31, top + 23);
        input.Size = new Size(588, 29);
        input.Font = new Font("Segoe UI", 10);
        if (input is TextBox textBox)
        {
            textBox.BorderStyle = BorderStyle.FixedSingle;
        }
        if (input is NumericUpDown numeric)
        {
            numeric.Minimum = 0;
            numeric.Maximum = 1_000_000_000;
            numeric.ThousandsSeparator = true;
            numeric.DecimalPlaces = 0;
        }
        _contentPanel.Controls.Add(input);
    }

    private void SaveProduct(object? sender, EventArgs e)
    {
        _error.Text = string.Empty;
        if (_category.SelectedItem is not CategoryCatalogItem category)
        {
            _error.Text = "Créez ou sélectionnez d'abord une catégorie.";
            return;
        }

        try
        {
            var categoryId = category.Id;
            if (_existing is null)
            {
                _productService.Create(_actor, _code.Text, _name.Text, _description.Text,
                    categoryId, Decimal.ToInt32(_purchasePrice.Value), Decimal.ToInt32(_salePrice.Value),
                    Decimal.ToInt32(_alertThreshold.Value));
            }
            else
            {
                _productService.Update(_actor, _existing.Id, _name.Text, _description.Text,
                    categoryId, Decimal.ToInt32(_purchasePrice.Value), Decimal.ToInt32(_salePrice.Value),
                    Decimal.ToInt32(_alertThreshold.Value));
            }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _error.Text = ex.Message;
        }
    }
}
