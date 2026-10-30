using Velopack;

namespace DynamicIsland;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Doit passer avant toute interface : l'installateur lance l'application pour ses étapes d'installation.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
