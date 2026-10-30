using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DynamicIsland.Services;

/// <summary>
/// Reprend l'installation faite sous l'ancien nom (NotchPixel) : mêmes réglages, mêmes comptes, et une seule capsule à l'écran.
/// L'ancien dossier est copié, pas déplacé : l'ancienne version reste utilisable jusqu'à sa désinstallation.
/// </summary>
internal static class LegacyMigration
{
    private const string LegacyName = "NotchPixel";
    private const string AppName = "Dynamic-Island";

    /// <summary>À appeler avant le premier accès aux réglages, qui lisent le nouveau dossier.</summary>
    public static void Run()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyFolder = Path.Combine(appData, LegacyName);
        var folder = Path.Combine(appData, AppName);
        var mustCopyData = Directory.Exists(legacyFolder) && !Directory.Exists(folder);

        if (mustCopyData || AutostartService.HasEntry(LegacyName))
        {
            StopLegacyInstances(); // Sinon, deux capsules se superposent, et la note en cours d'écriture serait copiée à moitié.
        }
        if (mustCopyData)
        {
            CopyData(legacyFolder, folder);
        }
        AutostartService.AdoptEntry(LegacyName);
    }

    private static void StopLegacyInstances()
    {
        foreach (var process in Process.GetProcessesByName(LegacyName))
        {
            try
            {
                process.Kill();
                process.WaitForExit(2000);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Déjà fermée, ou lancée par un autre compte.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    // Dossier temporaire puis renommage : une copie interrompue sera reprise au prochain lancement.
    private static void CopyData(string source, string destination)
    {
        var temporary = destination + ".tmp";
        try
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
            Directory.CreateDirectory(temporary);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(temporary, Path.GetFileName(file)));
            }
            Directory.Move(temporary, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // L'application démarre avec ses valeurs par défaut.
        }
    }
}
