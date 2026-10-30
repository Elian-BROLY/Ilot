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
