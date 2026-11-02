using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Ilot.Interop;

namespace Ilot.Services;

/// <summary>
/// Suit le dossier Téléchargements : les navigateurs y écrivent dans un fichier provisoire (.crdownload, .part…), puis le
/// renomment une fois terminé. Windows ne donne pas la progression d'un téléchargement ni d'une copie de fichiers : seuls le
/// début et la fin se voient ainsi.
/// </summary>
internal sealed class DownloadWatcher(Dispatcher dispatcher) : IDisposable
{
    private static readonly string[] PartialExtensions = [".crdownload", ".part", ".partial", ".download", ".opdownload"];
    private static Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    private readonly HashSet<string> _inProgress = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;

    /// <summary>Le chemin du fichier terminé.</summary>
    public event Action<string>? Completed;

    public event Action? ActivityChanged;

    public bool IsActive => _inProgress.Count > 0;

    /// <summary>Un fichier en cours d'écriture par un navigateur.</summary>
    internal static bool IsPartial(string path) =>
        PartialExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Le fichier terminé, si ce renommage marque la fin d'un téléchargement. Null sinon.</summary>
    internal static string? CompletedPath(string oldPath, string newPath) =>
        IsPartial(oldPath) && !IsPartial(newPath) ? newPath : null;

    public void Start()
    {
        if (_watcher is not null || FindDownloadsFolder() is not { } folder)
        {
            return;
        }
        try
        {
            _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
            _watcher.Created += (_, e) => OnUi(() => Track(e.FullPath));
            _watcher.Deleted += (_, e) => OnUi(() => Untrack(e.FullPath));
            _watcher.Renamed += (_, e) => OnUi(() => OnRenamed(e.OldFullPath, e.FullPath));
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex);
            _watcher?.Dispose();
            _watcher = null;
            return;
        }
        // Les téléchargements déjà commencés au lancement d'Ilot.
        _ = Task.Run(() => Directory.EnumerateFiles(folder).Where(IsPartial).ToList())
            .ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    OnUi(() => task.Result.ForEach(Track));
                }
            }, TaskScheduler.Default);
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_inProgress.Count > 0)
        {
            _inProgress.Clear();
            ActivityChanged?.Invoke();
        }
    }

    public void Dispose() => Stop();

    private void OnUi(Action action) => dispatcher.BeginInvoke(() =>
    {
        if (_watcher is not null)
        {
            action();
        }
    });

    private void Track(string path)
    {
        if (IsPartial(path) && _inProgress.Add(path) && _inProgress.Count == 1)
        {
            ActivityChanged?.Invoke();
        }
    }

    private void Untrack(string path)
    {
        if (_inProgress.Remove(path) && _inProgress.Count == 0)
        {
            ActivityChanged?.Invoke();
        }
    }

    private void OnRenamed(string oldPath, string newPath)
    {
        Untrack(oldPath);
        Track(newPath); // Chrome renomme « Unconfirmed 1234.crdownload » en « nom.zip.crdownload » avant la fin.
        if (CompletedPath(oldPath, newPath) is { } completed)
        {
            Completed?.Invoke(completed);
        }
    }

    // Le dossier Téléchargements a pu être déplacé par l'utilisateur : Windows connaît son emplacement réel.
    private static string? FindDownloadsFolder()
    {
        if (NativeMethods.SHGetKnownFolderPath(ref DownloadsFolderId, 0, IntPtr.Zero, out var pointer) != 0)
        {
            return null;
        }
        try
        {
            var path = Marshal.PtrToStringUni(pointer);
            return Directory.Exists(path) ? path : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pointer);
        }
    }
}
