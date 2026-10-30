using System.IO;
using System.Text;

namespace DynamicIsland.Services;

/// <summary>Écrit un fichier en entier ou pas du tout : une coupure de courant laisse l'ancienne version intacte.</summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string text) => WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }
}
