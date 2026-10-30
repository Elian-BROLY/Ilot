using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace DynamicIsland.Services;

/// <summary>
/// Journal des erreurs, pour comprendre une panne après coup. N'y écrire que des erreurs techniques, jamais le contenu d'un
/// mail ou d'une note. Deux fichiers au plus : app.log, et app.old.log une fois la taille maximale atteinte.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    /// <summary>Remplaçable pour tester sans toucher au vrai journal.</summary>
    internal static string LogPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dynamic-Island", "app.log");

    public static void Write(Exception exception, [CallerMemberName] string source = "") =>
        Write($"{source} : {exception}");

    public static void Write(string message)
    {
        // Le journal sert à diagnostiquer les erreurs : il ne doit jamais en provoquer une.
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                var file = new FileInfo(LogPath);
                if (file.Exists && file.Length >= MaxBytes)
                {
                    File.Move(LogPath, Path.ChangeExtension(LogPath, ".old.log"), overwrite: true);
                }
                var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(LogPath, $"{time} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Disque plein ou fichier verrouillé : l'erreur d'origine reste sans trace.
        }
    }
}
