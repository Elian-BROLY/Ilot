using System.IO;
using Microsoft.Win32;

namespace Ilot.Services;

/// <summary>Démarrage avec Windows via la clé Run de l'utilisateur : aucun droit administrateur requis.</summary>
internal static class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Ilot";

    /// <summary>
    /// Après une installation, le démarrage automatique peut encore viser l'ancien dossier : il suit la copie installée,
    /// reconnaissable à l'Update.exe de l'installateur dans le dossier parent.
    /// </summary>
    public static void RepointToInstalledCopy()
    {
        var folder = Path.GetDirectoryName(Environment.ProcessPath);
        if (folder is not null && IsEnabled && File.Exists(Path.Combine(folder, "..", "Update.exe")))
        {
            IsEnabled = true;
        }
    }

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (value)
            {
                key?.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
