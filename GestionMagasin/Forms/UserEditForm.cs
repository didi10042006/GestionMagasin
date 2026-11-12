using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class UserEditForm : Form
{
    private readonly TextBox _username = new();
    private readonly TextBox _fullName = new();
    private readonly TextBox _password = new();
    private readonly ComboBox _role = new();

    public UserEditForm(ManagedUser? user = null)
    {
        Text = user is null ? "Créer un utilisateur" : "Modifier un utilisateur";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(510, 370);
        BackColor = CatalogPalette.Background;
        Font = new Font("Segoe UI", 10);

        _username.Text = user?.Username ?? "";
        _fullName.Text = user?.FullName ?? "";
        _password.UseSystemPasswordChar = true;
        _password.PlaceholderText = user is null ? "12 caractères minimum" : "Laisser vide pour conserver le mot de passe";
        _role.DropDownStyle = ComboBoxStyle.DropDownList;
        _role.DataSource = Enum.GetValues<UserRole>();
        _role.SelectedItem = user?.Role ?? UserRole.Vendeur;

        var form = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 5, BackColor = CatalogPalette.Background };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 4; row++) form.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        form.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        AddField(form, 0, "Nom utilisateur", _username);
        AddField(form, 1, "Nom complet", _fullName);
        AddField(form, 2, user is null ? "Mot de passe" : "Nouveau mot de passe", _password);
        AddField(form, 3, "Rôle", _role);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0) };
        var save = CatalogPalette.CreateButton("Enregistrer", CatalogPalette.Primary);
        save.Size = new Size(130, 38);
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_username.Text) || string.IsNullOrWhiteSpace(_fullName.Text))
            {
                MessageBox.Show(this, "Le nom utilisateur et le nom complet sont obligatoires.", "Utilisateur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (user is null && _password.Text.Length < 12)
            {
                MessageBox.Show(this, "Le mot de passe doit contenir au moins 12 caractères.", "Utilisateur", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button { Text = "Annuler", Size = new Size(110, 38), DialogResult = DialogResult.Cancel };
        buttons.Controls.AddRange([save, cancel]);
        form.Controls.Add(buttons, 0, 4);
        form.SetColumnSpan(buttons, 2);
        Controls.Add(form);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public string Username => _username.Text.Trim();
    public string FullName => _fullName.Text.Trim();
    public string Password => _password.Text;
    public UserRole Role => (UserRole)_role.SelectedItem!;

    private static void AddField(TableLayoutPanel layout, int row, string caption, Control control)
    {
        layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill;
        if (control is TextBox text) text.Margin = new Padding(0, 10, 0, 10);
        layout.Controls.Add(control, 1, row);
    }
}
