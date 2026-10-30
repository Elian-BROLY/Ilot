using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

/// <summary>
/// Icône d'une application d'après son identifiant, via le dossier Applications de Windows.
/// C'est la seule source pour les applications classiques (Discord, par exemple) : GetLogo échoue pour elles.
/// </summary>
internal static class AppIconService
{
    private const int IconSize = 64;
    private const int ImageIconOnly = 0x4;
    private const uint DibRgbColors = 0;

    private static readonly Guid AppsFolder = new("1e87508d-89c2-42f0-8a7e-645a0f50ca58");
    private static readonly Dictionary<string, BitmapSource?> Cache = [];

    /// <summary>Ouvre l'application : Windows ne donne pas accès à l'action de la notification elle-même.</summary>
    public static void OpenApp(string appUserModelId)
    {
        if (appUserModelId.Length == 0)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo($@"shell:AppsFolder\{appUserModelId}") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // Application absente du dossier Applications : le clic se contente de fermer la bannière.
        }
    }

    public static BitmapSource? Get(string appUserModelId)
    {
        if (!Cache.TryGetValue(appUserModelId, out var icon))
        {
            icon = Load(appUserModelId);
            Cache[appUserModelId] = icon;
        }
        return icon;
    }

    private static BitmapSource? Load(string appUserModelId)
    {
        var folder = AppsFolder;
        var iid = typeof(IShellItemImageFactory).GUID;
        if (NativeMethods.SHCreateItemInKnownFolder(ref folder, 0, appUserModelId, ref iid, out var item) < 0 || item is null)
        {
            return null;
        }

        var hbitmap = IntPtr.Zero;
        try
        {
            var size = new NativeMethods.SIZE { cx = IconSize, cy = IconSize };
            return ((IShellItemImageFactory)item).GetImage(size, ImageIconOnly, out hbitmap) < 0 ? null : ToBitmapSource(hbitmap);
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hbitmap);
            }
            Marshal.ReleaseComObject(item);
        }
    }

    // CreateBitmapSourceFromHBitmap perd la transparence : on relit les pixels en BGRA, de haut en bas.
    private static BitmapSource? ToBitmapSource(IntPtr hbitmap)
    {
        if (NativeMethods.GetObject(hbitmap, Marshal.SizeOf<NativeMethods.BITMAP>(), out var bitmap) == 0)
        {
            return null;
        }

        var info = new NativeMethods.BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
            biWidth = bitmap.bmWidth,
            biHeight = -bitmap.bmHeight,
            biPlanes = 1,
            biBitCount = 32,
        };
        var stride = bitmap.bmWidth * 4;
        var pixels = new byte[stride * bitmap.bmHeight];
        var hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (NativeMethods.GetDIBits(hdc, hbitmap, 0, (uint)bitmap.bmHeight, pixels, ref info, DibRgbColors) == 0)
            {
                return null;
            }
        }
        finally
        {
            NativeMethods.DeleteDC(hdc);
        }

        var image = BitmapSource.Create(bitmap.bmWidth, bitmap.bmHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        image.Freeze();
        return image;
    }
}
