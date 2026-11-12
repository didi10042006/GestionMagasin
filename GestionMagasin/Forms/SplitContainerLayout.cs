namespace GestionMagasin.Forms;

public readonly record struct SplitContainerLayoutResult(
    int Panel1Minimum,
    int Panel2Minimum,
    int SplitterDistance);

public static class SplitContainerLayout
{
    public static void ConfigureWhenSized(
        SplitContainer split,
        int preferredDistance,
        int requestedPanel1Minimum,
        int requestedPanel2Minimum)
    {
        ArgumentNullException.ThrowIfNull(split);

        void ApplyLayout()
        {
            if (split.IsDisposed || !split.IsHandleCreated)
                return;

            var dimension = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            var layout = Calculate(dimension, split.SplitterWidth, preferredDistance,
                requestedPanel1Minimum, requestedPanel2Minimum);

            // Clear the previous bounds first. A resize can make yesterday's minimums
            // invalid before the replacement splitter distance is assigned.
            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            if (dimension <= split.SplitterWidth)
                return;

            split.SplitterDistance = layout.SplitterDistance;
            split.Panel1MinSize = layout.Panel1Minimum;
            split.Panel2MinSize = layout.Panel2Minimum;
        }

        split.SizeChanged += (_, _) => ApplyLayout();
        split.HandleCreated += (_, _) => split.BeginInvoke((Action)ApplyLayout);
        split.VisibleChanged += (_, _) =>
        {
            if (split.Visible && split.IsHandleCreated)
                split.BeginInvoke((Action)ApplyLayout);
        };
    }

    public static SplitContainerLayoutResult Calculate(
        int dimension,
        int splitterWidth,
        int preferredDistance,
        int requestedPanel1Minimum,
        int requestedPanel2Minimum)
    {
        if (dimension <= 0 || splitterWidth < 0)
            return new SplitContainerLayoutResult(0, 0, 0);

        var available = Math.Max(0, dimension - splitterWidth);
        if (available == 0)
            return new SplitContainerLayoutResult(0, 0, 0);

        var panel1Minimum = Math.Min(Math.Max(0, requestedPanel1Minimum), available / 2);
        var panel2Minimum = Math.Min(Math.Max(0, requestedPanel2Minimum), available - panel1Minimum);
        var minimumDistance = panel1Minimum;
        var maximumDistance = Math.Max(minimumDistance, available - panel2Minimum);
        var distance = Math.Clamp(preferredDistance, minimumDistance, maximumDistance);

        return new SplitContainerLayoutResult(panel1Minimum, panel2Minimum, distance);
    }
}
