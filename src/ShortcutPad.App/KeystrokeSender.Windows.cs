using System.Runtime.InteropServices;

namespace ShortcutPad;

/// <summary> Sends keystrokes to whatever app is currently foreground. </summary>
public interface IKeystrokeSender
{
    void Send(string keys);
    void TypeText(string text);
}

/// <summary> Windows implementation built on user32 SendInput. </summary>
public sealed class WindowsKeystrokeSender : IKeystrokeSender
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern ushort VkKeyScanW(char ch);

    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E,
        0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3,
    };

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["backspace"] = 0x08, ["tab"] = 0x09, ["enter"] = 0x0D, ["return"] = 0x0D,
        ["esc"] = 0x1B, ["escape"] = 0x1B, ["space"] = 0x20,
        ["pageup"] = 0x21, ["pagedown"] = 0x22, ["end"] = 0x23, ["home"] = 0x24,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["insert"] = 0x2D, ["delete"] = 0x2E, ["del"] = 0x2E,
        ["printscreen"] = 0x2C, ["prtsc"] = 0x2C,
        ["volume_mute"] = 0xAD, ["mute"] = 0xAD,
        ["volume_down"] = 0xAE, ["volumedown"] = 0xAE,
        ["volume_up"] = 0xAF, ["volumeup"] = 0xAF,
        ["media_next"] = 0xB0, ["medianext"] = 0xB0, ["nexttrack"] = 0xB0,
        ["media_prev"] = 0xB1, ["mediaprev"] = 0xB1, ["prevtrack"] = 0xB1,
        ["media_stop"] = 0xB2, ["mediastop"] = 0xB2,
        ["media_play_pause"] = 0xB3, ["mediaplaypause"] = 0xB3, ["playpause"] = 0xB3,
    };

    private static readonly Dictionary<string, ushort> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = 0xA2, ["alt"] = 0xA4, ["shift"] = 0xA0, ["win"] = 0x5B,
    };

    static WindowsKeystrokeSender()
    {
        for (var i = 1; i <= 12; i++) NamedKeys["f" + i] = (ushort)(0xF0 + i);
    }

    public void Send(string keys)
    {
        var spec = KeySpec.Parse(keys);
        if (spec.Error is not null) throw new FormatException(spec.Error);

        var mods = new List<ushort>();
        foreach (var m in spec.Modifiers)
            if (Modifiers.TryGetValue(m, out var vk)) mods.Add(vk);

        ushort main;
        if (spec.NamedKey is not null)
        {
            if (!NamedKeys.TryGetValue(spec.NamedKey, out main))
                throw new FormatException($"Key '{spec.NamedKey}' is not supported on Windows.");
        }
        else if (spec.CharKey is char ch)
        {
            var scan = VkKeyScanW(ch);
            if ((scan & 0xFF) == 0xFF && (scan >> 8) == 0xFF)
                throw new FormatException($"Cannot map character '{ch}' to a key.");
            main = (ushort)(scan & 0xFF);
            if (((scan >> 8) & 1) != 0 && !mods.Contains(0xA0)) mods.Add(0xA0);
        }
        else throw new FormatException($"No key to press in '{keys}'.");

        var inputs = new List<INPUT>(mods.Count * 2 + 2);
        foreach (var vk in mods) inputs.Add(KeyInput(vk, up: false));
        inputs.Add(KeyInput(main, up: false));
        inputs.Add(KeyInput(main, up: true));
        foreach (var vk in mods) inputs.Add(KeyInput(vk, up: true));

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count)
            throw new InvalidOperationException(
                $"SendInput delivered {sent}/{inputs.Count} events (win32 error {Marshal.GetLastWin32Error()}).");
    }

    public void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                inputs.Add(KeyInput(0x0D, up: false));
                inputs.Add(KeyInput(0x0D, up: true));
            }
            else
            {
                inputs.Add(UnicodeInput(ch, up: false));
                inputs.Add(UnicodeInput(ch, up: true));
            }
        }
        if (inputs.Count == 0) return;
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (sent != inputs.Count)
            throw new InvalidOperationException(
                $"SendInput delivered {sent}/{inputs.Count} events (win32 error {Marshal.GetLastWin32Error()}).");
    }

    private static INPUT KeyInput(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                Vk = vk,
                Scan = 0,
                Flags = (up ? KEYEVENTF_KEYUP : 0) | (ExtendedKeys.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0),
                Time = 0,
                ExtraInfo = IntPtr.Zero,
            }
        }
    };

    private static INPUT UnicodeInput(char ch, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                Vk = 0,
                Scan = (ushort)ch,
                Flags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                Time = 0,
                ExtraInfo = IntPtr.Zero,
            }
        }
    };

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
