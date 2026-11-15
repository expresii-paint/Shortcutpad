using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Controls.Templates;
using Avalonia.Media;

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

    public ButtonEditorWindow(PadConfig draft, string configPath, Action<PadConfig> onSave)
    {
        _draft = draft;
        _configPath = configPath;
        _onSave = onSave;

        Title = "Edit buttons";
        Icon = AppIcon.TryLoad();
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
    }

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
