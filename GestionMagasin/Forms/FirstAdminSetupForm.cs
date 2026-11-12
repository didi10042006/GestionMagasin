using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class FirstAdminSetupForm : Form
{
    private readonly UserService _userService;
    private readonly TextBox _username = new();
    private readonly TextBox _fullName = new();
    private readonly TextBox _password = new();
    private readonly TextBox _confirmation = new();
    private readonly Label _error = new();
    private bool _created;

    public FirstAdminSetupForm(UserService userService)
    {
        _userService = userService;
        Text = "Configuration initiale — Gestion-Magasin";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ControlBox = false;
        ShowInTaskbar = true;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(560, 500);
        BackColor = Color.FromArgb(247, 249, 252);
        Shown += (_, _) => DpiFormLayout.FitDialogToWorkingArea(this);

        var title = new Label
        {
            Text = "Créer le premier administrateur",
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 43, 66),
            AutoSize = true,
            Location = new Point(32, 28)
        };
        var info = new Label
        {
            Text = "Cette étape est obligatoire pour sécuriser la base réelle. Aucun compte ni mot de passe par défaut n'est créé.",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(75, 87, 104),
            Location = new Point(36, 76),
            Size = new Size(485, 46)
        };

        AddField("Nom d'utilisateur", _username, 140);
        AddField("Nom complet", _fullName, 205);
        AddField("Mot de passe (12 caractères minimum)", _password, 270, true);
        AddField("Confirmer le mot de passe", _confirmation, 335, true);

        _error.AutoSize = false;
        _error.ForeColor = Color.Firebrick;
        _error.Location = new Point(36, 392);
        _error.Size = new Size(485, 32);

        var createButton = new Button
        {
            Text = "Créer le compte administrateur",
            Location = new Point(36, 438),
            Size = new Size(485, 42),
            BackColor = Color.FromArgb(29, 78, 137),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            UseVisualStyleBackColor = false
        };
        createButton.FlatAppearance.BorderSize = 0;
        createButton.Click += CreateAdministrator;
        AcceptButton = createButton;
        Controls.AddRange([title, info, _error, createButton]);
    }

    public AuthenticatedUser? CreatedUser { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_created)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    private void AddField(string label, TextBox input, int top, bool password = false)
    {
        Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Location = new Point(36, top),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(56, 68, 86)
        });
        input.Location = new Point(36, top + 23);
        input.Size = new Size(485, 27);
        input.Font = new Font("Segoe UI", 10);
        input.UseSystemPasswordChar = password;
        Controls.Add(input);
    }

    private void CreateAdministrator(object? sender, EventArgs e)
    {
        _error.Text = string.Empty;
        if (!string.Equals(_password.Text, _confirmation.Text, StringComparison.Ordinal))
        {
            _error.Text = "Les deux mots de passe ne correspondent pas.";
            return;
        }

        try
        {
            CreatedUser = _userService.CreateInitialAdministrator(_username.Text, _fullName.Text, _password.Text);
            _created = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _error.Text = ex is Microsoft.Data.Sqlite.SqliteException
                ? "Impossible de créer ce compte. Vérifiez le nom d'utilisateur et réessayez."
                : ex.Message;
        }
    }
}
