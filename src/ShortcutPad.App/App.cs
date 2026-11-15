using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;

namespace ShortcutPad;

public partial class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ClassicDesktopStyleApplicationLifetime lifetime)
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "shortcut_pad.json");
            var editorMode = Environment.GetCommandLineArgs().Contains("--edit-buttons");

            if (editorMode)
            {
                // Standalone editor: ShortcutPad --edit-buttons
                lifetime.ShutdownMode = ShutdownMode.OnMainWindowClose;
                lifetime.MainWindow = new ButtonEditorWindow(
                    ConfigStore.Load(configPath), configPath, cfg => ConfigStore.Save(configPath, cfg));
            }
            else
            {
                // The pad is a tray/overlay utility: closing the window hides
                // it, it does not quit the app.
                lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                lifetime.MainWindow = new PadWindow(configPath);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
