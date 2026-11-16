using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ShortcutPad;

/// <summary>
/// Editor for the pad's buttons: add, remove, reorder and edit each button's
/// action and appearance. Save writes shortcut_pad.json, which the pad
/// hot-reloads. Nothing in the pad changes until Save is pressed.
/// </summary>
public sealed class ButtonEditorWindow : Window
{
    private const string AutoIcon = "(auto)";

    private readonly string _configPath;
    private readonly PadConfig _draft;
    private readonly Action<PadConfig> _onSave;
    private readonly ListBox _list = new() { Width = 240 };

    /// <summary>
    /// The list's items. This is assigned to <see cref="ListBox.ItemsSource"/>
    /// exactly once and mutated in place afterwards: reassigning ItemsSource
    /// makes the item containers briefly hold a null item, which crashes the
    /// item template.
    /// </summary>
    private readonly ObservableCollection<PadButton> _items = new();
    private readonly TextBlock _hint = new()
    {
        Text = "Pick a button, edit it on the right, then Save.",
        Foreground = Brushes.Gray,
        FontSize = 12,
        Margin = new Thickness(6),
    };

    private readonly TextBox _label = new();
    private readonly RadioButton _modeKeys = MakeMode("Keystrokes");
    private readonly RadioButton _modeText = MakeMode("Type text");
    private readonly RadioButton _modeRun = MakeMode("Run program");
    private readonly TextBox _keys = new();
    private readonly TextBox _text = new();
    private readonly TextBox _run = new();
    private readonly TextBox _color = new() { Width = 140 };
    private readonly TextBox _foreground = new() { Width = 140 };
    private readonly ComboBox _icon = new();
    private readonly NumericUpDown _iconSize = MakeNumber(0, 200);
    private readonly NumericUpDown _fontSize = MakeNumber(0, 96);
    private readonly NumericUpDown _cornerRadius = MakeNumber(0, 100);
    private readonly NumericUpDown _spanX = MakeNumber(1, 12);
    private readonly NumericUpDown _spanY = MakeNumber(1, 12);
    private readonly Border _colorPreview = new() { Width = 26, Height = 20 };
    private readonly Border _foregroundPreview = new() { Width = 26, Height = 20 };

    private bool _inSelection;
    private PadButton? _current;

    // Drag-to-reorder state. A press only becomes a drag after the pointer has
    // travelled <see cref="DragThreshold"/> pixels, so a plain click still just
    // selects the row.
    private const string DragFormat = "application/x-shortcutpad-button";
    private const string TargetClass = "reorder-target";
    private const double DragThreshold = 4.0;

    private PadButton? _dragItem;
    private Point _dragOrigin;
    private bool _dragArmed;
    private ListBoxItem? _targetRow;

    public ButtonEditorWindow(PadConfig draft, string configPath, Action<PadConfig> onSave)
    {
        _draft = draft;
        _configPath = configPath;
        _onSave = onSave;

        Title = "Edit buttons";
        Icon = AppIcon.TryLoad();
        Opened += (_, _) => WindowChrome.HideMaximize(this, PadPlatform.Create());
        Width = 860;
        Height = 560;
        MinWidth = 680;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");

        foreach (var name in PadIcons.Names.OrderBy(n => n, StringComparer.Ordinal))
            _icon.Items.Add(name);
        _icon.Items.Add(AutoIcon);

        _list.ItemTemplate = new FuncDataTemplate<PadButton>((b, _) =>
        {
            var tb = new TextBlock { Padding = new Thickness(6, 3, 6, 3) };
            if (b is null) { tb.Text = "(none)"; return tb; }
            tb.Bind(TextBlock.TextProperty, new Binding("Description") { Source = b });
            return tb;
        });
        _list.SelectionChanged += (_, _) =>
        {
            if (_inSelection) return;
            _inSelection = true;
            try { LoadInto(_selected()); }
            finally { _inSelection = false; }
        };

        _label.TextChanged += (_, _) => ApplyFromInputs();
        _keys.TextChanged += (_, _) => ApplyFromInputs();
        _text.TextChanged += (_, _) => ApplyFromInputs();
        _run.TextChanged += (_, _) => ApplyFromInputs();
        _color.TextChanged += (_, _) => ApplyFromInputs();
        _foreground.TextChanged += (_, _) => ApplyFromInputs();
        _icon.SelectionChanged += (_, _) => ApplyFromInputs();
        foreach (var n in new[] { _iconSize, _fontSize, _cornerRadius, _spanX, _spanY })
            n.ValueChanged += (_, _) => ApplyFromInputs();
        foreach (var r in new[] { _modeKeys, _modeText, _modeRun })
            r.Click += (_, _) => ApplyFromInputs();

        Content = BuildLayout();
        foreach (var b in _draft.Buttons) _items.Add(b);
        _list.ItemsSource = _items;
        if (_items.Count > 0)
            _list.SelectedIndex = 0;

        InitReorder();
    }

    // ------------------------------------------------------------ drag reorder

    /// <summary>
    /// Lets a row be dragged to a new position. Dragging is done with
    /// <see cref="Avalonia.Input.DragDrop"/> rather than raw pointer events so
    /// the platform handles pointer capture and the drag session; the list is
    /// both the source and the target.
    /// </summary>
    private void InitReorder()
    {
        DragDrop.SetAllowDrop(_list, true);
        _list.AddHandler<DragEventArgs>(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Bubble);
        _list.AddHandler<DragEventArgs>(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble);
        _list.AddHandler<DragEventArgs>(DragDrop.DragLeaveEvent, OnDragLeave, RoutingStrategies.Bubble);

        // Tunnel phase, so a press is recorded before the row selects itself.
        _list.AddHandler<PointerPressedEventArgs>(PointerPressedEvent, OnListPressed, RoutingStrategies.Tunnel);
        _list.AddHandler<PointerEventArgs>(PointerMovedEvent, OnListMoved, RoutingStrategies.Tunnel);
        _list.AddHandler<PointerReleasedEventArgs>(PointerReleasedEvent, OnListReleased, RoutingStrategies.Tunnel);
    }

    private void OnListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
        var item = ItemFrom(e.Source);
        if (item is null) return;
        _dragItem = item;
        _dragOrigin = e.GetPosition(_list);
        _dragArmed = true;
    }

    private void OnListReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A press that never became a drag is just a selection.
        _dragArmed = false;
        _dragItem = null;
    }

    private async void OnListMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragArmed || _dragItem is null) return;
        if (!e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed) return;
        if (Math.Abs(e.GetPosition(_list).Y - _dragOrigin.Y) < DragThreshold) return;

        _dragArmed = false;
        var item = _dragItem;
        _dragItem = null;

        var data = new DataObject();
        data.Set(DragFormat, item);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        ClearTarget();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(DragFormat) is not PadButton)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        ShowTarget(TargetIndex(e.GetPosition(_list).Y));
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get(DragFormat) is not PadButton item)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var to = TargetIndex(e.GetPosition(_list).Y);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
        ClearTarget();
        Reorder(item, to);
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => ClearTarget();

    /// <summary>
    /// Moves <paramref name="item"/> to <paramref name="to"/>, which is an
    /// insertion index in the current order (0..Count). Both the list and the
    /// draft config are kept in step, exactly as Move up/down does.
    /// </summary>
    private void Reorder(PadButton item, int to)
    {
        var from = _items.IndexOf(item);
        if (from < 0) return;

        _items.RemoveAt(from);
        _items.Insert(Math.Clamp(to, 0, _items.Count), item);

        var i = _draft.Buttons.IndexOf(item);
        if (i >= 0)
        {
            _draft.Buttons.RemoveAt(i);
            _draft.Buttons.Insert(Math.Clamp(to, 0, _draft.Buttons.Count), item);
        }

        _list.SelectedItem = item;
        _hint.Text = "Reordered — press Save to keep the new order.";
    }

    /// <summary>Index of the gap the pointer is over: how many rows are above it.</summary>
    private int TargetIndex(double y)
    {
        var index = 0;
        foreach (var row in Rows())
        {
            if (y > RowCentre(row)) index++;
            else break;
        }
        return index;
    }

    private void ShowTarget(int index)
    {
        var rows = Rows();
        var row = index >= 0 && index < rows.Count ? rows[index] : null;
        if (ReferenceEquals(row, _targetRow)) return;

        ClearTarget();
        _targetRow = row;
        _targetRow?.Classes.Add(TargetClass);
    }

    private void ClearTarget()
    {
        _targetRow?.Classes.Remove(TargetClass);
        _targetRow = null;
    }

    /// <summary>
    /// The realized rows, top to bottom. Virtualization means only the visible
    /// ones exist, which is fine: the row under the pointer is always realized.
    /// </summary>
    private List<ListBoxItem> Rows() =>
        _list.GetVisualDescendants().OfType<ListBoxItem>()
            .OrderBy(RowCentre)
            .ToList();

    private double RowCentre(ListBoxItem row) =>
        row.TranslatePoint(new Point(0, row.Bounds.Height / 2), _list)?.Y ?? 0;

    private static PadButton? ItemFrom(object? source) =>
        source is Control control ? control.DataContext as PadButton : null;

    // ------------------------------------------------------------ UI

    private Control BuildLayout()
    {
        var root = new DockPanel();

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6) };
        toolbar.Children.Add(MakeButton("Move up", () => Move(-1)));
        toolbar.Children.Add(MakeButton("Move down", () => Move(+1)));
        toolbar.Children.Add(new Control { Width = 12 });
        toolbar.Children.Add(MakeButton("Add", Add));
        toolbar.Children.Add(MakeButton("Delete", Delete));
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Margin = new Thickness(6),
        };
        bottom.Children.Add(MakeButton("Revert", Revert));
        bottom.Children.Add(MakeButton("Save", Save));
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);

        DockPanel.SetDock(_hint, Dock.Bottom);
        _hint.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(_hint);

        DockPanel.SetDock(_list, Dock.Left);
        root.Children.Add(_list);

        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(120)));
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        var row = 0;
        void AddRow(string caption, Control control)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var lbl = new TextBlock
            {
                Text = caption,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            Grid.SetRow(lbl, row);
            Grid.SetColumn(lbl, 0);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            grid.Children.Add(lbl);
            grid.Children.Add(control);
            row++;
        }

        AddRow("Label", _label);

        var modes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        modes.Children.Add(_modeKeys);
        modes.Children.Add(_modeText);
        modes.Children.Add(_modeRun);
        AddRow("Action", modes);
        AddRow("Keys", _keys);
        AddRow("Text", _text);
        AddRow("Program", _run);

        var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        colorRow.Children.Add(_color);
        colorRow.Children.Add(_colorPreview);
        AddRow("Background", colorRow);

        var fgRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        fgRow.Children.Add(_foreground);
        fgRow.Children.Add(_foregroundPreview);
        AddRow("Foreground", fgRow);

        AddRow("Icon", _icon);
        AddRow("Icon size", _iconSize);
        AddRow("Font size", _fontSize);
        AddRow("Corner radius", _cornerRadius);
        AddRow("Span columns", _spanX);
        AddRow("Span rows", _spanY);

        var note = new TextBlock
        {
            Text = "Size and radius 0 = use the default. The icon \"(auto)\" is picked from the action.",
            Foreground = Brushes.Gray,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Grid.SetRow(note, row);
        Grid.SetColumn(note, 1);
        grid.Children.Add(note);

        // The DockPanel is the window content; the editor grid fills the rest.
        root.Children.Add(new ScrollViewer { Content = grid });
        return root;
    }

    private static RadioButton MakeMode(string header) => new() { Content = header, MinWidth = 100 };

    private static NumericUpDown MakeNumber(decimal min, decimal max) => new()
    {
        Minimum = min,
        Maximum = max,
        Increment = 1,
        Width = 90,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static Button MakeButton(string header, Action action)
    {
        var b = new Button { Content = header, Padding = new Thickness(10, 5, 10, 5) };
        b.Click += (_, _) => action();
        return b;
    }

    private static string Describe(PadButton? b)
    {
        // The virtualizing panel can build a container for a stale index, so a
        // null item is possible while the list is being refreshed.
        if (b is null) return "(none)";
        var action = !string.IsNullOrWhiteSpace(b.Keys) ? b.Keys
                  : !string.IsNullOrWhiteSpace(b.Text) ? "“" + b.Text + "”"
                  : !string.IsNullOrWhiteSpace(b.Run) ? b.Run
                  : "(no action)";
        return $"{b.Label}   [{action}]";
    }

    // ------------------------------------------------------------ selection

    private PadButton? _selected() => _list.SelectedItem as PadButton;

    private void LoadInto(PadButton? b)
    {
        _current = b;
        if (b is null)
        {
            _hint.Text = "No button selected.";
            return;
        }
        _label.Text = b.Label;
        _modeKeys.IsChecked = !string.IsNullOrWhiteSpace(b.Keys);
        _modeText.IsChecked = !string.IsNullOrWhiteSpace(b.Text);
        _modeRun.IsChecked = !string.IsNullOrWhiteSpace(b.Run);
        _keys.Text = b.Keys ?? "";
        _text.Text = b.Text ?? "";
        _run.Text = b.Run ?? "";
        _color.Text = b.Color ?? "";
        _foreground.Text = b.Foreground ?? "";
        _icon.SelectedItem = string.IsNullOrWhiteSpace(b.Icon) ? AutoIcon : b.Icon;
        _iconSize.Value = b.IconSize;
        _fontSize.Value = b.FontSize;
        _cornerRadius.Value = b.CornerRadius;
        _spanX.Value = Math.Max(1, b.SpanX);
        _spanY.Value = Math.Max(1, b.SpanY);
        UpdatePreviews();
        UpdateModeVisibility();
        _hint.Text = $"Button {_draft.Buttons.IndexOf(b) + 1} of {_draft.Buttons.Count}.";
    }

    private void ApplyFromInputs()
    {
        var b = _current;
        if (b is null) return;
        b.Label = _label.Text;
        b.Keys = _modeKeys.IsChecked == true && !string.IsNullOrWhiteSpace(_keys.Text) ? _keys.Text : null;
        b.Text = _modeText.IsChecked == true && !string.IsNullOrWhiteSpace(_text.Text) ? _text.Text : null;
        b.Run = _modeRun.IsChecked == true && !string.IsNullOrWhiteSpace(_run.Text) ? _run.Text : null;
        b.Color = string.IsNullOrWhiteSpace(_color.Text) ? null : _color.Text;
        b.Foreground = string.IsNullOrWhiteSpace(_foreground.Text) ? null : _foreground.Text;
        var icon = _icon.SelectedItem as string;
        b.Icon = string.IsNullOrEmpty(icon) || icon == AutoIcon ? null : icon;
        b.IconSize = _iconSize.Value is decimal v1 && v1 > 0 ? (int)v1 : null;
        b.FontSize = _fontSize.Value is decimal v2 && v2 > 0 ? (int)v2 : null;
        b.CornerRadius = _cornerRadius.Value is decimal v3 && v3 > 0 ? (int)v3 : null;
        b.SpanX = _spanX.Value is decimal v4 && v4 > 0 ? (int)v4 : 1;
        b.SpanY = _spanY.Value is decimal v5 && v5 > 0 ? (int)v5 : 1;
        UpdatePreviews();
        _hint.Text = "Unsaved changes — press Save.";
    }

    private void UpdatePreviews()
    {
        _colorPreview.Background = Parse(_color.Text) ?? Brushes.LightGray;
        _foregroundPreview.Background = Parse(_foreground.Text) ?? Brushes.Black;
    }

    private void UpdateModeVisibility()
    {
        _keys.IsEnabled = _modeKeys.IsChecked == true;
        _text.IsEnabled = _modeText.IsChecked == true;
        _run.IsEnabled = _modeRun.IsChecked == true;
    }

    private static IBrush? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (IBrush)new BrushConverter().ConvertFromString(hex); }
        catch { return null; }
    }

    // ------------------------------------------------------------ actions

    private void Add()
    {
        var b = new PadButton { Label = "New button", Keys = "ctrl+n" };
        _draft.Buttons.Add(b);
        _items.Add(b);
        _list.SelectedItem = b;
        _hint.Text = "Button added — press Save to keep it.";
    }

    private void Delete()
    {
        var b = _selected();
        if (b is null) return;
        var i = _items.IndexOf(b);
        if (i < 0) return;
        _items.RemoveAt(i);
        var j = _draft.Buttons.IndexOf(b);
        if (j >= 0) _draft.Buttons.RemoveAt(j);
        if (_items.Count > 0)
            _list.SelectedIndex = Math.Min(i, _items.Count - 1);
    }

    private void Move(int delta)
    {
        var b = _selected();
        if (b is null) return;
        var i = _items.IndexOf(b);
        var j = Math.Clamp(i + delta, 0, _items.Count - 1);
        if (i == j) return;
        // Remove + Insert rather than Move: the virtualizing panel re-realizes
        // the containers for those, so the visible order follows the data.
        // (ObservableCollection.Move raises a Move notification that the panel
        // ignores, leaving the list looking unchanged.)
        _items.RemoveAt(i);
        _items.Insert(j, b);
        var k = _draft.Buttons.IndexOf(b);
        if (k >= 0) _draft.Buttons.RemoveAt(k);
        _draft.Buttons.Insert(j, b);
        _list.SelectedItem = b;
    }

    private void Revert()
    {
        var fresh = ConfigStore.Load(_configPath);
        _draft.Buttons.Clear();
        _draft.Buttons.AddRange(fresh.Buttons);
        _items.Clear();
        foreach (var b in fresh.Buttons) _items.Add(b);
        _list.SelectedIndex = _items.Count > 0 ? 0 : -1;
        _hint.Text = "Reloaded from shortcut_pad.json.";
    }

    private void Save()
    {
        ApplyFromInputs();
        _onSave(_draft);
        Close();
    }
}
