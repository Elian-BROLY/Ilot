using System.IO;
using System.Text.Json;
using Ilot.Services;

namespace Ilot.Settings;

internal static class PageIds
{
    public const string Media = "media";
    public const string Weather = "weather";
    public const string Mail = "mail";
    public const string Audio = "audio";
    public const string Timer = "timer";
    public const string Note = "note";
    public const string AppVolume = "appvolume";
    public const string Lyrics = "lyrics";
    public const string SystemStats = "systemstats";

    public static readonly string[] All = [Media, Lyrics, Weather, Mail, Audio, AppVolume, Timer, Note, SystemStats];
}

internal sealed class PageEntry
{
    public string Id { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

internal sealed class AppSettings
{
    public List<PageEntry> Pages { get; set; } = [];
    public int ExpandDelayMs { get; set; } = 100;
    public int CollapseDelayMs { get; set; } = 300;
    /// <summary>Multiplicateur des durées d'animation : 1 normal, 0,6 rapide, 0 aucune animation.</summary>
    public double AnimationDurationFactor { get; set; } = 1;
    /// <summary>Nom système de l'écran (\\.\DISPLAY1). Absent : écran principal.</summary>
    public string? MonitorDevice { get; set; }
    /// <summary>Vrai : la capsule passe sur l'écran où se trouve la souris, et <see cref="MonitorDevice"/> est ignoré.</summary>
    public bool ScreenFollowsCursor { get; set; }
    /// <summary>Le minuteur en cours : il survit à un redémarrage de l'application.</summary>
    public TimerSnapshot? SavedTimer { get; set; }
    /// <summary>Position de la capsule le long du bord haut, de 0 (gauche) à 1 (droite).</summary>
    public double HorizontalPosition { get; set; } = 0.5;
    public bool HideInFullscreen { get; set; } = true;
    public bool TintEnabled { get; set; } = true;
    public string? City { get; set; } = "Strasbourg";
    public double? Latitude { get; set; } = 48.58392;
    public double? Longitude { get; set; } = 7.74553;
    public bool GmailEnabled { get; set; } = true;
    public bool Gmail2Enabled { get; set; } = true;
    public bool OutlookEnabled { get; set; } = true;
    public bool MailBannerEnabled { get; set; } = true;
    public bool SystemNotificationsEnabled { get; set; } = true;
    public bool CompactTemperatureEnabled { get; set; } = true;
    public bool PrivacyIndicatorsEnabled { get; set; } = true;
    public bool BatteryAlertsEnabled { get; set; } = true;
    public bool GlobalHotkeyEnabled { get; set; } = true;
    public bool FocusWithTimerEnabled { get; set; } = true;
    public bool NetworkAlertsEnabled { get; set; } = true;
    public bool LightTheme { get; set; }
    /// <summary>Vrai : le thème suit celui de Windows, et <see cref="LightTheme"/> est ignoré.</summary>
    public bool FollowSystemTheme { get; set; }
    public string AccentName { get; set; } = "Bleu";
    /// <summary>Fragment de l'identifiant du lecteur à afficher en priorité (« deezer »). Vide : le lecteur désigné par Windows.</summary>
    public string PreferredPlayer { get; set; } = "deezer";
    /// <summary>Vrai : si le lecteur préféré est en pause alors qu'un autre joue, c'est l'autre qui s'affiche.</summary>
    public bool ShowPlayingOverPausedPreferred { get; set; }
    public bool TrackBannerEnabled { get; set; } = true;
    public bool AutoUpdateEnabled { get; set; } = true;
    /// <summary>Échelle de la capsule : 0,85 petite, 1 normale, 1,15 grande.</summary>
    public double CapsuleScale { get; set; } = 1;
}

/// <summary>Réglages de l'application, enregistrés dans %APPDATA%\Ilot\settings.json.</summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ilot", "settings.json");

    public SettingsStore()
    {
        Current = Load();
        Normalize(Current);
    }

    public event Action? Changed;

    public AppSettings Current { get; }

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Normalize(Current);
        Save();
        Changed?.Invoke();
    }

    /// <summary>Enregistre sans notifier : pour les changements déjà appliqués, comme le déplacement de la capsule.</summary>
    public void UpdateSilently(Action<AppSettings> change)
    {
        change(Current);
        Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Fichier illisible : on repart des valeurs par défaut.
        }
        return new AppSettings();
    }

    private void Save()
    {
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Les réglages restent appliqués pour cette session.
        }
    }

    internal static void Normalize(AppSettings settings)
    {
        settings.Pages = settings.Pages
            .Where(page => PageIds.All.Contains(page.Id))
            .DistinctBy(page => page.Id)
            .ToList();
        foreach (var id in PageIds.All.Where(id => settings.Pages.All(page => page.Id != id)))
        {
            settings.Pages.Add(new PageEntry { Id = id });
        }
        // Au moins une page doit rester affichée.
        if (settings.Pages.All(page => !page.IsEnabled))
        {
            settings.Pages[0].IsEnabled = true;
        }
        settings.HorizontalPosition = Math.Clamp(settings.HorizontalPosition, 0, 1);
    }
}
