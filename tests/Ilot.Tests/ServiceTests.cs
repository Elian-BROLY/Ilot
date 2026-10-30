using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Ilot.Services;
using Xunit;

namespace Ilot.Tests;

public class ChooseSourceTests
{
    private const string Deezer = "Deezer.62021768415AF_q7m17pa7q8kj0!Deezer.Music";
    private const string Spotify = "Spotify.exe";
    private const string Edge = "MSEdge";
    private static readonly string[] All = [Deezer, Spotify, Edge];

    [Fact]
    public void PreferredWinsOverWindowsChoice() =>
        Assert.Equal(Deezer, MediaService.ChooseSource(All, null, Spotify, "deezer"));

    [Fact]
    public void ManualChoiceWinsOverPreferred() =>
        Assert.Equal(Edge, MediaService.ChooseSource(All, Edge, Spotify, "deezer"));

    [Fact]
    public void ClosedManualChoiceFallsBackToPreferred() =>
        Assert.Equal(Deezer, MediaService.ChooseSource([Deezer, Edge], Spotify, Edge, "deezer"));

    [Fact]
    public void WithoutPreferredFollowsWindows() =>
        Assert.Equal(Edge, MediaService.ChooseSource([Spotify, Edge], null, Edge, "deezer"));

    [Fact]
    public void EmptyPreferenceFollowsWindows() =>
        Assert.Equal(Spotify, MediaService.ChooseSource(All, null, Spotify, string.Empty));

    [Fact]
    public void NoSessionGivesNothing() =>
        Assert.Null(MediaService.ChooseSource([], null, null, "deezer"));

    [Fact]
    public void PausedPreferredStaysWhenOptionIsOff() =>
        Assert.Equal(Deezer, MediaService.ChooseSource(All, null, Edge, "deezer"));

    [Fact]
    public void PausedPreferredYieldsToPlayingSourceWhenOptionIsOn() =>
        Assert.Equal(Edge, MediaService.ChooseSource(All, null, Spotify, "deezer", [Edge]));

    [Fact]
    public void PlayingPreferredWinsWhenSeveralPlay() =>
        Assert.Equal(Deezer, MediaService.ChooseSource(All, null, Edge, "deezer", [Deezer, Edge]));

    [Fact]
    public void NothingPlayingKeepsPreferred() =>
        Assert.Equal(Deezer, MediaService.ChooseSource(All, null, Edge, "deezer", []));

    [Theory]
    [InlineData(Deezer, "Deezer")]
    [InlineData(Spotify, "Spotify")]
    [InlineData(Edge, "Edge")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("foobar2000.exe", "foobar2000")]
    public void NamesAreReadable(string id, string expected) =>
        Assert.Equal(expected, MediaService.GetSourceName(id));
}

public class TrackChangeDetectorTests
{
    [Fact]
    public void FirstTrackIsNotAnnounced() =>
        Assert.False(new TrackChangeDetector().Update("deezer", "A", "Un"));

    [Fact]
    public void NextTrackIsAnnounced()
    {
        var detector = new TrackChangeDetector();
        detector.Update("deezer", "A", "Un");
        Assert.True(detector.Update("deezer", "B", "Deux"));
    }

    [Fact]
    public void SameTrackIsNotAnnounced()
    {
        var detector = new TrackChangeDetector();
        detector.Update("deezer", "A", "Un");
        Assert.False(detector.Update("deezer", "A", "Un"));
    }

    [Fact]
    public void SwitchingPlayerIsNotANewTrack()
    {
        var detector = new TrackChangeDetector();
        detector.Update("deezer", "A", "Un");
        Assert.False(detector.Update("spotify", "B", "Deux"));
        Assert.False(detector.Update("deezer", "A", "Un"));
    }

    // Comportement réel de Deezer : sa session disparaît environ 1,5 s à chaque changement de morceau.
    [Fact]
    public void SessionClosingBetweenTracksIsStillANewTrack()
    {
        var detector = new TrackChangeDetector();
        detector.Update("deezer", "A", "Un");
        Assert.False(detector.Update(null, string.Empty, string.Empty));
        Assert.True(detector.Update("deezer", "B", "Deux"));
    }

    [Fact]
    public void SessionClosingWithoutChangeIsNotAnnounced()
    {
        var detector = new TrackChangeDetector();
        detector.Update("deezer", "A", "Un");
        detector.Update(null, string.Empty, string.Empty);
        Assert.False(detector.Update("deezer", "A", "Un"));
    }

    [Fact]
    public void EmptyTitleBetweenTracksIsIgnored()
    {
        var detector = new TrackChangeDetector();
        detector.Update("deezer", "A", "Un");
        Assert.False(detector.Update("deezer", string.Empty, string.Empty));
        Assert.False(detector.Update("deezer", "A", "Un"));
        Assert.True(detector.Update("deezer", "B", "Deux"));
    }
}

public class BatteryAlertPolicyTests
{
    [Fact]
    public void AlertsOncePerThreshold()
    {
        var policy = new BatteryAlertPolicy(20, 10);
        Assert.Null(policy.Update(50, false));
        Assert.Equal(20, policy.Update(20, false));
        Assert.Null(policy.Update(19, false));
        Assert.Equal(10, policy.Update(10, false));
    }

    [Fact]
    public void LowestWinsWhenSeveralAreCrossed() =>
        Assert.Equal(10, new BatteryAlertPolicy(20, 10).Update(5, false));

    [Fact]
    public void ChargingRearms()
    {
        var policy = new BatteryAlertPolicy(20);
        Assert.Equal(20, policy.Update(15, false));
        Assert.Null(policy.Update(15, true));
        Assert.Equal(20, policy.Update(15, false));
    }
}

public class NewMailDetectorTests
{
    private static MailItem Mail(string id) => new(id, null, null, null, null, false);

    [Fact]
    public void FirstReadAnnouncesNothing() =>
        Assert.Empty(new NewMailDetector().Update(MailProvider.Gmail, [Mail("a")]));

    [Fact]
    public void NewMailOnTopIsAnnounced()
    {
        var detector = new NewMailDetector();
        detector.Update(MailProvider.Gmail, [Mail("a")]);
        Assert.Equal("b", Assert.Single(detector.Update(MailProvider.Gmail, [Mail("b"), Mail("a")])).Id);
    }

    [Fact]
    public void OldMailMovingUpAfterDeletionIsNotAnnounced()
    {
        var detector = new NewMailDetector();
        detector.Update(MailProvider.Gmail, [Mail("a"), Mail("b")]);
        Assert.Empty(detector.Update(MailProvider.Gmail, [Mail("b"), Mail("c")]));
    }

    [Fact]
    public void ResetForgetsTheAccount()
    {
        var detector = new NewMailDetector();
        detector.Update(MailProvider.Gmail, [Mail("a")]);
        detector.Reset(MailProvider.Gmail);
        Assert.Empty(detector.Update(MailProvider.Gmail, [Mail("b")]));
    }
}

public class MailSummaryTests
{
    private static readonly MailItem Item = new("a", null, "Jeanne", "Sujet", "https://example.org", true);

    [Fact]
    public void NetworkFailureKeepsLastMails()
    {
        var last = new MailSummary(3, [Item], null);
        var merged = new MailSummary(0, [], "Lecture impossible").KeepingContentOf(last);
        Assert.Equal(3, merged.Unread);
        Assert.Single(merged.Items);
        Assert.Equal("Lecture impossible", merged.Error);
    }

    [Fact]
    public void RepeatedFailuresKeepLastMails()
    {
        var first = new MailSummary(0, [], "Erreur").KeepingContentOf(new MailSummary(3, [Item], null));
        Assert.Equal(3, new MailSummary(0, [], "Erreur").KeepingContentOf(first).Unread);
    }

    [Fact]
    public void LostAccessClearsMails()
    {
        var merged = new MailSummary(0, [], "Reconnexion", NeedsReconnect: true).KeepingContentOf(new MailSummary(3, [Item], null));
        Assert.Equal(0, merged.Unread);
        Assert.Empty(merged.Items);
    }

    [Fact]
    public void SuccessReplacesEverything()
    {
        var merged = new MailSummary(0, [], null).KeepingContentOf(new MailSummary(3, [Item], null));
        Assert.Equal(0, merged.Unread);
        Assert.Null(merged.Error);
    }

    [Fact]
    public void FailureWithoutHistoryIsKept()
    {
        var merged = new MailSummary(0, [], "Erreur").KeepingContentOf(null);
        Assert.Equal("Erreur", merged.Error);
    }
}

public sealed class MailServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ilot-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FakeTransport(HttpStatusCode apiStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isToken = request.RequestUri!.Host == "oauth2.googleapis.com";
            return Task.FromResult(new HttpResponseMessage(isToken ? HttpStatusCode.OK : apiStatus)
            {
                Content = new StringContent(isToken ? "{\"access_token\":\"t\",\"expires_in\":3600}" : "{}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private MailService CreateService(HttpStatusCode apiStatus)
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"), new FakeTransport(apiStatus));
        service.Credentials.GmailRefreshToken = "r";
        return service;
    }

    [Fact]
    public async Task ServerErrorIsTemporary()
    {
        var summary = await CreateService(HttpStatusCode.InternalServerError).GetSummaryAsync(MailProvider.Gmail);
        Assert.NotNull(summary.Error);
        Assert.False(summary.NeedsReconnect);
    }

    [Fact]
    public async Task RefusedAccessNeedsReconnection()
    {
        var summary = await CreateService(HttpStatusCode.Unauthorized).GetSummaryAsync(MailProvider.Gmail);
        Assert.True(summary.NeedsReconnect);
    }
}

public sealed class FileTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("ilot-tests").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AtomicWriteReplacesContentAndLeavesNoTemporaryFile()
    {
        var path = Path.Combine(_folder, "sub", "note.txt");
        AtomicFile.WriteAllText(path, "avant");
        AtomicFile.WriteAllText(path, "après");
        Assert.Equal("après", File.ReadAllText(path));
        Assert.Equal(["note.txt"], Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void LogRotatesAtMaximumSize()
    {
        var original = AppLog.LogPath;
        AppLog.LogPath = Path.Combine(_folder, "app.log");
        try
        {
            File.WriteAllText(AppLog.LogPath, new string('x', 300 * 1024));
            AppLog.Write("message");
            Assert.True(File.Exists(Path.Combine(_folder, "app.old.log")));
            Assert.Contains("message", File.ReadAllText(AppLog.LogPath));
        }
        finally
        {
            AppLog.LogPath = original;
        }
    }
}

public class LyricsCacheTests
{
    [Fact]
    public void RemembersMissingLyrics()
    {
        var cache = new LyricsCache(2);
        cache.Add("a", null);
        Assert.True(cache.TryGet("a", out var lyrics));
        Assert.Null(lyrics);
    }

    [Fact]
    public void OldestLeavesFirst()
    {
        var cache = new LyricsCache(2);
        cache.Add("a", null);
        cache.Add("b", null);
        cache.Add("c", null);
        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }
}
