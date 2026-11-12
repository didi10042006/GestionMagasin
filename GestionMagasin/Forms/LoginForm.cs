using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class LoginForm : Form
{
    private readonly AuthenticationService _authenticationService;
    private readonly TextBox _username = new();
    private readonly TextBox _password = new();
    private readonly Label _error = new();

    public LoginForm(AuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
        Text = "Connexion — Gestion-Magasin";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(440, 330);
        BackColor = Color.FromArgb(247, 249, 252);
        Shown += (_, _) => DpiFormLayout.FitDialogToWorkingArea(this);

        Controls.Add(new Label
        {
            Text = "Connexion à la base réelle",
            AutoSize = true,
            Location = new Point(30, 27),
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 43, 66)
        });
        AddField("Nom d'utilisateur", _username, 90, false);
        AddField("Mot de passe", _password, 155, true);

        _error.Location = new Point(32, 215);
        _error.Size = new Size(372, 34);
        _error.ForeColor = Color.Firebrick;
        Controls.Add(_error);

        var signIn = new Button
        {
            Text = "Se connecter",
            Location = new Point(32, 259),
            Size = new Size(372, 40),
            BackColor = Color.FromArgb(29, 78, 137),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        signIn.FlatAppearance.BorderSize = 0;
        signIn.Click += SignIn;
        AcceptButton = signIn;
        Controls.Add(signIn);
    }

    public AuthenticatedUser? AuthenticatedUser { get; private set; }

    private void AddField(string label, TextBox input, int top, bool password)
    {
        Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Location = new Point(32, top),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        input.Location = new Point(32, top + 24);
        input.Size = new Size(372, 27);
        input.Font = new Font("Segoe UI", 10);
        input.UseSystemPasswordChar = password;
        Controls.Add(input);
    }

    private void SignIn(object? sender, EventArgs e)
    {
        AuthenticatedUser = _authenticationService.Authenticate(_username.Text, _password.Text);
        if (AuthenticatedUser is null)
        {
            _error.Text = "Identifiants invalides ou compte désactivé.";
            _password.Clear();
            _password.Focus();
            return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
