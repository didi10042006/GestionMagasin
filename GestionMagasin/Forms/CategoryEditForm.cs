using GestionMagasin.Services;

namespace GestionMagasin.Forms;

public sealed class CategoryEditForm : Form
{
    private readonly CategoryService _service;
    private readonly AuthenticatedUser _actor;
    private readonly CategoryCatalogItem? _existing;
    private readonly TextBox _name = new();
    private readonly TextBox _description = new();
    private readonly Label _error = new();

    public CategoryEditForm(CategoryService service, AuthenticatedUser actor, CategoryCatalogItem? existing = null)
    {
        _service = service;
        _actor = actor;
        _existing = existing;
        Text = existing is null ? "Nouvelle catégorie" : "Modifier la catégorie";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 310);
        BackColor = CatalogPalette.Background;
        Font = new Font("Segoe UI", 9.5f);
        Shown += (_, _) => DpiFormLayout.FitDialogToWorkingArea(this);

        Controls.Add(new Label
        {
            Text = existing is null ? "Créer une catégorie" : "Modifier une catégorie",
            AutoSize = true,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            ForeColor = CatalogPalette.Text,
            Location = new Point(26, 22)
        });
        AddField("Nom de la catégorie *", _name, 82);
        AddField("Description", _description, 147);
        _name.Text = existing?.Name ?? string.Empty;
        _description.Text = existing?.Description ?? string.Empty;
        _error.Location = new Point(28, 214);
        _error.Size = new Size(440, 27);
        _error.ForeColor = Color.Firebrick;
        Controls.Add(_error);

        var save = CatalogPalette.CreateButton("Enregistrer", CatalogPalette.Primary);
        save.Location = new Point(280, 253);
        save.Size = new Size(188, 36);
        save.Click += SaveCategory;
        var cancel = CatalogPalette.CreateButton("Annuler", Color.FromArgb(229, 233, 240));
        cancel.ForeColor = CatalogPalette.Text;
        cancel.Location = new Point(170, 253);
        cancel.Size = new Size(100, 36);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.AddRange([save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void AddField(string label, TextBox input, int top)
    {
        Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Location = new Point(28, top),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = CatalogPalette.Text
        });
        input.Location = new Point(28, top + 24);
        input.Size = new Size(440, 29);
        input.Font = new Font("Segoe UI", 10);
        input.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(input);
    }

    private void SaveCategory(object? sender, EventArgs e)
    {
        try
        {
            if (_existing is null) _service.Create(_actor, _name.Text, _description.Text);
            else _service.Update(_actor, _existing.Id, _name.Text, _description.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _error.Text = ex.Message;
        }
    }
}
