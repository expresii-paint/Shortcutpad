using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortcutPad;

public sealed class PadConfig
{
    public int Columns { get; set; } = 5;
    public bool AlwaysOnTop { get; set; } = true;
    public int X { get; set; } = 60;
    public int Y { get; set; } = 60;
    public List<PadButton> Buttons { get; set; } = new();
}

public sealed class PadButton
{
    public string Label { get; set; } = "";
    public string? Keys { get; set; }
    public string? Text { get; set; }
    public string? Run { get; set; }
    public string? Color { get; set; }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PadConfig Load(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<PadConfig>(File.ReadAllText(path), Options);
                if (cfg is not null) return cfg;
            }
            catch (JsonException)
            {
                // fall through: rewrite a known-good default
            }
        }
        var def = Default();
        Save(path, def);
        return def;
    }

    public static void Save(string path, PadConfig cfg)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Options));
        File.Move(tmp, path, overwrite: true);
    }

    public static PadConfig Default() => new()
    {
        Columns = 5,
        AlwaysOnTop = true,
        Buttons = new List<PadButton>
        {
            new() { Label = "Copy",       Keys = "ctrl+c",              Color = "#2d6cdf" },
            new() { Label = "Paste",      Keys = "ctrl+v",              Color = "#2d6cdf" },
            new() { Label = "Cut",        Keys = "ctrl+x" },
            new() { Label = "Undo",       Keys = "ctrl+z" },
            new() { Label = "Redo",       Keys = "ctrl+y" },
            new() { Label = "Save",       Keys = "ctrl+s" },
            new() { Label = "Select all", Keys = "ctrl+a" },
            new() { Label = "Find",       Keys = "ctrl+f" },
            new() { Label = "Enter",      Keys = "enter" },
            new() { Label = "Esc",        Keys = "esc" },
            new() { Label = "Tab",        Keys = "tab" },
            new() { Label = "Backspace",  Keys = "backspace" },
            new() { Label = "Home",       Keys = "home" },
            new() { Label = "End",        Keys = "end" },
            new() { Label = "Delete",     Keys = "delete" },
            new() { Label = "Task mgr",   Keys = "ctrl+shift+esc" },
            new() { Label = "Vol -",      Keys = "volume_down" },
            new() { Label = "Mute",       Keys = "volume_mute" },
            new() { Label = "Vol +",      Keys = "volume_up" },
            new() { Label = "Play/Pause", Keys = "media_play_pause" },
            new() { Label = "Next",       Keys = "media_next" },
            new() { Label = "Prev",       Keys = "media_prev" },
            new() { Label = "Desktop",    Keys = "win+d" },
            new() { Label = "Calc",       Run = "calc.exe" },
            new() { Label = "Hello",      Text = "hello from pad",      Color = "#2e9e5b" },
        },
    };
}
