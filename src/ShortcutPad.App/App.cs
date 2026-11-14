using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace ShortcutPad;

public class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ClassicDesktopStyleApplicationLifetime lifetime)
        {
            // The pad is a tray/overlay utility: closing the window hides it,
            // it does not quit the app.
            lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var configPath = Path.Combine(AppContext.BaseDirectory, "shortcut_pad.json");
            lifetime.MainWindow = new PadWindow(configPath);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
