using System.Runtime.InteropServices;

namespace ShortcutPad;

/// <summary>
/// Linux implementation built on X11 + the XTest extension.
///
/// Works on any X11 session, including XWayland (XTest talks to the X server,
/// not to the compositor). A native Wayland session has no generic injection
/// API; that case needs a virtual input device via /dev/uinput instead.
///
/// Runtime requirements: libX11 and libXtst (Debian/Ubuntu: libxtst6).
/// </summary>
public sealed class LinuxKeystrokeSender : IKeystrokeSender
{
    private const string X11 = "libX11.so.6";
    private const string Xtst = "libXtst.so.6";

    [DllImport(X11)] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport(X11)] private static extern int XDisplayKeycodes(IntPtr d, out int min, out int max);
    [DllImport(X11)] private static extern IntPtr XGetKeyboardMapping(IntPtr d, byte first, int count, out int perKeycode);
    [DllImport(X11)] private static extern int XFree(IntPtr p);
    [DllImport(X11)] private static extern int XFlush(IntPtr d);
    [DllImport(X11)] private static extern byte XKeysymToKeycode(IntPtr d, ulong keysym);
    [DllImport(X11)] private static extern ulong XStringToKeysym(string s);

    [DllImport(Xtst)] private static extern int XTestFakeKeyEvent(IntPtr d, byte keycode, bool isPress, ulong delayUs);

    // Named keys -> X11 keysym names.
    private static readonly Dictionary<string, string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backspace"] = "BackSpace", ["tab"] = "Tab",
        ["enter"] = "Return", ["return"] = "Return",
        ["esc"] = "Escape", ["escape"] = "Escape", ["space"] = "space",
        ["pageup"] = "Prior", ["pagedown"] = "Next",
        ["end"] = "End", ["home"] = "Home",
        ["left"] = "Left", ["up"] = "Up", ["right"] = "Right", ["down"] = "Down",
        ["insert"] = "Insert", ["delete"] = "Delete", ["del"] = "Delete",
        ["printscreen"] = "Print", ["prtsc"] = "Print",
        // XF86 media keys
        ["volume_mute"] = "XF86AudioMute", ["mute"] = "XF86AudioMute",
        ["volume_down"] = "XF86AudioLowerVolume", ["volumedown"] = "XF86AudioLowerVolume",
        ["volume_up"] = "XF86AudioRaiseVolume", ["volumeup"] = "XF86AudioRaiseVolume",
        ["media_next"] = "XF86AudioNext", ["medianext"] = "XF86AudioNext", ["nexttrack"] = "XF86AudioNext",
        ["media_prev"] = "XF86AudioPrev", ["mediaprev"] = "XF86AudioPrev", ["prevtrack"] = "XF86AudioPrev",
        ["media_stop"] = "XF86AudioStop", ["mediastop"] = "XF86AudioStop",
        ["media_play_pause"] = "XF86AudioPlay", ["mediaplaypause"] = "XF86AudioPlay", ["playpause"] = "XF86AudioPlay",
    };

    // Modifier keycodes, resolved once from the keysyms below.
    private static readonly Dictionary<string, ulong> ModifierKeysyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0xFFE3,     // XK_Control_L
        ["alt"] = 0xFFE9,      // XK_Alt_L
        ["shift"] = 0xFFE1,    // XK_Shift_L
        ["win"] = 0xFFEB,      // XK_Super_L
    };

    static LinuxKeystrokeSender()
    {
        for (var i = 1; i <= 12; i++) NamedKeys["f" + i] = "F" + i;
    }

    private IntPtr _dpy;
    private bool _opened;

    private IntPtr Display
    {
        get
        {
            if (!_opened)
            {
                _dpy = XOpenDisplay(null);
                if (_dpy == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "Linux: no X11 display available (DISPLAY is not set, or libX11 could not connect).");
                _opened = true;
            }
            return _dpy;
        }
    }

    public void Send(string keys)
    {
        var spec = KeySpec.Parse(keys);
        if (spec.Error is not null) throw new FormatException(spec.Error);

        var mods = new List<byte>();
        foreach (var m in spec.Modifiers)
            if (ModifierKeysyms.TryGetValue(m, out var ks)) mods.Add(Keycode(ks, m));

        ulong main;
        if (spec.NamedKey is not null)
        {
            if (!NamedKeys.TryGetValue(spec.NamedKey, out var name))
                throw new FormatException($"Key '{spec.NamedKey}' is not supported on Linux.");
            main = Keysym(name, spec.NamedKey);
        }
        else if (spec.CharKey is char ch)
        {
            main = CharKeysym(ch);
        }
        else throw new FormatException($"No key to press in '{keys}'.");

        Tap(main, mods);
    }

    public void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n') { Tap(0xFF0D, new List<byte>()); continue; }   // XK_Return
            Tap(CharKeysym(ch), new List<byte>());
        }
    }

    // ------------------------------------------------------------------ core

    private void Tap(ulong keysym, List<byte> mods)
    {
        var (keycode, shifted) = Locate(keysym);
        if (shifted && !mods.Contains(Keycode(ModifierKeysyms["shift"], "shift")))
            mods.Add(Keycode(ModifierKeysyms["shift"], "shift"));

        var d = Display;
        foreach (var m in mods) XTestFakeKeyEvent(d, m, true, 0);
        XTestFakeKeyEvent(d, keycode, true, 0);
        XTestFakeKeyEvent(d, keycode, false, 0);
        for (var i = mods.Count - 1; i >= 0; i--) XTestFakeKeyEvent(d, mods[i], false, 0);
        XFlush(d);
    }

    private byte Keycode(ulong keysym, string what)
    {
        var kc = XKeysymToKeycode(Display, keysym);
        if (kc == 0)
            throw new InvalidOperationException($"Linux: no keycode for '{what}' in the current keyboard layout.");
        return kc;
    }

    private static ulong Keysym(string name, string what)
    {
        var ks = XStringToKeysym(name);
        if (ks == 0 || ks == 0xFFFFFFFFFFFFFFFFUL)
            throw new InvalidOperationException($"Linux: unknown keysym '{name}' ({what}).");
        return ks;
    }

    private static ulong CharKeysym(char ch)
    {
        // Latin-1 keysyms are the Latin-1 code points; everything else uses the
        // X11 "Unicode keysym" convention (U+01000000 | code point).
        return ch < 0x80 ? (ulong)ch : 0x01000000UL | (ulong)ch;
    }

    /// <summary>
    /// Find the keycode and shift level that produce this keysym.
    /// Level 0 = plain, level 1 = with Shift (the only levels handled here).
    /// </summary>
    private (byte keycode, bool shift) Locate(ulong keysym)
    {
        var d = Display;
        XDisplayKeycodes(d, out var min, out var max);
        if (max < min) throw new InvalidOperationException("Linux: the X server reported an empty keycode range.");
        var count = max - min + 1;
        var ptr = XGetKeyboardMapping(d, (byte)min, count, out var per);
        if (ptr == IntPtr.Zero)
            throw new InvalidOperationException("Linux: XGetKeyboardMapping failed (is libX11 usable?).");
        try
        {
            var n = count * per;
            var map = new ulong[n];
            // KeySym is a 64-bit unsigned long on LP64.
            for (var i = 0; i < n; i++)
                map[i] = (ulong)Marshal.ReadInt64(ptr, i * 8);

            for (var level = 0; level <= 1; level++)
            {
                for (var i = 0; i < count; i++)
                {
                    if (level >= per) break;
                    if (map[i * per + level] == keysym)
                        return ((byte)(min + i), level == 1);
                }
            }
        }
        finally { XFree(ptr); }

        throw new InvalidOperationException(
            $"Linux: keysym 0x{keysym:X} is not reachable in the current keyboard layout (needs a non-Shift modifier).");
    }
}
