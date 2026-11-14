namespace ShortcutPad;

/// <summary>
/// Vector icons for common button actions.
///
/// Path data is Material Design Icons (https://materialdesignicons.com/),
/// Apache-2.0, drawn on a 24x24 grid. Render inside a <see cref="Avalonia.Controls.Path"/>
/// with <c>Stretch="Uniform"</c> and a fixed Width/Height. Nothing binary is
/// shipped, and the icons scale crisply at any size.
/// </summary>
public static class PadIcons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backspace-outline"] = "M19,15.59L17.59,17L14,13.41L10.41,17L9,15.59L12.59,12L9,8.41L10.41,7L14,10.59L17.59,7L19,8.41L15.41,12L19,15.59M22,3A2,2 0 0,1 24,5V19A2,2 0 0,1 22,21H7C6.31,21 5.77,20.64 5.41,20.11L0,12L5.41,3.88C5.77,3.35 6.31,3 7,3H22M22,5H7L2.28,12L7,19H22V5Z",
        ["backspace"] = "M22,3H7C6.31,3 5.77,3.35 5.41,3.88L0,12L5.41,20.11C5.77,20.64 6.31,21 7,21H22A2,2 0 0,0 24,19V5A2,2 0 0,0 22,3M19,15.59L17.59,17L14,13.41L10.41,17L9,15.59L12.59,12L9,8.41L10.41,7L14,10.59L17.59,7L19,8.41L15.41,12",
        ["calculator"] = "M7,2H17A2,2 0 0,1 19,4V20A2,2 0 0,1 17,22H7A2,2 0 0,1 5,20V4A2,2 0 0,1 7,2M7,4V8H17V4H7M7,10V12H9V10H7M11,10V12H13V10H11M15,10V12H17V10H15M7,14V16H9V14H7M11,14V16H13V14H11M15,14V16H17V14H15M7,18V20H9V18H7M11,18V20H13V18H11M15,18V20H17V18H15Z",
        ["content-copy"] = "M19,21H8V7H19M19,5H8A2,2 0 0,0 6,7V21A2,2 0 0,0 8,23H19A2,2 0 0,0 21,21V7A2,2 0 0,0 19,5M16,1H4A2,2 0 0,0 2,3V17H4V3H16V1Z",
        ["content-cut"] = "M19,3L13,9L15,11L22,4V3M12,12.5A0.5,0.5 0 0,1 11.5,12A0.5,0.5 0 0,1 12,11.5A0.5,0.5 0 0,1 12.5,12A0.5,0.5 0 0,1 12,12.5M6,20A2,2 0 0,1 4,18C4,16.89 4.9,16 6,16A2,2 0 0,1 8,18C8,19.11 7.1,20 6,20M6,8A2,2 0 0,1 4,6C4,4.89 4.9,4 6,4A2,2 0 0,1 8,6C8,7.11 7.1,8 6,8M9.64,7.64C9.87,7.14 10,6.59 10,6A4,4 0 0,0 6,2A4,4 0 0,0 2,6A4,4 0 0,0 6,10C6.59,10 7.14,9.87 7.64,9.64L10,12L7.64,14.36C7.14,14.13 6.59,14 6,14A4,4 0 0,0 2,18A4,4 0 0,0 6,22A4,4 0 0,0 10,18C10,17.41 9.87,16.86 9.64,16.36L12,14L19,21H22V20L9.64,7.64Z",
        ["content-paste"] = "M19,20H5V4H7V7H17V4H19M12,2A1,1 0 0,1 13,3A1,1 0 0,1 12,4A1,1 0 0,1 11,3A1,1 0 0,1 12,2M19,2H14.82C14.4,0.84 13.3,0 12,0C10.7,0 9.6,0.84 9.18,2H5A2,2 0 0,0 3,4V20A2,2 0 0,0 5,22H19A2,2 0 0,0 21,20V4A2,2 0 0,0 19,2Z",
        ["content-save"] = "M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z",
        ["home"] = "M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z",
        ["keyboard-esc"] = "M1 7H7V9H3V11H7V13H3V15H7V17H1V7M11 7H15V9H11V11H13C14.11 11 15 11.9 15 13V15C15 16.11 14.11 17 13 17H9V15H13V13H11C9.9 13 9 12.11 9 11V9C9 7.9 9.9 7 11 7M19 7H21C22.11 7 23 7.9 23 9V10H21V9H19V15H21V14H23V15C23 16.11 22.11 17 21 17H19C17.9 17 17 16.11 17 15V9C17 7.9 17.9 7 19 7Z",
        ["keyboard-return"] = "M19,7V11H5.83L9.41,7.41L8,6L2,12L8,18L9.41,16.58L5.83,13H21V7H19Z",
        ["keyboard-tab"] = "M20,18H22V6H20M11.59,7.41L15.17,11H1V13H15.17L11.59,16.58L13,18L19,12L13,6L11.59,7.41Z",
        ["magnify"] = "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z",
        ["monitor"] = "M21,16H3V4H21M21,2H3C1.89,2 1,2.89 1,4V16A2,2 0 0,0 3,18H10V20H8V22H16V20H14V18H21A2,2 0 0,0 23,16V4C23,2.89 22.1,2 21,2Z",
        ["play-pause"] = "M3,5V19L11,12M13,19H16V5H13M18,5V19H21V5",
        ["redo"] = "M18.4,10.6C16.55,9 14.15,8 11.5,8C6.85,8 2.92,11.03 1.54,15.22L3.9,16C4.95,12.81 7.95,10.5 11.5,10.5C13.45,10.5 15.23,11.22 16.62,12.38L13,16H22V7L18.4,10.6Z",
        ["select-all"] = "M9,9H15V15H9M7,17H17V7H7M15,5H17V3H15M15,21H17V19H15M19,17H21V15H19M19,9H21V7H19M19,21A2,2 0 0,0 21,19H19M19,13H21V11H19M11,21H13V19H11M9,3H7V5H9M3,17H5V15H3M5,21V19H3A2,2 0 0,0 5,21M19,3V5H21A2,2 0 0,0 19,3M13,3H11V5H13M3,9H5V7H3M7,21H9V19H7M3,13H5V11H3M3,5H5V3A2,2 0 0,0 3,5Z",
        ["skip-next"] = "M16,18H18V6H16M6,18L14.5,12L6,6V18Z",
        ["skip-previous"] = "M6,18V6H8V18H6M9.5,12L18,6V18L9.5,12Z",
        ["undo"] = "M12.5,8C9.85,8 7.45,9 5.6,10.6L2,7V16H11L7.38,12.38C8.77,11.22 10.54,10.5 12.5,10.5C16.04,10.5 19.05,12.81 20.1,16L22.47,15.22C21.08,11.03 17.15,8 12.5,8Z",
        ["view-dashboard"] = "M13,3V9H21V3M13,21H21V11H13M3,21H11V15H3M3,13H11V3H3V13Z",
        ["volume-minus"] = "M3,9H7L12,4V20L7,15H3V9M14,11H22V13H14V11Z",
        ["volume-mute"] = "M3,9H7L12,4V20L7,15H3V9M16.59,12L14,9.41L15.41,8L18,10.59L20.59,8L22,9.41L19.41,12L22,14.59L20.59,16L18,13.41L15.41,16L14,14.59L16.59,12Z",
        ["volume-plus"] = "M3,9H7L12,4V20L7,15H3V9M14,11H17V8H19V11H22V13H19V16H17V13H14V11Z",
    };

    /// <summary>Icon names understood by the pad.</summary>
    public static IEnumerable<string> Names => Paths.Keys;

    public static bool TryGet(string? name, out string path)
    {
        path = string.Empty;
        return !string.IsNullOrWhiteSpace(name) && Paths.TryGetValue(name, out path);
    }

    /// <summary>
    /// Pick an icon for a button: an explicit <c>icon</c> in the config wins,
    /// otherwise the action (<c>keys</c> / <c>run</c>) decides.
    /// </summary>
    public static string? ResolveFor(PadButton b)
    {
        if (TryGet(b.Icon, out _)) return b.Icon;

        var keys = (b.Keys ?? string.Empty).Trim().ToLowerInvariant();
        switch (keys)
        {
            case "ctrl+c": return "content-copy";
            case "ctrl+v": return "content-paste";
            case "ctrl+x": return "content-cut";
            case "ctrl+z": return "undo";
            case "ctrl+y":
            case "ctrl+shift+z": return "redo";
            case "ctrl+s": return "content-save";
            case "ctrl+a":
            case "selectall": return "select-all";
            case "ctrl+f":
            case "ctrl+g":
            case "ctrl+h": return "magnify";
            case "enter":
            case "return": return "keyboard-return";
            case "esc":
            case "escape": return "keyboard-esc";
            case "tab": return "keyboard-tab";
            case "backspace": return "backspace";
            case "home": return "home";
            case "end": return "keyboard-return";
            case "delete":
            case "del": return "backspace-outline";
            case "ctrl+shift+esc": return "view-dashboard";
            case "volume_down":
            case "volumedown": return "volume-minus";
            case "volume_mute":
            case "mute": return "volume-mute";
            case "volume_up":
            case "volumeup": return "volume-plus";
            case "media_play_pause":
            case "mediaplaypause":
            case "playpause": return "play-pause";
            case "media_next":
            case "medianext":
            case "nexttrack": return "skip-next";
            case "media_prev":
            case "mediaprev":
            case "prevtrack": return "skip-previous";
            case "win+d":
            case "super+d": return "monitor";
        }

        var run = (b.Run ?? string.Empty).ToLowerInvariant();
        if (run.Contains("calc")) return "calculator";

        return null;
    }
}
