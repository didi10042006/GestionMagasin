using System.Reflection;
using System.Runtime.InteropServices;

namespace GestionMagasin.Forms;

internal static class BrandingAssets
{
    public static Image? LoadLogo(string? configuredPath = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            {
                using var fileImage = Image.FromFile(configuredPath);
                return new Bitmap(fileImage);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            // Invalid or absent merchant artwork falls back to the built-in brand mark.
        }

        try
        {
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("Resources.gestion-magasin.png", StringComparison.OrdinalIgnoreCase));
            if (resource is null) return null;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream is null) return null;
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }

    public static Icon? LoadApplicationIcon()
    {
        try
        {
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                .FirstOrDefault(name => name.EndsWith("Resources.gestion-magasin.ico", StringComparison.OrdinalIgnoreCase));
            if (resource is null) return null;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            return stream is null ? null : new Icon(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException)
        {
            return null;
        }
    }
}
