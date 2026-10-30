using System.Globalization;
using System.Text.Json;

namespace Ilot.Services;

/// <summary>JoinUrl : la visio si le rendez-vous en a une. OpenUrl : la page du rendez-vous.</summary>
internal sealed record CalendarEvent(string Id, string Title, DateTime StartUtc, bool IsAllDay, string? JoinUrl, string? OpenUrl);

/// <summary>NeedsPermission : le compte a été connecté sans l'accès à l'agenda, ou l'API n'est pas activée.</summary>
internal sealed record CalendarResult(IReadOnlyList<CalendarEvent> Events, bool NeedsPermission = false, bool HasFailed = false);

/// <summary>Lecture des réponses de Google Agenda et de Microsoft Graph, et choix de ce qui mérite d'être affiché.</summary>
internal static class CalendarEvents
{
    private const int CountdownMinutes = 15;
    private const int ReminderMinutes = 5;

    public static List<CalendarEvent> ParseGoogle(string json)
    {
        var events = new List<CalendarEvent>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("items", out var items))
        {
            return events;
        }
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("start", out var start))
            {
                continue;
            }
            var hasTime = start.TryGetProperty("dateTime", out var dateTime);
            var startUtc = hasTime
                ? DateTimeOffset.Parse(dateTime.GetString()!, CultureInfo.InvariantCulture).UtcDateTime
                : start.TryGetProperty("date", out var date)
                    ? DateTime.SpecifyKind(DateTime.ParseExact(date.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateTimeKind.Local).ToUniversalTime()
                    : (DateTime?)null;
            if (startUtc is not { } value)
            {
                continue;
            }
            events.Add(new CalendarEvent(
                Text(item, "id") ?? string.Empty,
                Text(item, "summary") ?? Loc.T("(sans titre)"),
                value,
                !hasTime,
                FindGoogleJoinUrl(item),
                Text(item, "htmlLink")));
        }
        return events;
    }

    public static List<CalendarEvent> ParseGraph(string json)
    {
        var events = new List<CalendarEvent>();
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("value", out var items))
        {
            return events;
        }
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("start", out var start) || Text(start, "dateTime") is not { } dateTime)
            {
                continue;
            }
            // Demandé en UTC par l'en-tête Prefer : Graph n'ajoute pas le fuseau à sa réponse.
            var startUtc = DateTime.SpecifyKind(DateTime.Parse(dateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), DateTimeKind.Utc);
            string? join = null;
            if (item.TryGetProperty("onlineMeeting", out var meeting) && meeting.ValueKind == JsonValueKind.Object)
            {
                join = Text(meeting, "joinUrl");
            }
            events.Add(new CalendarEvent(
                Text(item, "id") ?? string.Empty,
                Text(item, "subject") ?? Loc.T("(sans titre)"),
                startUtc,
                item.TryGetProperty("isAllDay", out var allDay) && allDay.ValueKind == JsonValueKind.True,
                join,
                Text(item, "webLink")));
        }
        return events;
    }

    /// <summary>Ce qui n'est pas encore passé, dans l'ordre. Une journée entière commencée reste affichée tant que la journée dure.</summary>
    public static List<CalendarEvent> Upcoming(IEnumerable<CalendarEvent> events, DateTime nowUtc) => events
        .Where(item => item.StartUtc >= nowUtc || (item.IsAllDay && item.StartUtc.ToLocalTime().Date == nowUtc.ToLocalTime().Date))
        .OrderBy(item => item.StartUtc)
        .ToList();

    /// <summary>Minutes avant le prochain rendez-vous à heure fixe, s'il commence dans moins de 15 minutes.</summary>
    public static int? CountdownMinutesLeft(IEnumerable<CalendarEvent> events, DateTime nowUtc)
    {
        var next = events.Where(item => !item.IsAllDay && item.StartUtc > nowUtc).OrderBy(item => item.StartUtc).FirstOrDefault();
        if (next is null)
        {
            return null;
        }
        var minutes = (int)Math.Ceiling((next.StartUtc - nowUtc).TotalMinutes);
        return minutes <= CountdownMinutes ? minutes : null;
    }

    /// <summary>Le rendez-vous à annoncer maintenant : il commence dans moins de 5 minutes et n'a pas encore été annoncé.</summary>
    public static CalendarEvent? DueForReminder(IEnumerable<CalendarEvent> events, DateTime nowUtc, ISet<string> alreadyAnnounced)
    {
        var due = events
            .Where(item => !item.IsAllDay && item.StartUtc > nowUtc && item.StartUtc - nowUtc <= TimeSpan.FromMinutes(ReminderMinutes) && !alreadyAnnounced.Contains(item.Id))
            .OrderBy(item => item.StartUtc)
            .FirstOrDefault();
        if (due is not null)
        {
            alreadyAnnounced.Add(due.Id);
        }
        return due;
    }

    private static string? FindGoogleJoinUrl(JsonElement item)
    {
        if (Text(item, "hangoutLink") is { } meet)
        {
            return meet;
        }
        if (item.TryGetProperty("conferenceData", out var conference) && conference.TryGetProperty("entryPoints", out var points))
        {
            foreach (var point in points.EnumerateArray())
            {
                if (Text(point, "entryPointType") == "video" && Text(point, "uri") is { } uri)
                {
                    return uri;
                }
            }
        }
        // Zoom ou Teams collé dans le lieu du rendez-vous.
        return Text(item, "location") is { } location && location.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? location : null;
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
