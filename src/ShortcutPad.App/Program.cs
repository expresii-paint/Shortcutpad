using Avalonia;
using Avalonia.Controls;

namespace ShortcutPad;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Headless trigger, handy for scripts and for verifying a platform's
        // input path without the window:
        //   ShortcutPad --send "ctrl+c"
        //   ShortcutPad --type "hello from the command line"
        if (args.Length == 2 && (args[0] == "--send" || args[0] == "--type"))
        {
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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
