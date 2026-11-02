using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ilot.Services;

/// <summary>
/// Un élément copié. Une image est gardée compressée en PNG, avec une miniature : une capture d'écran 4K brute occuperait
/// une trentaine de Mo.
/// </summary>
internal sealed record ClipboardEntry(string Key, string? Text, byte[]? Png, BitmapSource? Thumbnail, int Width, int Height);

/// <summary>Les derniers éléments copiés, du plus récent au plus ancien. Rien n'est écrit sur le disque.</summary>
internal sealed class ClipboardHistory(int capacity = 10, int maxImages = 3)
{
    private readonly List<ClipboardEntry> _entries = [];

    public IReadOnlyList<ClipboardEntry> Entries => _entries;

    /// <summary>Un élément déjà présent remonte en tête au lieu d'apparaître deux fois.</summary>
    public void Add(ClipboardEntry entry)
    {
        _entries.RemoveAll(existing => existing.Key == entry.Key);
        _entries.Insert(0, entry);
        // Les images pèsent bien plus que le texte : seules les plus récentes restent.
        var images = _entries.Where(existing => existing.Png is not null).Skip(maxImages).ToList();
        _entries.RemoveAll(images.Contains);
        if (_entries.Count > capacity)
        {
            _entries.RemoveRange(capacity, _entries.Count - capacity);
        }
    }

    public void Remove(ClipboardEntry entry) => _entries.Remove(entry);

    public void Clear() => _entries.Clear();

    public static ClipboardEntry FromText(string text) => new("t:" + text, text, null, null, 0, 0);
}

/// <summary>Lecture et écriture du presse-papiers de Windows, qu'une autre application peut garder ouvert un instant.</summary>
internal static class ClipboardReader
{
    private const int ThumbnailHeight = 40;
    private const int MaxTextLength = 100_000;

    /// <summary>
    /// Ce qui vient d'être copié, ou null. Les gestionnaires de mots de passe marquent leurs copies pour qu'elles restent hors
    /// de tout historique : Ilot respecte ces marques, comme l'historique de Windows (Win + V).
    /// </summary>
    public static ClipboardEntry? ReadCurrent()
    {
        try
        {
            if (IsExcluded())
            {
                return null;
            }
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                return string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength ? null : ClipboardHistory.FromText(text);
            }
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                return FromImage(image);
            }
        }
        catch (Exception ex) when (ex is ExternalException or OutOfMemoryException or NotSupportedException or ArgumentException)
        {
            // Presse-papiers occupé ou contenu illisible : cet élément n'entre pas dans l'historique.
        }
        return null;
    }

    public static bool TryWrite(ClipboardEntry entry)
    {
        try
        {
            if (entry.Text is { } text)
            {
                Clipboard.SetText(text);
            }
            else if (entry.Png is { } png)
            {
                using var stream = new MemoryStream(png);
                Clipboard.SetImage(BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
            }
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or NotSupportedException)
        {
            AppLog.Write(ex);
            return false;
        }
    }

    private static bool IsExcluded()
    {
        if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing") || Clipboard.ContainsData("Clipboard Viewer Ignore"))
        {
            return true;
        }
        // Un DWORD à 0 : « ne pas garder dans l'historique ».
        return Clipboard.ContainsData("CanIncludeInClipboardHistory")
            && Clipboard.GetData("CanIncludeInClipboardHistory") is MemoryStream { Length: >= 4 } flag
            && BitConverter.ToInt32(flag.ToArray(), 0) == 0;
    }

    private static ClipboardEntry FromImage(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var png = stream.ToArray();

        var scale = ThumbnailHeight / (double)Math.Max(1, image.PixelHeight);
        var thumbnail = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        var frozen = new WriteableBitmap(thumbnail);
        frozen.Freeze();
        return new ClipboardEntry("i:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)), null, png, frozen, image.PixelWidth, image.PixelHeight);
    }
}
