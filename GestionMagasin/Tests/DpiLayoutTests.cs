using System.Drawing;
using GestionMagasin.Forms;
using Xunit;

namespace GestionMagasin.Tests;

public sealed class DpiLayoutTests
{
    [Theory]
    [InlineData(150, 4, 650, 300, 350)]
    [InlineData(600, 4, 650, 300, 350)]
    [InlineData(1320, 4, 650, 300, 350)]
    [InlineData(200, 4, 300, 130, 120)]
    public void SplitterDistanceAndPanelMinimumsFitAvailableControlSize(
        int dimension, int splitterWidth, int preferredDistance, int requestedPanel1Minimum, int requestedPanel2Minimum)
    {
        var layout = SplitContainerLayout.Calculate(dimension, splitterWidth, preferredDistance,
            requestedPanel1Minimum, requestedPanel2Minimum);
        var available = Math.Max(0, dimension - splitterWidth);

        Assert.True(layout.Panel1Minimum + layout.Panel2Minimum <= available);
        Assert.InRange(layout.SplitterDistance, layout.Panel1Minimum,
            available - layout.Panel2Minimum);
        Assert.True(layout.Panel1Minimum <= requestedPanel1Minimum);
        Assert.True(layout.Panel2Minimum <= requestedPanel2Minimum);
    }

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
