using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ShortcutPad;

public sealed class PadForm : Form
{
    private readonly string _configPath;
    private readonly IKeystrokeSender _sender;
    private PadConfig _cfg = new();

    private TableLayoutPanel _grid = null!;
    private Label _status = null!;
    private NotifyIcon? _tray;
    private FileSystemWatcher? _watcher;
    private System.Windows.Forms.Timer? _fgTimer;
    private System.Windows.Forms.Timer? _saveTimer;
    private bool _suppressWatcher;
    private IntPtr _lastOtherForeground = IntPtr.Zero;

    private const int ButtonWidth = 96;
    private const int ButtonHeight = 44;

    public PadForm(string configPath, IKeystrokeSender sender)
    {
        _configPath = configPath;
        _sender = sender;

        InitializeComponent();
        _cfg = ConfigStore.Load(_configPath);
        BuildGrid();
        ApplySettings();
        InitTimers();
        InitTray();
        InitWatcher();

        FormClosed += (_, _) => { _tray?.Dispose(); _watcher?.Dispose(); _fgTimer?.Dispose(); _saveTimer?.Dispose(); };
    }

    private void InitializeComponent()
    {
        Text = "Shortcut Pad";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(6);

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2) };
        _grid = new TableLayoutPanel { Dock = DockStyle.Fill };
        panel.Controls.Add(_grid);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Text = "ready",
            ForeColor = Color.Gray,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        Controls.Add(panel);
        Controls.Add(_status);
    }

    private void BuildGrid()
    {
        _grid.SuspendLayout();
        _grid.Controls.Clear();
        _grid.ColumnStyles.Clear();
        _grid.RowStyles.Clear();

        var cols = Math.Max(1, _cfg.Buttons.Count == 0 ? 1 : _cfg.Columns);
        _grid.ColumnCount = cols;
        for (var c = 0; c < cols; c++)
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cols));

        var rows = Math.Max(1, (int)Math.Ceiling(_cfg.Buttons.Count / (double)cols));
        _grid.RowCount = rows;
        for (var r = 0; r < rows; r++)
            _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, ButtonHeight));

        for (var i = 0; i < _cfg.Buttons.Count; i++)
        {
            var b = _cfg.Buttons[i];
            var btn = new Button
            {
                Text = b.Label,
                Dock = DockStyle.Fill,
                Margin = new Padding(3),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f),
                Tag = b,
                Cursor = Cursors.Hand,
            };
            if (!string.IsNullOrWhiteSpace(b.Color))
            {
                btn.BackColor = ColorTranslator.FromHtml(b.Color);
                if (btn.BackColor.GetBrightness() < 0.55f) btn.ForeColor = Color.White;
            }
            btn.FlatAppearance.BorderColor = Color.FromArgb(120, 120, 130);
            btn.Click += (s, _) => Execute((PadButton)((Button)s!).Tag!);
            _grid.Controls.Add(btn, i % cols, i / cols);
        }

        ClientSize = new Size(cols * ButtonWidth + 20, rows * ButtonHeight + 42);
        _grid.ResumeLayout();
    }

    private void ApplySettings()
    {
        TopMost = _cfg.AlwaysOnTop;
        if (_cfg.X >= 0 && _cfg.Y >= 0) Location = new Point(_cfg.X, _cfg.Y);
    }

    private void InitTimers()
    {
        _fgTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _fgTimer.Tick += (_, _) =>
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && fg != this.Handle) _lastOtherForeground = fg;
        };
        _fgTimer.Start();

        _saveTimer = new System.Windows.Forms.Timer { Interval = 600 };
        _saveTimer.Tick += (_, _) => { _saveTimer!.Stop(); SaveConfig(); };
    }

    private void InitTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show / hide", null, (_, _) => ToggleVisible());
        menu.Items.Add("Reload config", null, (_, _) => Reload());
        menu.Items.Add("Edit config", null, (_, _) =>
            Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());

        _tray = new NotifyIcon { Text = "Shortcut Pad", Icon = MakeIcon(), ContextMenuStrip = menu, Visible = true };
        _tray.MouseDoubleClick += (_, _) => ToggleVisible();
    }

    private void InitWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_configPath)!, Path.GetFileName(_configPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += (_, _) => BeginInvoke(ReloadIfIdle);
            _watcher.Renamed += (_, _) => BeginInvoke(ReloadIfIdle);
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

    private void ToggleVisible()
    {
        if (Visible) Hide();
        else { Show(); Activate(); }
    }

    private void Reload()
    {
        try
        {
            _cfg = ConfigStore.Load(_configPath);
            BuildGrid();
            ApplySettings();
            Status("config reloaded");
        }
        catch (Exception ex)
        {
            Status("reload failed: " + ex.Message);
        }
    }

    private void Execute(PadButton b)
    {
        try
        {
            // Return focus to the app the user was in before clicking the pad,
            // so the keystrokes land there instead of in the pad itself.
            var target = _lastOtherForeground;
            var restored = false;
            if (target != IntPtr.Zero && target != this.Handle && IsWindow(target))
                restored = SetForegroundWindow(target);
            var fgNow = GetForegroundWindow();

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

    private void Status(string message)
    {
        _status.Text = message;
    }

    private void SaveConfig()
    {
        try
        {
            _cfg.X = Location.X;
            _cfg.Y = Location.Y;
            _suppressWatcher = true;
            ConfigStore.Save(_configPath, _cfg);
            _ = Task.Run(async () => { await Task.Delay(500); _suppressWatcher = false; });
        }
        catch (Exception ex)
        {
            Status("save failed: " + ex.Message);
        }
    }

    private void QueueSave()
    {
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    protected override void OnMove(EventArgs e) { base.OnMove(e); QueueSave(); }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); QueueSave(); }
    protected override void OnFormClosing(FormClosingEventArgs e) { SaveConfig(); base.OnFormClosing(e); }

    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(45, 108, 223));
            using var brush = new SolidBrush(Color.White);
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++)
                    g.FillRectangle(brush, 6 + c * 8, 8 + r * 8, 6, 6);
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
}
