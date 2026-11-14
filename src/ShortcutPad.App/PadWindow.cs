using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ShortcutPad;

public sealed class PadWindow : Window
{
    private readonly string _configPath;
    private readonly IPadPlatform _platform;
    private readonly IKeystrokeSender _sender;
    private PadConfig _cfg = new();

    private Grid _grid = null!;
    private TextBlock _status = null!;
    private TrayIcon? _tray;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _fgTimer;
    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _dockTimer;
    private bool _suppressWatcher;
    private PixelPoint _lastPosition;

    private const int ButtonWidth = 100;
    private const int ButtonHeight = 62;    // DIPs: comfortably touch-sized
    private const int IconSize = 30;
    private const int LabelWithIcon = 13;
    private const int LabelNoIcon = 20;     // glanceable when there is no icon
    private const int SnapDistance = 24;

    public PadWindow(string configPath)
    {
        _configPath = configPath;
        _platform = PadPlatform.Create();
        _sender = PadPlatformSender.Create();

        BuildUi();
        Title = "Shortcut Pad";
        ShowInTaskbar = true;
        Topmost = true;
        SizeToContent = SizeToContent.Manual;
        MinWidth = 150;
        MinHeight = 110;
        FontFamily = new FontFamily("Segoe UI");

        _cfg = ConfigStore.Load(_configPath);
        ApplyConfig();
        InitTimers();
        InitTray();
        InitWatcher();

        PositionChanged += OnPositionChanged;
        SizeChanged += OnSizeChanged;

    }

    // ------------------------------------------------------------ UI

    private void BuildUi()
    {
        _grid = new Grid { Margin = new Thickness(6) };

        _status = new TextBlock
        {
            Margin = new Thickness(6, 0, 6, 4),
            FontSize = 12,
            Foreground = Brushes.Gray,
            Text = "ready",
        };

        var panel = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        panel.Children.Add(_status);
        panel.Children.Add(_grid);

        Content = panel;
        ContextMenu = BuildContextMenu();
    }

    private void BuildGrid()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();

        var cols = Math.Max(1, _cfg.Buttons.Count == 0 ? 1 : _cfg.Columns);
        for (var c = 0; c < cols; c++)
            _grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        var rows = Math.Max(1, (int)Math.Ceiling(_cfg.Buttons.Count / (double)cols));
        // Star rows: the pad fills whatever height it is given, so a docked pad
        // stretches its buttons over the whole work-area height.
        for (var r = 0; r < rows; r++)
            _grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        for (var i = 0; i < _cfg.Buttons.Count; i++)
        {
            var b = _cfg.Buttons[i];
            var btn = new Button
            {
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinWidth = 0,
                MinHeight = 0,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            // A null Background makes a control transparent to hit-testing in
            // Avalonia, so every button needs an explicit brush.
            btn.Background = string.IsNullOrWhiteSpace(b.Color)
                ? new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
                : (IBrush)new BrushConverter().ConvertFromString(b.Color);
            // Always set an explicit foreground too: the icon's Fill binds to it.
            btn.Foreground = btn.Background is ISolidColorBrush solid && Luminance(solid.Color) < 0.55f
                ? Brushes.White
                : Brushes.Black;

            var icon = PadIcons.ResolveFor(b);
            if (icon is not null && PadIcons.TryGet(icon, out var pathData))
            {
                var iconPath = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(pathData),
                    Width = IconSize,
                    Height = IconSize,
                    Stretch = Stretch.Uniform,
                    Fill = btn.Foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var caption = new TextBlock
                {
                    Text = b.Label,
                    FontSize = LabelWithIcon,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                };
                var stack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                stack.Children.Add(iconPath);
                stack.Children.Add(caption);
                btn.Content = stack;
            }
            else
            {
                // No icon available: make the label itself large and legible.
                btn.Content = new TextBlock
                {
                    Text = b.Label,
                    FontSize = LabelNoIcon,
                    FontWeight = FontWeight.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            var index = i;
            btn.Click += (_, _) => Execute(_cfg.Buttons[index]);
            _grid.Children.Add(btn);
            Grid.SetRow(btn, i / cols);
            Grid.SetColumn(btn, i % cols);
        }

        Width = cols * ButtonWidth + 12;
        Height = rows * ButtonHeight + 34;
        ApplyFlow();
    }

    private static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    // ------------------------------------------------------------ reflow

    private bool _flowSuspended;

    private bool IsDocked => _cfg.Dock is "left" or "right";

    /// <summary>
    /// Recompute the column/row counts from the current window size.
    ///
    /// Auto flow: the counts follow the window (more width -> more columns),
    /// and the window's minimum size keeps the buttons at or above the target
    /// size instead of squeezing them. Fixed: the configured column count is
    /// used and the buttons simply stretch.
    /// </summary>
    private void ApplyFlow()
    {
        var n = _cfg.Buttons.Count;
        if (n == 0 || _grid.Children.Count == 0) return;

        const double margin = 12.0;    // the grid's margin, DIPs
        const double status = 20.0;    // the status line, DIPs

        // ClientSize is stale until the first layout, so derive from Width
        // (which ApplyConfig sets explicitly) instead.
        var clientW = Math.Max(1.0, Width - margin);

        var cols = _cfg.AutoFlow
            ? Math.Max(1, Math.Min(n, (int)(clientW / Math.Max(20, _cfg.ButtonWidth))))
            : Math.Max(1, _cfg.Columns);
        var rows = Math.Max(1, (int)Math.Ceiling(n / (double)cols));

        _flowSuspended = true;
        try
        {
            _grid.ColumnDefinitions.Clear();
            for (var c = 0; c < cols; c++)
                _grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            _grid.RowDefinitions.Clear();
            for (var r = 0; r < rows; r++)
                _grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            for (var i = 0; i < _grid.Children.Count && i < n; i++)
            {
                Grid.SetRow(_grid.Children[i], i / cols);
                Grid.SetColumn(_grid.Children[i], i % cols);
            }

            if (!IsDocked)
            {
                // The column formula already keeps buttons at or above the
                // target width, so only one column is a hard floor. Height is
                // different: more rows means the window must grow, otherwise
                // the buttons would be squeezed short.
                MinWidth = _cfg.ButtonWidth + margin;
                MinHeight = rows * _cfg.ButtonHeight + margin + status + ChromeHeight();
            }
            Status($"{cols} x {rows}");
        }
        finally { _flowSuspended = false; }
    }

    /// <summary>Title-bar height in DIPs, measured from the window itself.</summary>
    private double ChromeHeight()
    {
        try
        {
            var scale = RenderScale();
            var origin = Avalonia.VisualExtensions.PointToScreen(this, new Point(0, 0));
            return (origin.Y - Position.Y) / scale;
        }
        catch { return 28.0; }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_flowSuspended) return;
        ApplyFlow();

        if (_cfg.AutoFlow && !IsDocked)
        {
            _cfg.Width = ClientSize.Width;
            _cfg.Height = ClientSize.Height;
            QueueSave();
        }
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(MakeItem("Show / hide", ToggleVisible));
        menu.Items.Add(MakeItem("Reload config", Reload));
        menu.Items.Add(MakeItem("Edit config", () =>
            Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true })));
        menu.Items.Add(new Separator());
        foreach (var opacity in new[] { 1.0, 0.85, 0.7, 0.5 })
            menu.Items.Add(MakeItem($"Opacity {opacity * 100:0}%", () => SetOpacity(opacity)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeCheckable("Auto flow", _cfg.AutoFlow, on =>
        {
            _cfg.AutoFlow = on;
            ApplyFlow();
            Status($"auto flow {(on ? "on" : "off")}");
            QueueSave();
        }));
        menu.Items.Add(MakeCheckable("Resizable", _cfg.Resizable, on =>
        {
            _cfg.Resizable = on;
            CanResize = on && !IsDocked;
            Status($"resizable {(on ? "on" : "off")}");
            QueueSave();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Dock left", () => SetDock("left")));
        menu.Items.Add(MakeItem("Dock right", () => SetDock("right")));
        menu.Items.Add(MakeItem("Undock", () => SetDock("none")));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Exit", Exit));
        return menu;
    }

    private static MenuItem MakeItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static string Mark(string header, bool on) => (on ? "✓ " : "   ") + header;

    private static MenuItem MakeCheckable(string header, bool isChecked, Action<bool> setter)
    {
        var item = new MenuItem { Header = Mark(header, isChecked) };
        item.Click += (_, _) =>
        {
            isChecked = !isChecked;
            item.Header = Mark(header, isChecked);
            setter(isChecked);
        };
        return item;
    }

    private static NativeMenuItem MakeTrayCheckable(string header, bool isChecked, Action<bool> setter)
    {
        var item = new NativeMenuItem { Header = Mark(header, isChecked) };
        item.Click += (_, _) =>
        {
            isChecked = !isChecked;
            item.Header = Mark(header, isChecked);
            setter(isChecked);
        };
        return item;
    }

    // ------------------------------------------------------------ config

    private void ApplyConfig()
    {
        Topmost = _cfg.AlwaysOnTop;
        Opacity = Math.Clamp(_cfg.Opacity <= 0 ? 1.0 : _cfg.Opacity, 0.1, 1.0);
        BuildGrid();

        if (_cfg.Dock is "left" or "right")
        {
            ApplyDock(_cfg.Dock);
        }
        else
        {
            if (_cfg.X >= 0 && _cfg.Y >= 0 && _cfg.X < (Screens.Primary?.Bounds.Width ?? int.MaxValue))
                Position = new PixelPoint(_cfg.X, _cfg.Y);

            // Restore the remembered size, then reflow it into a grid.
            Width = Math.Max(MinWidth, _cfg.Width);
            Height = Math.Max(MinHeight, _cfg.Height);
            CanResize = _cfg.Resizable;
            ApplyFlow();
        }

        _platform.SelfId = OperatingSystem.IsLinux() || OperatingSystem.IsWindows()
            ? TryGetWindowHandle().ToString()
            : (MacSelfBundleId() ?? string.Empty);
    }

    private void Reload()
    {
        try
        {
            _cfg = ConfigStore.Load(_configPath);
            ApplyConfig();
            Status("config reloaded");
        }
        catch (Exception ex)
        {
            Status("reload failed: " + ex.Message);
        }
    }

    private void SetOpacity(double opacity)
    {
        Opacity = opacity;
        _cfg.Opacity = opacity;
        Status($"opacity {opacity * 100:0}%");
        QueueSave();
    }

    private void SetDock(string side)
    {
        if (side == "none")
        {
            _cfg.Dock = "none";
            MinWidth = 150;
            MinHeight = 110;
            CanResize = _cfg.Resizable;
            ApplyFlow();
            Status("undocked");
        }
        else
        {
            ApplyDock(side);
            _cfg.Dock = side;
            Status($"docked {side}");
        }
        QueueSave();
    }

    private void ApplyDock(string side)
    {
        var screen = CurrentScreen();
        if (screen is null) return;
        var wa = screen.WorkingArea;                       // physical pixels
        var scale = RenderScale();
        var origin = Avalonia.VisualExtensions.PointToScreen(this, new Point(0, 0));
        var topChrome = (origin.Y - Position.Y) / scale;   // title bar, DIPs
        var sideChrome = (origin.X - Position.X) / scale;  // border, DIPs
        var width = Math.Clamp(Width, 200.0, Math.Max(200.0, wa.Width / scale - 2 * sideChrome));
        Width = width;
        Height = wa.Height / scale - topChrome - sideChrome;
        Position = side == "left"
            ? new PixelPoint(wa.X, wa.Y)
            : new PixelPoint((int)(wa.Right - width * scale - 2 * sideChrome * scale), wa.Y);
        Topmost = true;
        CanResize = false;
    }

    /// <summary>Physical pixels per DIP, measured from the window itself.</summary>
    private double RenderScale()
    {
        try
        {
            var a = Avalonia.VisualExtensions.PointToScreen(this, new Point(0, 0));
            var b = Avalonia.VisualExtensions.PointToScreen(this, new Point(ClientSize.Width, ClientSize.Height));
            var w = Math.Abs(b.X - a.X);
            return ClientSize.Width > 0 ? w / ClientSize.Width : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    private void QueueSave()
    {
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    private void SaveConfig()
    {
        try
        {
            if (_cfg.Dock == "none")
            {
                _cfg.X = Position.X;
                _cfg.Y = Position.Y;
            }
            _suppressWatcher = true;
            ConfigStore.Save(_configPath, _cfg);
            _ = Task.Run(async () => { await Task.Delay(500); _suppressWatcher = false; });
        }
        catch (Exception ex)
        {
            Status("save failed: " + ex.Message);
        }
    }

    // ------------------------------------------------------------ timers / tray / watcher

    private void InitTimers()
    {
        _fgTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _fgTimer.Tick += (_, _) => _platform.Poll();
        _fgTimer.Start();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer!.Stop(); SaveConfig(); };

        _dockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _dockTimer.Tick += (_, _) => { _dockTimer!.Stop(); SnapIfNeeded(); };
    }

    private void InitTray()
    {
        try
        {
            using var stream = GetType().Assembly.GetManifestResourceStream("ShortcutPad.App.icon.png");
            if (stream is null) return;
            var icon = new WindowIcon(stream);
            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "Shortcut Pad",
                IsVisible = true,
                Menu = BuildTrayMenu(),
            };
            _tray.Clicked += (_, _) => ToggleVisible();
            Application.Current!.SetValue(TrayIcon.IconsProperty, new TrayIcons { _tray });
        }
        catch (Exception ex)
        {
            Status("tray unavailable: " + ex.Message);
        }
    }

    private NativeMenu BuildTrayMenu()
    {
        var menu = new NativeMenu();
        AddTrayItem(menu, "Show / hide", ToggleVisible);
        AddTrayItem(menu, "Reload config", Reload);
        AddTrayItem(menu, "Edit config", () =>
            Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true }));
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(MakeTrayCheckable("Auto flow", _cfg.AutoFlow, on =>
        {
            _cfg.AutoFlow = on;
            ApplyFlow();
            Status($"auto flow {(on ? "on" : "off")}");
            QueueSave();
        }));
        menu.Items.Add(MakeTrayCheckable("Resizable", _cfg.Resizable, on =>
        {
            _cfg.Resizable = on;
            CanResize = on && !IsDocked;
            Status($"resizable {(on ? "on" : "off")}");
            QueueSave();
        }));
        foreach (var opacity in new[] { 1.0, 0.85, 0.7, 0.5 })
            AddTrayItem(menu, $"Opacity {opacity * 100:0}%", () => SetOpacity(opacity));
        AddTrayItem(menu, "Dock left", () => SetDock("left"));
        AddTrayItem(menu, "Dock right", () => SetDock("right"));
        AddTrayItem(menu, "Undock", () => SetDock("none"));
        AddTrayItem(menu, "Exit", Exit);
        return menu;
    }

    private static void AddTrayItem(NativeMenu menu, string header, Action action)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void InitWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_configPath)!, Path.GetFileName(_configPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += (_, _) => Dispatcher.UIThread.Post(ReloadIfIdle);
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(ReloadIfIdle);
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null;
        }
    }

    private void ReloadIfIdle()
    {
        if (!_suppressWatcher) Reload();
    }

    // ------------------------------------------------------------ behaviour

    private void OnPositionChanged(object? sender, PixelPointEventArgs e)
    {
        _lastPosition = e.Point;
        if (_cfg.Dock != "none") return;
        _dockTimer?.Stop();
        _dockTimer?.Start();
        QueueSave();
    }

    private void SnapIfNeeded()
    {
        if (_cfg.Dock != "none") return;
        var screen = Screens.ScreenFromPoint(_lastPosition) ?? Screens.Primary;
        if (screen is null) return;
        var wa = screen.WorkingArea;
        var scale = RenderScale();
        var snap = SnapDistance * scale;

        if (_lastPosition.X <= wa.X + snap)
            SetDock("left");
        else if (_lastPosition.X + Width * scale >= wa.Right - snap)
            SetDock("right");
    }

    private Avalonia.Platform.Screen? CurrentScreen()
    {
        try { return Screens.ScreenFromWindow(this); }
        catch { return Screens.ScreenFromPoint(Position) ?? Screens.Primary; }
    }

    private void ToggleVisible()
    {
        if (IsVisible) Hide();
        else { Show(); Activate(); }
    }

    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is ClassicDesktopStyleApplicationLifetime lifetime)
            lifetime.Shutdown();
    }

    private void Execute(PadButton b)
    {
        try
        {
            // Hand focus back to the app the user was in before clicking the pad.
            var target = _platform.LastOther;
            var restored = target is not null && _platform.Restore(target);

            if (!string.IsNullOrWhiteSpace(b.Run))
            {
                Process.Start(new ProcessStartInfo(b.Run) { UseShellExecute = true });
                Status($"{b.Label}: run {b.Run}");
            }
            else if (!string.IsNullOrWhiteSpace(b.Text))
            {
                _sender.TypeText(b.Text);
                Status($"{b.Label}: typed{(restored ? "" : " (focus lost)")}");
            }
            else if (!string.IsNullOrWhiteSpace(b.Keys))
            {
                _sender.Send(b.Keys);
                Status($"{b.Label}: {b.Keys}{(restored ? "" : " (focus lost)")}");
            }
            else
            {
                Status($"{b.Label}: no action configured");
            }
        }
        catch (Exception ex)
        {
            Status($"{b.Label}: {ex.Message}");
        }
    }

    private void Status(string message) => _status.Text = message;

    protected override void OnClosed(EventArgs e)
    {
        SaveConfig();
        _tray?.Dispose();
        _watcher?.Dispose();
        _fgTimer?.Stop();
        _saveTimer?.Stop();
        _dockTimer?.Stop();
        base.OnClosed(e);
    }

    private IntPtr TryGetWindowHandle()
    {
        try { return TryGetPlatformHandle().Handle; }
        catch { return IntPtr.Zero; }
    }

    private static string? MacSelfBundleId() => null;
}

/// <summary>Keeps the sender creation in one place.</summary>
public static class PadPlatformSender
{
    public static IKeystrokeSender Create() =>
        OperatingSystem.IsWindows() ? new WindowsKeystrokeSender()
        : OperatingSystem.IsMacOS() ? new MacKeystrokeSender()
        : OperatingSystem.IsLinux() ? new LinuxKeystrokeSender()
        : throw new PlatformNotSupportedException();
}
