using System.Runtime.InteropServices;

namespace ShortcutPad;

/// <summary>
/// macOS implementation built on CoreGraphics CGEvent.
///
/// Requires Accessibility permission: the first time it posts an event macOS
/// blocks it and asks the user to grant Privacy &amp; Security → Accessibility
/// (the same prompt every hotkey utility gets).
/// </summary>
public sealed class MacKeystrokeSender : IKeystrokeSender
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    private const int kCGHIDEventTap = 0;

    // CGEvent flags
    private const ulong kCGEventFlagMaskShift = 1UL << 17;
    private const ulong kCGEventFlagMaskControl = 1UL << 18;
    private const ulong kCGEventFlagMaskAlternate = 1UL << 19;
    private const ulong kCGEventFlagMaskCommand = 1UL << 20;

    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keycode, bool keyDown);
    [DllImport(CoreGraphics)] private static extern void CGEventSetFlags(IntPtr ev, ulong flags);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(int tap, IntPtr ev);
    [DllImport(CoreGraphics)] private static extern void CGEventKeyboardSetUnicodeString(IntPtr ev, ushort length, string str);
    [DllImport(CoreGraphics)] private static extern void CFRelease(IntPtr obj);

    [DllImport(Foundation)] private static extern IntPtr objc_getClass(string name);
    [DllImport(Foundation)] private static extern IntPtr sel_getUid(string name);
    [DllImport(Foundation)] private static extern IntPtr objc_msgSend(IntPtr recv, IntPtr sel);
    [DllImport(Foundation)] private static extern IntPtr objc_msgSend(IntPtr recv, IntPtr sel, IntPtr arg);
    [DllImport(Foundation)] private static extern IntPtr objc_msgSend(IntPtr recv, IntPtr sel, IntPtr arg, bool arg2);

    // kVK_* virtual key codes (ANSI layout)
    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = 0x00, ["s"] = 0x01, ["d"] = 0x02, ["f"] = 0x03, ["h"] = 0x04, ["g"] = 0x05,
        ["z"] = 0x06, ["x"] = 0x07, ["c"] = 0x08, ["v"] = 0x09, ["b"] = 0x0B, ["q"] = 0x0C,
        ["w"] = 0x0D, ["e"] = 0x0E, ["r"] = 0x0F, ["y"] = 0x10, ["t"] = 0x11,
        ["1"] = 0x12, ["2"] = 0x13, ["3"] = 0x14, ["4"] = 0x15, ["6"] = 0x16, ["5"] = 0x17,
        ["9"] = 0x19, ["7"] = 0x1A, ["8"] = 0x1C, ["0"] = 0x1D, ["-"] = 0x1B, ["="] = 0x18,
        ["["] = 0x21, ["]"] = 0x1E, [";"] = 0x29, ["'"] = 0x27, ["\\"] = 0x2A, [","] = 0x2B,
        ["."] = 0x2F, ["/"] = 0x2C, ["`"] = 0x32,
        ["enter"] = 0x24, ["return"] = 0x24, ["tab"] = 0x30, ["space"] = 0x31,
        ["backspace"] = 0x33, ["delete"] = 0x75, ["del"] = 0x75,
        ["esc"] = 0x35, ["escape"] = 0x35,
        ["home"] = 0x73, ["end"] = 0x77, ["pageup"] = 0x74, ["pagedown"] = 0x79,
        ["left"] = 0x7B, ["right"] = 0x7C, ["down"] = 0x7D, ["up"] = 0x7E,
        ["f1"] = 0x7A, ["f2"] = 0x78, ["f3"] = 0x63, ["f4"] = 0x76, ["f5"] = 0x60,
        ["f6"] = 0x61, ["f7"] = 0x62, ["f8"] = 0x64, ["f9"] = 0x65, ["f10"] = 0x6D,
        ["f11"] = 0x67, ["f12"] = 0x6F,
        // NX_KEYTYPE_* — media keys are posted with these codes.
        ["volume_up"] = 0x00, ["volumeup"] = 0x00,
        ["volume_down"] = 0x01, ["volumedown"] = 0x01,
        ["volume_mute"] = 0x07, ["mute"] = 0x07,
        ["media_play_pause"] = 0x10, ["mediaplaypause"] = 0x10, ["playpause"] = 0x10,
        ["media_next"] = 0x11, ["medianext"] = 0x11, ["nexttrack"] = 0x11,
        ["media_prev"] = 0x12, ["mediaprev"] = 0x12, ["prevtrack"] = 0x12,
    };

    public void Send(string keys)
    {
        var spec = KeySpec.Parse(keys);
        if (spec.Error is not null) throw new FormatException(spec.Error);

        var flags = 0UL;
        foreach (var m in spec.Modifiers)
        {
            flags |= m switch
            {
                "shift" => kCGEventFlagMaskShift,
                "ctrl" => kCGEventFlagMaskControl,
                "alt" => kCGEventFlagMaskAlternate,
                "win" => kCGEventFlagMaskCommand,
                _ => 0UL,
            };
        }

        // A bare character without modifiers is typed as Unicode, which is
        // layout-independent (no need to guess the active keymap).
        if (spec.CharKey is char ch && spec.Modifiers.Count == 0)
        {
            TypeText(ch.ToString());
            return;
        }

        ushort keycode;
        if (spec.NamedKey is string name)
        {
            if (!NamedKeys.TryGetValue(name, out keycode))
                throw new FormatException($"Key '{name}' is not supported on macOS.");
        }
        else if (spec.CharKey is char c)
        {
            keycode = AnsiKeycode(c);
        }
        else
        {
            throw new FormatException($"No key to press in '{keys}'.");
        }

        Post(keycode, flags, down: true);
        Post(keycode, flags, down: false);
    }

    public void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                Post(0x24, 0, down: true);
                Post(0x24, 0, down: false);
                continue;
            }
            var ev = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
            if (ev == IntPtr.Zero) throw new InvalidOperationException("CGEventCreateKeyboardEvent failed (macOS Accessibility permission missing?).");
            var s = ToNSString(ch.ToString());
            CGEventKeyboardSetUnicodeString(ev, 1, ch.ToString());
            CGEventPost(kCGHIDEventTap, ev);
            CFRelease(ev);
            ReleaseNSString(s);
        }
    }

    private void Post(ushort keycode, ulong flags, bool down)
    {
        var ev = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, down);
        if (ev == IntPtr.Zero)
            throw new InvalidOperationException(
                "CGEventCreateKeyboardEvent failed — macOS Accessibility permission is required.");
        if (flags != 0) CGEventSetFlags(ev, flags);
        CGEventPost(kCGHIDEventTap, ev);
        CFRelease(ev);
    }

    private static ushort AnsiKeycode(char c) => char.ToLowerInvariant(c) switch
    {
        'a' => 0x00, 's' => 0x01, 'd' => 0x02, 'f' => 0x03, 'h' => 0x04, 'g' => 0x05,
        'z' => 0x06, 'x' => 0x07, 'c' => 0x08, 'v' => 0x09, 'b' => 0x0B, 'q' => 0x0C,
        'w' => 0x0D, 'e' => 0x0E, 'r' => 0x0F, 'y' => 0x10, 't' => 0x11,
        '1' => 0x12, '2' => 0x13, '3' => 0x14, '4' => 0x15, '6' => 0x16, '5' => 0x17,
        '9' => 0x19, '7' => 0x1A, '8' => 0x1C, '0' => 0x1D, '-' => 0x1B, '=' => 0x18,
        '[' => 0x21, ']' => 0x1E, ';' => 0x29, '\'' => 0x27, '\\' => 0x2A, ',' => 0x2B,
        '.' => 0x2F, '/' => 0x2C, '`' => 0x32,
        _ => throw new FormatException($"Character '{c}' has no macOS key code; use a 'text' button instead."),
    };

    private static IntPtr ToNSString(string s)
    {
        var ptr = Marshal.StringToHGlobalAnsi(s);
        var nsstr = objc_msgSend(objc_getClass("NSString"), sel_getUid("stringWithUTF8String:"), ptr);
        Marshal.FreeHGlobal(ptr);
        return nsstr;
    }

    private static void ReleaseNSString(IntPtr nsstr) => CFRelease(nsstr);
}
