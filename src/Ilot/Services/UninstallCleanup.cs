using System.IO;

namespace Ilot.Services;

/// <summary>
/// Ménage à la désinstallation : démarrage automatique, identifiants, réglages et journal.
/// Seules les notes restent : c'est le texte de l'utilisateur, et rien ne le préviendrait de sa perte.
/// </summary>
internal static class UninstallCleanup
{
    public static void Run()
    {
        try
        {
            AutostartService.IsEnabled = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // La désinstallation doit aller à son terme.
        }
        RemoveData(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ilot"));
    }

    internal static void RemoveData(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }
        foreach (var file in Directory.GetFiles(folder))
        {
            if (!NoteStore.IsNoteFile(Path.GetFileName(file)))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Fichier verrouillé : on passe au suivant.
                }
            }
        }
        if (Directory.GetFileSystemEntries(folder).Length == 0)
        {
            Directory.Delete(folder);
        }
    }
}
