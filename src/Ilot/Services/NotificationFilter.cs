namespace Ilot.Services;

/// <summary>Décide si une bannière peut s'afficher : applications coupées et heures de silence.</summary>
internal static class NotificationFilter
{
    /// <summary>La plage peut passer minuit (22 h à 7 h). Une plage vide ne couvre rien.</summary>
    public static bool IsQuietTime(TimeOnly now, int startMinutes, int endMinutes)
    {
        var minutes = now.Hour * 60 + now.Minute;
        return startMinutes <= endMinutes
            ? minutes >= startMinutes && minutes < endMinutes
            : minutes >= startMinutes || minutes < endMinutes;
    }

    public static bool IsMuted(string appId, IEnumerable<string> mutedApps) =>
        mutedApps.Contains(appId, StringComparer.OrdinalIgnoreCase);

    /// <summary>« 7:30 » ou « 22:00 » en minutes depuis minuit. Null si l'heure n'est pas valide.</summary>
    public static int? ParseTime(string text) =>
        TimeOnly.TryParseExact(text.Trim(), ["H:mm", "HH:mm"], System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var time)
            ? time.Hour * 60 + time.Minute
            : null;

    public static string FormatTime(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}
