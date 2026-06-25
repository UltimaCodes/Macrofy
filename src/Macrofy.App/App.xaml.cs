using System.Linq;
using System.Windows;

namespace Macrofy.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tray app: we own the lifetime (ShutdownMode=OnExplicitShutdown). When launched at
        // login with --minimized, come up silently in the tray instead of showing the window.
        bool minimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow();
        MainWindow = window;
        if (!minimized)
            window.Show();
    }
}
