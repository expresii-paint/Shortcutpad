using System.Reflection;

namespace ShortcutPad;

/// <summary>
/// The version the app advertises about itself - in the pad's title bar, the
/// tray tooltip and the Options window title, so the running build can be
/// identified at a glance. Comes from the assembly version, which the csproj
/// sets with <c>&lt;Version&gt;</c>.
/// </summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>"Shortcut Pad 2.0.0"</summary>
    public static string NameWithVersion => $"Shortcut Pad {Version}";
}
