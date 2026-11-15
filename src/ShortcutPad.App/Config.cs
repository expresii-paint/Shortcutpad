using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortcutPad;

public sealed class PadConfig
{
    public int Columns { get; set; } = 5;
    public bool AlwaysOnTop { get; set; } = true;
    public int X { get; set; } = 60;
    public int Y { get; set; } = 60;

    /// <summary>Window opacity, 0.1 - 1.0.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Pin the pad to a desktop edge: "none", "left" or "right".</summary>
    public string Dock { get; set; } = "none";

    /// <summary>Let the user resize the pad. Ignored while docked.</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>
    /// Derive the column/row counts from the window size so buttons keep a
    /// sensible touch size. When false, <see cref="Columns"/> is used and
    /// resizing just stretches the buttons.
    /// </summary>
    public bool AutoFlow { get; set; } = true;

    /// <summary>Remembered window size in DIPs (autoFlow only).</summary>
    public double Width { get; set; } = 526;

    /// <summary>Remembered window size in DIPs (autoFlow only).</summary>
    public double Height { get; set; } = 381;

    /// <summary>Target button size in DIPs, used by auto flow.</summary>
    public int ButtonWidth { get; set; } = 100;
    public int ButtonHeight { get; set; } = 62;

    public List<PadButton> Buttons { get; set; } = new();
}

public sealed class PadButton : INotifyPropertyChanged
{
    private string _label = "";
    private string? _keys, _text, _run, _color, _icon, _foreground;
    private int? _fontSize, _iconSize, _cornerRadius;
    private int _spanX = 1, _spanY = 1;

    public string Label { get => _label; set => Set(ref _label, value); }
    public string? Keys { get => _keys; set => Set(ref _keys, value); }
    public string? Text { get => _text; set => Set(ref _text, value); }
    public string? Run { get => _run; set => Set(ref _run, value); }
    public string? Color { get => _color; set => Set(ref _color, value); }

    /// <summary>
    /// Optional icon name from <see cref="PadIcons"/>, e.g. "undo", "content-copy".
    /// When omitted the icon is inferred from <see cref="Keys"/> / <see cref="Run"/>.
    /// </summary>
    public string? Icon { get => _icon; set => Set(ref _icon, value); }

    /// <summary>Text/icon colour. Omit to pick white or black from <see cref="Color"/>.</summary>
    public string? Foreground { get => _foreground; set => Set(ref _foreground, value); }

    /// <summary>Caption font size. Default: 13 with an icon, 20 without.</summary>
    public int? FontSize { get => _fontSize; set => Set(ref _fontSize, value); }

    /// <summary>Icon size in DIPs. Default 30.</summary>
    public int? IconSize { get => _iconSize; set => Set(ref _iconSize, value); }

    /// <summary>Button corner radius in DIPs. Omit for the theme default.</summary>
    public int? CornerRadius { get => _cornerRadius; set => Set(ref _cornerRadius, value); }

    /// <summary>How many grid columns this button spans (default 1).</summary>
    public int SpanX { get => _spanX; set => Set(ref _spanX, value); }

    /// <summary>How many grid rows this button spans (default 1).</summary>
    public int SpanY { get => _spanY; set => Set(ref _spanY, value); }

    /// <summary>
    /// One-line summary used by the editor's list. Not serialized; it is
    /// recomputed and announced whenever the fields it depends on change.
    /// </summary>
    [JsonIgnore]
    public string Description => Describe();

    public event PropertyChangedEventHandler? PropertyChanged;

    private string Describe()
    {
        var action = !string.IsNullOrWhiteSpace(Keys) ? Keys
                  : !string.IsNullOrWhiteSpace(Text) ? "“" + Text + "”"
                  : !string.IsNullOrWhiteSpace(Run) ? Run
                  : "(no action)";
        return $"{Label}   [{action}]";
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(Label) or nameof(Keys) or nameof(Text) or nameof(Run))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Description)));
    }
}

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
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

    /// <summary>Deep copy, for editors that should not touch the live config.</summary>
    public static PadConfig Clone(PadConfig cfg) =>
        JsonSerializer.Deserialize<PadConfig>(JsonSerializer.Serialize(cfg, Options), Options) ?? new PadConfig();

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
        Opacity = 1.0,
        Dock = "none",
        AutoFlow = true,
        ButtonWidth = 100,
        ButtonHeight = 62,
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
