using System.Diagnostics;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ShortcutPad;

public sealed class PadWindow : Window
{
    private readonly string _configPath;
    private readonly IPadPlatform _platform;
    private readonly IKeystrokeSender _sender;
    private PadConfig _cfg = new();

    private Grid _grid = null!;
    private TextBlock _title = null!;
    private DockPanel _statusRow = null!;
    private DispatcherTimer? _titleTimer;
    private int _visibleButtons;
    private Button _menuButton = null!;
    private TrayIcon? _tray;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _fgTimer;
    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _dockTimer;
    private bool _suppressWatcher;
    private PixelPoint _lastPosition;

    private const int SnapDistance = 24;

    /// <summary>Bounds for the user-editable target button size, in DIPs.</summary>
    private const int MinButtonEdge = 32;
    private const int MaxButtonEdge = 400;
    private const int IconSize = 30;
    private const int LabelWithIcon = 13;
    private const int LabelNoIcon = 20;     // glanceable when there is no icon
    private const double GridMargin = 12.0; // matches the grid's XAML margin
    private const double StatusHeight = 40.0; // title bar: title text + menu button
    private const double TitleFontSize = 13.0;
    private const double TitleMinFontSize = 9.0;
    // Room the title never gets: the menu button plus both margins.
    private const double TitleReserve = 62.0;

    private readonly record struct Placement(int Row, int Col, int SpanY, int SpanX);

    public PadWindow(string configPath)
    {
        _configPath = configPath;
        _platform = PadPlatform.Create();
        _sender = PadPlatformSender.Create();

        BuildUi();
        Title = "Shortcut Pad";
        Icon = AppIcon.TryLoad();
        // No native caption: border-only decorations mean no minimize and no
        // maximize button at all. The top bar doubles as the title bar (see
        // BuildUi), and the thick frame stays, so the window is still resizable.
        SystemDecorations = SystemDecorations.BorderOnly;
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
        Opened += (_, _) => WindowChrome.HideMaximize(this, _platform);

    }

    // ------------------------------------------------------------ UI

    /// <summary>
    /// Resizing follows the user's setting, but the maximize control is never
    /// wanted. Changing <see cref="Window.CanResize"/> makes Avalonia rebuild
    /// the native frame, which restores the platform's maximize button, so it
    /// is hidden again here.
    /// </summary>
    private void SetResizable(bool value)
    {
        CanResize = value;
        WindowChrome.HideMaximize(this, _platform);
    }

    private void BuildUi()
    {
        _grid = new Grid { Margin = new Thickness(6) };

        _title = new TextBlock
        {
            Margin = new Thickness(10, 0, 6, 4),
            FontSize = TitleFontSize,
            Opacity = 0.85,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "ShortcutPad",
        };

        // A visible, touch-sized entry point to the same options menu that the
        // tray icon and the right-click open: on a touch screen neither of those
        // is obvious or easy to hit.
        _menuButton = new Button
        {
            Content = "☰",
            FontSize = 17,
            Width = 46,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(_menuButton, "Options, settings and the button editor");
        _menuButton.Click += (_, _) => OpenOptions();

        var statusRow = _statusRow = new DockPanel { MinHeight = StatusHeight };
        // A panel with no background is not hit-testable, so the bar would never
        // see the press that starts a drag.
        statusRow.Background = Brushes.Transparent;
        DockPanel.SetDock(_menuButton, Dock.Right);
        statusRow.Children.Add(_menuButton);
        statusRow.Children.Add(_title);
        ToolTip.SetTip(statusRow, "Drag this bar to move the pad");

        // With no native title bar, the top bar is the drag grip. A press on
        // the menu button still just opens the menu. The move is done by hand
        // (BeginMoveDrag is a no-op for a border-only window) and the pointer is
        // captured so the drag keeps tracking outside the bar.
        statusRow.PointerPressed += (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<Button>() is not null) return;
            if (!e.GetCurrentPoint(statusRow).Properties.IsLeftButtonPressed) return;
            BeginMoveDrag(e);
        };

        var panel = new DockPanel();
        DockPanel.SetDock(statusRow, Dock.Top);
        panel.Children.Add(statusRow);
        panel.Children.Add(_grid);

        // The pad itself, plus a hit-test-transparent layer that carries the
        // drag ghost.
        _ghostLayer = new Canvas { IsHitTestVisible = false };
        var overlay = new Grid();
        overlay.Children.Add(panel);
        overlay.Children.Add(_ghostLayer);
        Content = overlay;

        ContextMenu = BuildContextMenu();
    }

    /// <summary>
    /// Open the options window. A window rather than a popup menu: popups do not
    /// render reliably for this always-on-top, transparent window, and large
    /// buttons are easier to hit on a touch screen.
    /// </summary>
    private void OpenOptions()
    {
        var actions = new OptionsActions
        {
            OpenEditor = OpenButtonEditor,
            SetOpacity = SetOpacity,
            SetButtonSize = (w, h) => ApplyButtonSize(w, h),
            Exit = Exit,
        };
        new OptionsWindow(_cfg, _configPath, actions).Show(this);
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
            if (!string.IsNullOrWhiteSpace(b.Foreground) &&
                new BrushConverter().ConvertFromString(b.Foreground) is IBrush fgBrush)
                btn.Foreground = fgBrush;
            if (b.CornerRadius is > 0)
                btn.CornerRadius = new CornerRadius(b.CornerRadius.Value);

            var icon = PadIcons.ResolveFor(b);
            if (icon is not null && PadIcons.TryGet(icon, out var pathData))
            {
                var iconPath = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse(pathData),
                    Width = b.IconSize ?? IconSize,
                    Height = b.IconSize ?? IconSize,
                    Stretch = Stretch.Uniform,
                    Fill = btn.Foreground,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                var caption = new TextBlock
                {
                    Text = b.Label,
                    FontSize = b.FontSize ?? LabelWithIcon,
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
                    FontSize = b.FontSize ?? LabelNoIcon,
                    FontWeight = FontWeight.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            // Hover (or touch-and-hold) shows what the button will do.
            ToolTip.SetTip(btn, Describe(b));

            var index = i;
            btn.Click += (_, _) => { if (!_suppressClick) Execute(_cfg.Buttons[index]); };
            btn.AddHandler<PointerPressedEventArgs>(PointerPressedEvent, (_, e) =>
            {
                if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
                _dragIndex = index;
                _dragStart = e.GetPosition(btn);
                _dragActive = false;
                _dragTarget = -1;
                e.Pointer.Capture(btn);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            btn.AddHandler<PointerEventArgs>(PointerMovedEvent, (_, e) =>
            {
                if (_dragIndex != index) return;
                if (!e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed) return;
                if (!_dragActive)
                {
                    // A click and a drag share the same press: only once the
                    // pointer has travelled does this become a reorder.
                    var offset = e.GetPosition(btn) - _dragStart;
                    if (Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) < DragThreshold) return;
                    _dragActive = true;
                    _suppressClick = true;
                    btn.Opacity = 0.35;
                    ShowGhost(_cfg.Buttons[index], btn, e.GetPosition(_ghostLayer));
                    Status($"dragging {GetLabel(index)} - drop it on another slot");
                }
                MoveGhost(e.GetPosition(_ghostLayer));
                UpdateDragTarget(e);
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            btn.AddHandler<PointerReleasedEventArgs>(PointerReleasedEvent, (_, e) =>
            {
                if (_dragIndex != index) return;
                e.Pointer.Capture(null);
                if (_dragActive) EndDrag(e);
                _dragIndex = -1;
                _dragActive = false;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            btn.AddHandler<PointerCaptureLostEventArgs>(PointerCaptureLostEvent, (_, _) =>
            {
                if (_dragIndex != index) return;
                CancelDrag();
                _dragIndex = -1;
                _dragActive = false;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            _grid.Children.Add(btn);
            Grid.SetRow(btn, i / cols);
            Grid.SetColumn(btn, i % cols);
        }

        ApplyFlow();
    }

    // ------------------------------------------------------------ drag to reorder

    private int _dragIndex = -1;
    private Point _dragStart;
    private bool _dragActive;
    private bool _suppressClick;
    private int _dragTarget = -1;
    private IBrush? _targetOriginalBrush;
    private Canvas _ghostLayer = null!;
    private Border? _ghost;
    private const double DragThreshold = 6.0;
    private static readonly IBrush TargetBrush =
        new SolidColorBrush(Color.FromRgb(0x2D, 0x6C, 0xDF));

    /// <summary>Index of the visible button under a point in grid coordinates.</summary>
    private int SlotAt(Point p)
    {
        for (var i = 0; i < _grid.Children.Count; i++)
        {
            if (_grid.Children[i] is not Visual v || !v.IsVisible) continue;
            if (v.Bounds.Contains(p)) return i;
        }
        return -1;
    }

    private void UpdateDragTarget(PointerEventArgs e)
    {
        var slot = SlotAt(e.GetPosition(_grid));
        if (slot < 0 || slot == _dragTarget) return;

        RestoreTarget();
        if (_grid.Children[slot] is not Button target) return;
        _dragTarget = slot;
        _targetOriginalBrush = target.Background;
        target.Background = TargetBrush;
    }

    private void RestoreTarget()
    {
        if (_dragTarget >= 0 && _dragTarget < _grid.Children.Count
            && _grid.Children[_dragTarget] is Button target)
            target.Background = _targetOriginalBrush;
        _targetOriginalBrush = null;
        _dragTarget = -1;
    }

    private void EndDrag(PointerEventArgs e)
    {
        var from = _dragIndex;
        var target = SlotAt(e.GetPosition(_grid));

        if (from >= 0 && from < _grid.Children.Count)
            _grid.Children[from].Opacity = 1.0;
        RestoreTarget();
        HideGhost();

        // A drop that misses every button still counts: take the nearest slot.
        if (target < 0) target = NearestSlot(e.GetPosition(_grid));
        if (target >= 0 && target != from) MoveButton(from, target);

        // The Button still raises its Click for this release; swallow that one.
        Dispatcher.UIThread.Post(() => _suppressClick = false, DispatcherPriority.Input);
    }

    private void CancelDrag()
    {
        if (_dragIndex >= 0 && _dragIndex < _grid.Children.Count)
            _grid.Children[_dragIndex].Opacity = 1.0;
        RestoreTarget();
        HideGhost();
        _suppressClick = false;
    }

    private string GetLabel(int index) =>
        index >= 0 && index < _cfg.Buttons.Count ? _cfg.Buttons[index].Label : "button";

    /// <summary>
    /// A copy of the dragged button that follows the pointer, so the gesture
    /// reads as moving the button rather than just lighting up a slot.
    /// </summary>
    private void ShowGhost(PadButton b, Button visual, Point p)
    {
        HideGhost();
        var size = visual.Bounds.Size;
        _ghost = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Background = visual.Background,
            BorderBrush = visual.Foreground,
            BorderThickness = new Thickness(1),
            CornerRadius = visual.CornerRadius,
            Opacity = 0.75,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = b.Label,
                FontSize = 15,
                FontWeight = FontWeight.SemiBold,
                Foreground = visual.Foreground,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4),
            },
        };
        _ghostLayer.Children.Add(_ghost);
        MoveGhost(p);
    }

    private void MoveGhost(Point p)
    {
        if (_ghost is null) return;
        Canvas.SetLeft(_ghost, p.X - _ghost.Width / 2);
        Canvas.SetTop(_ghost, p.Y - _ghost.Height / 2);
    }

    private void HideGhost()
    {
        if (_ghost is null) return;
        _ghostLayer.Children.Remove(_ghost);
        _ghost = null;
    }

    /// <summary>
    /// The visible slot nearest to a point in grid coordinates - used when the
    /// drop lands between buttons.
    /// </summary>
    private int NearestSlot(Point p)
    {
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 0; i < _grid.Children.Count; i++)
        {
            if (_grid.Children[i] is not Visual v || !v.IsVisible) continue;
            var cx = v.Bounds.X + v.Bounds.Width / 2;
            var cy = v.Bounds.Y + v.Bounds.Height / 2;
            var d = (cx - p.X) * (cx - p.X) + (cy - p.Y) * (cy - p.Y);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    /// <summary>Moves a button to another slot and persists the new order.</summary>
    private void MoveButton(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _cfg.Buttons.Count || to >= _cfg.Buttons.Count || from == to)
            return;

        var item = _cfg.Buttons[from];
        _cfg.Buttons.RemoveAt(from);
        _cfg.Buttons.Insert(to, item);
        BuildGrid();
        SaveConfig();
        Status($"moved {item.Label} to slot {to + 1}");
    }

    /// <summary>
    /// What a button does, for its tooltip. Same precedence as
    /// <see cref="Execute"/>: run a program, then type text, then send keys.
    /// </summary>
    private static string Describe(PadButton b)
    {
        if (!string.IsNullOrWhiteSpace(b.Run)) return $"Run: {b.Run}";
        if (!string.IsNullOrWhiteSpace(b.Text)) return $"Type text: {b.Text}";
        if (!string.IsNullOrWhiteSpace(b.Keys)) return $"Keystrokes: {b.Keys}";
        return "No action configured";
    }

    /// <summary>Relative luminance, normalized to 0..1.</summary>
    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    // ------------------------------------------------------------ reflow

    private bool _flowSuspended;

    private bool IsDocked => _cfg.Dock is "left" or "right";

    /// <summary>
    /// The title bar's real height. It is <see cref="StatusHeight"/> until the
    /// title wraps onto more than one line, and the grid has to subtract
    /// whatever it actually is or the last row would sit under the bar.
    /// </summary>
    private double BarHeight() => Math.Max(StatusHeight, _statusRow?.Bounds.Height ?? 0);

    /// <summary>
    /// Compute the grid for the space the window actually has: the column count
    /// from the width and the row count from the height. Auto flow no longer
    /// grows rows to fit every button — whatever does not fit is left out, so
    /// the window stays freely resizable. Fixed mode keeps the configured column
    /// count and lets the buttons stretch.
    /// </summary>
    private (int cols, int rows, Placement?[] slots) ComputeGrid(double widthDips, double heightDips)
    {
        var n = _cfg.Buttons.Count;
        if (n == 0) return (1, 1, Array.Empty<Placement?>());

        var cols = _cfg.AutoFlow
            ? Math.Max(1, Math.Min(n, (int)((widthDips - GridMargin) / Math.Max(20, _cfg.ButtonWidth))))
            : Math.Max(1, Math.Min(n, _cfg.Columns));

        var rows = _cfg.AutoFlow
            ? Math.Max(1, Math.Min(n, (int)((heightDips - GridMargin - BarHeight()) / Math.Max(20, _cfg.ButtonHeight))))
            : Math.Max(1, (int)Math.Ceiling(n / (double)cols));

        return (cols, rows, PackVisible(_cfg.Buttons, cols, rows));
    }

    /// <summary>
    /// Place buttons left-to-right, top-to-bottom, honouring their spans. A
    /// button that does not fit in the available cols x rows gets a null slot
    /// (it is hidden) instead of squeezing the rest or growing the window.
    /// </summary>
    private static Placement?[] PackVisible(IReadOnlyList<PadButton> buttons, int cols, int rows)
    {
        var taken = new bool[rows * cols];
        var slots = new Placement?[buttons.Count];
        for (var i = 0; i < buttons.Count; i++)
        {
            var b = buttons[i];
            var sx = Math.Clamp(b.SpanX < 1 ? 1 : b.SpanX, 1, cols);
            var sy = Math.Clamp(b.SpanY < 1 ? 1 : b.SpanY, 1, rows);
            for (var r = 0; r <= rows - sy; r++)
            {
                var placed = false;
                for (var c = 0; c <= cols - sx; c++)
                {
                    if (!IsFree(taken, cols, rows, r, c, sy, sx)) continue;
                    Occupy(taken, cols, r, c, sy, sx);
                    slots[i] = new Placement(r, c, sy, sx);
                    placed = true;
                    break;
                }
                if (placed) break;
            }
        }
        return slots;
    }

    private static bool IsFree(bool[] taken, int cols, int rows, int r, int c, int sy, int sx)
    {
        if (r < 0 || c < 0 || r + sy > rows || c + sx > cols) return false;
        for (var y = r; y < r + sy; y++)
            for (var x = c; x < c + sx; x++)
                if (taken[y * cols + x]) return false;
        return true;
    }

    private static void Occupy(bool[] taken, int cols, int r, int c, int sy, int sx)
    {
        for (var y = r; y < r + sy; y++)
            for (var x = c; x < c + sx; x++)
                taken[y * cols + x] = true;
    }

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

        // The space the grid actually gets. ClientSize is authoritative; fall
        // back to the window size minus the title bar before the first layout.
        var availW = ClientSize.Width > 0 ? ClientSize.Width : Width;
        var availH = ClientSize.Height > 0 ? ClientSize.Height : Math.Max(1, Height - ChromeHeight());

        var (cols, rows, slots) = ComputeGrid(availW, availH);

        _flowSuspended = true;
        try
        {
            _grid.ColumnDefinitions.Clear();
            for (var c = 0; c < cols; c++)
                _grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            _grid.RowDefinitions.Clear();
            for (var r = 0; r < rows; r++)
                _grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            var shown = 0;
            for (var i = 0; i < _grid.Children.Count && i < slots.Length; i++)
            {
                var child = _grid.Children[i];
                var slot = slots[i];
                child.IsVisible = slot is not null;
                // Reset the attached values too: the grid measures children even
                // when they are not visible, and a stale row/span from a larger
                // previous layout would be out of range.
                Grid.SetRow(child, slot?.Row ?? 0);
                Grid.SetColumn(child, slot?.Col ?? 0);
                Grid.SetRowSpan(child, slot?.SpanY ?? 1);
                Grid.SetColumnSpan(child, slot?.SpanX ?? 1);
                if (slot is null) continue;
                shown++;
            }

            if (!IsDocked)
            {
                if (_cfg.AutoFlow)
                {
                    // Freely resizable: whatever does not fit is left out. The
                    // floor still has to fit the title bar, or "ShortcutPad"
                    // would wrap mid-word.
                    MinWidth = Math.Max(GridMargin + 80,
                        TitleReserve + MeasureTitle("ShortcutPad", TitleFontSize));
                    MinHeight = GridMargin + BarHeight() + ChromeHeight() + 40;
                }
                else
                {
                    // Fixed columns: keep the buttons at their target size.
                    // The title bar needs room for the menu button and the word
                    // "ShortcutPad" too, or the title would wrap mid-word.
                    MinWidth = Math.Max(_cfg.ButtonWidth + GridMargin,
                        TitleReserve + MeasureTitle("ShortcutPad", TitleFontSize));
                    MinHeight = rows * _cfg.ButtonHeight + GridMargin + BarHeight() + ChromeHeight();
                }
            }

            _visibleButtons = shown;
            ShowTitle();
            FitTitleFont();
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

        // Remember the size the user settled on. Auto flow or not, the size is
        // their choice. A docked pad's size is the work area rather than a
        // choice, so that one is not saved.
        if (!IsDocked)
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
        menu.Items.Add(MakeItem("Edit buttons…", OpenButtonEditor));
        menu.Items.Add(new Separator());
        foreach (var opacity in new[] { 1.0, 0.85, 0.7, 0.5 })
            menu.Items.Add(MakeItem($"Opacity {opacity * 100:0}%", () => SetOpacity(opacity)));
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
            SetResizable(_cfg.Resizable);
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

    /// <summary>
    /// Open the button editor. It edits a copy of the config; Save writes the
    /// file and the pad picks it up like any other edit.
    /// </summary>
    private void OpenButtonEditor()
    {
        var editor = new ButtonEditorWindow(ConfigStore.Clone(_cfg), _configPath, draft =>
        {
            _suppressWatcher = true;
            try { ConfigStore.Save(_configPath, draft); }
            finally { _suppressWatcher = false; }
            Reload();
            Status("buttons saved");
        });
        editor.Show(this);
    }

    private void SetOpacity(double opacity)
    {
        Opacity = opacity;
        _cfg.Opacity = opacity;
        Status($"opacity {opacity * 100:0}%");
        SaveConfig();
    }

    /// <summary>
    /// Applies a new target button size (DIPs) and reflows the grid. Auto flow
    /// divides the window by this size, so a bigger target means fewer, larger
    /// buttons - which is what makes the pad comfortable for finger touch.
    /// </summary>
    private void ApplyButtonSize(int width, int height)
    {
        var w = Math.Clamp(width, MinButtonEdge, MaxButtonEdge);
        var h = Math.Clamp(height, MinButtonEdge, MaxButtonEdge);
        if (w == _cfg.ButtonWidth && h == _cfg.ButtonHeight) return;

        _cfg.ButtonWidth = w;
        _cfg.ButtonHeight = h;
        ApplyFlow();
        SaveConfig();
        Status($"button size {w} x {h}");
    }

    private void SetDock(string side)
    {
        if (side == "none")
        {
            _cfg.Dock = "none";
            MinWidth = 150;
            MinHeight = 110;
            SetResizable(_cfg.Resizable);
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
        // Docking pins the pad to an edge and gives it the work-area height, but
        // it stays resizable: the user can pull it back to a size they like.
        SetResizable(_cfg.Resizable);
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
            var icon = AppIcon.TryLoad();
            if (icon is null) return;
            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = AppInfo.NameWithVersion,
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
        AddTrayItem(menu, "Edit buttons…", OpenButtonEditor);
        menu.Items.Add(new NativeMenuItemSeparator());
        foreach (var opacity in new[] { 1.0, 0.85, 0.7, 0.5 })
            AddTrayItem(menu, $"Opacity {opacity * 100:0}%", () => SetOpacity(opacity));
        menu.Items.Add(new NativeMenuItemSeparator());
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
        // Only snap a window the user is actually dragging. Without this, the
        // position restored at startup counts as a move and a saved position
        // inside the snap band silently docks the pad on every launch.
        if (!_platform.IsPointerDown()) return;
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

    /// <summary>
    /// "ShortcutPad", plus "(12 of 25)" when auto flow has left buttons off
    /// screen. The count is only interesting when buttons are missing: with the
    /// whole pad on screen "ShortcutPad (25 of 25)" is just noise.
    /// </summary>
    private void ShowTitle()
    {
        _titleTimer?.Stop();
        _title.Opacity = 0.85;
        _title.Text = TitleText;
    }

    /// <summary>
    /// The title as one string. The count is one unbreakable unit: the spaces
    /// inside it are non-breaking, so a narrow bar puts "(12 of 25)" on its own
    /// line instead of splitting it, while the ordinary space before it is still
    /// a wrap point. "ShortcutPad" has no spaces and cannot break.
    /// </summary>
    /// <summary>
    /// Never let the title break inside the word "ShortcutPad". While the bar is
    /// wide enough for that word, the ordinary wrap (before the count) is left
    /// alone; below that the font shrinks until the word fits.
    /// </summary>
    private void FitTitleFont()
    {
        _title.FontSize = TitleFontSize;

        var available = ClientSize.Width - TitleReserve;
        if (available <= 0) return;

        const string word = "ShortcutPad";
        var wordWidth = MeasureTitle(word, TitleFontSize);
        if (wordWidth <= 0 || wordWidth <= available) return;

        var size = TitleFontSize * available / wordWidth;
        _title.FontSize = Math.Max(TitleMinFontSize, Math.Round(size * 2) / 2);
    }

    private static double MeasureTitle(string text, double size)
    {
        var probe = new TextBlock { FontSize = size, Text = text };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    private string TitleText =>
        _cfg.Buttons.Count == 0 || _visibleButtons >= _cfg.Buttons.Count
            ? $"ShortcutPad {AppInfo.Version}"
            : $"ShortcutPad {AppInfo.Version} ({_visibleButtons}\u00A0of\u00A0{_cfg.Buttons.Count})";

    /// <summary>
    /// Flash a message in the title bar - "config reloaded", "button size 156 x
    /// 100" - then put the title back.
    /// </summary>
    private void Status(string message)
    {
        _titleTimer?.Stop();
        _title.Text = message;
        _title.Opacity = 0.7;
        _titleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _titleTimer.Tick += (_, _) =>
        {
            _titleTimer!.Stop();
            ShowTitle();
        };
        _titleTimer.Start();
    }

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
