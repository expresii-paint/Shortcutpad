using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace ShortcutPad;

/// <summary>
/// The actions the options window can perform. Supplied by
/// <see cref="PadWindow"/>, which owns the real implementation.
/// </summary>
public sealed class OptionsActions
{
    public Action ToggleVisible { get; set; } = () => { };
    public Action Reload { get; set; } = () => { };
    public Action EditConfig { get; set; } = () => { };
    public Action OpenEditor { get; set; } = () => { };
    public Action<bool> SetAutoFlow { get; set; } = _ => { };
    public Action<bool> SetResizable { get; set; } = _ => { };
    public Action<double> SetOpacity { get; set; } = _ => { };
    public Action<string> SetDock { get; set; } = _ => { };
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

    private readonly PadConfig _cfg;
    private readonly OptionsActions _actions;

    public OptionsWindow(PadConfig cfg, string configPath, OptionsActions actions)
    {
        _cfg = cfg;
        _actions = actions;

        Title = "Options";
        Width = 430;
        Height = 660;
        MinWidth = 340;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Segoe UI");

        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(14) };
        stack.Children.Add(MakeToggle("Auto flow", cfg.AutoFlow,
            () => _cfg.AutoFlow, on => _actions.SetAutoFlow(on)));
        stack.Children.Add(MakeToggle("Resizable", cfg.Resizable,
            () => _cfg.Resizable, on => _actions.SetResizable(on)));

        stack.Children.Add(Separator());
        stack.Children.Add(MakeButton("Show / hide pad", _actions.ToggleVisible));
        stack.Children.Add(MakeButton("Reload config", _actions.Reload));
        stack.Children.Add(MakeButton("Edit config (JSON)", _actions.EditConfig));
        stack.Children.Add(MakeButton("Edit buttons…", _actions.OpenEditor));

        stack.Children.Add(Separator());
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

        stack.Children.Add(Separator());
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

    private static Button MakeButton(string header, Action action)
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
        b.Click += (_, _) => action();
        return b;
    }

    private static Button MakeToggle(string header, bool isOn, Func<bool> get, Action<bool> set)
    {
        var b = new Button
        {
            Height = RowHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FontSize = 15,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (_, _) =>
        {
            var on = !get();
            set(on);
            b.Content = Label(header, on);
        };
        b.Content = Label(header, isOn);
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
