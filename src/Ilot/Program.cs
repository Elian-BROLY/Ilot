using Ilot.Services;
using Velopack;

namespace Ilot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Doit passer avant toute interface : l'installateur lance l'application pour ses étapes d'installation.
        VelopackApp.Build().Run();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                AppLog.Write(exception, "Plantage");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write(e.Exception, "Tâche en échec");
            e.SetObserved();
        };

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
