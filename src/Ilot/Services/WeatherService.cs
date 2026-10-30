using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Ilot.Services;

internal sealed record GeocodedCity(string Name, double Latitude, double Longitude, string? Region = null, string? Country = null)
{
    /// <summary>De quoi distinguer deux villes du même nom.</summary>
    public string Label => string.Join(", ", new[] { Name, Region, Country }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

internal sealed record DailyForecast(DateOnly Date, int WeatherCode, int Max, int Min);

internal sealed record WeatherReport(int Temperature, int WeatherCode, IReadOnlyList<DailyForecast> Days);

/// <summary>Météo via Open-Meteo : aucune clé d'API, seules la ville et ses coordonnées sont transmises.</summary>
internal static class WeatherService
{
    private const int MaxCityChoices = 5;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Les villes qui portent ce nom, les plus importantes d'abord. Liste vide si rien n'est trouvé ou si le service ne répond pas.</summary>
    public static async Task<IReadOnlyList<GeocodedCity>> GeocodeAsync(string city)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count={MaxCityChoices}&language={Loc.Language}&format=json";
            return ParseCities(await Http.GetStringAsync(url));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException)
        {
            return [];
        }
    }

    internal static IReadOnlyList<GeocodedCity> ParseCities(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results))
        {
            return [];
        }
        return results.EnumerateArray().Select(city => new GeocodedCity(
            city.GetProperty("name").GetString() ?? string.Empty,
            city.GetProperty("latitude").GetDouble(),
            city.GetProperty("longitude").GetDouble(),
            city.TryGetProperty("admin1", out var region) ? region.GetString() : null,
            city.TryGetProperty("country", out var country) ? country.GetString() : null)).ToList();
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
        0 => Loc.T("Ciel dégagé"),
        1 or 2 => Loc.T("Éclaircies"),
        3 => Loc.T("Couvert"),
        45 or 48 => Loc.T("Brouillard"),
        >= 51 and <= 57 => Loc.T("Bruine"),
        >= 61 and <= 67 or >= 80 and <= 82 => Loc.T("Pluie"),
        >= 71 and <= 77 or 85 or 86 => Loc.T("Neige"),
        >= 95 => Loc.T("Orage"),
        _ => "—",
    };
}
