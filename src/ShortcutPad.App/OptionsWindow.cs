using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ShortcutPad;

/// <summary>
/// The actions the options window can perform. Supplied by
/// <see cref="PadWindow"/>, which owns the real implementation.
/// </summary>
public sealed class OptionsActions
{
    public Action EditConfig { get; set; } = () => { };
    public Action OpenEditor { get; set; } = () => { };
    public Action<double> SetOpacity { get; set; } = _ => { };
    public Action<string> SetDock { get; set; } = _ => { };

    /// <summary>
    /// Target button size in DIPs. Auto flow derives the column and row counts
    /// from it, so it is how a user sizes the pad for their own fingers.
    /// </summary>
    public Action<int, int> SetButtonSize { get; set; } = (_, _) => { };
    public Action Exit { get; set; } = () => { };
}

/// <summary>
/// Touch-friendly options window, opened by the pad's "☰ Menu" button. It
/// offers the same actions as the tray and right-click menus, but as large
/// buttons that are easy to hit without a mouse.
/// </summary>
public sealed class OptionsWindow : Window
{
    private const double RowHeight = 52.0;
    private const double SmallButtonWidth = 92.0;

    /// <summary>Bounds for the editable target button size, in DIPs.</summary>
    private const int MinEdge = 32;
    private const int MaxEdge = 400;

    private readonly PadConfig _cfg;
    private readonly OptionsActions _actions;
    private TextBox _width = null!;
    private TextBox _height = null!;
    private DispatcherTimer? _sizeTimer;
    private bool _inSizeSync;

    public OptionsWindow(PadConfig cfg, string configPath, OptionsActions actions)
    {
        _cfg = cfg;
        _actions = actions;

        Title = "Options";
        Icon = AppIcon.TryLoad();
        Opened += (_, _) => WindowChrome.HideMaximize(this, PadPlatform.Create());
        // Sized to fit its content: everything below is chosen so the dialog
        // never needs to scroll on a normal display.
        Width = 440;
        Height = 540;
        MinWidth = 340;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(14) };

        stack.Children.Add(Row(new[]
        {
            MakeButton("Edit config (JSON)", _actions.EditConfig, 203),
            MakeButton("Edit buttons…", _actions.OpenEditor, 203),
        }));

        stack.Children.Add(Caption("Opacity"));
        stack.Children.Add(Row(new[]
        {
            MakeButton("100%", () => _actions.SetOpacity(1.0)),
            MakeButton("85%", () => _actions.SetOpacity(0.85)),
            MakeButton("70%", () => _actions.SetOpacity(0.70)),
            MakeButton("50%", () => _actions.SetOpacity(0.50)),
        }));

        stack.Children.Add(Caption("Dock to a screen edge"));
        stack.Children.Add(Row(new[]
        {
            MakeButton("Left", () => _actions.SetDock("left")),
            MakeButton("Right", () => _actions.SetDock("right")),
            MakeButton("Undock", () => _actions.SetDock("none")),
        }));

        stack.Children.Add(Caption("Button size (DIPs) — what fits your finger"));
        stack.Children.Add(Row(new[]
        {
            MakeButton("Compact", () => ApplySize(48, 48)),
            MakeButton("Default", () => ApplySize(62, 62)),
            MakeButton("Large", () => ApplySize(124, 80)),
            MakeButton("XL", () => ApplySize(156, 100)),
        }));

        _width = MakeSizeBox(cfg.ButtonWidth);
        _height = MakeSizeBox(cfg.ButtonHeight);
        foreach (var box in new[] { _width, _height })
        {
            box.TextChanged += (_, _) => { if (!_inSizeSync) ArmSizeTimer(); };
            box.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Return) ApplySizeFromBoxes();
            };
            box.LostFocus += (_, _) => ApplySizeFromBoxes();
        }
        stack.Children.Add(Row2(_width, _height));
        stack.Children.Add(Caption("DIPs. Applies as you type; bigger buttons are easier to hit."));

        stack.Children.Add(MakeButton("Exit", _actions.Exit));
        stack.Children.Add(Caption("Settings are stored in:"));
        stack.Children.Add(new TextBlock
        {
            Text = configPath,
            FontSize = 11,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });

        Content = new ScrollViewer
        {
            Content = stack,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    private static string Label(string text, bool on) => $"{text}: {(on ? "on" : "off")}";

    private static Button MakeButton(string header, Action action, double? width = null)
    {
        var b = new Button
        {
            Content = header,
            Height = RowHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 15,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (width is not null) b.Width = width.Value;
        b.Click += (_, _) => action();
        return b;
    }

    private static Control Row(Control[] controls)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in controls)
        {
            if (c is Button b) b.Width = SmallButtonWidth;
            panel.Children.Add(c);
        }
        return panel;
    }

    /// <summary>Width x height editors for the target button size.</summary>
    private Control Row2(TextBox width, TextBox height)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(width);
        panel.Children.Add(new TextBlock
        {
            Text = "×",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 15,
        });
        panel.Children.Add(height);
        return panel;
    }

    private static TextBox MakeSizeBox(int value) => new()
    {
        Text = value.ToString(),
        Width = 92,
        HorizontalContentAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// Short debounce so typing a size applies once, after the last keystroke,
    /// instead of reflowing the pad for every intermediate value.
    /// </summary>
    private void ArmSizeTimer()
    {
        if (_sizeTimer is null)
        {
            _sizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _sizeTimer.Tick += (_, _) => { _sizeTimer!.Stop(); ApplySizeFromBoxes(); };
        }
        _sizeTimer.Stop();
        _sizeTimer.Start();
    }

    private void ApplySizeFromBoxes()
    {
        _sizeTimer?.Stop();
        if (!int.TryParse(_width.Text, out var w)) return;
        if (!int.TryParse(_height.Text, out var h)) return;
        ApplySize(w, h);
    }

    private void ApplySize(int width, int height)
    {
        var w = Math.Clamp(width, MinEdge, MaxEdge);
        var h = Math.Clamp(height, MinEdge, MaxEdge);

        _inSizeSync = true;
        _width.Text = w.ToString();
        _height.Text = h.ToString();
        _inSizeSync = false;

        _actions.SetButtonSize(w, h);
    }

    private static Control Caption(string text) => new TextBlock
    {
        Text = text,
        FontSize = 12,
        Foreground = Brushes.Gray,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static Control Separator() => new Border
    {
        Height = 1,
        Margin = new Thickness(0, 8),
        Background = Brushes.Gray,
        Opacity = 0.35,
    };
}
