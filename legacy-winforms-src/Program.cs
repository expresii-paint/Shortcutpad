namespace ShortcutPad;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var configPath = Path.Combine(AppContext.BaseDirectory, "shortcut_pad.json");
        var sender = new WindowsKeystrokeSender();
        Application.Run(new PadForm(configPath, sender));
    }
}
