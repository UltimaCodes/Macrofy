using Velopack;

namespace Macrofy.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Must run first: when the installer or updater launches Macrofy to run a hook, this
        // handles it and exits before any window or tray icon appears.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => AutoStartManager.SetEnabled(false))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
