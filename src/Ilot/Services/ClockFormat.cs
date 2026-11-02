using System.Globalization;

namespace Ilot.Services;

/// <summary>L'heure de la capsule, au format choisi dans Windows : 24 h, ou 12 h sans AM/PM, trop large pour la capsule.</summary>
internal static class ClockFormat
{
    public static string Current { get; } = FromPattern(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern);

    internal static string FromPattern(string shortTimePattern) => shortTimePattern.Contains('h') ? "h:mm" : "HH:mm";

    public static string Format(DateTime time) => time.ToString(Current, CultureInfo.InvariantCulture);
}
