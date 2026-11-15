using System.IO;
using Avalonia.Controls;

namespace ShortcutPad;

/// <summary>
/// The application icon. It is embedded once and shared by the executable
/// (via <c>ApplicationIcon</c> in the project file), the window title bars and
/// the tray icon, so all three show the same art.
/// </summary>
internal static class AppIcon
{
    // Matches the default name MSBuild gives an EmbeddedResource at the project
    // root: <RootNamespace>.<file name>.
    private const string IcoResource = "ShortcutPad.app.ico";

    /// <summary>
    /// Load a fresh icon. Each caller gets its own instance because
    /// <see cref="WindowIcon"/> keeps the stream it was built from.
    /// Returns null when the resource is missing, so a bad deploy degrades to
    /// the default icon instead of crashing the pad.
    /// </summary>
    public static WindowIcon? TryLoad()
    {
        try
        {
            using var raw = typeof(AppIcon).Assembly.GetManifestResourceStream(IcoResource);
            if (raw is null) return null;

            var copy = new MemoryStream();
            raw.CopyTo(copy);
            copy.Position = 0;
            return new WindowIcon(copy);
        }
        catch
        {
            return null;
        }
    }
}
