using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland.Services;

internal sealed record GeocodedCity(string Name, double Latitude, double Longitude);

internal sealed record DailyForecast(DateOnly Date, int WeatherCode, int Max, int Min);

internal sealed record WeatherReport(int Temperature, int WeatherCode, IReadOnlyList<DailyForecast> Days);

/// <summary>Météo via Open-Meteo : aucune clé d'API, seules la ville et ses coordonnées sont transmises.</summary>
internal static class WeatherService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<GeocodedCity?> GeocodeAsync(string city)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=fr&format=json";
            using var document = JsonDocument.Parse(await Http.GetStringAsync(url));
            if (!document.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            {
                return null;
            }
            var first = results[0];
            return new GeocodedCity(first.GetProperty("name").GetString() ?? city,
                first.GetProperty("latitude").GetDouble(), first.GetProperty("longitude").GetDouble());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }

    public static async Task<WeatherReport?> FetchAsync(double latitude, double longitude)
    {
        try
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&forecast_days=4&timezone=auto");
            using var document = JsonDocument.Parse(await Http.GetStringAsync(url));
            var current = document.RootElement.GetProperty("current");
            var daily = document.RootElement.GetProperty("daily");
            var dates = daily.GetProperty("time");
            var codes = daily.GetProperty("weather_code");
            var maxima = daily.GetProperty("temperature_2m_max");
            var minima = daily.GetProperty("temperature_2m_min");

            var days = new List<DailyForecast>();
            for (var i = 0; i < dates.GetArrayLength(); i++)
            {
                days.Add(new DailyForecast(
                    DateOnly.ParseExact(dates[i].GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    codes[i].GetInt32(),
                    (int)Math.Round(maxima[i].GetDouble()),
                    (int)Math.Round(minima[i].GetDouble())));
            }
            return new WeatherReport((int)Math.Round(current.GetProperty("temperature_2m").GetDouble()),
                current.GetProperty("weather_code").GetInt32(), days);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>Libellé d'un code météo WMO.</summary>
    public static string Describe(int code) => code switch
    {
        0 => "Ciel dégagé",
        1 or 2 => "Éclaircies",
        3 => "Couvert",
        45 or 48 => "Brouillard",
        >= 51 and <= 57 => "Bruine",
        >= 61 and <= 67 or >= 80 and <= 82 => "Pluie",
        >= 71 and <= 77 or 85 or 86 => "Neige",
        >= 95 => "Orage",
        _ => "—",
    };
}
