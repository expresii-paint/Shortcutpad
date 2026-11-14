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
            }
        }

        ApplicationConfiguration.Initialize();

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

    private static void ClickByName(string padTitle, string buttonName)
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
