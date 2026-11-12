using GestionMagasin.Data;
using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class ProductsCatalogForm : Form
{
    private sealed record CategoryFilter(int Id, string Name);
    private sealed record DisplayFilter(int Value, string Name);

    private readonly ProductService _products;
    private readonly CategoryService _categories;
    private readonly AuthenticatedUser? _user;
    private readonly bool _canManage;
    private readonly TextBox _productSearch = new();
    private readonly ComboBox _productCategoryFilter = new();
    private readonly ComboBox _availabilityFilter = new();
    private readonly ComboBox _productStateFilter = new();
    private readonly DataGridView _productGrid = new();
    private readonly TextBox _categorySearch = new();
    private readonly ComboBox _categoryStateFilter = new();
    private readonly DataGridView _categoryGrid = new();
    private readonly Button _editProduct = CatalogPalette.CreateButton("Modifier", CatalogPalette.Primary);
    private readonly Button _archiveProduct = CatalogPalette.CreateButton("Archiver", Color.FromArgb(185, 63, 63));
    private readonly Button _editCategory = CatalogPalette.CreateButton("Modifier", CatalogPalette.Primary);
    private readonly Button _archiveCategory = CatalogPalette.CreateButton("Archiver", Color.FromArgb(185, 63, 63));
    private readonly Label _productCount = new();

    public ProductsCatalogForm(DatabaseSession session, AuthenticatedUser? user,
        AvailabilityFilter initialAvailability = AvailabilityFilter.Tous,
        RecordStateFilter initialState = RecordStateFilter.Tous)
    {
        _user = user;
        _canManage = user?.Role == UserRole.Administrateur;
        var factory = new SqliteConnectionFactory(session.DatabasePath);
        _products = new ProductService(factory);
        _categories = new CategoryService(factory);

        Text = "Catalogue des produits et catégories";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(760, 430);
        Size = new Size(1360, 820);
        WindowState = FormWindowState.Maximized;
        BackColor = CatalogPalette.Background;
        ForeColor = CatalogPalette.Text;
        Font = new Font("Segoe UI", 9.5f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(22, 18, 22, 20),
            BackColor = CatalogPalette.Background
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = BuildHeader(session);
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        var productsTab = new TabPage("Produits") { BackColor = CatalogPalette.Background, Padding = new Padding(12) };
        var categoriesTab = new TabPage("Catégories") { BackColor = CatalogPalette.Background, Padding = new Padding(12) };
        productsTab.Controls.Add(BuildProductsPage());
        categoriesTab.Controls.Add(BuildCategoriesPage());
        tabs.TabPages.AddRange([productsTab, categoriesTab]);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(tabs, 0, 1);
        Controls.Add(layout);

        LoadCategoriesIntoFilter();
        _availabilityFilter.SelectedValue = (int)initialAvailability;
        _productStateFilter.SelectedValue = (int)initialState;
        LoadProducts();
        LoadCategories();
        UpdateProductActions();
        UpdateCategoryActions();
    }

    private Control BuildHeader(DatabaseSession session)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = CatalogPalette.Background
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        var title = new Label
        {
            Text = "Catalogue",
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = CatalogPalette.Text,
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        var subtitle = new Label
        {
            Text = "Recherchez, filtrez et gérez les produits et catégories.",
            ForeColor = Color.FromArgb(92, 103, 119),
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        var mode = new Label
        {
            Text = _canManage ? $"{session.ModeLabel} · Administrateur" : $"{session.ModeLabel} · Lecture seule",
            ForeColor = _canManage ? CatalogPalette.Primary : Color.FromArgb(92, 103, 119),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            Dock = DockStyle.Fill
        };
        panel.Controls.Add(title, 0, 0);
        panel.Controls.Add(subtitle, 0, 1);
        panel.Controls.Add(mode, 1, 0);
        panel.SetRowSpan(mode, 2);
        return panel;
    }

    private Control BuildProductsPage()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = CatalogPalette.Background
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 2, 0, 0),
            BackColor = CatalogPalette.Background
        };
        ConfigureFilter(_productSearch, 230, "Référence ou nom…");
        ConfigureCombo(_productCategoryFilter, 180);
        ConfigureCombo(_availabilityFilter, 150);
        ConfigureCombo(_productStateFilter, 130);
        filters.Controls.Add(FilterBox("Recherche", _productSearch, 260));
        filters.Controls.Add(FilterBox("Catégorie", _productCategoryFilter, 195));
        filters.Controls.Add(FilterBox("Disponibilité", _availabilityFilter, 165));
        filters.Controls.Add(FilterBox("État", _productStateFilter, 145));
        _availabilityFilter.DataSource = new[]
        {
            new DisplayFilter(0, "Toutes"), new DisplayFilter(1, "Disponible"),
            new DisplayFilter(2, "Stock faible"), new DisplayFilter(3, "En rupture")
        };
        _availabilityFilter.DisplayMember = nameof(DisplayFilter.Name);
        _availabilityFilter.ValueMember = nameof(DisplayFilter.Value);
        _productStateFilter.DataSource = StateFilters();
        _productStateFilter.DisplayMember = nameof(DisplayFilter.Name);
        _productStateFilter.ValueMember = nameof(DisplayFilter.Value);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 2),
            BackColor = CatalogPalette.Background
        };
        var newProduct = CatalogPalette.CreateButton("+ Nouveau produit", CatalogPalette.Primary);
        newProduct.Size = new Size(160, 36);
        newProduct.Click += (_, _) => OpenProductEditor(null);
        _editProduct.Size = new Size(110, 36);
        _editProduct.Click += (_, _) => OpenSelectedProductEditor();
        _archiveProduct.Size = new Size(110, 36);
        _archiveProduct.Click += (_, _) => ArchiveSelectedProduct();
        var refresh = CatalogPalette.CreateButton("Actualiser", Color.FromArgb(229, 233, 240));
        refresh.ForeColor = CatalogPalette.Text;
        refresh.Size = new Size(110, 36);
        refresh.Click += (_, _) => LoadProducts();
        _productCount.AutoSize = true;
        _productCount.ForeColor = Color.FromArgb(92, 103, 119);
        _productCount.Margin = new Padding(12, 11, 0, 0);
        toolbar.Controls.AddRange([newProduct, _editProduct, _archiveProduct, refresh, _productCount]);

        SetupProductGrid();
        layout.Controls.Add(filters, 0, 0);
        layout.Controls.Add(toolbar, 0, 1);
        layout.Controls.Add(_productGrid, 0, 2);

        _productSearch.TextChanged += (_, _) => LoadProducts();
        _productCategoryFilter.SelectedIndexChanged += (_, _) => LoadProducts();
        _availabilityFilter.SelectedIndexChanged += (_, _) => LoadProducts();
        _productStateFilter.SelectedIndexChanged += (_, _) => LoadProducts();
        _productGrid.SelectionChanged += (_, _) => UpdateProductActions();
        _productGrid.CellDoubleClick += (_, _) => OpenSelectedProductEditor();
        return layout;
    }

    private Control BuildCategoriesPage()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = CatalogPalette.Background
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 9, 0, 2),
            BackColor = CatalogPalette.Background
        };
        ConfigureFilter(_categorySearch, 300, "Rechercher une catégorie…");
        ConfigureCombo(_categoryStateFilter, 180);
        _categoryStateFilter.DataSource = StateFilters();
        _categoryStateFilter.DisplayMember = nameof(DisplayFilter.Name);
        _categoryStateFilter.ValueMember = nameof(DisplayFilter.Value);
        toolbar.Controls.Add(FilterBox("Recherche catégorie", _categorySearch, 330));
        toolbar.Controls.Add(FilterBox("État", _categoryStateFilter, 195));

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = CatalogPalette.Background
        };
        var newCategory = CatalogPalette.CreateButton("+ Nouvelle catégorie", CatalogPalette.Primary);
        newCategory.Size = new Size(178, 36);
        newCategory.Click += (_, _) => OpenCategoryEditor(null);
        _editCategory.Size = new Size(110, 36);
        _editCategory.Click += (_, _) => OpenSelectedCategoryEditor();
        _archiveCategory.Size = new Size(110, 36);
        _archiveCategory.Click += (_, _) => ArchiveSelectedCategory();
        actions.Controls.AddRange([newCategory, _editCategory, _archiveCategory]);
        toolbar.Controls.Add(actions);

        SetupCategoryGrid();
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_categoryGrid, 0, 1);
        _categorySearch.TextChanged += (_, _) => LoadCategories();
        _categoryStateFilter.SelectedIndexChanged += (_, _) => LoadCategories();
        _categoryGrid.SelectionChanged += (_, _) => UpdateCategoryActions();
        _categoryGrid.CellDoubleClick += (_, _) => OpenSelectedCategoryEditor();
        return layout;
    }

    private void SetupProductGrid()
    {
        ConfigureGrid(_productGrid);
        AddGridColumn(_productGrid, "Référence", nameof(ProductCatalogItem.Code), 105);
        AddGridColumn(_productGrid, "Nom", nameof(ProductCatalogItem.Name), 180, true);
        AddGridColumn(_productGrid, "Catégorie", nameof(ProductCatalogItem.CategoryName), 140);
        var price = AddGridColumn(_productGrid, "Prix vente (FCFA)", nameof(ProductCatalogItem.SalePrice), 130);
        price.DefaultCellStyle.Format = "N0";
        AddGridColumn(_productGrid, "Stock", nameof(ProductCatalogItem.Stock), 80);
        AddGridColumn(_productGrid, "Seuil", nameof(ProductCatalogItem.AlertThreshold), 75);
        AddGridColumn(_productGrid, "Disponibilité", nameof(ProductCatalogItem.Availability), 120);
        AddGridColumn(_productGrid, "État", nameof(ProductCatalogItem.State), 85);
        _productGrid.CellFormatting += StyleAvailabilityCell;
    }

    private void SetupCategoryGrid()
    {
        ConfigureGrid(_categoryGrid);
        AddGridColumn(_categoryGrid, "Catégorie", nameof(CategoryCatalogItem.Name), 230, true);
        AddGridColumn(_categoryGrid, "Description", nameof(CategoryCatalogItem.Description), 400, true);
        AddGridColumn(_categoryGrid, "État", nameof(CategoryCatalogItem.State), 120);
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = Color.FromArgb(232, 236, 242);
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersHeight = 42;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 242, 247);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = CatalogPalette.Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = CatalogPalette.Text;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = CatalogPalette.Text;
        grid.DefaultCellStyle.Padding = new Padding(5, 2, 5, 2);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(250, 251, 253);
        grid.RowTemplate.Height = 36;
    }

    private static void StyleAvailabilityCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0 ||
            grid.Columns[e.ColumnIndex].DataPropertyName != nameof(ProductCatalogItem.Availability))
        {
            return;
        }

        var (foreground, background) = Convert.ToString(e.Value) switch
        {
            "En rupture" => (CatalogPalette.OutOfStockText, CatalogPalette.OutOfStockBackground),
            "Stock faible" => (CatalogPalette.LowStockText, CatalogPalette.LowStockBackground),
            "Disponible" => (CatalogPalette.AvailableText, CatalogPalette.AvailableBackground),
            _ => (CatalogPalette.Text, Color.White)
        };
        e.CellStyle.ForeColor = foreground;
        e.CellStyle.BackColor = background;
        e.CellStyle.SelectionForeColor = foreground;
        e.CellStyle.SelectionBackColor = background;
        e.CellStyle.Font = CatalogPalette.AvailabilityFont;
        e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        e.CellStyle.Padding = new Padding(8, 2, 8, 2);
        e.FormattingApplied = true;
    }

    private static DataGridViewColumn AddGridColumn(DataGridView grid, string header, string property, int width, bool fill = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            MinimumWidth = Math.Min(width, 70),
            SortMode = DataGridViewColumnSortMode.Automatic,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
        };
        grid.Columns.Add(column);
        return column;
    }

    private static Control FilterBox(string label, Control input, int width)
    {
        var group = new TableLayoutPanel
        {
            Width = width,
            Height = 70,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 12, 0),
            BackColor = CatalogPalette.Background
        };
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(77, 89, 106),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        }, 0, 0);
        input.Dock = DockStyle.Fill;
        group.Controls.Add(input, 0, 1);
        return group;
    }

    private static void ConfigureFilter(TextBox textBox, int width, string placeholder)
    {
        textBox.Width = width;
        textBox.Font = new Font("Segoe UI", 9.5f);
        textBox.BorderStyle = BorderStyle.FixedSingle;
        textBox.PlaceholderText = placeholder;
    }

    private static void ConfigureCombo(ComboBox comboBox, int width)
    {
        comboBox.Width = width;
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Font = new Font("Segoe UI", 9.5f);
        comboBox.FlatStyle = FlatStyle.Flat;
    }

    private static DisplayFilter[] StateFilters() =>
    [new(0, "Tous"), new(1, "Actifs"), new(2, "Archivés")];

    private void LoadCategoriesIntoFilter()
    {
        var choices = new List<CategoryFilter> { new(0, "Toutes les catégories") };
        choices.AddRange(_categories.GetAll().Select(category =>
            new CategoryFilter(category.Id, category.Active ? category.Name : $"{category.Name} (archivée)")));
        _productCategoryFilter.DataSource = choices;
        _productCategoryFilter.DisplayMember = nameof(CategoryFilter.Name);
        _productCategoryFilter.ValueMember = nameof(CategoryFilter.Id);
    }

    private void LoadProducts()
    {
        if (_productCategoryFilter.SelectedValue is null || _availabilityFilter.SelectedValue is null || _productStateFilter.SelectedValue is null)
        {
            return;
        }
        var categoryValue = Convert.ToInt32(_productCategoryFilter.SelectedValue);
        var availabilityValue = Convert.ToInt32(_availabilityFilter.SelectedValue);
        var stateValue = Convert.ToInt32(_productStateFilter.SelectedValue);
        _productGrid.DataSource = _products.Search(_productSearch.Text, categoryValue == 0 ? null : categoryValue,
            (AvailabilityFilter)availabilityValue, (RecordStateFilter)stateValue).ToList();
        _productCount.Text = $"{_productGrid.Rows.Count} produit(s)";
        UpdateProductActions();
    }

    private void LoadCategories()
    {
        if (_categoryStateFilter.SelectedValue is null) return;
        var state = (RecordStateFilter)Convert.ToInt32(_categoryStateFilter.SelectedValue);
        _categoryGrid.DataSource = _categories.GetAll(_categorySearch.Text, state).ToList();
        UpdateCategoryActions();
    }

    private ProductCatalogItem? SelectedProduct => _productGrid.CurrentRow?.DataBoundItem as ProductCatalogItem;
    private CategoryCatalogItem? SelectedCategory => _categoryGrid.CurrentRow?.DataBoundItem as CategoryCatalogItem;

    private void UpdateProductActions()
    {
        var selected = SelectedProduct;
        _editProduct.Enabled = _canManage && selected is { Product.Actif: true };
        _archiveProduct.Enabled = _canManage && selected is { Product.Actif: true };
    }

    private void UpdateCategoryActions()
    {
        var selected = SelectedCategory;
        _editCategory.Enabled = _canManage && selected is { Active: true };
        _archiveCategory.Enabled = _canManage && selected is { Active: true };
    }

    private void OpenProductEditor(ProductCatalogItem? product)
    {
        if (!_canManage || _user is null) return;
        using var dialog = new ProductEditForm(_products, _categories, _user, product?.Product);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadCategoriesIntoFilter();
            LoadProducts();
        }
    }

    private void OpenSelectedProductEditor()
    {
        if (SelectedProduct is { Product.Actif: true } product) OpenProductEditor(product);
    }

    private void ArchiveSelectedProduct()
    {
        if (!_canManage || _user is null || SelectedProduct is not { Product.Actif: true } product) return;
        var result = MessageBox.Show(this,
            $"Archiver « {product.Name} » ({product.Code}) ? Le produit restera consultable et sa référence ne pourra pas être réutilisée.",
            "Confirmer l'archivage", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;
        try
        {
            _products.Archive(_user, product.Id);
            LoadProducts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Archivage impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenCategoryEditor(CategoryCatalogItem? category)
    {
        if (!_canManage || _user is null) return;
        using var dialog = new CategoryEditForm(_categories, _user, category);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            LoadCategoriesIntoFilter();
            LoadCategories();
            LoadProducts();
        }
    }

    private void OpenSelectedCategoryEditor()
    {
        if (SelectedCategory is { Active: true } category) OpenCategoryEditor(category);
    }

    private void ArchiveSelectedCategory()
    {
        if (!_canManage || _user is null || SelectedCategory is not { Active: true } category) return;
        var result = MessageBox.Show(this,
            $"Archiver la catégorie « {category.Name} » ? Ses produits restent dans le catalogue.",
            "Confirmer l'archivage", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (result != DialogResult.Yes) return;
        try
        {
            _categories.Archive(_user, category.Id);
            LoadCategoriesIntoFilter();
            LoadCategories();
            LoadProducts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Archivage impossible", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
