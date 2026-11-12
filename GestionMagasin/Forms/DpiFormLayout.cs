namespace GestionMagasin.Forms;

public static class DpiFormLayout
{
    public static void FitDialogToWorkingArea(Form form)
    {
        if (!form.IsHandleCreated || form.WindowState != FormWindowState.Normal)
        {
            return;
        }

        var workArea = Screen.FromControl(form).WorkingArea;
        var frameWidth = form.Width - form.ClientSize.Width;
        var frameHeight = form.Height - form.ClientSize.Height;
        var fittedClientSize = CalculateFittedClientSize(form.ClientSize,
            new Size(frameWidth, frameHeight), workArea.Size);

        if (fittedClientSize != form.ClientSize)
        {
            form.ClientSize = fittedClientSize;
        }

        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(
            workArea.Left + Math.Max(0, (workArea.Width - form.Width) / 2),
            workArea.Top + Math.Max(0, (workArea.Height - form.Height) / 2));
    }

    public static Size CalculateFittedClientSize(Size desiredClientSize, Size frameSize, Size workAreaSize)
    {
        var maxClientWidth = Math.Max(320, workAreaSize.Width - frameSize.Width - 24);
        var maxClientHeight = Math.Max(240, workAreaSize.Height - frameSize.Height - 24);
        return new Size(
            Math.Min(desiredClientSize.Width, maxClientWidth),
            Math.Min(desiredClientSize.Height, maxClientHeight));
    }
}
