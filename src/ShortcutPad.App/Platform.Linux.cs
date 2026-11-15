using System.Runtime.InteropServices;

namespace ShortcutPad;

/// <summary>
/// Linux foreground tracking over X11, using the EWMH properties
/// <c>_NET_ACTIVE_WINDOW</c> (which window has focus) and <c>_NET_WM_PID</c>
/// (which process owns it). Focus is handed back with a
/// <c>_NET_ACTIVE_WINDOW</c> client message to the root window — the same
/// mechanism a task switcher uses, so it depends on the window manager
/// honouring EWMH (true for essentially every X11 WM).
///
/// Wayland has no equivalent of _NET_ACTIVE_WINDOW, so on a native Wayland
/// session this degrades to "cannot track"; the rest of the pad still works.
/// </summary>
public sealed class LinuxPlatform : IPadPlatform
{
    private const string X11 = "libX11.so.6";

    private const int ClientMessage = 33;
    private const long SubstructureRedirectMask = 1L << 24;
    private const long SubstructureNotifyMask = 1L << 25;

    [DllImport(X11)] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport(X11)] private static extern ulong XDefaultRootWindow(IntPtr d);
    [DllImport(X11)] private static extern ulong XInternAtom(IntPtr d, string name, bool onlyIfExists);
    [DllImport(X11)] private static extern int XFlush(IntPtr d);

    [DllImport(X11)] private static extern int XGetWindowProperty(
        IntPtr d, ulong w, ulong property,
        long longOffset, long longLength, bool delete, ulong reqType,
        out ulong actualType, out int actualFormat,
        out ulong nitems, out ulong bytesAfter, out IntPtr prop);

    [DllImport(X11)] private static extern int XFree(IntPtr p);
    [DllImport(X11)] private static extern int XSendEvent(IntPtr d, ulong w, bool propagate, long mask, ref XEvent ev);

    [DllImport(X11)] private static extern bool XQueryPointer(
        IntPtr d, ulong w,
        out ulong root, out ulong child,
        out int rootX, out int rootY,
        out int winX, out int winY, out uint mask);

    private const uint Button1Mask = 1u << 8;

    public string Name => "Linux";
    public string? SelfId { get; set; }
    public ForegroundTarget? LastOther { get; private set; }

    private IntPtr _dpy;
    private bool _opened;

    private IntPtr Display
    {
        get
        {
            if (!_opened)
            {
                _dpy = XOpenDisplay(null);
                _opened = true;
            }
            return _dpy;
        }
    }

    private ulong SelfWindow => ulong.TryParse(SelfId, out var v) ? v : 0;

    public void Poll()
    {
        try
        {
            if (Display == IntPtr.Zero) return;
            var win = ActiveWindow();
            if (win == 0 || win == SelfWindow) return;
            LastOther = new ForegroundTarget { X11Window = win, LinuxPid = WmPid(win) };
        }
        catch
        {
            // A missing display or a vanished window is not fatal: the pad just
            // keeps whatever target it had.
        }
    }

    public bool Restore(ForegroundTarget target)
    {
        try
        {
            if (target.X11Window == 0 || Display == IntPtr.Zero) return false;

            var netActive = XInternAtom(Display, "_NET_ACTIVE_WINDOW", false);
            if (netActive == 0) return false;

            var ev = new XEvent
            {
                ClientMessage = new XClientMessageEvent
                {
                    type = ClientMessage,
                    send_event = 1,
                    display = Display,
                    window = target.X11Window,
                    message_type = netActive,
                    format = 32,
                    data0 = 2,   // source indication: 2 = application
                    data1 = 0,   // timestamp: 0 = unknown (most WMs accept this)
                    data2 = 0,   // currently-active window: none
                    data3 = 0,
                    data4 = 0,
                }
            };

            XSendEvent(Display, XDefaultRootWindow(Display), false,
                SubstructureRedirectMask | SubstructureNotifyMask, ref ev);
            XFlush(Display);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Pointer button state from the X server; see IPadPlatform.</summary>
    public bool IsPointerDown()
    {
        try
        {
            if (Display == IntPtr.Zero) return false;
            var ok = XQueryPointer(Display, XDefaultRootWindow(Display),
                out _, out _, out _, out _, out _, out _, out var mask);
            return ok && (mask & Button1Mask) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Publishes <c>_NET_WM_ALLOWED_ACTIONS</c> without the two maximize
    /// actions, which is how an EWMH window manager knows not to offer
    /// maximizing. Resizing stays allowed.
    /// </summary>
    public void SetMaximizeButton(bool visible, IntPtr window)
    {
        try
        {
            if (Display == IntPtr.Zero || window == IntPtr.Zero) return;

            var names = VisibleActions(visible);
            var atoms = new ulong[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                atoms[i] = XInternAtom(Display, names[i], false);
                if (atoms[i] == 0) return;
            }

            var property = XInternAtom(Display, "_NET_WM_ALLOWED_ACTIONS", false);
            var type = XInternAtom(Display, "ATOM", false);
            if (property == 0 || type == 0) return;

            var pinned = GCHandle.Alloc(atoms, GCHandleType.Pinned);
            try
            {
                XChangeProperty(Display, (ulong)window.ToInt64(), property, type, 32, 0,
                    pinned.AddrOfPinnedObject(), atoms.Length);
                XFlush(Display);
            }
            finally
            {
                pinned.Free();
            }
        }
        catch
        {
            // A window manager without EWMH simply keeps its default buttons.
        }
    }

    private static readonly string[] AllActions =
    {
        "_NET_WM_ACTION_MOVE",
        "_NET_WM_ACTION_RESIZE",
        "_NET_WM_ACTION_MINIMIZE",
        "_NET_WM_ACTION_SHADE",
        "_NET_WM_ACTION_STICK",
        "_NET_WM_ACTION_MAXIMIZE_HORZ",
        "_NET_WM_ACTION_MAXIMIZE_VERT",
        "_NET_WM_ACTION_FULLSCREEN",
        "_NET_WM_ACTION_CHANGE_DESKTOP",
        "_NET_WM_ACTION_CLOSE",
        "_NET_WM_ACTION_ABOVE",
        "_NET_WM_ACTION_BELOW",
    };

    private static string[] VisibleActions(bool maximize) =>
        maximize ? AllActions
        : AllActions.Where(a => !a.Contains("MAXIMIZE")).ToArray();

    [DllImport(X11)] private static extern int XChangeProperty(
        IntPtr d, ulong w, ulong property, ulong type, int format, int mode,
        IntPtr data, int nelements);

    // ------------------------------------------------------------------ X11

    private ulong ActiveWindow()
    {
        var root = XDefaultRootWindow(Display);
        var prop = XInternAtom(Display, "_NET_ACTIVE_WINDOW", false);
        if (prop == 0) return 0;

        var data = GetCardinal(Display, root, prop);
        return data.Count > 0 ? data[0] : 0;
    }

    private string? WmPid(ulong window)
    {
        var prop = XInternAtom(Display, "_NET_WM_PID", false);
        if (prop == 0) return null;
        var data = GetCardinal(Display, window, prop);
        return data.Count > 0 ? data[0].ToString() : null;
    }

    /// <summary>Reads a format-32 cardinal/window property as 32-bit values.</summary>
    private static List<ulong> GetCardinal(IntPtr d, ulong window, ulong property)
    {
        var result = new List<ulong>();
        var ok = XGetWindowProperty(d, window, property, 0, 8, false, 0,
            out _, out var format, out var items, out _, out var prop);
        if (ok != 0 || prop == IntPtr.Zero || items == 0)
        {
            if (prop != IntPtr.Zero) XFree(prop);
            return result;
        }

        try
        {
            for (ulong i = 0; i < items; i++)
            {
                result.Add(format switch
                {
                    32 => (uint)Marshal.ReadInt32(prop, (int)(i * 4)),
                    16 => (ushort)Marshal.ReadInt16(prop, (int)(i * 2)),
                    8  => Marshal.ReadByte(prop, (int)i),
                    _  => 0UL,
                });
            }
        }
        finally { XFree(prop); }

        return result;
    }

    [StructLayout(LayoutKind.Explicit, Size = 96)]
    private struct XClientMessageEvent
    {
        [FieldOffset(0)]  public int type;
        [FieldOffset(8)]  public ulong serial;
        [FieldOffset(16)] public int send_event;
        [FieldOffset(24)] public IntPtr display;
        [FieldOffset(32)] public ulong window;
        [FieldOffset(40)] public ulong message_type;
        [FieldOffset(48)] public int format;
        // data union: 5 x 64-bit longs on LP64 (format 32 uses these)
        [FieldOffset(56)] public long data0;
        [FieldOffset(64)] public long data1;
        [FieldOffset(72)] public long data2;
        [FieldOffset(80)] public long data3;
        [FieldOffset(88)] public long data4;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct XEvent
    {
        [FieldOffset(0)] public XClientMessageEvent ClientMessage;
    }
}
