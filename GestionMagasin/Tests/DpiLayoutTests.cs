using System.Drawing;
using GestionMagasin.Forms;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class DpiLayoutTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(125)]
    [InlineData(150)]
    public void ProductDialogFits1366By768WorkAreaAtWindowsScale(int scalePercent)
    {
        const int screenWidth = 1366;
        const int workAreaHeight = 728;
        var scale = scalePercent / 100f;
        var desiredClient = new Size((int)Math.Round(650 * scale), (int)Math.Round(630 * scale));
        var frame = new Size((int)Math.Round(16 * scale), (int)Math.Round(39 * scale));

        var fittedClient = DpiFormLayout.CalculateFittedClientSize(desiredClient, frame,
            new Size(screenWidth, workAreaHeight));

        Assert.True(fittedClient.Width + frame.Width + 24 <= screenWidth);
        Assert.True(fittedClient.Height + frame.Height + 24 <= workAreaHeight);
        Assert.True(fittedClient.Width >= 320);
        Assert.True(fittedClient.Height >= 240);
    }
}
