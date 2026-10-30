using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ilot.Services;

internal sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Synced vide si seules les paroles brutes existent. Le service n'envoie que l'artiste et le titre.</summary>
internal sealed record Lyrics(IReadOnlyList<LyricLine> Synced, string Plain);

/// <summary>Les paroles des derniers morceaux, en mémoire : une réécoute ne renvoie aucune requête. Les plus anciennes sortent en premier.</summary>
internal sealed class LyricsCache(int capacity)
{
    private readonly Dictionary<string, Lyrics?> _entries = [];
    private readonly Queue<string> _order = new();

    public bool TryGet(string key, out Lyrics? lyrics) => _entries.TryGetValue(key, out lyrics);

    public void Add(string key, Lyrics? lyrics)
    {
        if (!_entries.ContainsKey(key))
        {
            _order.Enqueue(key);
            if (_order.Count > capacity)
            {
                _entries.Remove(_order.Dequeue());
            }
        }
        _entries[key] = lyrics;
    }
}

/// <summary>Paroles via LRCLIB : service gratuit, sans clé d'API.</summary>
internal static partial class LyricsService
{
    private const string Api = "https://lrclib.net/api";
    private const int CacheCapacity = 30;
    private static readonly HttpClient Http = CreateClient();
    private static readonly LyricsCache Cache = new(CacheCapacity);

    public static async Task<Lyrics?> FindAsync(string artist, string title)
    {
        var key = $"{artist}|{title}";
        if (Cache.TryGet(key, out var cached))
        {
            return cached;
        }

        var cleanTitle = NoisePattern().Replace(title, string.Empty).Trim();
        try
        {
            using var exact = await Http.GetAsync($"{Api}/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(cleanTitle)}");
            if (exact.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await exact.Content.ReadAsStringAsync());
                if (Parse(document.RootElement) is { } found)
                {
                    Cache.Add(key, found);
                    return found;
                }
            }

            // Sites web : l'artiste et le titre sont souvent mélangés, la recherche libre s'en accommode.
            var results = await Http.GetStringAsync($"{Api}/search?q={Uri.EscapeDataString($"{artist} {cleanTitle}".Trim())}");
            using var list = JsonDocument.Parse(results);
            foreach (var item in list.RootElement.EnumerateArray())
            {
                if (Parse(item) is { } found)
                {
                    Cache.Add(key, found);
                    return found;
                }
            }
            Cache.Add(key, null); // Seule une vraie absence est retenue : une coupure réseau ne l'est pas.
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return null;
        }
    }

    private static Lyrics? Parse(JsonElement item)
    {
        var synced = ParseSynced(ReadString(item, "syncedLyrics"));
        var plain = ReadString(item, "plainLyrics");
        return synced.Count == 0 && plain.Length == 0 ? null : new Lyrics(synced, plain);
    }

    private static string ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    internal static List<LyricLine> ParseSynced(string text)
    {
        var lines = new List<LyricLine>();
        foreach (Match match in SyncedLinePattern().Matches(text))
        {
            var time = TimeSpan.FromMinutes(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
                + TimeSpan.FromSeconds(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            lines.Add(new LyricLine(time, match.Groups[3].Value.Trim()));
        }
        return lines;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Ilot/1.4");
        return client;
    }

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*(official|video|clip|lyrics?|audio|remaster|paroles)[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex NoisePattern();

    [GeneratedRegex(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$", RegexOptions.Multiline)]
    private static partial Regex SyncedLinePattern();
}
