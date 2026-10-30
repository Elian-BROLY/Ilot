using System.IO;

namespace Ilot.Services;

/// <summary>Les fichiers déposés sur la capsule, pour la session : seuls leurs chemins sont gardés, rien n'est copié ni envoyé.</summary>
internal sealed class FileShelf
{
    public const int MaxFiles = 20;

    private readonly List<string> _files = [];

    public IReadOnlyList<string> Files => _files;

    /// <summary>Le plus récent en tête. Un fichier déjà présent remonte au lieu d'être doublé ; le plus ancien sort si la liste est pleine.</summary>
    public void Add(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            _files.RemoveAll(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase));
            _files.Insert(0, path);
        }
        if (_files.Count > MaxFiles)
        {
            _files.RemoveRange(MaxFiles, _files.Count - MaxFiles);
        }
    }

    public void Remove(string path) => _files.RemoveAll(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase));

    public void Clear() => _files.Clear();

    /// <summary>Retire les fichiers déplacés ou supprimés entre-temps. Renvoie vrai si la liste a changé.</summary>
    public bool RemoveMissing() => _files.RemoveAll(file => !File.Exists(file) && !Directory.Exists(file)) > 0;
}
