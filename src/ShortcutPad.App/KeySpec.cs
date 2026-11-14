namespace ShortcutPad;

/// <summary>
/// A platform-neutral keystroke description: modifiers + one main key.
/// Each platform maps it onto its own key codes / event flags.
/// </summary>
public sealed class ParsedKeys
{
    public List<string> Modifiers { get; } = new();
    public string? NamedKey { get; set; }
    public char? CharKey { get; set; }
    public string? Error { get; set; }
    public bool HasMain => NamedKey is not null || CharKey is not null;
}

public static class KeySpec
{
    private static readonly Dictionary<string, string[]> ModifierAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = new[] { "ctrl" }, ["control"] = new[] { "ctrl" }, ["lcontrol"] = new[] { "ctrl" },
        ["alt"] = new[] { "alt" }, ["menu"] = new[] { "alt" }, ["option"] = new[] { "alt" },
        ["shift"] = new[] { "shift" }, ["lshift"] = new[] { "shift" },
        ["win"] = new[] { "win" }, ["windows"] = new[] { "win" }, ["super"] = new[] { "win" },
        ["cmd"] = new[] { "win" }, ["command"] = new[] { "win" },
    };

    public static readonly HashSet<string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "backspace", "tab", "enter", "return", "esc", "escape", "space",
        "pageup", "pagedown", "end", "home", "left", "up", "right", "down",
        "insert", "delete", "del", "printscreen", "prtsc",
        "volume_mute", "mute", "volume_down", "volumedown", "volume_up", "volumeup",
        "media_next", "medianext", "nexttrack", "media_prev", "mediaprev", "prevtrack",
        "media_stop", "mediastop", "media_play_pause", "mediaplaypause", "playpause",
    };

    static KeySpec()
    {
        for (var i = 1; i <= 12; i++) NamedKeys.Add("f" + i);
    }

    public static ParsedKeys Parse(string keys)
    {
        var result = new ParsedKeys();
        foreach (var raw in keys.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ModifierAliases.TryGetValue(raw, out var names))
            {
                foreach (var n in names)
                    if (!result.Modifiers.Contains(n)) result.Modifiers.Add(n);
                continue;
            }
            if (result.HasMain)
            {
                result.Error = $"More than one main key in '{keys}'.";
                return result;
            }
            if (NamedKeys.Contains(raw))
            {
                result.NamedKey = raw;
                continue;
            }
            if (raw.Length == 1)
            {
                result.CharKey = raw[0];
                continue;
            }
            result.Error = $"Unknown key '{raw}' in '{keys}'.";
            return result;
        }
        if (!result.HasMain) result.Error = $"No key to press in '{keys}'.";
        return result;
    }
}
