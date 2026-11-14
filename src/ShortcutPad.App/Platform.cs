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
}

public static class PadPlatform
{
    public static IPadPlatform Create() =>
        OperatingSystem.IsWindows() ? new WindowsPlatform()
        : OperatingSystem.IsMacOS() ? new MacPlatform()
        : OperatingSystem.IsLinux() ? new LinuxPlatform()
        : throw new PlatformNotSupportedException("Shortcut Pad supports Windows, macOS and Linux (X11).");
}

// ---------------------------------------------------------------- Windows

public sealed class WindowsPlatform : IPadPlatform
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

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
