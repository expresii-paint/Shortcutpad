using Avalonia;
using Avalonia.Controls;

namespace ShortcutPad;

public static class Program
{
    // Held for the life of the process: a local would be collected early and
    // the second-instance guard would silently stop working.
    private static Mutex? s_single;

    [STAThread]
    public static void Main(string[] args)
    {
        // Headless trigger, handy for scripts and for verifying a platform's
        // input path without the window:
        //   ShortcutPad --send "ctrl+c"
        //   ShortcutPad --type "hello from the command line"
        if (args.Length == 2 && (args[0] == "--send" || args[0] == "--type"))
        {
            // A WinExe build has no console of its own. Attach to the caller's
            // so the headless mode still reports errors when run from a shell.
            if (OperatingSystem.IsWindows()) NativeMethods.AttachConsole(NativeMethods.AttachParent);

            var sender = PadPlatformSender.Create();
            try
            {
                if (args[0] == "--send") sender.Send(args[1]);
                else sender.TypeText(args[1]);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ShortcutPad: {ex.Message}");
                Environment.Exit(1);
            }
            return;
        }

        // One pad per desktop. A second launch would run a second pad writing
        // the same shortcut_pad.json, and the two would overwrite each other's
        // settings - which looks exactly like "my settings do not stick".
        s_single = new Mutex(true, "ShortcutPad-single-instance", out var first);
        if (!first)
        {
            if (OperatingSystem.IsWindows()) NativeMethods.FocusPad();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? className, string windowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        /// <summary>Bring the running pad forward; used when a second launch is ignored.</summary>
        public static void FocusPad()
        {
            const int swRestore = 9;
            var hwnd = FindWindow(null, "Shortcut Pad");
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, swRestore);
            SetForegroundWindow(hwnd);
        }

        /// <summary>AttachConsole(dwProcessId): pass -1 for the parent process.</summary>
        public const uint AttachParent = 0xFFFFFFFF;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        public static extern bool AttachConsole(uint dwProcessId);
    }
}
