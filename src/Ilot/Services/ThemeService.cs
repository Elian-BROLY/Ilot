using System.Windows;
using System.Windows.Media;
using Ilot.Settings;
using Microsoft.Win32;

namespace Ilot.Services;

/// <summary>
/// Applique le thème au démarrage, avant la création des fenêtres. WPF gèle tout ce qui entre dans les ressources de
/// l'application, et les éléments créés par code gardent le pinceau qu'ils ont reçu : un changement de thème passe donc par
/// un redémarrage de l'application, plutôt que de laisser des couleurs de l'ancien thème à l'écran.
/// </summary>
internal static class ThemeService
{
    public static readonly (string Name, Color Color)[] Accents =
    [
        ("Bleu", Color.FromRgb(0x60, 0xCD, 0xFF)),
        ("Vert", Color.FromRgb(0x30, 0xD1, 0x58)),
        ("Orange", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        ("Rose", Color.FromRgb(0xFF, 0x5C, 0x8A)),
        ("Violet", Color.FromRgb(0xB1, 0x8C, 0xFF)),
        ("Rouge", Color.FromRgb(0xFF, 0x5A, 0x52)),
    ];

    private static string? _appliedTheme;

    public static Color CompactFill { get; private set; } = Color.FromRgb(0x0D, 0x0D, 0x0D);

    public static Color ExpandedFill { get; private set; } = Color.FromArgb(0xEB, 0x16, 0x16, 0x1A);

    public static bool NeedsRestart(AppSettings settings) => ThemeKey(settings) != _appliedTheme;

    public static bool IsLight(AppSettings settings) => settings.FollowSystemTheme ? IsSystemLight() : settings.LightTheme;

    private static bool IsSystemLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is 1;
    }

    public static void ApplyAtStartup(AppSettings settings)
    {
        _appliedTheme = ThemeKey(settings);
        var isLight = IsLight(settings);
        CompactFill = isLight ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x0D, 0x0D, 0x0D);
        ExpandedFill = isLight ? Color.FromArgb(0xF2, 0xF2, 0xF2, 0xF6) : Color.FromArgb(0xEB, 0x16, 0x16, 0x1A);

        // Noir ou blanc selon le fond : seule l'opacité varie d'un pinceau à l'autre.
        var ink = isLight ? (byte)0x00 : (byte)0xFF;
        SetBrush("TextPrimaryBrush", Color.FromArgb(isLight ? (byte)0xE6 : (byte)0xF2, ink, ink, ink));
        SetBrush("TextSecondaryBrush", Color.FromArgb(isLight ? (byte)0x8C : (byte)0x99, ink, ink, ink));
        SetBrush("SurfaceBrush", Color.FromArgb(isLight ? (byte)0x10 : (byte)0x14, ink, ink, ink));
        SetBrush("HoverBrush", Color.FromArgb(isLight ? (byte)0x1C : (byte)0x24, ink, ink, ink));
        SetBrush("PressedBrush", Color.FromArgb(isLight ? (byte)0x30 : (byte)0x38, ink, ink, ink));

        var accent = Accents.FirstOrDefault(item => item.Name == settings.AccentName, Accents[0]);
        SetBrush("AccentBrush", accent.Color);
    }

    private static string ThemeKey(AppSettings settings) => $"{IsLight(settings)}|{settings.AccentName}";

    private static void SetBrush(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);
}
