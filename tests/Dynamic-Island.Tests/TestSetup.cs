using System.IO;
using System.Runtime.CompilerServices;
using DynamicIsland.Services;

namespace DynamicIsland.Tests;

internal static class TestSetup
{
    // Les tests provoquent des erreurs : elles ne doivent pas finir dans le vrai journal de l'application.
    [ModuleInitializer]
    internal static void RedirectLog() =>
        AppLog.LogPath = Path.Combine(Path.GetTempPath(), "dynamic-island-tests", "app.log");
}
