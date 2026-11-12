using GestionMagasin.Data;
using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class AdministrationForm : Form
{
    private readonly AuthenticatedUser _administrator;
    private readonly UserService _users;
    private readonly AdministrationService _settings;
    private readonly AuditLogService _audit;
    private readonly DataGridView _userGrid = new();
    private readonly DataGridView _auditGrid = new();
    private readonly TextBox _storeName = new();
    private readonly ComboBox _timeZone = new();

    public AdministrationForm(DatabaseSession session, AuthenticatedUser administrator)
    {
        AuthenticationService.RequireRole(administrator, UserRole.Administrateur);
        _administrator = administrator;
        var factory = new SqliteConnectionFactory(session.DatabasePath);
        _users = new UserService(factory, new PasswordHashingService());
        _settings = new AdministrationService(factory);
        _audit = new AuditLogService(factory);

        Text = $"Administration — {session.ModeLabel}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 600);
        Size = new Size(1280, 820);
        WindowState = FormWindowState.Maximized;
        BackColor = CatalogPalette.Background;
        ForeColor = CatalogPalette.Text;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 2, BackColor = CatalogPalette.Background };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label { Text = "Panneau d’administration", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 22, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        var usersPage = new TabPage("Utilisateurs") { BackColor = CatalogPalette.Background, Padding = new Padding(12) };
        var settingsPage = new TabPage("Paramètres magasin") { BackColor = CatalogPalette.Background, Padding = new Padding(20) };
        var auditPage = new TabPage("Journal d’audit") { BackColor = CatalogPalette.Background, Padding = new Padding(12) };
        usersPage.Controls.Add(BuildUsersPage());
        settingsPage.Controls.Add(BuildSettingsPage());
        auditPage.Controls.Add(BuildAuditPage());
        tabs.TabPages.AddRange([usersPage, settingsPage, auditPage]);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == auditPage) LoadAudit(); };
        root.Controls.Add(tabs, 0, 1);
        Controls.Add(root);
        LoadUsers();
        LoadSettings();
        LoadAudit();
    }

    private Control BuildUsersPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = CatalogPalette.Background };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = CatalogPalette.Background };
        var create = CatalogPalette.CreateButton("Créer un compte", CatalogPalette.Primary);
        create.Size = new Size(160, 36);
        create.Click += (_, _) => EditUser(null);
        var edit = CatalogPalette.CreateButton("Modifier", CatalogPalette.Primary);
        edit.Size = new Size(120, 36);
        edit.Click += (_, _) => EditUser(SelectedUser());
        var toggle = CatalogPalette.CreateButton("Activer / désactiver", Color.FromArgb(75, 92, 116));
        toggle.Size = new Size(180, 36);
        toggle.Click += (_, _) => ToggleSelectedUser();
        var refresh = new Button { Text = "Actualiser", Size = new Size(120, 36) };
        refresh.Click += (_, _) => LoadUsers();
        actions.Controls.AddRange([create, edit, toggle, refresh]);
        layout.Controls.Add(actions, 0, 0);
        ConfigureGrid(_userGrid);
        _userGrid.AutoGenerateColumns = false;
        AddGridColumn(_userGrid, "Username", "Identifiant", 18);
        AddGridColumn(_userGrid, "FullName", "Nom complet", 28);
        AddGridColumn(_userGrid, "Role", "Rôle", 16);
        AddGridColumn(_userGrid, "Active", "Actif", 12, "Oui", "Non");
        AddGridColumn(_userGrid, "CreatedAtUtc", "Créé le (UTC)", 26, "", "");
        layout.Controls.Add(_userGrid, 0, 1);
        _userGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditUser(SelectedUser()); };
        return layout;
    }

    private Control BuildSettingsPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 5, Width = 700, Height = 300, BackColor = CatalogPalette.Background };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddSettingField(layout, 0, "Nom commercial", _storeName);
        var currency = new TextBox { Text = "FCFA XAF", ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.White };
        AddSettingField(layout, 1, "Devise", currency);
        _timeZone.DropDownStyle = ComboBoxStyle.DropDownList;
        _timeZone.DisplayMember = nameof(TimeZoneInfo.Id);
        _timeZone.DataSource = TimeZoneInfo.GetSystemTimeZones().OrderBy(zone => zone.Id).ToList();
        AddSettingField(layout, 2, "Fuseau horaire", _timeZone);
        var save = CatalogPalette.CreateButton("Enregistrer les paramètres", CatalogPalette.Primary);
        save.Size = new Size(230, 40);
        save.Click += (_, _) => SaveSettings();
        layout.Controls.Add(save, 1, 3);
        var note = new Label { Text = "Les rapports et dates métier utilisent le fuseau horaire configuré pour le magasin.", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(92, 103, 119), TextAlign = ContentAlignment.TopLeft };
        layout.Controls.Add(note, 0, 4);
        layout.SetColumnSpan(note, 2);
        return layout;
    }

    private Control BuildAuditPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = CatalogPalette.Background };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = CatalogPalette.Background };
        var refresh = new Button { Text = "Actualiser le journal", Size = new Size(165, 36) };
        refresh.Click += (_, _) => LoadAudit();
        var backup = CatalogPalette.CreateButton("Créer une sauvegarde", CatalogPalette.Primary);
        backup.Size = new Size(180, 36);
        backup.Click += (_, _) => CreateBackup();
        actions.Controls.AddRange([refresh, backup]);
        layout.Controls.Add(actions, 0, 0);
        ConfigureGrid(_auditGrid);
        _auditGrid.AutoGenerateColumns = true;
        layout.Controls.Add(_auditGrid, 0, 1);
        return layout;
    }

    private void LoadUsers()
    {
        try { _userGrid.DataSource = _users.GetUsers(_administrator); }
        catch (Exception ex) { ShowError(ex); }
        if (_userGrid.Columns[nameof(ManagedUser.CreatedAtUtc)] is { } dateColumn)
            dateColumn.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm:ss";
    }

    private ManagedUser? SelectedUser() => _userGrid.CurrentRow?.DataBoundItem as ManagedUser;

    private void EditUser(ManagedUser? user)
    {
        using var dialog = new UserEditForm(user);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (user is null)
                _users.CreateUser(_administrator, dialog.Username, dialog.FullName, dialog.Password, dialog.Role);
            else
                _users.UpdateUser(_administrator, user.Id, dialog.Username, dialog.FullName, dialog.Role, dialog.Password);
            LoadUsers();
            LoadAudit();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ToggleSelectedUser()
    {
        var user = SelectedUser();
        if (user is null) { MessageBox.Show(this, "Sélectionnez un compte.", "Utilisateurs", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        try
        {
            _users.SetActive(_administrator, user.Id, !user.Active);
            LoadUsers();
            LoadAudit();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadSettings()
    {
        try
        {
            var settings = _settings.GetStoreSettings();
            _storeName.Text = settings.StoreName;
            _timeZone.SelectedItem = TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(zone => zone.Id == settings.TimeZoneId);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void SaveSettings()
    {
        try
        {
            if (_timeZone.SelectedItem is not TimeZoneInfo zone) throw new InvalidOperationException("Sélectionnez un fuseau horaire valide.");
            _settings.SaveStoreSettings(_administrator, _storeName.Text, "FCFA XAF", zone.Id);
            MessageBox.Show(this, "Les paramètres du magasin ont été enregistrés.", "Administration", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadAudit();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void LoadAudit()
    {
        try
        {
            _auditGrid.DataSource = _audit.GetRecent(_administrator);
            if (_auditGrid.Columns[nameof(AuditLogEntry.CreatedAtUtc)] is { } dateColumn)
                dateColumn.DefaultCellStyle.Format = "yyyy-MM-dd HH:mm:ss";
            if (_auditGrid.Columns[nameof(AuditLogEntry.Details)] is { } detailsColumn)
                detailsColumn.FillWeight = 240;
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void CreateBackup()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Base SQLite (*.db)|*.db|Base SQLite (*.sqlite)|*.sqlite",
            DefaultExt = "db",
            AddExtension = true,
            FileName = $"gestion-magasin-sauvegarde-{DateTime.Now:yyyyMMdd-HHmmss}.db"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var path = _settings.CreateBackup(_administrator, dialog.FileName);
            MessageBox.Show(this, $"Sauvegarde créée : {path}", "Administration", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadAudit();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ShowError(Exception exception) => MessageBox.Show(this, exception.Message, "Administration", MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 237, 245);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = CatalogPalette.Text;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
    }

    private static void AddGridColumn(DataGridView grid, string property, string title, float fill, string? trueText = null, string? falseText = null)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = title,
            FillWeight = fill
        });
        if (property == nameof(ManagedUser.Active) && trueText is not null)
            grid.CellFormatting += (_, e) =>
            {
                if (grid.Columns[e.ColumnIndex].DataPropertyName == property && e.Value is bool active)
                {
                    e.Value = active ? trueText : falseText;
                    e.FormattingApplied = true;
                }
            };
    }

    private static void AddSettingField(TableLayoutPanel layout, int row, string title, Control control)
    {
        layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 10, 0, 10);
        layout.Controls.Add(control, 1, row);
    }
}
