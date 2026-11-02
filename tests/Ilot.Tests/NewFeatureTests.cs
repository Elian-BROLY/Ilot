using System.Text.Json;
using Ilot.Services;
using Ilot.Settings;
using Xunit;

namespace Ilot.Tests;

public class UpgradeKeepsSettingsTests
{
    // Un fichier écrit par la version précédente : aucune page Presse-papiers ni Raccourcis, et des choix bien à lui.
    private const string PreviousVersionFile = """
        {
          "Pages": [
            { "Id": "media", "IsEnabled": true },
            { "Id": "mail", "IsEnabled": true },
            { "Id": "note", "IsEnabled": true },
            { "Id": "audio", "IsEnabled": false },
            { "Id": "appvolume", "IsEnabled": false },
            { "Id": "timer", "IsEnabled": true },
            { "Id": "weather", "IsEnabled": false },
            { "Id": "systemstats", "IsEnabled": true },
            { "Id": "calendar", "IsEnabled": false },
            { "Id": "files", "IsEnabled": false }
          ],
          "AnimationStyle": "fade",
          "HorizontalPosition": 0.51,
          "ContextualPageEnabled": false,
          "WheelVolumeEnabled": false,
          "AccentName": "Rose",
          "PreferredPlayer": "deezer",
          "CapsuleScale": 1.15
        }
        """;

    private static AppSettings LoadPrevious()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(PreviousVersionFile)!;
        SettingsStore.Normalize(settings);
        return settings;
    }

    [Fact]
    public void NewPagesArriveHidden()
    {
        var settings = LoadPrevious();
        Assert.False(settings.Pages.Single(page => page.Id == PageIds.Clipboard).IsEnabled);
        Assert.False(settings.Pages.Single(page => page.Id == PageIds.Shortcuts).IsEnabled);
    }

    [Fact]
    public void PagesKeepTheirOrderAndChoices()
    {
        var settings = LoadPrevious();
        string[] previous = ["media", "mail", "note", "audio", "appvolume", "timer", "weather", "systemstats", "calendar", "files"];
        Assert.Equal(previous, settings.Pages.Take(previous.Length).Select(page => page.Id));
        Assert.Equal(["media", "mail", "note", "timer", "systemstats"], settings.Pages.Where(page => page.IsEnabled).Select(page => page.Id));
    }

    [Fact]
    public void ChoicesAreUnchanged()
    {
        var settings = LoadPrevious();
        Assert.Equal("fade", settings.AnimationStyle);
        Assert.Equal(0.51, settings.HorizontalPosition);
        Assert.False(settings.ContextualPageEnabled);
        Assert.False(settings.WheelVolumeEnabled);
        Assert.Equal("Rose", settings.AccentName);
        Assert.Equal("deezer", settings.PreferredPlayer);
        Assert.Equal(1.15, settings.CapsuleScale);
    }

    [Fact]
    public void NewOptionsKeepTheCapsuleAsItWas()
    {
        var settings = LoadPrevious();
        Assert.False(settings.HardwareRenderingEnabled); // Même rendu logiciel qu'avant.
        Assert.True(settings.CompactMailEnabled);
        Assert.True(settings.CompactHeadsetEnabled);
        Assert.True(settings.CompactEqualizerEnabled);
        Assert.True(settings.CompactCountdownEnabled);
    }

    [Fact]
    public void FreshInstallStillShowsTheUsualPages()
    {
        var settings = new AppSettings();
        SettingsStore.Normalize(settings);
        Assert.All(settings.Pages.Where(page => !PageIds.OptIn.Contains(page.Id)), page => Assert.True(page.IsEnabled));
    }
}

public class DownloadWatcherTests
{
    [Theory]
    [InlineData(@"C:\Téléchargements\film.mp4.crdownload", true)]
    [InlineData(@"C:\Téléchargements\archive.zip.part", true)]
    [InlineData(@"C:\Téléchargements\Unconfirmed 1234.CRDOWNLOAD", true)]
    [InlineData(@"C:\Téléchargements\archive.zip", false)]
    [InlineData(@"C:\Téléchargements\notes.txt", false)]
    public void RecognizesBrowserTemporaryFiles(string path, bool expected) => Assert.Equal(expected, DownloadWatcher.IsPartial(path));

    [Fact]
    public void FinalRenameMarksTheEnd() =>
        Assert.Equal(@"C:\D\archive.zip", DownloadWatcher.CompletedPath(@"C:\D\archive.zip.crdownload", @"C:\D\archive.zip"));

    [Fact]
    public void ChromeIntermediateRenameIsNotTheEnd() =>
        Assert.Null(DownloadWatcher.CompletedPath(@"C:\D\Unconfirmed 1234.crdownload", @"C:\D\archive.zip.crdownload"));

    [Fact]
    public void OrdinaryRenameIsIgnored() =>
        Assert.Null(DownloadWatcher.CompletedPath(@"C:\D\a.txt", @"C:\D\b.txt"));
}

public class ClipboardHistoryTests
{
    private static ClipboardEntry Image(string key) => new(key, null, [1, 2, 3], null, 10, 10);

    [Fact]
    public void NewestComesFirst()
    {
        var history = new ClipboardHistory();
        history.Add(ClipboardHistory.FromText("un"));
        history.Add(ClipboardHistory.FromText("deux"));
        Assert.Equal(["deux", "un"], history.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void CopyingAgainMovesToTheTopWithoutDuplicate()
    {
        var history = new ClipboardHistory();
        history.Add(ClipboardHistory.FromText("un"));
        history.Add(ClipboardHistory.FromText("deux"));
        history.Add(ClipboardHistory.FromText("un"));
        Assert.Equal(["un", "deux"], history.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void KeepsAtMostTheCapacity()
    {
        var history = new ClipboardHistory(capacity: 3);
        foreach (var text in new[] { "a", "b", "c", "d" })
        {
            history.Add(ClipboardHistory.FromText(text));
        }
        Assert.Equal(["d", "c", "b"], history.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void KeepsOnlyTheNewestImages()
    {
        var history = new ClipboardHistory(capacity: 10, maxImages: 2);
        history.Add(Image("i:1"));
        history.Add(ClipboardHistory.FromText("texte"));
        history.Add(Image("i:2"));
        history.Add(Image("i:3"));
        Assert.Equal(["i:3", "i:2", "t:texte"], history.Entries.Select(entry => entry.Key));
    }
}

public class ClockFormatTests
{
    [Theory]
    [InlineData("HH:mm", "HH:mm")] // Français.
    [InlineData("h:mm tt", "h:mm")] // Anglais américain : sans AM/PM, trop large pour la capsule.
    [InlineData("H:mm", "HH:mm")]
    public void FollowsWindowsFormat(string pattern, string expected) => Assert.Equal(expected, ClockFormat.FromPattern(pattern));
}
