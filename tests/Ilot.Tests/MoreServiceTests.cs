using System.IO;
using System.Text.Json;
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

public class LyricsParsingTests
{
    [Fact]
    public void ReadsTimedLines()
    {
        var lines = LyricsService.ParseSynced("[00:12.50] Première ligne\n[01:02.00]Deuxième");
        Assert.Equal(2, lines.Count);
        Assert.Equal(TimeSpan.FromSeconds(12.5), lines[0].Time);
        Assert.Equal("Première ligne", lines[0].Text);
        Assert.Equal(TimeSpan.FromSeconds(62), lines[1].Time);
    }

    [Fact]
    public void IgnoresLinesWithoutTime() => Assert.Empty(LyricsService.ParseSynced("Texte sans horodatage"));
}

public class SettingsNormalizationTests
{
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
        Assert.Equal("deezer", settings.PreferredPlayer);
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
