using System.ComponentModel;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;
using GestionMagasin.Data;
using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class SalesForm : Form
{
    private sealed record ProductChoice(int Id, string Code, string Name, int UnitPriceXaf, int Stock, int AlertThreshold)
    {
        public string Price => FormatMoney(UnitPriceXaf);
        public string Availability => Stock == 0 ? "En rupture" : Stock <= AlertThreshold ? "Stock faible" : "Disponible";
    }

    private sealed class CartLineView
    {
        public required int ProductId { get; init; }
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required int UnitPriceXaf { get; set; }
        public int Quantity { get; set; }
        public long SubtotalXaf => (long)UnitPriceXaf * Quantity;
        public string UnitPrice => FormatMoney(UnitPriceXaf);
        public string Subtotal => FormatMoney(SubtotalXaf);
    }

    private sealed record SaleHistoryRow(int Id, string Numero, DateTime Date, string Caissier,
        int TotalXaf, string Total, string Paiement, string Statut, string MotifAnnulation);
    private sealed record SellerFilter(int Id, string Name);
    private sealed record StatusFilter(string Value, string Label);

    private readonly DatabaseSession _session;
    private readonly AuthenticatedUser? _user;
    private readonly SalesService _sales;
    private readonly ProductService _products;
    private readonly string? _logoPath;
    private readonly BindingList<CartLineView> _cart = [];
    private readonly TextBox _productSearch = new();
    private readonly DataGridView _productGrid = new();
    private readonly DataGridView _cartGrid = new();
    private readonly BindingSource _cartBindingSource = new();
    private readonly NumericUpDown _quantityInput = new();
    private readonly ComboBox _paymentMethod = new();
    private readonly NumericUpDown _amountPaid = new();
    private readonly TextBox _paymentDescription = new();
    private readonly Label _amountPaidLabel = new();
    private readonly Label _totalLabel = new();
    private readonly Label _changeLabel = new();
    private readonly Button _checkoutButton = CatalogPalette.CreateButton("Valider la vente", CatalogPalette.Primary);
    private readonly Button _addToCartButton = CatalogPalette.CreateButton("Ajouter au panier", CatalogPalette.Primary);
    private readonly TextBox _historySearch = new();
    private readonly DateTimePicker _historyFrom = new();
    private readonly DateTimePicker _historyThrough = new();
    private readonly ComboBox _historySeller = new();
    private readonly ComboBox _historyStatusFilter = new();
    private readonly DataGridView _historyGrid = new();
    private readonly TextBox _saleDetails = new();
    private readonly Button _previewTicketButton = CatalogPalette.CreateButton("Aperçu ticket", CatalogPalette.Primary);
    private readonly Button _printTicketButton = CatalogPalette.CreateButton("Imprimer ticket", CatalogPalette.Primary);
    private readonly Button _cancelSaleButton = CatalogPalette.CreateButton("Annuler la vente", Color.FromArgb(185, 28, 28));
    private readonly Label _historyStatus = new();
    private int _cartQuantityBeforeEdit;
    private Guid? _validationToken;
    private int _checkoutInProgress;

    public SalesForm(DatabaseSession session, AuthenticatedUser? user, bool openTodayHistory = false)
    {
        _session = session;
        _user = user;
        var factory = new SqliteConnectionFactory(session.DatabasePath);
        _sales = new SalesService(factory);
        _products = new ProductService(factory);
        _logoPath = new AdministrationService(factory).GetStoreSettings().LogoPath;

        Text = "Ventes et caisse";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(940, 620);
        Size = new Size(1360, 820);
        WindowState = FormWindowState.Maximized;
        BackColor = CatalogPalette.Background;
        ForeColor = CatalogPalette.Text;
        Font = new Font("Segoe UI", 9.5f);
        Shown += (_, _) => DpiFormLayout.FitDialogToWorkingArea(this);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(20),
            BackColor = CatalogPalette.Background
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        var checkoutTab = new TabPage("Nouvelle vente") { BackColor = CatalogPalette.Background, Padding = new Padding(12) };
        var historyTab = new TabPage("Historique") { BackColor = CatalogPalette.Background, Padding = new Padding(12) };
        checkoutTab.Controls.Add(BuildCheckoutPage());
        historyTab.Controls.Add(BuildHistoryPage());
        tabs.TabPages.AddRange([checkoutTab, historyTab]);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == historyTab) LoadHistory(); };
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);

        _productSearch.TextChanged += (_, _) => LoadProducts();
        _productGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) AddSelectedProduct(); };
        _productGrid.SelectionChanged += (_, _) => UpdateAddAction();
        _cart.ListChanged += (_, _) =>
        {
            _validationToken = null;
            RefreshCart();
        };
        _cartGrid.CellBeginEdit += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _cartGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(CartLineView.Quantity))
                _cartQuantityBeforeEdit = _cart[e.RowIndex].Quantity;
        };
        _cartGrid.DataError += (_, e) => e.ThrowException = false;
        _cartGrid.CellEndEdit += (_, e) => UpdateCartQuantity(e.RowIndex);
        _cartGrid.CellContentClick += (_, e) => RemoveCartLine(e.RowIndex, e.ColumnIndex);
        _paymentMethod.SelectedIndexChanged += (_, _) =>
        {
            _validationToken = null;
            ApplyPaymentMethodDefaults();
        };
        _amountPaid.ValueChanged += (_, _) =>
        {
            _validationToken = null;
            UpdatePaymentSummary();
        };
        _paymentDescription.TextChanged += (_, _) =>
        {
            _validationToken = null;
            UpdatePaymentSummary();
        };
        _checkoutButton.Click += CheckoutButtonClicked;
        _historySearch.TextChanged += (_, _) => LoadHistory();
        _historyFrom.ValueChanged += (_, _) => LoadHistory();
        _historyThrough.ValueChanged += (_, _) => LoadHistory();
        _historySeller.SelectedValueChanged += (_, _) => LoadHistory();
        _historyStatusFilter.SelectedValueChanged += (_, _) => LoadHistory();
        _historyGrid.SelectionChanged += (_, _) => ShowSelectedSale();
        _previewTicketButton.Click += (_, _) => PreviewSelectedTicket();
        _printTicketButton.Click += (_, _) => PrintSelectedTicket();
        _cancelSaleButton.Click += (_, _) => CancelSelectedSale();

        if (user?.Role == UserRole.Vendeur)
        {
            _historySeller.SelectedValue = user.Id;
            _historySeller.Enabled = false;
        }
        if (openTodayHistory)
        {
            _historyFrom.Value = DateTime.Today;
            _historyThrough.Value = DateTime.Today;
            _historyStatusFilter.SelectedValue = "Validee";
            tabs.SelectedTab = historyTab;
        }

        LoadProducts();
        LoadHistory();
        ApplyPaymentMethodDefaults();
        UpdateAddAction();
    }

    private Control BuildHeader()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = CatalogPalette.Background };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.Controls.Add(new Label
        {
            Text = "Ventes et caisse", Dock = DockStyle.Fill, AutoSize = false,
            Font = new Font("Segoe UI", 21, FontStyle.Bold), ForeColor = CatalogPalette.Text,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = $"{_session.ModeLabel} · {_user?.FullName ?? "Consultation"}", Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(77, 89, 106), TextAlign = ContentAlignment.MiddleRight
        }, 1, 0);
        return panel;
    }

    private Control BuildCheckoutPage()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = CatalogPalette.Background
        };
        SplitContainerLayout.ConfigureWhenSized(split, preferredDistance: 650,
            requestedPanel1Minimum: 300, requestedPanel2Minimum: 350);

        var productPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 0, 10, 0) };
        productPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        productPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        productPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        productPanel.Controls.Add(SectionLabel("Produits actifs"), 0, 0);
        ConfigureSearch(_productSearch, "Rechercher par référence ou nom...");
        productPanel.Controls.Add(_productSearch, 0, 1);
        ConfigureGrid(_productGrid);
        AddGridColumn(_productGrid, "Référence", nameof(ProductChoice.Code), 110);
        AddGridColumn(_productGrid, "Produit", nameof(ProductChoice.Name), 180, fill: true);
        AddGridColumn(_productGrid, "Prix unitaire", nameof(ProductChoice.Price), 125);
        AddGridColumn(_productGrid, "Stock", nameof(ProductChoice.Stock), 70);
        AddGridColumn(_productGrid, "Disponibilité", nameof(ProductChoice.Availability), 120);
        productPanel.Controls.Add(_productGrid, 0, 2);
        split.Panel1.Controls.Add(productPanel);

        var cartPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10, 0, 0, 0) };
        cartPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        cartPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cartPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        cartPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
        cartPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        cartPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        cartPanel.Controls.Add(SectionLabel("Panier"), 0, 0);
        ConfigureGrid(_cartGrid);
        AddGridColumn(_cartGrid, "Produit", nameof(CartLineView.Name), 120, fill: true);
        AddGridColumn(_cartGrid, "Prix", nameof(CartLineView.UnitPrice), 85);
        AddGridColumn(_cartGrid, "Qté", nameof(CartLineView.Quantity), 55);
        AddGridColumn(_cartGrid, "Sous-total", nameof(CartLineView.Subtotal), 105);
        _cartGrid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = "Remove", HeaderText = "", Text = "Retirer", UseColumnTextForButtonValue = true,
            Width = 72, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _cartBindingSource.DataSource = _cart;
        _cartGrid.DataSource = _cartBindingSource;
        cartPanel.Controls.Add(_cartGrid, 0, 1);

        var addLine = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        addLine.Controls.Add(new Label { Text = "Quantité", AutoSize = true, Margin = new Padding(0, 10, 8, 0) });
        ConfigureQuantity(_quantityInput, 1);
        _quantityInput.Width = 92;
        addLine.Controls.Add(_quantityInput);
        _addToCartButton.Width = 180;
        _addToCartButton.Click += AddToCartClicked;
        addLine.Controls.Add(_addToCartButton);
        cartPanel.Controls.Add(addLine, 0, 2);

        var payment = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(0, 3, 0, 0) };
        payment.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        payment.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        payment.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        payment.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        payment.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        payment.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        payment.Controls.Add(FieldLabel("Paiement"), 0, 0);
        _paymentMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        _paymentMethod.Dock = DockStyle.Fill;
        _paymentMethod.DataSource = Enum.GetValues<SalePaymentMethod>().ToList();
        _paymentMethod.Format += (_, e) =>
        {
            if (e.ListItem is SalePaymentMethod method) e.Value = SalesService.PaymentMethodLabel(method);
        };
        payment.Controls.Add(_paymentMethod, 1, 0);
        _amountPaidLabel.Text = "Montant remis (FCFA)";
        _amountPaidLabel.Dock = DockStyle.Fill;
        _amountPaidLabel.TextAlign = ContentAlignment.MiddleLeft;
        _amountPaidLabel.ForeColor = Color.FromArgb(77, 89, 106);
        payment.Controls.Add(_amountPaidLabel, 0, 1);
        ConfigureQuantity(_amountPaid, 0);
        _amountPaid.Maximum = int.MaxValue;
        _amountPaid.Dock = DockStyle.Fill;
        payment.Controls.Add(_amountPaid, 1, 1);
        payment.Controls.Add(FieldLabel("Référence / description"), 0, 2);
        _paymentDescription.Dock = DockStyle.Fill;
        _paymentDescription.MaxLength = 300;
        _paymentDescription.PlaceholderText = "Référence Mobile Money ou détail carte";
        _paymentDescription.BorderStyle = BorderStyle.FixedSingle;
        payment.Controls.Add(_paymentDescription, 1, 2);
        payment.Controls.Add(FieldLabel("Monnaie à rendre"), 0, 3);
        _changeLabel.Text = FormatMoney(0);
        _changeLabel.Dock = DockStyle.Fill;
        _changeLabel.TextAlign = ContentAlignment.MiddleRight;
        _changeLabel.Font = new Font(Font, FontStyle.Bold);
        payment.Controls.Add(_changeLabel, 1, 3);
        cartPanel.Controls.Add(payment, 0, 3);

        _totalLabel.Text = "Total : 0 FCFA";
        _totalLabel.Font = new Font("Segoe UI", 17, FontStyle.Bold);
        _totalLabel.TextAlign = ContentAlignment.MiddleRight;
        _totalLabel.Dock = DockStyle.Fill;
        cartPanel.Controls.Add(_totalLabel, 0, 4);
        _checkoutButton.Dock = DockStyle.Fill;
        _checkoutButton.Enabled = false;
        cartPanel.Controls.Add(_checkoutButton, 0, 5);
        split.Panel2.Controls.Add(cartPanel);
        return split;
    }

    private Control BuildHistoryPage()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 6, 0, 2) };
        ConfigureSearch(_historySearch, "Rechercher un numéro de vente");
        _historySearch.Width = 210;
        filters.Controls.Add(FilterGroup("Recherche", _historySearch, 240));
        _historyFrom.Format = DateTimePickerFormat.Short;
        _historyFrom.Value = DateTime.Today.AddDays(-30);
        filters.Controls.Add(FilterGroup("Depuis", _historyFrom, 140));
        _historyThrough.Format = DateTimePickerFormat.Short;
        filters.Controls.Add(FilterGroup("Jusqu'au", _historyThrough, 140));
        ConfigureFilterCombo(_historySeller);
        var sellerChoices = new List<SellerFilter> { new(0, "Tous les vendeurs") };
        sellerChoices.AddRange(_sales.GetSellers().Select(seller => new SellerFilter(seller.Id, seller.Name)));
        _historySeller.DataSource = sellerChoices;
        _historySeller.DisplayMember = nameof(SellerFilter.Name);
        _historySeller.ValueMember = nameof(SellerFilter.Id);
        filters.Controls.Add(FilterGroup("Vendeur", _historySeller, 205));
        ConfigureFilterCombo(_historyStatusFilter);
        _historyStatusFilter.DataSource = new List<StatusFilter>
        {
            new("Tous", "Tous les statuts"),
            new("Validee", "Validées"),
            new("Annulee", "Annulées")
        };
        _historyStatusFilter.DisplayMember = nameof(StatusFilter.Label);
        _historyStatusFilter.ValueMember = nameof(StatusFilter.Value);
        filters.Controls.Add(FilterGroup("Statut", _historyStatusFilter, 155));
        root.Controls.Add(filters, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        SplitContainerLayout.ConfigureWhenSized(split, preferredDistance: 300,
            requestedPanel1Minimum: 130, requestedPanel2Minimum: 120);
        ConfigureGrid(_historyGrid);
        AddGridColumn(_historyGrid, "N° vente", nameof(SaleHistoryRow.Numero), 240);
        AddGridColumn(_historyGrid, "Date", nameof(SaleHistoryRow.Date), 155);
        AddGridColumn(_historyGrid, "Caissier", nameof(SaleHistoryRow.Caissier), 180, fill: true);
        AddGridColumn(_historyGrid, "Total", nameof(SaleHistoryRow.Total), 130);
        AddGridColumn(_historyGrid, "Paiement", nameof(SaleHistoryRow.Paiement), 130);
        AddGridColumn(_historyGrid, "État", nameof(SaleHistoryRow.Statut), 100);
        split.Panel1.Controls.Add(_historyGrid);

        var detailPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 8, 0, 0) };
        detailPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        detailPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        detailPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        detailPanel.Controls.Add(SectionLabel("Détail de la vente sélectionnée"), 0, 0);
        _saleDetails.ReadOnly = true;
        _saleDetails.Multiline = true;
        _saleDetails.ScrollBars = ScrollBars.Vertical;
        _saleDetails.BorderStyle = BorderStyle.FixedSingle;
        _saleDetails.BackColor = Color.White;
        _saleDetails.Dock = DockStyle.Fill;
        detailPanel.Controls.Add(_saleDetails, 0, 1);
        var actionPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10, 0, 0, 0) };
        actionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        actionPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _previewTicketButton.Dock = DockStyle.Fill;
        _previewTicketButton.Enabled = false;
        actionPanel.Controls.Add(_previewTicketButton, 0, 0);
        _printTicketButton.Dock = DockStyle.Fill;
        _printTicketButton.Enabled = false;
        actionPanel.Controls.Add(_printTicketButton, 0, 1);
        _cancelSaleButton.Dock = DockStyle.Top;
        _cancelSaleButton.Enabled = false;
        _cancelSaleButton.Visible = _user?.Role == UserRole.Administrateur && _session.Mode == DatabaseMode.Reelle;
        actionPanel.Controls.Add(_cancelSaleButton, 0, 2);
        detailPanel.Controls.Add(actionPanel, 1, 0);
        detailPanel.SetRowSpan(actionPanel, 2);
        split.Panel2.Controls.Add(detailPanel);
        root.Controls.Add(split, 0, 1);

        _historyStatus.ForeColor = Color.FromArgb(77, 89, 106);
        _historyStatus.Dock = DockStyle.Fill;
        root.Controls.Add(_historyStatus, 0, 2);
        return root;
    }

    private void LoadProducts()
    {
        var items = _products.Search(_productSearch.Text, state: RecordStateFilter.Actif)
            .Select(item => new ProductChoice(item.Id, item.Code, item.Name, item.SalePrice, item.Stock, item.AlertThreshold))
            .ToList();
        _productGrid.DataSource = items;
        var firstSellableRow = _productGrid.Rows.Cast<DataGridViewRow>()
            .FirstOrDefault(row => row.DataBoundItem is ProductChoice { Stock: > 0 });
        if (firstSellableRow is not null && _productGrid.CurrentRow is null && _productGrid.Columns.Count > 0)
            _productGrid.CurrentCell = firstSellableRow.Cells[0];
        if (_productGrid.Columns[nameof(ProductChoice.Stock)] is { } stockColumn)
            stockColumn.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        UpdateAddAction();
    }

    private void AddToCartClicked(object? sender, EventArgs e) => AddSelectedProduct();

    private void AddSelectedProduct()
    {
        var product = _productGrid.CurrentRow?.DataBoundItem as ProductChoice
            ?? _productGrid.SelectedRows.Cast<DataGridViewRow>()
                .Select(row => row.DataBoundItem as ProductChoice)
                .FirstOrDefault(choice => choice is not null);
        if (product is null) return;
        var requested = decimal.ToInt32(_quantityInput.Value);
        if (product.Stock <= 0)
        {
            MessageBox.Show(this, "Ce produit est en rupture de stock.", "Ajout impossible", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var existing = _cart.SingleOrDefault(line => line.ProductId == product.Id);
        if (existing is not null)
        {
            var newQuantity = checked(existing.Quantity + requested);
            if (newQuantity > product.Stock)
            {
                MessageBox.Show(this, $"Stock disponible : {product.Stock}.", "Quantité insuffisante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            existing.Quantity = newQuantity;
        }
        else
        {
            if (requested > product.Stock)
            {
                MessageBox.Show(this, $"Stock disponible : {product.Stock}.", "Quantité insuffisante", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _cart.Add(new CartLineView { ProductId = product.Id, Code = product.Code, Name = product.Name, UnitPriceXaf = product.UnitPriceXaf, Quantity = requested });
        }
        _validationToken = null;
        RefreshCart();
    }

    private void UpdateCartQuantity(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _cart.Count) return;
        var line = _cart[rowIndex];
        if (!int.TryParse(Convert.ToString(_cartGrid.Rows[rowIndex].Cells[nameof(CartLineView.Quantity)].Value, CultureInfo.InvariantCulture), out var quantity) || quantity <= 0)
        {
            MessageBox.Show(this, "La quantité doit être un entier positif.", "Quantité invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            line.Quantity = _cartQuantityBeforeEdit;
            RefreshCart();
            return;
        }
        var currentProduct = _products.GetById(line.ProductId);
        if (currentProduct is null || !currentProduct.Actif || quantity > currentProduct.QuantiteStock)
        {
            MessageBox.Show(this, "La quantité dépasse le stock disponible ou le produit n'est plus vendable.", "Quantité invalide", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            line.Quantity = _cartQuantityBeforeEdit;
            RefreshCart();
            return;
        }
        line.Quantity = quantity;
        _validationToken = null;
        RefreshCart();
    }

    private void RemoveCartLine(int rowIndex, int columnIndex)
    {
        var removeColumnIndex = _cartGrid.Columns["Remove"]?.Index ?? -1;
        if (rowIndex >= 0 && columnIndex == removeColumnIndex && rowIndex < _cart.Count)
            _cart.RemoveAt(rowIndex);
    }

    private void RefreshCart()
    {
        _cartBindingSource.ResetBindings(metadataChanged: false);
        _cartGrid.Refresh();
        var total = _cart.Aggregate(0L, (sum, line) => checked(sum + line.SubtotalXaf));
        _totalLabel.Text = $"Total : {FormatMoney(total)} FCFA";
        if (_paymentMethod.SelectedItem is not SalePaymentMethod.Especes)
            _amountPaid.Value = total <= int.MaxValue ? total : 0;
        UpdatePaymentSummary();
    }

    private void ApplyPaymentMethodDefaults()
    {
        var isCash = _paymentMethod.SelectedItem is SalePaymentMethod.Especes;
        _amountPaid.Enabled = isCash;
        _paymentDescription.Enabled = !isCash;
        _amountPaidLabel.Text = isCash ? "Montant remis (FCFA)" : "Montant confirmé (FCFA)";
        _paymentDescription.PlaceholderText = _paymentMethod.SelectedItem is SalePaymentMethod.Carte
            ? "Description obligatoire (saisie manuelle)"
            : "Référence Mobile Money (facultative)";
        if (isCash)
        {
            _amountPaid.Value = 0;
            _paymentDescription.Clear();
        }
        else
        {
            var total = _cart.Aggregate(0L, (sum, line) => checked(sum + line.SubtotalXaf));
            _amountPaid.Value = total <= int.MaxValue ? total : 0;
        }
        UpdatePaymentSummary();
    }

    private void UpdatePaymentSummary()
    {
        var total = _cart.Aggregate(0L, (sum, line) => checked(sum + line.SubtotalXaf));
        var isCash = _paymentMethod.SelectedItem is SalePaymentMethod.Especes;
        var cashShortfall = Math.Max(0, total - (long)_amountPaid.Value);
        var change = isCash ? Math.Max(0, (long)_amountPaid.Value - total) : 0;
        var descriptionIsValid = _paymentMethod.SelectedItem is not SalePaymentMethod.Carte || !string.IsNullOrWhiteSpace(_paymentDescription.Text);
        var paymentIsValid = isCash ? cashShortfall == 0 : (long)_amountPaid.Value == total && descriptionIsValid;
        _changeLabel.Text = isCash && cashShortfall > 0
            ? $"Manque {FormatMoney(cashShortfall)} FCFA"
            : $"{FormatMoney(change)} FCFA";
        if (!isCash && !descriptionIsValid)
            _changeLabel.Text = "Description carte obligatoire";
        else if (!isCash && (long)_amountPaid.Value != total && total <= int.MaxValue)
            _changeLabel.Text = "Paiement exact requis";
        else if (total > int.MaxValue)
            _changeLabel.Text = "Total supérieur au maximum accepté";
        _changeLabel.ForeColor = paymentIsValid && change > 0
            ? CatalogPalette.AvailableText
            : paymentIsValid ? CatalogPalette.Text : CatalogPalette.OutOfStockText;
        _checkoutButton.Enabled = _user is not null && _cart.Count > 0 && total > 0 && total <= int.MaxValue &&
            _paymentMethod.SelectedItem is SalePaymentMethod;
    }

    private void CheckoutButtonClicked(object? sender, EventArgs e) => Checkout();

    private void Checkout()
    {
        if (_user is null || _cart.Count == 0 || _paymentMethod.SelectedItem is not SalePaymentMethod method) return;
        if (Interlocked.Exchange(ref _checkoutInProgress, 1) != 0) return;
        _checkoutButton.Enabled = false;
        try
        {
            _validationToken ??= Guid.NewGuid();
            var requests = _cart.Select(line => new CartLineRequest(line.ProductId, line.Quantity, line.UnitPriceXaf)).ToList();
            var receipt = _sales.Checkout(_user, requests, method, decimal.ToInt32(_amountPaid.Value),
                _validationToken.Value, string.IsNullOrWhiteSpace(_paymentDescription.Text) ? null : _paymentDescription.Text.Trim());
            var message = BuildReceiptText(receipt);
            _cart.Clear();
            _validationToken = null;
            _amountPaid.Value = 0;
            LoadProducts();
            LoadHistory();
            MessageBox.Show(this, message, "Vente validée", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (SalePriceChangedException ex)
        {
            var prices = ex.RefreshedLines.ToDictionary(line => line.ProductId, line => line.UnitPriceXaf);
            foreach (var line in _cart)
                if (prices.TryGetValue(line.ProductId, out var refreshedPrice)) line.UnitPriceXaf = refreshedPrice;
            _validationToken = null;
            RefreshCart();
            LoadProducts();
            MessageBox.Show(this,
                $"Un prix du catalogue a changé. Le panier affiche maintenant les prix à jour. Nouveau total : {FormatMoney(ex.RefreshedTotalXaf)} FCFA. Vérifiez-le puis cliquez à nouveau sur « Valider la vente ».",
                "Prix actualisés — nouvelle validation requise", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Vente impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LoadProducts();
        }
        finally
        {
            Interlocked.Exchange(ref _checkoutInProgress, 0);
            UpdatePaymentSummary();
        }
    }

    private void LoadHistory()
    {
        if (!IsHandleCreated || _historySeller.SelectedValue is null || _historyStatusFilter.SelectedValue is null) return;
        try
        {
            var fromUtc = DateTime.SpecifyKind(_historyFrom.Value.Date, DateTimeKind.Local).ToUniversalTime();
            var throughUtc = DateTime.SpecifyKind(_historyThrough.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
            var sellerValue = _user?.Role == UserRole.Vendeur
                ? _user.Id
                : Convert.ToInt32(_historySeller.SelectedValue, CultureInfo.InvariantCulture);
            var status = Convert.ToString(_historyStatusFilter.SelectedValue, CultureInfo.InvariantCulture) ?? "Tous";
            var entries = _sales.GetHistory(fromUtc, throughUtc, _historySearch.Text,
                sellerValue == 0 ? null : sellerValue, status);
            _historyGrid.DataSource = entries.Select(sale => new SaleHistoryRow(sale.Id, sale.Number, sale.SoldAtUtc.ToLocalTime(),
                sale.CashierName, sale.TotalXaf, FormatMoney(sale.TotalXaf), sale.PaymentMethod,
                sale.Status == "Annulee" ? "Annulée" : "Validée", sale.CancellationReason ?? string.Empty)).ToList();
            _historyStatus.Text = $"{entries.Count} vente(s) · montant des ventes validées : {FormatMoney(entries.Where(entry => entry.Status == "Validee").Sum(entry => (long)entry.TotalXaf))} FCFA";
            ShowSelectedSale();
        }
        catch (Exception ex)
        {
            _historyStatus.Text = $"Impossible de charger l'historique : {ex.Message}";
        }
    }

    private void ShowSelectedSale()
    {
        if (_historyGrid.CurrentRow?.DataBoundItem is not SaleHistoryRow row)
        {
            _saleDetails.Clear();
            _previewTicketButton.Enabled = false;
            _printTicketButton.Enabled = false;
            _cancelSaleButton.Enabled = false;
            return;
        }
        var detail = _sales.GetById(row.Id);
        if (detail is null) return;
        var text = new StringBuilder();
        text.AppendLine($"Vente : {detail.Sale.Number}");
        text.AppendLine($"Date : {detail.Sale.SoldAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        text.AppendLine($"Caissier : {detail.Sale.CashierName}");
        text.AppendLine($"Paiement : {detail.Sale.PaymentMethod}");
        if (detail.Sale.PaymentDescription is not null)
            text.AppendLine($"Détail paiement : {detail.Sale.PaymentDescription}");
        text.AppendLine($"Total : {FormatMoney(detail.Sale.TotalXaf)} FCFA");
        text.AppendLine($"Payé : {FormatMoney(detail.Sale.AmountPaidXaf)} FCFA");
        text.AppendLine($"Monnaie : {FormatMoney(detail.Sale.ChangeXaf)} FCFA");
        text.AppendLine($"État : {(detail.Sale.Status == "Annulee" ? "Annulée" : "Validée")}");
        text.AppendLine();
        text.AppendLine("Articles :");
        foreach (var line in detail.Lines)
            text.AppendLine($"• {line.ProductName} ({line.ProductCode}) — {line.Quantity} × {FormatMoney(line.UnitPriceXaf)} = {FormatMoney(line.SubtotalXaf)} FCFA");
        if (detail.Sale.CancellationReason is not null)
        {
            text.AppendLine();
            text.AppendLine($"Motif d'annulation : {detail.Sale.CancellationReason}");
            text.AppendLine($"Annulée par : {detail.Sale.CancelledBy} le {detail.Sale.CancelledAtUtc?.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        }
        _saleDetails.Text = text.ToString();
        _previewTicketButton.Enabled = true;
        _printTicketButton.Enabled = true;
        _cancelSaleButton.Enabled = _cancelSaleButton.Visible && detail.Sale.Status == "Validee";
    }

    private void PreviewSelectedTicket()
    {
        var detail = GetSelectedSaleDetail();
        if (detail is null) return;
        try
        {
            using var document = CreateTicketDocument(detail, _logoPath);
            using var preview = new PrintPreviewDialog
            {
                Document = document,
                WindowState = FormWindowState.Maximized,
                Text = $"Aperçu du ticket — {detail.Sale.Number}"
            };
            preview.ShowDialog(this);
        }
        catch (Exception ex)
        {
            ShowPrintFailure(ex);
        }
    }

    private void PrintSelectedTicket()
    {
        var detail = GetSelectedSaleDetail();
        if (detail is null) return;
        try
        {
            using var document = CreateTicketDocument(detail, _logoPath);
            using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            document.Print();
        }
        catch (Exception ex)
        {
            ShowPrintFailure(ex);
        }
    }

    private SaleDetail? GetSelectedSaleDetail()
    {
        if (_historyGrid.CurrentRow?.DataBoundItem is not SaleHistoryRow row) return null;
        return _sales.GetById(row.Id);
    }

    private static PrintDocument CreateTicketDocument(SaleDetail detail, string? logoPath)
    {
        var document = new PrintDocument
        {
            DocumentName = $"Ticket {detail.Sale.Number}",
            OriginAtMargins = true
        };
        document.DefaultPageSettings.Margins = new Margins(45, 45, 40, 45);
        var lineIndex = 0;
        document.BeginPrint += (_, _) => lineIndex = 0;
        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics;
            if (graphics is null) throw new InvalidOperationException("Le pilote d'impression n'a pas fourni de surface graphique.");
            using var titleFont = new Font("Segoe UI", 15, FontStyle.Bold);
            using var boldFont = new Font("Segoe UI", 9, FontStyle.Bold);
            using var regularFont = new Font("Segoe UI", 9);
            using var titleBrush = new SolidBrush(Color.FromArgb(31, 41, 55));
            using var textBrush = new SolidBrush(Color.Black);
            using var mutedBrush = new SolidBrush(Color.FromArgb(90, 90, 90));
            using var logo = BrandingAssets.LoadLogo(logoPath);
            var left = e.MarginBounds.Left;
            var width = e.MarginBounds.Width;
            var y = (float)e.MarginBounds.Top;
            var bottom = (float)e.MarginBounds.Bottom;

            if (lineIndex == 0)
            {
                if (logo is not null)
                {
                    graphics.DrawImage(logo, left, y, 42, 42);
                    graphics.DrawString("Gestion-Magasin", titleFont, titleBrush, left + 52, y + 7);
                    y += 47;
                }
                else
                {
                    graphics.DrawString("Gestion-Magasin", titleFont, titleBrush, left, y);
                    y += 32;
                }
                graphics.DrawString("TICKET DE VENTE", boldFont, textBrush, left, y);
                y += 18;
                graphics.DrawString($"N° {detail.Sale.Number}", regularFont, textBrush, left, y);
                y += 17;
                graphics.DrawString($"Date : {detail.Sale.SoldAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}", regularFont, textBrush, left, y);
                y += 17;
                graphics.DrawString($"Vendeur : {detail.Sale.CashierName}", regularFont, textBrush, left, y);
                y += 17;
                graphics.DrawString($"Paiement : {detail.Sale.PaymentMethod}", regularFont, textBrush, left, y);
                y += 17;
                if (detail.Sale.PaymentDescription is not null)
                {
                    graphics.DrawString($"Détail : {detail.Sale.PaymentDescription}", regularFont, textBrush,
                        new RectangleF(left, y, width, 40));
                    y += 34;
                }
                graphics.DrawLine(Pens.DimGray, left, y, left + width, y);
                y += 9;
            }
            else
            {
                graphics.DrawString($"Ticket {detail.Sale.Number} (suite)", boldFont, textBrush, left, y);
                y += 22;
            }

            while (lineIndex < detail.Lines.Count)
            {
                var line = detail.Lines[lineIndex];
                var itemText = $"{line.ProductName} ({line.ProductCode}) — {line.Quantity} × {FormatMoney(line.UnitPriceXaf)} = {FormatMoney(line.SubtotalXaf)} FCFA";
                var measured = graphics.MeasureString(itemText, regularFont, width);
                if (y + measured.Height + 4 > bottom)
                {
                    e.HasMorePages = true;
                    return;
                }
                graphics.DrawString(itemText, regularFont, textBrush, new RectangleF(left, y, width, measured.Height + 2));
                y += measured.Height + 4;
                lineIndex++;
            }

            var footerHeight = 90f;
            if (y + footerHeight > bottom)
            {
                e.HasMorePages = true;
                return;
            }
            y += 8;
            graphics.DrawLine(Pens.DimGray, left, y, left + width, y);
            y += 10;
            graphics.DrawString($"TOTAL : {FormatMoney(detail.Sale.TotalXaf)} FCFA", boldFont, textBrush, left, y);
            y += 18;
            graphics.DrawString($"Payé : {FormatMoney(detail.Sale.AmountPaidXaf)} FCFA", regularFont, textBrush, left, y);
            y += 17;
            graphics.DrawString($"Monnaie rendue : {FormatMoney(detail.Sale.ChangeXaf)} FCFA", regularFont, textBrush, left, y);
            y += 22;
            graphics.DrawString(detail.Sale.Status == "Annulee" ? "VENTE ANNULÉE" : "Merci de votre visite", boldFont, mutedBrush, left, y);
            e.HasMorePages = false;
        };
        return document;
    }

    private void ShowPrintFailure(Exception exception) => MessageBox.Show(this,
        $"L'aperçu ou l'impression a échoué : {exception.Message}\n\nLa vente reste enregistrée. Vous pouvez réessayer depuis le détail de la vente.",
        "Impression indisponible", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void CancelSelectedSale()
    {
        if (_user?.Role != UserRole.Administrateur || _session.Mode != DatabaseMode.Reelle ||
            _historyGrid.CurrentRow?.DataBoundItem is not SaleHistoryRow selected) return;
        using var prompt = new CancellationReasonForm();
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _sales.Cancel(_user, selected.Id, prompt.Reason);
            LoadHistory();
            MessageBox.Show(this, "La vente a été annulée et les stocks ont été compensés.", "Annulation enregistrée", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Annulation impossible", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UpdateAddAction()
    {
        var canEditCart = _user is not null || _session.Mode == DatabaseMode.Demonstration;
        _addToCartButton.Enabled = canEditCart && _productGrid.CurrentRow?.DataBoundItem is ProductChoice { Stock: > 0 };
    }

    private static string BuildReceiptText(SaleReceipt receipt)
    {
        var text = new StringBuilder();
        text.AppendLine($"Vente : {receipt.Number}");
        text.AppendLine($"Date : {receipt.SoldAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
        text.AppendLine($"Caissier : {receipt.CashierName}");
        text.AppendLine();
        foreach (var line in receipt.Lines)
            text.AppendLine($"{line.ProductName} · {line.Quantity} × {FormatMoney(line.UnitPriceXaf)} = {FormatMoney(line.SubtotalXaf)} FCFA");
        text.AppendLine();
        text.AppendLine($"Total : {FormatMoney(receipt.TotalXaf)} FCFA");
        text.AppendLine($"Payé ({SalesService.PaymentMethodLabel(receipt.PaymentMethod)}) : {FormatMoney(receipt.AmountPaidXaf)} FCFA");
        if (receipt.PaymentDescription is not null) text.AppendLine($"Détail paiement : {receipt.PaymentDescription}");
        if (receipt.ChangeXaf > 0) text.AppendLine($"Monnaie rendue : {FormatMoney(receipt.ChangeXaf)} FCFA");
        return text.ToString();
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = CatalogPalette.Text
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(77, 89, 106)
    };

    private static void ConfigureSearch(TextBox input, string placeholder)
    {
        input.Dock = DockStyle.Fill;
        input.PlaceholderText = placeholder;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Font = new Font("Segoe UI", 10);
    }

    private static void ConfigureFilterCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Font = new Font("Segoe UI", 9.5f);
        combo.FlatStyle = FlatStyle.Flat;
    }

    private static void ConfigureQuantity(NumericUpDown input, int initial)
    {
        input.Minimum = 0;
        input.Maximum = 1_000_000;
        input.Value = initial;
        input.DecimalPlaces = 0;
        input.ThousandsSeparator = true;
        input.TextAlign = HorizontalAlignment.Right;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = false;
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
        grid.ColumnHeadersHeight = 40;
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

    private static void AddGridColumn(DataGridView grid, string heading, string property, int width, bool fill = false)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = heading, DataPropertyName = property, Width = width,
            MinimumWidth = Math.Min(70, width), SortMode = DataGridViewColumnSortMode.Automatic,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            ReadOnly = grid != null && property != nameof(CartLineView.Quantity)
        });
    }

    private static Control FilterGroup(string label, Control editor, int width)
    {
        var group = new TableLayoutPanel { Width = width, Height = 62, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 10, 0) };
        group.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(77, 89, 106), Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) }, 0, 0);
        editor.Dock = DockStyle.Fill;
        group.Controls.Add(editor, 0, 1);
        return group;
    }

    private static string FormatMoney(long amount) => amount.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

    private sealed class CancellationReasonForm : Form
    {
        private readonly TextBox _reason = new();
        public string Reason => _reason.Text;

        public CancellationReasonForm()
        {
            Text = "Motif d'annulation";
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(500, 230);
            MinimumSize = new Size(390, 200);
            AutoScroll = true;
            BackColor = CatalogPalette.Background;
            Font = new Font("Segoe UI", 9.5f);
            Shown += (_, _) => DpiFormLayout.FitDialogToWorkingArea(this);
            var prompt = new Label { Text = "Indiquez le motif obligatoire de l'annulation :", Location = new Point(20, 20), Size = new Size(440, 28) };
            _reason.Multiline = true;
            _reason.Location = new Point(20, 54);
            _reason.Size = new Size(440, 90);
            _reason.MaxLength = 500;
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Location = new Point(20, 158), Size = new Size(440, 42), Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom };
            var cancel = CatalogPalette.CreateButton("Retour", Color.FromArgb(107, 114, 128));
            cancel.DialogResult = DialogResult.Cancel;
            var confirm = CatalogPalette.CreateButton("Confirmer l'annulation", Color.FromArgb(185, 28, 28));
            buttons.Controls.AddRange([cancel, confirm]);
            Controls.AddRange([prompt, _reason, buttons]);
            CancelButton = cancel;
            AcceptButton = confirm;
            confirm.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_reason.Text))
                {
                    MessageBox.Show(this, "Le motif est obligatoire.", "Motif requis", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
                else
                {
                    DialogResult = DialogResult.OK;
                    Close();
                }
            };
        }
    }
}
