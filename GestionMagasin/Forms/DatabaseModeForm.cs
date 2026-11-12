using GestionMagasin.Data;

namespace GestionMagasin.Forms;

public sealed class DatabaseModeForm : Form
{
    private DatabaseMode? _selectedMode;

    public DatabaseModeForm()
    {
        Text = "Gestion-Magasin — Choix du mode";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(600, 350);
        BackColor = Color.FromArgb(245, 247, 250);
        Shown += (_, _) => DpiFormLayout.FitDialogToWorkingArea(this);

        var title = new Label
        {
            Text = "Choisissez votre espace de travail",
            Font = new Font("Segoe UI", 19, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 43, 66),
            AutoSize = true,
            Location = new Point(34, 27)
        };
        var subtitle = new Label
        {
            Text = "Les données de démonstration et les données réelles sont stockées séparément.",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(80, 91, 108),
            AutoSize = true,
            Location = new Point(37, 73)
        };

        var realButton = CreateModeButton(
            "BASE RÉELLE",
            "Base persistante et vide au premier démarrage. Utilisez-la pour la gestion quotidienne.",
            Color.FromArgb(29, 78, 137));
        realButton.Location = new Point(36, 125);
        realButton.Click += (_, _) => SelectMode(DatabaseMode.Reelle);

        var demoButton = CreateModeButton(
            "BASE DE DÉMONSTRATION",
            "Base séparée avec catégories et 8 articles de référence. Vous pouvez l'explorer sans toucher aux données réelles.",
            Color.FromArgb(35, 125, 99));
        demoButton.Location = new Point(36, 225);
        demoButton.Click += (_, _) => SelectMode(DatabaseMode.Demonstration);

        Controls.AddRange([title, subtitle, realButton, demoButton]);
        AcceptButton = realButton;
    }

    public DatabaseMode? SelectedMode => _selectedMode;

    private static Button CreateModeButton(string heading, string description, Color accent)
    {
        var button = new Button
        {
            Size = new Size(528, 82),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = accent,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 8, 12, 8),
            Font = new Font("Segoe UI", 10),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(218, 224, 232);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 244, 250);
        button.Text = $"{heading}{Environment.NewLine}{description}";
        return button;
    }

    private void SelectMode(DatabaseMode mode)
    {
        _selectedMode = mode;
        DialogResult = DialogResult.OK;
        Close();
    }
}
