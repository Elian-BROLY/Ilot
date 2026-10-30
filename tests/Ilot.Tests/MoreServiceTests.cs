using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Ilot.Controls;
using Ilot.Services;
using Ilot.Settings;
using Xunit;

namespace Ilot.Tests;

public class TimerServiceTests
{
    [Fact]
    public void NoSnapshotWithoutTimer() => Assert.Null(new TimerService().Snapshot);

    [Fact]
    public void RunningTimerSurvivesRestart()
    {
        var first = new TimerService();
        first.Start(TimeSpan.FromMinutes(25));

        var second = new TimerService();
        second.Restore(RoundTrip(first.Snapshot!));

        Assert.True(second.IsActive);
        Assert.False(second.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(25), second.Duration);
        Assert.InRange(second.Remaining, TimeSpan.FromMinutes(24), TimeSpan.FromMinutes(25));
    }

    [Fact]
    public void PausedTimerStaysPausedAfterRestart()
    {
        var first = new TimerService();
        first.Start(TimeSpan.FromMinutes(5));
        first.TogglePause();

        var second = new TimerService();
        second.Restore(RoundTrip(first.Snapshot!));

        Assert.True(second.IsPaused);
        Assert.InRange(second.Remaining, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void StopClearsSnapshotAndRaisesStateChanged()
    {
        var timer = new TimerService();
        var changes = 0;
        timer.StateChanged += () => changes++;
        timer.Start(TimeSpan.FromMinutes(5));
        timer.Stop();
        Assert.Null(timer.Snapshot);
        Assert.Equal(2, changes);
    }

    private static TimerSnapshot RoundTrip(TimerSnapshot snapshot) =>
        JsonSerializer.Deserialize<TimerSnapshot>(JsonSerializer.Serialize(snapshot))!;
}

public class CapsuleMotionTests
{
    // Le panneau ouvert fait 420 × 208 dans une fenêtre de 468 × 240 : un rebond ne doit pas être coupé par ses bords.
    private const double MaxOvershoot = 468.0 / 420;

    public static TheoryData<string> Styles() => [.. CapsuleMotions.All];

    [Theory]
    [MemberData(nameof(Styles))]
    public void OpeningStaysInsideTheWindow(string style)
    {
        var motion = CapsuleMotions.Open(style);
        foreach (var step in new[] { motion.Width, motion.Height })
        {
            for (var t = 0.0; t <= 1; t += 0.01)
            {
                Assert.True((step.Ease?.Ease(t) ?? t) <= MaxOvershoot, $"{style} dépasse à t={t}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Styles))]
    public void EveryStyleOpensAndClosesInLessThanASecond(string style)
    {
        foreach (var motion in new[] { CapsuleMotions.Open(style), CapsuleMotions.Close(style) })
        {
            Assert.True(motion.Height.DurationMs + motion.Height.DelayMs < 1000);
            Assert.True(motion.Width.DurationMs + motion.Width.DelayMs < 1000);
        }
    }

    [Fact]
    public void ClassicKeepsTheOriginalTimings()
    {
        Assert.Equal(380, CapsuleMotions.Open(CapsuleMotions.Classic).Height.DurationMs);
        Assert.Equal(260, CapsuleMotions.Close(CapsuleMotions.Classic).Height.DurationMs);
    }

    [Fact]
    public void ClassicIsTheDefault() => Assert.Equal(CapsuleMotions.Classic, new AppSettings().AnimationStyle);

    [Fact]
    public void UnknownStyleFallsBackToClassic()
    {
        var settings = new AppSettings { AnimationStyle = "inconnu" };
        SettingsStore.Normalize(settings);
        Assert.Equal(CapsuleMotions.Classic, settings.AnimationStyle);
    }

    [Fact]
    public void ChosenStyleIsKept()
    {
        var settings = new AppSettings { AnimationStyle = CapsuleMotions.Slide };
        SettingsStore.Normalize(settings);
        Assert.Equal(CapsuleMotions.Slide, settings.AnimationStyle);
    }
}

public class NewUserDefaultsTests
{
    [Fact]
    public void DefaultsAreNotAnyoneInParticular()
    {
        var settings = new AppSettings();
        Assert.Equal(string.Empty, settings.PreferredPlayer);
        Assert.Null(settings.City);
        Assert.Null(settings.Latitude);
    }

    [Fact]
    public void PomodoroButtonIsHiddenUntilAsked()
    {
        Assert.False(new AppSettings().PomodoroButtonEnabled);
        Assert.False(JsonSerializer.Deserialize<AppSettings>("""{"City":"Strasbourg"}""")!.PomodoroButtonEnabled);
    }

    [Fact]
    public void ExistingFileKeepsItsChoices()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"PreferredPlayer":"deezer","City":"Strasbourg","Latitude":48.58,"Longitude":7.74}""")!;
        SettingsStore.Normalize(settings);
        Assert.Equal("deezer", settings.PreferredPlayer);
        Assert.Equal("Strasbourg", settings.City);
        Assert.Equal(48.58, settings.Latitude);
        Assert.Null(settings.SetupCompleted); // L'assistant ne s'ouvre pas pour quelqu'un qui utilise déjà l'application.
    }

    [Fact]
    public void NewPagesAreAddedToOldFile()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""{"Pages":[{"Id":"media","IsEnabled":true}]}""")!;
        SettingsStore.Normalize(settings);
        Assert.Contains(settings.Pages, page => page.Id == PageIds.Calendar);
        Assert.Contains(settings.Pages, page => page.Id == PageIds.Files);
    }
}

public class NotificationFilterTests
{
    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(3, 0, true)]
    [InlineData(7, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(22, 0, true)]
    public void QuietHoursCanCrossMidnight(int hour, int minute, bool expected) =>
        Assert.Equal(expected, NotificationFilter.IsQuietTime(new TimeOnly(hour, minute), 22 * 60, 7 * 60));

    [Theory]
    [InlineData(13, 0, true)]
    [InlineData(14, 0, false)]
    [InlineData(12, 59, false)]
    public void QuietHoursWithinADay(int hour, int minute, bool expected) =>
        Assert.Equal(expected, NotificationFilter.IsQuietTime(new TimeOnly(hour, minute), 13 * 60, 14 * 60));

    [Fact]
    public void EmptyRangeIsNeverQuiet() => Assert.False(NotificationFilter.IsQuietTime(new TimeOnly(8, 0), 480, 480));

    [Fact]
    public void MutedAppIgnoresCase() => Assert.True(NotificationFilter.IsMuted("discord.exe", ["Discord.EXE"]));

    [Fact]
    public void OtherAppIsNotMuted() => Assert.False(NotificationFilter.IsMuted("slack", ["discord"]));

    [Theory]
    [InlineData("22:00", 1320)]
    [InlineData("7:30", 450)]
    [InlineData(" 07:05 ", 425)]
    public void ReadsTimes(string text, int expected) => Assert.Equal(expected, NotificationFilter.ParseTime(text));

    [Theory]
    [InlineData("25:00")]
    [InlineData("midi")]
    [InlineData("")]
    public void RefusesBadTimes(string text) => Assert.Null(NotificationFilter.ParseTime(text));

    [Fact]
    public void FormatsTime() => Assert.Equal("07:05", NotificationFilter.FormatTime(425));
}

public class HotkeyGestureTests
{
    [Fact]
    public void RoundTrips()
    {
        var gesture = HotkeyGesture.TryParse("Ctrl+Alt+Maj+Space")!;
        Assert.Equal("Ctrl+Alt+Maj+Space", gesture.ToString());
        Assert.Equal(HotkeyGesture.TryParse(gesture.ToString()), gesture);
    }

    [Theory]
    [InlineData("N")]
    [InlineData("Maj+N")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Bidule")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesUnusableShortcuts(string? text) => Assert.Null(HotkeyGesture.TryParse(text));

    [Fact]
    public void AllDefaultsAreValidAndDistinct()
    {
        var gestures = HotkeyActions.Defaults.Values.Select(HotkeyGesture.TryParse).ToList();
        Assert.DoesNotContain(null, gestures);
        Assert.Equal(gestures.Count, gestures.Distinct().Count());
    }

    [Fact]
    public void DefaultsKeepTheHistoricalShortcuts()
    {
        var toggle = HotkeyActions.Resolve(HotkeyActions.TogglePanel, new Dictionary<string, string>());
        Assert.Equal(0x4Eu, toggle.VirtualKey); // N
    }

    [Fact]
    public void CustomShortcutWins() =>
        Assert.Equal("Ctrl+Alt+K", HotkeyActions.Resolve(HotkeyActions.TogglePanel, new Dictionary<string, string> { [HotkeyActions.TogglePanel] = "Ctrl+Alt+K" }).ToString());

    [Fact]
    public void UnreadableCustomShortcutFallsBack() =>
        Assert.Equal("Ctrl+Alt+N", HotkeyActions.Resolve(HotkeyActions.TogglePanel, new Dictionary<string, string> { [HotkeyActions.TogglePanel] = "???" }).ToString());
}

public class PomodoroTests
{
    [Fact]
    public void AlternatesWorkAndBreaks()
    {
        Assert.False(PomodoroCycle.IsBreak(0));
        Assert.True(PomodoroCycle.IsBreak(1));
        Assert.False(PomodoroCycle.IsBreak(2));
    }

    [Fact]
    public void LongBreakComesAfterFourSessions()
    {
        Assert.Equal(TimeSpan.FromMinutes(25), PomodoroCycle.Duration(0));
        Assert.Equal(TimeSpan.FromMinutes(5), PomodoroCycle.Duration(1));
        Assert.Equal(TimeSpan.FromMinutes(5), PomodoroCycle.Duration(5));
        Assert.Equal(TimeSpan.FromMinutes(15), PomodoroCycle.Duration(7));
        Assert.Equal(TimeSpan.FromMinutes(5), PomodoroCycle.Duration(9));
        Assert.Equal(TimeSpan.FromMinutes(15), PomodoroCycle.Duration(15));
    }

    [Fact]
    public void CountsSessions()
    {
        Assert.Equal(1, PomodoroCycle.Session(0));
        Assert.Equal(1, PomodoroCycle.Session(1));
        Assert.Equal(2, PomodoroCycle.Session(2));
    }

    [Fact]
    public void DisplayLearnsThePomodoroStepAtOnce()
    {
        var timer = new TimerService();
        int? lastSeen = null;
        timer.Changed += () => lastSeen = timer.PomodoroStep;
        timer.StartPomodoro();
        Assert.Equal(0, lastSeen);
    }

    [Fact]
    public void TimerMovesToBreakAndAnnouncesIt()
    {
        var timer = new TimerService();
        var steps = new List<int>();
        timer.PomodoroAdvanced += steps.Add;

        timer.StartPomodoro();
        Assert.False(timer.IsPomodoroBreak);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.Duration);

        timer.AdvancePomodoro();
        Assert.True(timer.IsPomodoroBreak);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Duration);
        Assert.Equal([1], steps);
        Assert.True(timer.IsActive);
    }

    [Fact]
    public void StepSurvivesRestart()
    {
        var first = new TimerService();
        first.StartPomodoro();
        first.AdvancePomodoro();

        var snapshot = JsonSerializer.Deserialize<TimerSnapshot>(JsonSerializer.Serialize(first.Snapshot!))!;
        var second = new TimerService();
        second.Restore(snapshot);

        Assert.Equal(1, second.PomodoroStep);
        Assert.True(second.IsPomodoroBreak);
    }

    [Fact]
    public void OldSnapshotStillLoads()
    {
        var snapshot = JsonSerializer.Deserialize<TimerSnapshot>("""{"EndUtc":"2026-10-30T10:00:00Z","DurationSeconds":60,"PausedRemainingSeconds":null}""")!;
        Assert.Null(snapshot.PomodoroStep);
    }

    [Fact]
    public void StartingNormalTimerLeavesPomodoro()
    {
        var timer = new TimerService();
        timer.StartPomodoro();
        timer.Start(TimeSpan.FromMinutes(5));
        Assert.Null(timer.PomodoroStep);
    }

    [Fact]
    public void StoppingLeavesPomodoro()
    {
        var timer = new TimerService();
        timer.StartPomodoro();
        timer.Stop();
        Assert.Null(timer.PomodoroStep);
        Assert.False(timer.IsActive);
    }
}

public sealed class NoteStoreTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ilot-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void FirstNoteKeepsItsHistoricalFile()
    {
        var store = new NoteStore(_folder);
        store.Write(0, "ma note");
        Assert.Equal("ma note", File.ReadAllText(Path.Combine(_folder, "note.txt")));
        Assert.Equal(1, store.CountExisting());
    }

    [Fact]
    public void CountsAndReadsSeveralNotes()
    {
        var store = new NoteStore(_folder);
        store.Write(0, "a");
        store.Write(1, "b");
        store.Write(2, "c");
        Assert.Equal(3, store.CountExisting());
        Assert.Equal("c", store.Read(2));
    }

    [Fact]
    public void RemovingShiftsFollowingNotes()
    {
        var store = new NoteStore(_folder);
        store.Write(0, "a");
        store.Write(1, "b");
        store.Write(2, "c");
        store.Remove(1, 3);
        Assert.Equal(2, store.CountExisting());
        Assert.Equal("c", store.Read(1));
    }

    [Fact]
    public void FirstNoteCannotBeRemoved()
    {
        var store = new NoteStore(_folder);
        store.Write(0, "a");
        store.Remove(0, 1);
        Assert.Equal("a", store.Read(0));
    }

    [Fact]
    public void UninstallKeepsEveryNote()
    {
        var store = new NoteStore(_folder);
        store.Write(0, "a");
        store.Write(1, "b");
        File.WriteAllText(Path.Combine(_folder, "mail.dat"), "x");
        UninstallCleanup.RemoveData(_folder);
        Assert.Equal(["note.txt", "note2.txt"], Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
    }
}

public class NoteChecklistTests
{
    [Fact]
    public void AddsBoxAtStartOfCurrentLine()
    {
        var (text, caret) = NoteChecklist.ToggleLineBox("un\ndeux", 5);
        Assert.Equal("un\n☐ deux", text);
        Assert.Equal(7, caret);
    }

    [Fact]
    public void RemovesExistingBox()
    {
        var (text, caret) = NoteChecklist.ToggleLineBox("☐ tâche", 4);
        Assert.Equal("tâche", text);
        Assert.Equal(2, caret);
    }

    [Fact]
    public void WorksOnEmptyNote() => Assert.Equal("☐ ", NoteChecklist.ToggleLineBox(string.Empty, 0).Text);

    [Fact]
    public void ClickChecksThenUnchecks()
    {
        var checkedText = NoteChecklist.ToggleBoxAt("☐ a\n☐ b", 4);
        Assert.Equal("☐ a\n☑ b", checkedText);
        Assert.Equal("☐ a\n☐ b", NoteChecklist.ToggleBoxAt(checkedText!, 4));
    }

    [Fact]
    public void ClickOutsideABoxDoesNothing()
    {
        Assert.Null(NoteChecklist.ToggleBoxAt("☐ a", 2));
        Assert.Null(NoteChecklist.ToggleBoxAt("texte", -1));
        Assert.Null(NoteChecklist.ToggleBoxAt("a ☐ b", 2)); // Pas en début de ligne : simple caractère du texte.
    }
}

public class FileShelfTests
{
    [Fact]
    public void NewestComesFirstWithoutDuplicates()
    {
        var shelf = new FileShelf();
        shelf.Add([@"C:\a.txt"]);
        shelf.Add([@"C:\b.txt"]);
        shelf.Add([@"c:\A.TXT"]);
        Assert.Equal([@"c:\A.TXT", @"C:\b.txt"], shelf.Files);
    }

    [Fact]
    public void KeepsAtMostTheLimit()
    {
        var shelf = new FileShelf();
        shelf.Add(Enumerable.Range(0, FileShelf.MaxFiles + 5).Select(i => $@"C:\{i}.txt"));
        Assert.Equal(FileShelf.MaxFiles, shelf.Files.Count);
        Assert.Equal($@"C:\{FileShelf.MaxFiles + 4}.txt", shelf.Files[0]);
    }

    [Fact]
    public void RemovesAFile()
    {
        var shelf = new FileShelf();
        shelf.Add([@"C:\a.txt"]);
        shelf.Remove(@"C:\A.txt");
        Assert.Empty(shelf.Files);
    }

    [Fact]
    public void ForgetsMissingFiles()
    {
        var shelf = new FileShelf();
        var real = Path.GetTempFileName();
        shelf.Add([real, @"C:\ilot-fichier-absent.txt"]);
        Assert.True(shelf.RemoveMissing());
        Assert.Equal([real], shelf.Files);
        Assert.False(shelf.RemoveMissing());
        File.Delete(real);
    }
}

public class CalendarEventsTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    private static CalendarEvent At(string id, double minutes, bool isAllDay = false) =>
        new(id, id, Now.AddMinutes(minutes), isAllDay, null, null);

    [Fact]
    public void ReadsGoogleEvent()
    {
        var events = CalendarEvents.ParseGoogle("""
            {"items":[{"id":"1","summary":"Réunion","start":{"dateTime":"2026-10-30T14:00:00+01:00"},"hangoutLink":"https://meet.google.com/abc","htmlLink":"https://calendar.google.com/x"}]}
            """);
        var item = Assert.Single(events);
        Assert.Equal("Réunion", item.Title);
        Assert.Equal(new DateTime(2026, 10, 30, 13, 0, 0, DateTimeKind.Utc), item.StartUtc);
        Assert.Equal("https://meet.google.com/abc", item.JoinUrl);
        Assert.False(item.IsAllDay);
    }

    [Fact]
    public void ReadsGoogleAllDayEventAndConferenceLink()
    {
        var events = CalendarEvents.ParseGoogle("""
            {"items":[
              {"id":"1","summary":"Congés","start":{"date":"2026-10-30"}},
              {"id":"2","start":{"dateTime":"2026-10-30T14:00:00Z"},"conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:1"},{"entryPointType":"video","uri":"https://zoom.us/j/1"}]}},
              {"id":"3","start":{"dateTime":"2026-10-30T15:00:00Z"},"location":"https://teams.microsoft.com/l/x"},
              {"id":"4","start":{"dateTime":"2026-10-30T16:00:00Z"},"location":"Salle 3"}
            ]}
            """);
        Assert.True(events[0].IsAllDay);
        Assert.Equal("https://zoom.us/j/1", events[1].JoinUrl);
        Assert.Equal("https://teams.microsoft.com/l/x", events[2].JoinUrl);
        Assert.Null(events[3].JoinUrl);
    }

    [Fact]
    public void ReadsGraphEventAsUtc()
    {
        var events = CalendarEvents.ParseGraph("""
            {"value":[{"id":"1","subject":"Point","start":{"dateTime":"2026-10-30T13:00:00.0000000","timeZone":"UTC"},"isAllDay":false,"onlineMeeting":{"joinUrl":"https://teams.microsoft.com/x"},"webLink":"https://outlook.office.com/y"}]}
            """);
        var item = Assert.Single(events);
        Assert.Equal(new DateTime(2026, 10, 30, 13, 0, 0, DateTimeKind.Utc), item.StartUtc);
        Assert.Equal("https://teams.microsoft.com/x", item.JoinUrl);
    }

    [Fact]
    public void GraphEventWithoutMeeting()
    {
        var events = CalendarEvents.ParseGraph("""{"value":[{"id":"1","subject":"Point","start":{"dateTime":"2026-10-30T13:00:00.0000000"},"onlineMeeting":null,"webLink":"https://x"}]}""");
        Assert.Null(Assert.Single(events).JoinUrl);
    }

    [Fact]
    public void EmptyResponses()
    {
        Assert.Empty(CalendarEvents.ParseGoogle("{}"));
        Assert.Empty(CalendarEvents.ParseGraph("{}"));
    }

    [Fact]
    public void UpcomingDropsPastAndSorts()
    {
        var upcoming = CalendarEvents.Upcoming([At("b", 60), At("passe", -30), At("a", 10)], Now);
        Assert.Equal(["a", "b"], upcoming.Select(item => item.Id));
    }

    [Fact]
    public void CountdownOnlyWhenClose()
    {
        Assert.Equal(10, CalendarEvents.CountdownMinutesLeft([At("a", 10)], Now));
        Assert.Null(CalendarEvents.CountdownMinutesLeft([At("a", 40)], Now));
        Assert.Null(CalendarEvents.CountdownMinutesLeft([At("a", 5, isAllDay: true)], Now));
        Assert.Null(CalendarEvents.CountdownMinutesLeft([], Now));
    }

    [Fact]
    public void ReminderOnlyOnce()
    {
        var announced = new HashSet<string>();
        var events = new[] { At("a", 4) };
        Assert.Equal("a", CalendarEvents.DueForReminder(events, Now, announced)?.Id);
        Assert.Null(CalendarEvents.DueForReminder(events, Now, announced));
    }

    [Fact]
    public void NoReminderTooEarlyOrForPastEvent()
    {
        var announced = new HashSet<string>();
        Assert.Null(CalendarEvents.DueForReminder([At("loin", 20), At("passe", -1)], Now, announced));
        Assert.Empty(announced);
    }
}

public class ImapParsingTests
{
    [Fact]
    public void DecodesEncodedSubjects()
    {
        Assert.Equal("Café", MailHeaders.DecodeEncodedWords("=?UTF-8?B?Q2Fmw6k=?="));
        Assert.Equal("Café du coin", MailHeaders.DecodeEncodedWords("=?UTF-8?Q?Caf=C3=A9_du_coin?="));
        Assert.Equal("Été", MailHeaders.DecodeEncodedWords("=?ISO-8859-1?Q?=C9t=E9?="));
        Assert.Equal("Été", MailHeaders.DecodeEncodedWords("=?windows-1252?Q?=C9t=E9?="));
        Assert.Equal("Sujet simple", MailHeaders.DecodeEncodedWords("Sujet simple"));
    }

    [Fact]
    public void JoinsConsecutiveEncodedWords() =>
        Assert.Equal("Bonjour tout", MailHeaders.DecodeEncodedWords("=?UTF-8?Q?Bonjour_?= =?UTF-8?Q?tout?="));

    [Fact]
    public void KeepsUnknownEncodingReadable() =>
        Assert.Equal("=?inconnu?Q?abc?=", MailHeaders.DecodeEncodedWords("=?inconnu?Q?abc?="));

    [Fact]
    public void ReadsFoldedHeaders()
    {
        var headers = ImapClient.ParseHeaders("* 1 FETCH (BODY {50}\r\nFrom: \"Jeanne Martin\" <j@exemple.fr>\r\nSubject: Un sujet\r\n  long\r\n\r\n)");
        Assert.Equal("\"Jeanne Martin\" <j@exemple.fr>", headers["from"]);
        Assert.Equal("Un sujet long", headers["subject"]);
        Assert.Equal("Jeanne Martin", MailHeaders.CleanSender(headers["from"]));
    }

    [Fact]
    public void QuotesCredentials() => Assert.Equal("\"a\\\"b\\\\c\"", ImapClient.Quote("a\"b\\c"));

    [Fact]
    public void FindsMarkedTrash() => Assert.Equal("[Gmail]/Corbeille", ImapClient.FindTrashFolder([
        "* LIST (\\HasNoChildren) \"/\" \"INBOX\"",
        "* LIST (\\HasNoChildren \\Trash) \"/\" \"[Gmail]/Corbeille\"",
    ]));

    [Fact]
    public void FallsBackOnUsualTrashNames() => Assert.Equal("INBOX.Trash", ImapClient.FindTrashFolder([
        "* LIST () \".\" \"INBOX\"",
        "* LIST () \".\" \"INBOX.Trash\"",
    ]));

    [Fact]
    public void NoTrashFound() => Assert.Null(ImapClient.FindTrashFolder(["* LIST () \"/\" \"INBOX\""]));

    [Theory]
    [InlineData("moi@gmail.com", "imap.gmail.com")]
    [InlineData("moi@orange.fr", "imap.orange.fr")]
    [InlineData("moi@inconnu.fr", null)]
    public void GuessesHost(string address, string? host) => Assert.Equal(host, ImapClient.GuessHost(address));
}

/// <summary>Joue un serveur IMAP : pour chaque commande reçue, renvoie la réponse prévue avec le bon numéro de commande.</summary>
internal sealed class FakeImapStream(params (string Expected, string Reply)[] script) : Stream
{
    private readonly Queue<(string Expected, string Reply)> _script = new(script);
    private readonly MemoryStream _toRead = Greeting();

    public List<string> Commands { get; } = [];

    private static MemoryStream Greeting()
    {
        var stream = new MemoryStream(); // Extensible : chaque réponse remplace la précédente.
        stream.Write(System.Text.Encoding.UTF8.GetBytes("* OK prêt\r\n"));
        stream.Position = 0;
        return stream;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var line = System.Text.Encoding.UTF8.GetString(buffer, offset, count).TrimEnd();
        var tag = line[..line.IndexOf(' ')];
        var command = line[(tag.Length + 1)..];
        Commands.Add(command);
        var (expected, reply) = _script.Dequeue();
        Assert.StartsWith(expected, command);
        _toRead.SetLength(0);
        _toRead.Write(System.Text.Encoding.UTF8.GetBytes(reply.Replace("{TAG}", tag)));
        _toRead.Position = 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => _toRead.Read(buffer, offset, count);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

public sealed class ImapClientTests : IDisposable
{
    private const string LoginOk = "{TAG} OK connecté\r\n";
    private readonly string _folder = Directory.CreateTempSubdirectory("ilot-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static (string, string) Fetch(string subject)
    {
        var headers = $"From: Jeanne <j@x.fr>\r\nSubject: {subject}\r\n\r\n";
        return ("UID FETCH", $"* 1 FETCH (UID 1 BODY[HEADER.FIELDS (FROM SUBJECT)] {{{System.Text.Encoding.UTF8.GetByteCount(headers)}}}\r\n{headers})\r\n{{TAG}} OK\r\n");
    }

    private MailService ServiceOver(FakeImapStream stream)
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"))
        {
            ImapConnector = (_, user, password, token) => ImapClient.OverStreamAsync(stream, user, password, token),
        };
        service.Credentials.ImapHost = "imap.exemple.fr";
        service.Credentials.ImapAddress = "moi@exemple.fr";
        service.Credentials.ImapPassword = "secret";
        return service;
    }

    [Fact]
    public async Task ReadsUnreadNewestFirstWithoutMarkingThemRead()
    {
        var stream = new FakeImapStream(
            ("LOGIN", LoginOk),
            ("EXAMINE INBOX", "* 3 EXISTS\r\n{TAG} OK\r\n"),
            ("UID SEARCH UNSEEN", "* SEARCH 4 9\r\n{TAG} OK\r\n"),
            Fetch("Récent"),
            Fetch("Ancien"));

        var summary = await ServiceOver(stream).GetSummaryAsync(MailProvider.Imap);

        Assert.Null(summary.Error);
        Assert.Equal(2, summary.Unread);
        Assert.Equal("9", summary.Items[0].Id);
        Assert.Equal("Récent", summary.Items[0].Subject);
        Assert.Equal("Jeanne", summary.Items[0].Sender);
        Assert.True(summary.Items[0].IsLoaded);
        Assert.All(stream.Commands.Where(command => command.StartsWith("UID FETCH")), command => Assert.Contains("BODY.PEEK", command));
    }

    [Fact]
    public async Task KnownServiceGivesAWebmailLink()
    {
        var stream = new FakeImapStream(
            ("LOGIN", LoginOk),
            ("EXAMINE INBOX", "{TAG} OK\r\n"),
            ("UID SEARCH UNSEEN", "* SEARCH 9\r\n{TAG} OK\r\n"),
            Fetch("Sujet"));
        var service = ServiceOver(stream);
        service.Credentials.ImapAddress = "moi@orange.fr";

        var summary = await service.GetSummaryAsync(MailProvider.Imap);

        Assert.Equal("https://messagerie.orange.fr/", summary.Items[0].OpenUrl);
    }

    [Fact]
    public async Task UnknownServiceHasNoLink()
    {
        var stream = new FakeImapStream(
            ("LOGIN", LoginOk),
            ("EXAMINE INBOX", "{TAG} OK\r\n"),
            ("UID SEARCH UNSEEN", "* SEARCH 9\r\n{TAG} OK\r\n"),
            Fetch("Sujet"));

        var summary = await ServiceOver(stream).GetSummaryAsync(MailProvider.Imap);

        Assert.Null(summary.Items[0].OpenUrl);
    }

    [Theory]
    [InlineData("moi@gmail.com", "https://mail.google.com/mail/")]
    [InlineData("MOI@Yahoo.FR", "https://mail.yahoo.com/")]
    [InlineData("moi@societe.fr", null)]
    [InlineData(null, null)]
    public void GuessesWebmail(string? address, string? expected) => Assert.Equal(expected, ImapClient.GuessWebmail(address));

    [Fact]
    public async Task EmptyInbox()
    {
        var stream = new FakeImapStream(("LOGIN", LoginOk), ("EXAMINE INBOX", "{TAG} OK\r\n"), ("UID SEARCH UNSEEN", "* SEARCH\r\n{TAG} OK\r\n"));
        var summary = await ServiceOver(stream).GetSummaryAsync(MailProvider.Imap);
        Assert.Equal(0, summary.Unread);
        Assert.Empty(summary.Items);
    }

    [Fact]
    public async Task RefusedLoginAsksToReconnect()
    {
        var stream = new FakeImapStream(("LOGIN", "{TAG} NO mauvais mot de passe\r\n"));
        var summary = await ServiceOver(stream).GetSummaryAsync(MailProvider.Imap);
        Assert.True(summary.NeedsReconnect);
    }

    [Fact]
    public async Task ServerFailureIsTemporary()
    {
        var stream = new FakeImapStream(("LOGIN", LoginOk), ("EXAMINE INBOX", "{TAG} BAD erreur\r\n"));
        var summary = await ServiceOver(stream).GetSummaryAsync(MailProvider.Imap);
        Assert.NotNull(summary.Error);
        Assert.False(summary.NeedsReconnect);
    }

    [Fact]
    public async Task MarksAsRead()
    {
        var stream = new FakeImapStream(("LOGIN", LoginOk), ("SELECT INBOX", "{TAG} OK\r\n"), ("UID STORE 9 +FLAGS.SILENT (\\Seen)", "{TAG} OK\r\n"));
        Assert.Null(await ServiceOver(stream).MarkAsReadAsync(MailProvider.Imap, "9"));
    }

    [Fact]
    public async Task MovesToTrashWithoutEverDeleting()
    {
        var stream = new FakeImapStream(
            ("LOGIN", LoginOk),
            ("LIST", "* LIST (\\Trash) \"/\" \"Trash\"\r\n{TAG} OK\r\n"),
            ("SELECT INBOX", "{TAG} OK\r\n"),
            ("UID MOVE 9 \"Trash\"", "{TAG} OK\r\n"));
        Assert.Null(await ServiceOver(stream).TrashAsync(MailProvider.Imap, "9"));
        Assert.DoesNotContain(stream.Commands, command => command.Contains("EXPUNGE") || command.Contains("\\Deleted"));
    }

    [Fact]
    public async Task NoTrashFolderRefusesToDelete()
    {
        var stream = new FakeImapStream(("LOGIN", LoginOk), ("LIST", "* LIST () \"/\" \"INBOX\"\r\n{TAG} OK\r\n"));
        Assert.NotNull(await ServiceOver(stream).TrashAsync(MailProvider.Imap, "9"));
        Assert.DoesNotContain(stream.Commands, command => command.StartsWith("UID"));
    }

    [Fact]
    public async Task ConnectVerifiesThenStoresCredentials()
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"))
        {
            ImapConnector = (_, user, password, token) => ImapClient.OverStreamAsync(new FakeImapStream(("LOGIN", LoginOk)), user, password, token),
        };
        Assert.Null(await service.ConnectImapAsync("moi@gmail.com", "secret", string.Empty));
        Assert.True(service.IsConnected(MailProvider.Imap));
        Assert.Equal("imap.gmail.com", service.Credentials.ImapHost);

        service.Disconnect(MailProvider.Imap);
        Assert.False(service.IsConnected(MailProvider.Imap));
        Assert.False(service.IsConnected(MailProvider.Outlook));
    }

    [Fact]
    public async Task ConnectTrimsPastedPassword()
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"))
        {
            ImapConnector = (_, user, password, token) => ImapClient.OverStreamAsync(new FakeImapStream(("LOGIN", LoginOk)), user, password, token),
        };
        Assert.Null(await service.ConnectImapAsync("moi@gmail.com", " secret\r\n", string.Empty));
        Assert.Equal("secret", service.Credentials.ImapPassword);
    }

    [Fact]
    public async Task RefusedConnectStoresNothing()
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"))
        {
            ImapConnector = (_, user, password, token) => ImapClient.OverStreamAsync(new FakeImapStream(("LOGIN", "{TAG} NO\r\n")), user, password, token),
        };
        Assert.Equal(ImapClient.LoginRefused, await service.ConnectImapAsync("moi@gmail.com", "faux", string.Empty));
        Assert.False(service.IsConnected(MailProvider.Imap));
    }

    [Fact]
    public async Task UnknownServerAsksForIt()
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"));
        Assert.Contains("Serveur inconnu", await service.ConnectImapAsync("moi@societe.fr", "secret", string.Empty));
    }
}

public class TranslationTests
{
    private static readonly string[] Untranslated =
    [
        "{0}", "Pomodoro",
    ];

    private static string SourceFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "src", "Ilot")))
        {
            folder = folder.Parent;
        }
        return Path.Combine(folder!.FullName, "src", "Ilot");
    }

    private static IEnumerable<string> SourceFiles(string pattern) => Directory
        .GetFiles(SourceFolder(), pattern, SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    [Fact]
    public void EveryCodeStringHasATranslation()
    {
        var missing = new List<string>();
        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "Loc\\.T\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                var key = System.Text.RegularExpressions.Regex.Unescape(match.Groups[1].Value);
                if (!Loc.English.ContainsKey(key))
                {
                    missing.Add(key);
                }
            }
        }
        Assert.Empty(missing.Distinct());
    }

    [Fact]
    public void EveryXamlStringHasATranslation()
    {
        var missing = new List<string>();
        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\\s(?:Text|Content|Header|ToolTip|Title|AutomationProperties\\.Name)=\"([^\"{&][^\"]*)\""))
            {
                var key = WebUtility.HtmlDecode(match.Groups[1].Value);
                if (!Loc.English.ContainsKey(key) && !Untranslated.Contains(key))
                {
                    missing.Add(key);
                }
            }
        }
        Assert.Empty(missing.Distinct());
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        foreach (var (french, english) in Loc.English)
        {
            Assert.Equal(
                System.Text.RegularExpressions.Regex.Matches(french, @"\{\d\}").Select(match => match.Value).Order(),
                System.Text.RegularExpressions.Regex.Matches(english, @"\{\d\}").Select(match => match.Value).Order());
        }
    }

    [Fact]
    public void MissingTextStaysInFrench() => Assert.Equal("Texte inconnu", Loc.ToEnglish("Texte inconnu"));

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("en", "en")]
    public void ExplicitLanguageWins(string language, string expected) => Assert.Equal(expected, Loc.Resolve(language));
}

public class HeadsetChoiceTests
{
    private static BluetoothAudioDevice Device(string name, bool isConnected = false) => new(Guid.NewGuid(), name, isConnected, null);

    [Fact]
    public void ChosenHeadsetWins()
    {
        var devices = new[] { Device("AirPods de Elian"), Device("Sony WH-1000XM5") };
        Assert.Equal("Sony WH-1000XM5", BluetoothAudioService.Choose(devices, "sony wh-1000xm5")?.Name);
    }

    [Fact]
    public void AirPodsComeFirstByDefault()
    {
        var devices = new[] { Device("Sony", isConnected: true), Device("AirPods de Elian") };
        Assert.Equal("AirPods de Elian", BluetoothAudioService.Choose(devices, string.Empty)?.Name);
    }

    [Fact]
    public void ConnectedHeadsetWithoutAirPods()
    {
        var devices = new[] { Device("JBL"), Device("Sony", isConnected: true) };
        Assert.Equal("Sony", BluetoothAudioService.Choose(devices, string.Empty)?.Name);
    }

    [Fact]
    public void ChosenButGoneFallsBack()
    {
        var devices = new[] { Device("AirPods de Elian") };
        Assert.Equal("AirPods de Elian", BluetoothAudioService.Choose(devices, "Ancien casque")?.Name);
    }

    [Fact]
    public void NoHeadset() => Assert.Null(BluetoothAudioService.Choose([], string.Empty));
}

public class CompactLayoutTests
{
    [Fact]
    public void EverythingFitsWithFewIndicators() => Assert.True(CompactLayout.Fits(180, 58, 30, 40));

    [Fact]
    public void CrowdedRightSideDoesNotFit() => Assert.False(CompactLayout.Fits(180, 58, 30, 75));

    [Fact]
    public void LongTimerDoesNotFitNextToAWideCenter() => Assert.False(CompactLayout.Fits(180, 100, 56, 20));

    [Fact]
    public void RemovingTheTemperatureMakesRoom()
    {
        Assert.False(CompactLayout.Fits(180, 58, 0, 65));
        Assert.True(CompactLayout.Fits(180, 36, 0, 65));
    }
}

public class ConsecutiveLowReadingsTests
{
    [Fact]
    public void IsolatedLowReadingIsIgnored()
    {
        var readings = new ConsecutiveLowReadings(20, 3);
        Assert.True(readings.IsTrusted(80));
        Assert.False(readings.IsTrusted(10)); // Une paire voisine.
        Assert.True(readings.IsTrusted(80));
        Assert.False(readings.IsTrusted(10));
    }

    [Fact]
    public void RepeatedLowReadingsAreTrusted()
    {
        var readings = new ConsecutiveLowReadings(20, 3);
        Assert.False(readings.IsTrusted(20));
        Assert.False(readings.IsTrusted(20));
        Assert.True(readings.IsTrusted(20));
        Assert.True(readings.IsTrusted(10));
    }
}

public class PrivacyIndicatorTests
{
    private static readonly DateTime Boot = new(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void UseStartedSinceBootIsActive() =>
        Assert.True(PrivacyIndicatorService.IsActive(Boot.AddHours(1).ToFileTimeUtc(), 0, Boot));

    [Fact]
    public void FinishedUseIsNotActive() =>
        Assert.False(PrivacyIndicatorService.IsActive(Boot.AddHours(1).ToFileTimeUtc(), Boot.AddHours(2).ToFileTimeUtc(), Boot));

    [Fact]
    public void CrashBeforeBootIsNotActive() =>
        Assert.False(PrivacyIndicatorService.IsActive(Boot.AddDays(-1).ToFileTimeUtc(), 0, Boot));

    [Fact]
    public void NeverUsedIsNotActive() => Assert.False(PrivacyIndicatorService.IsActive(0, 0, Boot));

    [Theory]
    [InlineData("C:#Program Files#Discord#Discord.exe", "Discord")]
    [InlineData("C:#Users#moi#AppData#app.test.exe", "app.test")]
    public void ReadsProgramNameFromKey(string key, string expected) =>
        Assert.Equal(expected, PrivacyIndicatorService.ProgramNameFromKey(key));
}

public class NetworkStatusTests
{
    private static readonly IPAddress[] Router = [IPAddress.Parse("192.168.1.1")];

    [Fact]
    public void RealAdapterWithGatewayIsOnline() =>
        Assert.True(NetworkStatus.IsUsable(OperationalStatus.Up, NetworkInterfaceType.Wireless80211, Router));

    [Fact]
    public void VirtualAdapterWithoutGatewayIsNotOnline() =>
        Assert.False(NetworkStatus.IsUsable(OperationalStatus.Up, NetworkInterfaceType.Ethernet, []));

    [Fact]
    public void EmptyGatewayIsNotOnline() =>
        Assert.False(NetworkStatus.IsUsable(OperationalStatus.Up, NetworkInterfaceType.Ethernet, [IPAddress.Any]));

    [Fact]
    public void DisconnectedAdapterIsNotOnline() =>
        Assert.False(NetworkStatus.IsUsable(OperationalStatus.Down, NetworkInterfaceType.Wireless80211, Router));

    [Fact]
    public void TunnelIsNotOnline() =>
        Assert.False(NetworkStatus.IsUsable(OperationalStatus.Up, NetworkInterfaceType.Tunnel, Router));
}

public class UninstallCleanupTests
{
    [Fact]
    public void RemovesEverythingButTheNote()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mail.dat"), "x");
        File.WriteAllText(Path.Combine(folder, "settings.json"), "x");
        File.WriteAllText(Path.Combine(folder, "note.txt"), "ma note");

        UninstallCleanup.RemoveData(folder);

        Assert.Equal(["note.txt"], Directory.GetFiles(folder).Select(Path.GetFileName));
        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void RemovesTheFolderWithoutNote()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ilot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "mail.dat"), "x");

        UninstallCleanup.RemoveData(folder);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void MissingFolderIsNotAnError() =>
        UninstallCleanup.RemoveData(Path.Combine(Path.GetTempPath(), "ilot-tests", Guid.NewGuid().ToString("N")));
}

public class MailRateLimitTests
{
    [Theory]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}]}}""")]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"userRateLimitExceeded"}]}}""")]
    [InlineData("""{"error":{"code":403,"errors":[{"reason":"quotaExceeded"}]}}""")]
    public void RecognizesRateLimits(string body) => Assert.True(MailService.IsRateLimitBody(body));

    [Fact]
    public void RealDenialIsNotARateLimit() =>
        Assert.False(MailService.IsRateLimitBody("""{"error":{"code":403,"errors":[{"reason":"insufficientPermissions"}]}}"""));
}

public class SettingsNormalizationTests
{
    [Fact]
    public void AbsurdValuesAreBroughtBackInRange()
    {
        var settings = new AppSettings
        {
            CapsuleScale = 0,
            AnimationDurationFactor = -3,
            ExpandDelayMs = -1,
            CollapseDelayMs = 999999,
            HorizontalPosition = double.NaN,
            Latitude = 500,
            Longitude = 7,
        };
        SettingsStore.Normalize(settings);
        Assert.Equal(0.5, settings.CapsuleScale);
        Assert.Equal(0, settings.AnimationDurationFactor);
        Assert.Equal(0, settings.ExpandDelayMs);
        Assert.Equal(5000, settings.CollapseDelayMs);
        Assert.Equal(0.5, settings.HorizontalPosition);
        Assert.Null(settings.Latitude);
        Assert.Null(settings.Longitude);
    }

    [Fact]
    public void ValidValuesAreKept()
    {
        var settings = new AppSettings { CapsuleScale = 1.15, AnimationDurationFactor = 0.6, Latitude = -33.8, Longitude = 151.2 };
        SettingsStore.Normalize(settings);
        Assert.Equal(1.15, settings.CapsuleScale);
        Assert.Equal(0.6, settings.AnimationDurationFactor);
        Assert.Equal(-33.8, settings.Latitude);
        Assert.Equal(151.2, settings.Longitude);
    }

    [Fact]
    public void NullPagesAreRebuilt()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"Pages\":null}")!;
        SettingsStore.Normalize(settings);
        Assert.Equal(PageIds.All.Length, settings.Pages.Count);
    }

    [Fact]
    public void OldLyricsPageIsDropped()
    {
        var settings = new AppSettings { Pages = [new PageEntry { Id = "lyrics" }, new PageEntry { Id = PageIds.Media }] };
        SettingsStore.Normalize(settings);
        Assert.DoesNotContain(settings.Pages, page => page.Id == "lyrics");
    }

    [Fact]
    public void AddsMissingPagesAndDropsUnknownOnes()
    {
        var settings = new AppSettings { Pages = [new PageEntry { Id = "inconnue" }, new PageEntry { Id = PageIds.Mail }, new PageEntry { Id = PageIds.Mail }] };
        SettingsStore.Normalize(settings);
        Assert.Equal(PageIds.All.Length, settings.Pages.Count);
        Assert.Equal(PageIds.Mail, settings.Pages[0].Id);
    }

    [Fact]
    public void KeepsAtLeastOnePage()
    {
        var settings = new AppSettings { Pages = PageIds.All.Select(id => new PageEntry { Id = id, IsEnabled = false }).ToList() };
        SettingsStore.Normalize(settings);
        Assert.Contains(settings.Pages, page => page.IsEnabled);
    }

    [Fact]
    public void ClampsPosition()
    {
        var settings = new AppSettings { HorizontalPosition = 4 };
        SettingsStore.Normalize(settings);
        Assert.Equal(1, settings.HorizontalPosition);
    }

    [Fact]
    public void OldSettingsFileStillLoads()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"City\":\"Strasbourg\",\"GmailEnabled\":true}")!;
        Assert.True(settings.Gmail2Enabled);
        Assert.Null(settings.SavedTimer);
    }
}

public class CityParsingTests
{
    [Fact]
    public void ReadsSeveralCitiesWithTheirRegion()
    {
        var cities = WeatherService.ParseCities("""
            {"results":[
              {"name":"Paris","latitude":48.85,"longitude":2.35,"admin1":"Île-de-France","country":"France"},
              {"name":"Paris","latitude":33.66,"longitude":-95.55,"admin1":"Texas","country":"États-Unis"}
            ]}
            """);
        Assert.Equal(2, cities.Count);
        Assert.Equal("Paris, Texas, États-Unis", cities[1].Label);
    }

    [Fact]
    public void LabelSkipsMissingParts() =>
        Assert.Equal("Monaco", new GeocodedCity("Monaco", 43.7, 7.4).Label);

    [Fact]
    public void NoResultGivesEmptyList() => Assert.Empty(WeatherService.ParseCities("{\"generationtime_ms\":0.1}"));
}

public class MailProviderTests
{
    [Fact]
    public void DescribesAccounts()
    {
        Assert.True(MailProvider.Gmail.IsGmail());
        Assert.True(MailProvider.Gmail2.IsGmail());
        Assert.False(MailProvider.Outlook.IsGmail());
        Assert.Equal("Gmail 2", MailProvider.Gmail2.Label());
        Assert.Equal("Outlook", MailProvider.Outlook.Label());
    }
}
