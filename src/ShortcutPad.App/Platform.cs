using Avalonia.Controls;
using System.Runtime.InteropServices;

namespace ShortcutPad;

/// <summary>The app the user was in before clicking the pad.</summary>
public sealed class ForegroundTarget
{
    public IntPtr Hwnd;          // Windows
    public string? BundleId;     // macOS
    public ulong X11Window;      // Linux: the focused X11 window (XID)
    public string? LinuxPid;     // Linux: _NET_WM_PID of that window
    public bool IsValid => Hwnd != IntPtr.Zero || !string.IsNullOrEmpty(BundleId) || X11Window != 0;
}

public interface IPadPlatform
{
    string Name { get; }

    /// <summary>Handle of the pad's own window (Windows) / its bundle id (macOS).</summary>
    string? SelfId { get; set; }

    ForegroundTarget? LastOther { get; }

    /// <summary>Sample the current foreground app. Call on a timer.</summary>
    void Poll();

    /// <summary>Hand focus back to a previously sampled app.</summary>
    bool Restore(ForegroundTarget target);

    /// <summary>
    /// Whether a pointer button is currently held down. The edge-snap uses this
    /// to tell a window the user is dragging apart from one the app moved
    /// itself (restoring a saved position, resizing) - otherwise simply
    /// restarting the pad inside the snap band silently docks it.
    /// </summary>
    bool IsPointerDown();

    /// <summary>
    /// Show or hide the window's maximize control. The pad is a utility window
    /// with no useful maximized state, but it must stay resizable so the user
    /// can size it freely - so this is not the same as making it
    /// non-resizable.
    /// </summary>
    /// <param name="visible">True to restore the platform default.</param>
    /// <param name="window">
    /// The pad's native window: HWND on Windows, NSWindow on macOS, X11 window
    /// id on Linux.
    /// </param>
    void SetMaximizeButton(bool visible, IntPtr window);
}

public static class PadPlatform
{
    public static IPadPlatform Create() =>
        OperatingSystem.IsWindows() ? new WindowsPlatform()
        : OperatingSystem.IsMacOS() ? new MacPlatform()
        : OperatingSystem.IsLinux() ? new LinuxPlatform()
        : throw new PlatformNotSupportedException("Shortcut Pad supports Windows, macOS and Linux (X11).");
}

/// <summary>
/// Window-chrome tweaks shared by the pad and its dialogs.
/// </summary>
internal static class WindowChrome
{
    /// <summary>
    /// Hide a window's maximize control. The pad and its dialogs are utility
    /// windows with no useful maximized state, but they stay resizable.
    /// Call this again after <see cref="Window.CanResize"/> changes: the
    /// platform rebuilds the native frame from it, which would bring the
    /// button back.
    /// </summary>
    public static void HideMaximize(Window window, IPadPlatform platform)
    {
        if (window.TryGetPlatformHandle() is { } handle)
            platform.SetMaximizeButton(false, handle.Handle);
    }
}

// ---------------------------------------------------------------- Windows

public sealed class WindowsPlatform : IPadPlatform
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);

    private const int VK_LBUTTON = 0x01;

    public string Name => "Windows";
    public string? SelfId { get; set; }
    public ForegroundTarget? LastOther { get; private set; }

    private IntPtr SelfHwnd => IntPtr.TryParse(SelfId, out var v) ? v : IntPtr.Zero;

    public void Poll()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == SelfHwnd || !IsWindow(fg)) return;
        LastOther = new ForegroundTarget { Hwnd = fg };
    }

    public bool Restore(ForegroundTarget target)
    {
        if (target.Hwnd == IntPtr.Zero || !IsWindow(target.Hwnd)) return false;
        return SetForegroundWindow(target.Hwnd);
    }

    public bool IsPointerDown() => (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;

    // -------------------------------------------------------- window chrome

    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;

    // SetWindowPos flags: force the frame (and its buttons) to be rebuilt.
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after,
        int x, int y, int cx, int cy, uint flags);

    /// <summary>
    /// Drops <c>WS_MAXIMIZEBOX</c> from the window style. The thick frame is
    /// left alone, so the pad can still be resized by dragging its edges.
    /// </summary>
    public void SetMaximizeButton(bool visible, IntPtr window)
    {
        if (window == IntPtr.Zero) return;

        var style = GetWindowLong(window, GWL_STYLE);
        var updated = visible ? style | WS_MAXIMIZEBOX : style & ~WS_MAXIMIZEBOX;
        if (updated == style) return;

        SetWindowLong(window, GWL_STYLE, updated);
        SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }
}

// ---------------------------------------------------------------- macOS

/// <summary>
/// macOS foreground tracking via NSWorkspace. The previous app is restored with
/// NSRunningApplication.activateWithOptions:.
/// </summary>
public sealed class MacPlatform : IPadPlatform
{
    private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    // NSApplicationActivateAllWindows | NSApplicationActivateIgnoringOtherApps
    private const int ActivateOptions = 1 | 2;

    [DllImport(Foundation)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Foundation)] private static extern IntPtr sel_getUid(string name);
    [DllImport(Foundation)] private static extern IntPtr objc_msgSend(IntPtr recv, IntPtr sel);
    [DllImport(Foundation)] private static extern IntPtr objc_msgSend(IntPtr recv, IntPtr sel, IntPtr arg);

    public string Name => "macOS";
    public string? SelfId { get; set; }
    public ForegroundTarget? LastOther { get; private set; }

    public void Poll()
    {
        var bundle = ActiveBundleId();
        if (string.IsNullOrEmpty(bundle) || bundle == SelfId) return;
        LastOther = new ForegroundTarget { BundleId = bundle };
    }

    public bool Restore(ForegroundTarget target)
    {
        if (string.IsNullOrEmpty(target.BundleId)) return false;
        var nsBundle = ToNSString(target.BundleId);
        var apps = objc_msgSend(objc_getClass("NSRunningApplication"),
            sel_getUid("runningApplicationsWithBundleIdentifier:"), nsBundle);
        Release(nsBundle);
        if (apps == IntPtr.Zero) return false;
        var app = objc_msgSend(apps, sel_getUid("firstObject"));
        if (app == IntPtr.Zero) return false;
        objc_msgSend(app, sel_getUid("activateWithOptions:"), (IntPtr)ActivateOptions);
        return true;
    }

    /// <summary>
    /// <c>[NSEvent pressedMouseButtons]</c>: non-zero while a button is held.
    /// </summary>
    public bool IsPointerDown() =>
        objc_msgSend(objc_getClass("NSEvent"), sel_getUid("pressedMouseButtons")) != IntPtr.Zero;

    /// <summary>
    /// Hides the green zoom button. Resizing is left untouched, so the pad can
    /// still be sized by dragging its corner.
    /// </summary>
    public void SetMaximizeButton(bool visible, IntPtr window)
    {
        if (window == IntPtr.Zero) return;

        // NSWindowZoomButton (close = 0, miniaturize = 1, zoom = 2)
        var button = objc_msgSend(window, sel_getUid("standardWindowButton:"), (IntPtr)2);
        if (button == IntPtr.Zero) return;
        objc_msgSend(button, sel_getUid("setHidden:"), (IntPtr)(visible ? 0 : 1));
    }

    private static string? ActiveBundleId()
    {
        var workspace = objc_msgSend(objc_getClass("NSWorkspace"), sel_getUid("sharedWorkspace"));
        if (workspace == IntPtr.Zero) return null;
        var app = objc_msgSend(workspace, sel_getUid("activeApplication"));
        if (app == IntPtr.Zero) return null;
        var key = ToNSString("NSApplicationBundleIdentifier");
        var value = objc_msgSend(app, sel_getUid("objectForKey:"), key);
        Release(key);
        if (value == IntPtr.Zero) return null;
        return Utf8(value);
    }

    private static IntPtr ToNSString(string s)
    {
        var ptr = Marshal.StringToHGlobalAnsi(s);
        var ns = objc_msgSend(objc_getClass("NSString"), sel_getUid("stringWithUTF8String:"), ptr);
        Marshal.FreeHGlobal(ptr);
        return ns;
    }

    private static string? Utf8(IntPtr nsString)
    {
        var cstr = objc_msgSend(nsString, sel_getUid("UTF8String"));
        return cstr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(cstr);
    }

    private static void Release(IntPtr nsObject) => CFRelease(nsObject);

    [DllImport(Foundation)] private static extern void CFRelease(IntPtr obj);
}
