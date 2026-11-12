namespace GestionMagasin.Forms;

internal static class CatalogPalette
{
    public static readonly Color Background = ColorTranslator.FromHtml("#F5F7FA");
    public static readonly Color Text = ColorTranslator.FromHtml("#1F2937");
    public static readonly Color Primary = ColorTranslator.FromHtml("#2563EB");
    public static readonly Color OutOfStockText = ColorTranslator.FromHtml("#B91C1C");
    public static readonly Color OutOfStockBackground = ColorTranslator.FromHtml("#FEE2E2");
    public static readonly Color LowStockText = ColorTranslator.FromHtml("#C2410C");
    public static readonly Color LowStockBackground = ColorTranslator.FromHtml("#FFEDD5");
    public static readonly Color AvailableText = ColorTranslator.FromHtml("#166534");
    public static readonly Color AvailableBackground = ColorTranslator.FromHtml("#DCFCE7");
    public static readonly Font AvailabilityFont = new("Segoe UI", 9, FontStyle.Bold);

    public static Button CreateButton(string text, Color background)
    {
        var button = new Button
        {
            Text = text,
            BackColor = background,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Dark(background, .08f);
        return button;
    }
}
