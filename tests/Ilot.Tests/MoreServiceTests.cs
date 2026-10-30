using System.IO;
using System.Net;
using System.Net.NetworkInformation;
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
