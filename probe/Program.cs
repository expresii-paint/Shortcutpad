using System.Runtime.InteropServices;
using System.Text;

namespace probe;

// Verification harness: shows a text box, installs a low-level keyboard hook,
// clicks a real button on the Shortcut Pad window, then reports every key event
// the hook saw plus whatever text actually landed in the target text box.
internal static class Program
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    private static IntPtr _hookId = IntPtr.Zero;
    private static readonly LowLevelKeyboardProc Proc = HookCallback;
    private static readonly List<string> Log = new();
    private static TextBox? _target;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);


    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static int Main(string[] args)
    {
        string padTitle = "Shortcut Pad";
        int buttonIndex = -1;
        string? buttonName = null;
        int waitMs = 1500;
        int delayMs = 0;
        var forceForeground = true;
        var clickPoint = (x: -1, y: -1);
        var layoutOnly = false;
        var shotOnly = false;
        string? shotPath = null;
        string? clickInTitle = null, clickInName = null;
        string? capTitle = null;
        var capClick = -1;
        var dragDx = 0;
        var dragDy = 0;
        var resizeDx = 0;
        var resizeDy = 0;
        var reorderFrom = -1;
        var reorderTo = -1;
        var whatX = -1;
        var whatY = -1;
        var pressAt = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pad": padTitle = args[++i]; break;
                case "--button": buttonIndex = int.Parse(args[++i]); break;
                case "--name": buttonName = args[++i]; break;
                case "--wait": waitMs = int.Parse(args[++i]); break;
                case "--delay": delayMs = int.Parse(args[++i]); break;
                case "--noforce": forceForeground = false; break;
                case "--click": clickPoint = (int.Parse(args[++i]), int.Parse(args[++i])); break;
                case "--layout": layoutOnly = true; break;
                case "--shot": shotPath = args[++i]; shotOnly = true; break;
                case "--editor-drag": EditorDrag(int.Parse(args[++i]), int.Parse(args[++i])); return 0;
                case "--editor-click": EditorClick(int.Parse(args[++i])); return 0;
                case "--editor-hold": EditorHold(int.Parse(args[++i]), int.Parse(args[++i]), args[++i]); return 0;
                case "--click-in": clickInTitle = args[++i]; clickInName = args[++i]; break;
                case "--cap-title": capTitle = args[++i]; break;
                case "--cap-click": capClick = int.Parse(args[++i]); break;
                case "--drag-bar": dragDx = int.Parse(args[++i]); dragDy = int.Parse(args[++i]); break;
                case "--resize": resizeDx = int.Parse(args[++i]); resizeDy = int.Parse(args[++i]); break;
                case "--reorder": reorderFrom = int.Parse(args[++i]); reorderTo = int.Parse(args[++i]); break;
                case "--whatat": whatX = int.Parse(args[++i]); whatY = int.Parse(args[++i]); break;
                case "--press-at": whatX = int.Parse(args[++i]); whatY = int.Parse(args[++i]); pressAt = true; break;
            }
        }

        if (shotOnly)
        {
            Capture(padTitle, shotPath);
            return 0;
        }

        if (layoutOnly)
        {
            DumpLayout(padTitle);
            return 0;
        }

        if (clickInTitle is not null)
        {
            ApplicationConfiguration.Initialize();
            ClickByName(clickInTitle, clickInName!);
            return 0;
        }

        if (capTitle is not null)
        {
            ApplicationConfiguration.Initialize();
            CaptureTitle(capTitle, capTitle + ".png");
            return 0;
        }

        if (whatX >= 0)
        {
            ApplicationConfiguration.Initialize();
            if (pressAt) { Console.WriteLine($"press at {whatX},{whatY}"); Click(whatX, whatY); }
            else Console.WriteLine($"AT {whatX},{whatY}: {DescribeWindowAt(whatX, whatY)}");
            return 0;
        }

        if (reorderFrom >= 0)
        {
            ApplicationConfiguration.Initialize();
            ReorderButtons("Shortcut Pad", reorderFrom, reorderTo);
            return 0;
        }

        if (resizeDx != 0 || resizeDy != 0)
        {
            ApplicationConfiguration.Initialize();
            ResizeWindow("Shortcut Pad", resizeDx, resizeDy);
            return 0;
        }

        if (dragDx != 0 || dragDy != 0)
        {
            ApplicationConfiguration.Initialize();
            DragBottomBar("Shortcut Pad", dragDx, dragDy);
            return 0;
        }

        if (capClick >= 0)
        {
            ApplicationConfiguration.Initialize();
            ClickCaption("Shortcut Pad", capClick, out var before, out var beforeStyle);
            Thread.Sleep(1200);
            ClickCaption("Shortcut Pad", -1, out var after, out var afterStyle);
            Console.WriteLine($"before rect={before} style=0x{beforeStyle:X8}");
            Console.WriteLine($"after  rect={after} style=0x{afterStyle:X8}");
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        var form = new Form
        {
            Text = "probe target",
            ClientSize = new Size(460, 220),
            StartPosition = FormStartPosition.Manual,
            Location = new Point(1300, 100),
        };
        _target = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 11f),
            Text = "target ready",
        };
        form.Controls.Add(_target);
        form.Show();
        form.Activate();
        _target.Focus();
        Pump(600);

        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, Proc, GetModuleHandle(null), 0);
        if (_hookId == IntPtr.Zero)
        {
            Console.WriteLine("HOOK FAILED error=" + Marshal.GetLastWin32Error());
            return 2;
        }

        // Let the pad's foreground tracker register our window, then click.
        Pump(500);

        Console.WriteLine($"MYHWND=0x{form.Handle:X} FOCUS=0x{GetFocus():X}");
        var preFg = GetForegroundWindow();
        var preClass = new StringBuilder(256);
        GetClassName(preFg, preClass, preClass.Capacity);
        var preText = new StringBuilder(512);
        GetWindowText(preFg, preText, preText.Capacity);
        Console.WriteLine($"PRE-FOREGROUND class='{preClass}' text='{preText}'");
        if (forceForeground) ForceForeground(form.Handle);
        _target.Focus();
        Pump(300);
        if (delayMs > 0) Pump(delayMs);
        Console.WriteLine($"MYHWND=0x{form.Handle:X} FOCUS=0x{GetFocus():X} FOREGROUND=0x{GetForegroundWindow():X}");

        var clicked = false;
        if (!string.IsNullOrEmpty(buttonName))
        {
            ForcePadTopmost(padTitle);
            ClickByName(padTitle, buttonName);
            clicked = true;
        }
        else if (clickPoint.x >= 0 && clickPoint.y >= 0)
        {
            ForcePadTopmost(padTitle);
            Click(clickPoint.x, clickPoint.y);
            clicked = true;
        }
        else if (buttonIndex >= 0)
        {
            var hwnd = FindWindow(null, padTitle);
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine($"PAD NOT FOUND title='{padTitle}'");
                return 3;
            }
            GetWindowRect(hwnd, out var padRect);
            Console.WriteLine($"PADRECT left={padRect.Left} top={padRect.Top} right={padRect.Right} bottom={padRect.Bottom}");

            var buttons = ListPadButtons(hwnd, out var names);
            for (var i = 0; i < buttons.Count; i++)
                Console.WriteLine($"BUTTON {i} name='{names[i]}' rect=({buttons[i].Left},{buttons[i].Top})-({buttons[i].Right},{buttons[i].Bottom})");

            if (buttons.Count == 0)
                DumpChildren(hwnd, 0, 3);

            if (buttonIndex < buttons.Count)
            {
                var r = buttons[buttonIndex];
                Click((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                clicked = true;
                Console.WriteLine($"CLICKED button {buttonIndex} at {(r.Left + r.Right) / 2},{(r.Top + r.Bottom) / 2}");
            }
            else
            {
                Console.WriteLine($"BUTTON INDEX {buttonIndex} OUT OF RANGE (count={buttons.Count})");
            }
        }

        var fg = GetForegroundWindow();
        Console.WriteLine($"FOREGROUND after click = 0x{fg:X}");
        var fgClass = new StringBuilder(256);
        GetClassName(fg, fgClass, fgClass.Capacity);
        var fgText = new StringBuilder(512);
        GetWindowText(fg, fgText, fgText.Capacity);
        Console.WriteLine($"FOREGROUND class='{fgClass}' text='{fgText}'");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < waitMs) Application.DoEvents();
        Thread.Sleep(50);
        Application.DoEvents();

        var fg2 = GetForegroundWindow();
        var fg2Class = new StringBuilder(256);
        GetClassName(fg2, fg2Class, fg2Class.Capacity);
        Console.WriteLine($"FOREGROUND-AFTER-WAIT=0x{fg2:X} class='{fg2Class}'");

        Console.WriteLine("--- HOOK LOG ---");
        foreach (var line in Log) Console.WriteLine(line);
        Console.WriteLine("--- END LOG ---");
        Console.WriteLine("TEXT:" + (_target.Text ?? ""));
        Console.WriteLine(clicked ? "CLICK OK" : "NO CLICK");

        UnhookWindowsHookEx(_hookId);

        // Stay visible briefly so a screen capture can include the result.
        var sw2 = System.Diagnostics.Stopwatch.StartNew();
        while (sw2.ElapsedMilliseconds < 4000) Application.DoEvents();
        return 0;
    }

    private static List<RECT> ListPadButtons(IntPtr hwnd, out List<string> names)
    {
        var pairs = new List<(RECT r, string n)>();
        Collect(hwnd, pairs, new HashSet<IntPtr>());
        pairs.Sort((a, b) => a.r.Top != b.r.Top ? a.r.Top.CompareTo(b.r.Top) : a.r.Left.CompareTo(b.r.Left));
        names = pairs.Select(p => p.n).ToList();
        return pairs.Select(p => p.r).ToList();
    }

    private static void DumpChildren(IntPtr parent, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        EnumChildWindows(parent, (child, _) =>
        {
            var sb = new StringBuilder(256);
            GetClassName(child, sb, sb.Capacity);
            var indent = new string(' ', depth * 2);
            if (GetWindowRect(child, out var r))
                Console.WriteLine($"{indent}child class='{sb}' rect=({r.Left},{r.Top})-({r.Right},{r.Bottom})");
            DumpChildren(child, depth + 1, maxDepth);
            return true;
        }, IntPtr.Zero);
    }

    private static void Collect(IntPtr parent, List<(RECT r, string n)> pairs, HashSet<IntPtr> seen)
    {
        EnumChildWindows(parent, (child, _) =>
        {
            var sb = new StringBuilder(256);
            GetClassName(child, sb, sb.Capacity);
            var cls = sb.ToString();
            if (cls.Contains("Button", StringComparison.Ordinal) && seen.Add(child) && GetWindowRect(child, out var r))
            {
                var tb = new StringBuilder(256);
                GetWindowText(child, tb, tb.Capacity);
                pairs.Add((r, tb.ToString()));
            }
            Collect(child, pairs, seen);
            return true;
        }, IntPtr.Zero);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var vk = Marshal.ReadInt32(lParam);
            var up = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;
            var sys = wParam == (IntPtr)WM_SYSKEYDOWN || wParam == (IntPtr)WM_SYSKEYUP;
            Log.Add($"{(up ? "UP  " : "DOWN")} vk=0x{vk:X2}{(sys ? " (sys)" : "")}");
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static string DescribeWindowAt(int x, int y)
    {
        var h = WindowFromPoint(new POINT { X = x, Y = y });
        if (h == IntPtr.Zero) return "<none>";
        var cls = new StringBuilder(256);
        GetClassName(h, cls, cls.Capacity);
        var txt = new StringBuilder(256);
        GetWindowText(h, txt, txt.Capacity);
        GetWindowRect(h, out var r);
        return $"0x{h:X} class='{cls}' text='{txt}' rect={r.Left},{r.Top}-{r.Right},{r.Bottom}";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    private static void ForcePadTopmost(string padTitle)
    {
        var hwnd = FindWindow(null, padTitle);
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0040);
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>Capture the pad window from a DPI-aware process (physical pixels).</summary>
    private static void Capture(string padTitle, string path)
    {
        var hwnd = FindWindow(null, padTitle);
        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine($"PAD NOT FOUND title='{padTitle}'");
            return;
        }
        GetWindowRect(hwnd, out var r);
        // GetWindowRect is DPI-virtualised for a non-aware process; scale up.
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var w = (int)((r.Right - r.Left) * scale);
        var h = (int)((r.Bottom - r.Top) * scale);
        using var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen((int)(r.Left * scale), (int)(r.Top * scale), 0, 0, new Size(w, h));
        bmp.Save(path);
        Console.WriteLine($"SHOT {path} {w}x{h} (dpi {GetDpiForWindow(hwnd)}, scale {scale:0.##})");
    }

    /// <summary>Dump every button in the pad with its on-screen rectangle (physical px).</summary>
    private static void DumpLayout(string padTitle)
    {
        var root = System.Windows.Automation.AutomationElement.RootElement;
        var pad = root.FindFirst(System.Windows.Automation.TreeScope.Children,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.NameProperty, padTitle));
        if (pad is null)
        {
            Console.WriteLine($"PAD NOT FOUND title='{padTitle}'");
            return;
        }
        var pr = pad.Current.BoundingRectangle;
        Console.WriteLine($"PAD rect=({pr.Left:0},{pr.Top:0})-({pr.Right:0},{pr.Bottom:0}) size={pr.Width:0}x{pr.Height:0}");

        var buttons = pad.FindAll(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.ControlTypeProperty,
                System.Windows.Automation.ControlType.Button));
        Console.WriteLine($"COUNT={buttons.Count}");

        var seen = new List<(string name, double l, double t, double r, double b)>();
        foreach (System.Windows.Automation.AutomationElement b in buttons)
        {
            var r = b.Current.BoundingRectangle;
            seen.Add((b.Current.Name, r.Left, r.Top, r.Right, r.Bottom));
        }
        seen.Sort((a, b) =>
        {
            var dr = Math.Abs(a.t - b.t) < 2 ? 0 : a.t.CompareTo(b.t);
            return dr != 0 ? dr : a.l.CompareTo(b.l);
        });
        for (var i = 0; i < seen.Count; i++)
        {
            var s = seen[i];
            Console.WriteLine($"BTN {i,2} name='{s.name}' rect=({s.l:0},{s.t:0})-({s.r:0},{s.b:0}) size={s.r - s.l:0}x{s.b - s.t:0}");
        }
    }

    /// <summary>
    /// Captures just the title bar of a window, using DPI-aware Win32 coordinates
    /// (the probe is per-monitor aware, so these are real screen pixels).
    /// </summary>
    /// <summary>
    /// Clicks a caption button (0 = minimize, 1 = maximize/restore, 2 = close) and
    /// reports the window rect/style before and after, to prove whether the
    /// maximize glyph is a real, working button.
    /// </summary>
    /// <summary>
    /// Drags the pad's top bar (its stand-in title bar) and reports the window
    /// rect before and after, to prove the bar moves the window.
    /// </summary>
    /// <summary>
    /// Drags pad button <paramref name="from"/> onto button <paramref name="to"/>,
    /// i.e. the pad's own drag-to-reorder gesture.
    /// </summary>
    private static void ReorderButtons(string title, int from, int to)
    {
        var root = System.Windows.Automation.AutomationElement.RootElement;
        var pad = root.FindFirst(System.Windows.Automation.TreeScope.Children,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.NameProperty, title));
        if (pad is null)
            pad = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.NameProperty, title));
        if (pad is null) { Console.WriteLine("PAD NOT FOUND"); return; }

        var rect = pad.Current.BoundingRectangle;
        var btnCond = new System.Windows.Automation.PropertyCondition(
            System.Windows.Automation.AutomationElement.ControlTypeProperty,
            System.Windows.Automation.ControlType.Button);
        var buttons = new System.Collections.Generic.List<System.Windows.Automation.AutomationElement>();
        foreach (var elem in pad.FindAll(System.Windows.Automation.TreeScope.Descendants, btnCond))
        {
            var e = (System.Windows.Automation.AutomationElement)elem;
            var name = e.Current.Name ?? "";
            if (name.Length == 1 && name[0] == '☰') continue;   // the menu button
            if (!e.Current.IsOffscreen) buttons.Add(e);
        }
        if (from >= buttons.Count || to >= buttons.Count)
        {
            Console.WriteLine($"BUTTONS {buttons.Count} - cannot reorder {from} -> {to}");
            return;
        }

        var hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("PAD HWND NOT FOUND"); return; }
        if (!GetClientRect(hwnd, out var client) || !ClientToScreen(hwnd, out var origin))
        { Console.WriteLine("CLIENT RECT FAILED"); return; }

        Point Centre(System.Windows.Automation.AutomationElement el)
        {
            var r = el.Current.BoundingRectangle;
            var fx = (r.X + r.Width / 2 - rect.X) / rect.Width;
            var fy = (r.Y + r.Height / 2 - rect.Y) / rect.Height;
            return new Point(origin.X + (int)(fx * client.Right), origin.Y + (int)(fy * client.Bottom));
        }

        var a = Centre(buttons[from]);
        var b = Centre(buttons[to]);
        Console.WriteLine($"reorder {from} -> {to}: ({a.X},{a.Y}) -> ({b.X},{b.Y}) of {buttons.Count} buttons");

        var size = Marshal.SizeOf<INPUT>();
        SendInput(1, new[] { MouseInput(a.X, a.Y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
        Thread.Sleep(150);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTDOWN) }, size);
        Thread.Sleep(150);
        for (var i = 1; i <= 15; i++)
        {
            SendInput(1, new[] { MouseInput(a.X + (b.X - a.X) * i / 15, a.Y + (b.Y - a.Y) * i / 15,
                MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
            Thread.Sleep(45);
        }
        Thread.Sleep(250);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTUP) }, size);
        Thread.Sleep(800);
        Console.WriteLine("drag done");
    }

    /// <summary>Drags the pad's bottom-right corner, i.e. a real user resize.</summary>
    private static void ResizeWindow(string title, int dx, int dy)
    {
        var hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("WINDOW NOT FOUND " + title); return; }
        GetWindowRect(hwnd, out var before);
        GetClientRect(hwnd, out var cr);
        ClientToScreen(hwnd, out var origin);

        // the resize border lives in the window frame, outside the client area
        var x = before.Right - 2;
        var y = before.Bottom - 2;
        Console.WriteLine($"corner {x},{y}  before=({before.Left},{before.Top})-({before.Right},{before.Bottom})");

        var size = Marshal.SizeOf<INPUT>();
        SendInput(1, new[] { MouseInput(x, y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
        Thread.Sleep(150);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTDOWN) }, size);
        Thread.Sleep(150);
        for (var i = 1; i <= 15; i++)
        {
            SendInput(1, new[] { MouseInput(x + dx * i / 15, y + dy * i / 15, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
            Thread.Sleep(45);
        }
        Thread.Sleep(200);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTUP) }, size);
        Thread.Sleep(800);

        GetWindowRect(hwnd, out var after);
        Console.WriteLine($"after =({after.Left},{after.Top})-({after.Right},{after.Bottom})  size delta=({after.Right - before.Right},{after.Bottom - before.Bottom})");
    }

    private static void DragBottomBar(string title, int dx, int dy)
    {
        var hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("WINDOW NOT FOUND " + title); return; }
        GetWindowRect(hwnd, out var before);
        GetClientRect(hwnd, out var cr);
        ClientToScreen(hwnd, out var origin);

        var x = origin.X + cr.Right / 2;
        var y = origin.Y + 20;                   // the title bar
        Console.WriteLine($"bar point {x},{y}  before=({before.Left},{before.Top})-({before.Right},{before.Bottom})");

        var size = Marshal.SizeOf<INPUT>();
        SendInput(1, new[] { MouseInput(x, y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
        Thread.Sleep(150);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTDOWN) }, size);
        Thread.Sleep(150);
        for (var i = 1; i <= 15; i++)
        {
            var px = x + dx * i / 15;
            var py = y + dy * i / 15;
            SendInput(1, new[] { MouseInput(px, py, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
            Thread.Sleep(45);
        }
        Thread.Sleep(200);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTUP) }, size);
        Thread.Sleep(600);

        GetWindowRect(hwnd, out var after);
        Console.WriteLine($"after=({after.Left},{after.Top})-({after.Right},{after.Bottom})  delta=({after.Left - before.Left},{after.Top - before.Top})");
    }

    private static void ClickCaption(string title, int index, out string rect, out int style)
    {
        rect = "";
        style = 0;
        var hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("WINDOW NOT FOUND " + title); return; }
        if (!GetWindowRect(hwnd, out var wr) || !GetClientRect(hwnd, out var cr) ||
            !ClientToScreen(hwnd, out var origin))
        {
            Console.WriteLine("RECT FAILED");
            return;
        }
        rect = $"({wr.Left},{wr.Top})-({wr.Right},{wr.Bottom})";
        style = GetWindowLong(hwnd, -16);
        if (index < 0) return;

        var titleHeight = origin.Y - wr.Top;
        // Windows 11 caption buttons are 46 DIPs square, three of them at the
        // right hand end of the title bar.
        var bw = Math.Max(20, (int)(46 * GetDpiForWindow(hwnd) / 96.0));
        var x = origin.X + cr.Right - (int)((2 - index) * bw) - bw / 2;
        var y = wr.Top + titleHeight / 2;
        Console.WriteLine($"clicking caption {index} at {x},{y}");
        Click(x, y);
    }

    private static void CaptureTitle(string title, string path)
    {
        var hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("WINDOW NOT FOUND " + title); return; }
        if (!GetWindowRect(hwnd, out var wr) || !GetClientRect(hwnd, out var cr) ||
            !ClientToScreen(hwnd, out var origin))
        {
            Console.WriteLine("RECT FAILED");
            return;
        }
        var titleHeight = origin.Y - wr.Top;
        if (titleHeight <= 0) { Console.WriteLine("NO TITLE BAR"); return; }

        var width = Math.Min(320, cr.Right);
        var bmp = new Bitmap(width, titleHeight);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(origin.X + cr.Right - width, wr.Top, 0, 0, new Size(width, titleHeight));
        bmp.Save(path);
        Console.WriteLine($"TITLEBAR {path} {width}x{titleHeight} dpi={GetDpiForWindow(hwnd)}");
    }

    private static void ClickByName(string padTitle, string buttonName)
    {
        var root = System.Windows.Automation.AutomationElement.RootElement;
        // Owned windows (the Options dialog is shown with Show(owner)) are not
        // top-level UIA children, so fall back to a whole-tree search.
        var pad = root.FindFirst(System.Windows.Automation.TreeScope.Children,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.NameProperty, padTitle));
        if (pad is null)
            pad = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.NameProperty, padTitle));
        if (pad is null)
        {
            Console.WriteLine($"PAD NOT FOUND title='{padTitle}'");
            return;
        }
        var rect = pad.Current.BoundingRectangle;
        Console.WriteLine($"PAD rect={rect.X},{rect.Y} {rect.Width}x{rect.Height}");
        var btn = pad.FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Button),
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.NameProperty, buttonName)));
        if (btn is null)
        {
            Console.WriteLine($"BUTTON '{buttonName}' NOT FOUND");
            return;
        }
        var r = btn.Current.BoundingRectangle;
        Console.WriteLine($"BUTTON '{buttonName}' uia rect={r.X},{r.Y} {r.Width}x{r.Height}");

        // UIA coordinates for Avalonia windows are not in screen space, but the
        // layout *inside* the window is correct. Combine the relative position
        // with the real Win32 window rect to get an accurate screen point.
        // UIA reports the Avalonia window's client area; Win32 gives the real
        // screen origin of that client area. Map the UIA fraction onto it.
        var fx = (r.X + r.Width / 2 - rect.X) / rect.Width;
        var fy = (r.Y + r.Height / 2 - rect.Y) / rect.Height;
        var hwnd = FindWindow(null, padTitle);
        if (hwnd == IntPtr.Zero)
        {
            Console.WriteLine("PAD HWND NOT FOUND");
            return;
        }
        if (!GetClientRect(hwnd, out var client) || !ClientToScreen(hwnd, out var origin))
        {
            Console.WriteLine("CLIENT RECT FAILED");
            return;
        }
        var x = origin.X + (int)((client.Right - client.Left) * fx);
        var y = origin.Y + (int)((client.Bottom - client.Top) * fy);
        Console.WriteLine($"CLIENT {origin.X},{origin.Y} {client.Right - client.Left}x{client.Bottom - client.Top} fractions {fx:F3},{fy:F3}");
        Console.WriteLine($"AT POINT: {DescribeWindowAt(x, y)}");
        Click(x, y);
        Console.WriteLine($"CLICKED '{buttonName}' at {x},{y}");
    }

    /// <summary>
    /// Drags row <paramref name="from"/> onto row <paramref name="to"/> in the
    /// "Edit buttons" dialog, then clicks Save, so the config file on disk shows
    /// the resulting order.
    /// </summary>
    private static void EditorDrag(int from, int to)
    {
        const string title = "Edit buttons";
        var hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("EDITOR NOT FOUND"); return; }
        ForceForeground(hwnd);
        Thread.Sleep(500);

        var editor = System.Windows.Automation.AutomationElement.RootElement.FindFirst(
            System.Windows.Automation.TreeScope.Children,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.NameProperty, title));
        if (editor is null) { Console.WriteLine("EDITOR UIA NOT FOUND"); return; }

        var rows = editor.FindAll(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.ControlTypeProperty,
                System.Windows.Automation.ControlType.ListItem));
        Console.WriteLine($"ROWS={rows.Count}");
        if (rows.Count == 0) return;

        var er = editor.Current.BoundingRectangle;
        if (!GetClientRect(hwnd, out var client) || !ClientToScreen(hwnd, out var origin))
        {
            Console.WriteLine("CLIENT RECT FAILED");
            return;
        }

        var centres = new List<(string name, int x, int y)>();
        foreach (System.Windows.Automation.AutomationElement r in rows)
        {
            var b = r.Current.BoundingRectangle;
            var fx = (b.X + b.Width / 2 - er.X) / er.Width;
            var fy = (b.Y + b.Height / 2 - er.Y) / er.Height;
            centres.Add((r.Current.Name,
                (int)(origin.X + (client.Right - client.Left) * fx),
                (int)(origin.Y + (client.Bottom - client.Top) * fy)));
        }
        for (var i = 0; i < centres.Count; i++)
            Console.WriteLine($"ROW {i,2} y={centres[i].y,4} name='{centres[i].name}'");

        from = Math.Clamp(from, 0, centres.Count - 1);
        to = Math.Clamp(to, 0, centres.Count - 1);
        var (name, x1, y1) = centres[from];
        var (_, _, y2) = centres[to];
        Console.WriteLine($"DRAG row {from} ('{name}') -> row {to}  from y={y1} to y={y2}");

        DragMouse(x1, y1, x1, y2);
        Thread.Sleep(600);

        // Save writes the draft (with its new order) to shortcut_pad.json.
        var save = editor.FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Button),
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.NameProperty, "Save")));
        if (save is null) { Console.WriteLine("SAVE NOT FOUND"); return; }
        var sb = save.Current.BoundingRectangle;
        var sfx = (sb.X + sb.Width / 2 - er.X) / er.Width;
        var sfy = (sb.Y + sb.Height / 2 - er.Y) / er.Height;
        Click((int)(origin.X + (client.Right - client.Left) * sfx),
              (int)(origin.Y + (client.Bottom - client.Top) * sfy));
        Console.WriteLine("SAVE CLICKED");
    }

    /// <summary>
    /// Clicks a row (no drag) and saves, to prove a plain click still only
    /// selects and never reorders.
    /// </summary>
    private static void EditorClick(int index)
    {
        var (editor, er, origin, client, centres) = EditorRows();
        if (centres is null || centres.Count == 0) return;
        index = Math.Clamp(index, 0, centres.Count - 1);
        Click(centres[index].x, centres[index].y);
        Thread.Sleep(400);
        ClickSave(editor, er, origin, client);
        Console.WriteLine("ROW CLICKED + SAVED");
    }

    /// <summary>
    /// Presses a row, drags it to another and holds there while a screenshot is
    /// taken, so the drop-target highlight can be seen.
    /// </summary>
    private static void EditorHold(int from, int to, string shotPath)
    {
        var (editor, er, origin, client, centres) = EditorRows();
        if (centres is null || centres.Count == 0) return;
        from = Math.Clamp(from, 0, centres.Count - 1);
        to = Math.Clamp(to, 0, centres.Count - 1);

        var (x1, y1) = (centres[from].x, centres[from].y);
        var (_, y2) = (centres[to].x, centres[to].y);
        var size = Marshal.SizeOf<INPUT>();
        SendInput(1, new[] { MouseInput(x1, y1, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
        Thread.Sleep(150);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTDOWN) }, size);
        Thread.Sleep(150);
        for (var i = 1; i <= 15; i++)
        {
            var y = y1 + (y2 - y1) * i / 15;
            SendInput(1, new[] { MouseInput(x1, y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
            Thread.Sleep(45);
        }
        Thread.Sleep(500);
        Capture("Edit buttons", shotPath);
        Thread.Sleep(1500);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTUP) }, size);
        Console.WriteLine("HELD + SHOT + RELEASED");
    }

    private static (System.Windows.Automation.AutomationElement editor, System.Windows.Rect er,
        POINT origin, RECT client, List<(string name, int x, int y)>? centres) EditorRows()
    {
        const string title = "Edit buttons";
        var hwnd = FindWindow(null, title);
        if (hwnd == IntPtr.Zero) { Console.WriteLine("EDITOR NOT FOUND"); return default; }
        ForceForeground(hwnd);
        Thread.Sleep(500);

        var editor = System.Windows.Automation.AutomationElement.RootElement.FindFirst(
            System.Windows.Automation.TreeScope.Children,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.NameProperty, title));
        if (editor is null) { Console.WriteLine("EDITOR UIA NOT FOUND"); return default; }

        var rows = editor.FindAll(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.ControlTypeProperty,
                System.Windows.Automation.ControlType.ListItem));
        Console.WriteLine($"ROWS={rows.Count}");
        if (rows.Count == 0) return (editor, default, default, default, null);

        var er = editor.Current.BoundingRectangle;
        RECT client = default;
        POINT origin = default;
        if (!GetClientRect(hwnd, out client) || !ClientToScreen(hwnd, out origin))
        {
            Console.WriteLine("CLIENT RECT FAILED");
            return (editor, er, origin, client, null);
        }

        var centres = new List<(string name, int x, int y)>();
        foreach (System.Windows.Automation.AutomationElement r in rows)
        {
            var b = r.Current.BoundingRectangle;
            var fx = (b.X + b.Width / 2 - er.X) / er.Width;
            var fy = (b.Y + b.Height / 2 - er.Y) / er.Height;
            centres.Add((r.Current.Name,
                (int)(origin.X + (client.Right - client.Left) * fx),
                (int)(origin.Y + (client.Bottom - client.Top) * fy)));
        }
        return (editor, er, origin, client, centres);
    }

    private static void ClickSave(System.Windows.Automation.AutomationElement editor,
        System.Windows.Rect er, POINT origin, RECT client)
    {
        var save = editor.FindFirst(System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.AndCondition(
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.ControlTypeProperty,
                    System.Windows.Automation.ControlType.Button),
                new System.Windows.Automation.PropertyCondition(
                    System.Windows.Automation.AutomationElement.NameProperty, "Save")));
        if (save is null) { Console.WriteLine("SAVE NOT FOUND"); return; }
        var sb = save.Current.BoundingRectangle;
        var sfx = (sb.X + sb.Width / 2 - er.X) / er.Width;
        var sfy = (sb.Y + sb.Height / 2 - er.Y) / er.Height;
        Click((int)(origin.X + (client.Right - client.Left) * sfx),
              (int)(origin.Y + (client.Bottom - client.Top) * sfy));
        Console.WriteLine("SAVE CLICKED");
    }

    private static void DragMouse(int x1, int y1, int x2, int y2)
    {
        var size = Marshal.SizeOf<INPUT>();
        SendInput(1, new[] { MouseInput(x1, y1, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
        Thread.Sleep(150);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTDOWN) }, size);
        Thread.Sleep(150);
        for (var i = 1; i <= 15; i++)
        {
            var x = x1 + (x2 - x1) * i / 15;
            var y = y1 + (y2 - y1) * i / 15;
            SendInput(1, new[] { MouseInput(x, y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE) }, size);
            Thread.Sleep(45);
        }
        Thread.Sleep(200);
        SendInput(1, new[] { MouseInput(0, 0, MOUSEEVENTF_LEFTUP) }, size);
        Thread.Sleep(250);
    }

    private static INPUT MouseInput(int x, int y, uint flags)
    {
        var w = Screen.PrimaryScreen.Bounds.Width;
        var h = Screen.PrimaryScreen.Bounds.Height;
        return new INPUT
        {
            type = 0,
            u = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = (int)((long)x * 65535 / (w - 1)),
                    dy = (int)((long)y * 65535 / (h - 1)),
                    dwFlags = flags,
                },
            },
        };
    }

    private static void Click(int x, int y)
    {
        var w = Screen.PrimaryScreen.Bounds.Width;
        var h = Screen.PrimaryScreen.Bounds.Height;
        var ax = (int)((long)x * 65535 / (w - 1));
        var ay = (int)((long)y * 65535 / (h - 1));
        var move = new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dx = ax, dy = ay, dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE } } };
        var down = new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } };
        var up = new INPUT { type = 0, u = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } };
        var size = Marshal.SizeOf<INPUT>();
        SendInput(1, new[] { move }, size);
        Thread.Sleep(90);
        SendInput(1, new[] { down }, size);
        Thread.Sleep(70);
        SendInput(1, new[] { up }, size);
        Console.WriteLine("SENTINPUT 1/1 x3 (split)");
    }

    private static void ForceForeground(IntPtr hWnd)
    {
        var fg = GetForegroundWindow();
        var fgThread = GetWindowThreadProcessId(fg, out _);
        var myThread = GetCurrentThreadId();
        if (fgThread != myThread && AttachThreadInput(myThread, fgThread, true))
        {
            SetForegroundWindow(hWnd);
            SetActiveWindow(hWnd);
            AttachThreadInput(myThread, fgThread, false);
        }
        else
        {
            SetForegroundWindow(hWnd);
        }
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    private static void Pump(int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public INPUTUNION u; }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr ExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint Time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }
}
