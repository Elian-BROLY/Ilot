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

    private sealed class FakeTransport(HttpStatusCode apiStatus, string apiBody) : HttpMessageHandler
    {
        public int ApiCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var isToken = request.RequestUri!.Host == "oauth2.googleapis.com";
            ApiCalls += isToken ? 0 : 1;
            return Task.FromResult(new HttpResponseMessage(isToken ? HttpStatusCode.OK : apiStatus)
            {
                Content = new StringContent(isToken ? "{\"access_token\":\"t\",\"expires_in\":3600}" : apiBody, Encoding.UTF8, "application/json"),
            });
        }
    }

    private MailService CreateService(HttpStatusCode apiStatus, string apiBody = "{}") => CreateService(new FakeTransport(apiStatus, apiBody));

    private MailService CreateService(FakeTransport transport)
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"), transport);
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

    [Fact]
    public async Task ExpiredTokenIsRetriedOnceBeforeAskingToReconnect()
    {
        var transport = new FakeTransport(HttpStatusCode.Unauthorized, "{}");
        await CreateService(transport).GetSummaryAsync(MailProvider.Gmail);
        Assert.Equal(2, transport.ApiCalls);
    }

    [Fact]
    public async Task RealForbiddenNeedsReconnection()
    {
        var body = """{"error":{"code":403,"errors":[{"reason":"insufficientPermissions"}]}}""";
        var summary = await CreateService(HttpStatusCode.Forbidden, body).GetSummaryAsync(MailProvider.Gmail);
        Assert.True(summary.NeedsReconnect);
    }

    [Fact]
    public async Task RateLimitIsTemporaryAndKeepsTheLastMails()
    {
        var body = """{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}]}}""";
        var last = new MailSummary(3, [new MailItem("1", null, "A", "B", null, true)], null);

        var summary = (await CreateService(HttpStatusCode.Forbidden, body).GetSummaryAsync(MailProvider.Gmail)).KeepingContentOf(last);

        Assert.False(summary.NeedsReconnect);
        Assert.NotNull(summary.Error);
        Assert.Equal(3, summary.Unread);
        Assert.Single(summary.Items);
    }

    // Répond comme Google : chaque jeton de rafraîchissement donne un jeton d'accès qui identifie son compte.
    private sealed class FakeGoogle(string newAccount) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body;
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                var form = await request.Content!.ReadAsStringAsync(cancellationToken);
                body = form.Contains("grant_type=authorization_code")
                    ? $"{{\"access_token\":\"{newAccount}\",\"expires_in\":3600,\"refresh_token\":\"r-new\"}}"
                    : "{\"access_token\":\"premier@exemple.fr\",\"expires_in\":3600}";
            }
            else
            {
                body = $"{{\"emailAddress\":\"{request.Headers.Authorization!.Parameter}\"}}";
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    // Joue le navigateur : suit la redirection vers l'écoute locale, comme après une connexion réussie.
    private static bool CompleteSignIn(string url)
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        var callback = $"{query["redirect_uri"]}?code=c&state={query["state"]}";
        _ = Task.Run(async () =>
        {
            using var browser = new HttpClient();
            await browser.GetStringAsync(callback);
        });
        return true;
    }

    private MailService CreateSecondAccountService(string account)
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"), new FakeGoogle(account)) { BrowserOpener = CompleteSignIn };
        service.Credentials.GmailClientId = "id";
        service.Credentials.GmailClientSecret = "secret";
        service.Credentials.GmailRefreshToken = "r";
        return service;
    }

    [Fact]
    public async Task SameGmailAccountCannotBeConnectedTwice()
    {
        var service = CreateSecondAccountService("premier@exemple.fr");
        var error = await service.ConnectAsync(MailProvider.Gmail2);
        Assert.Contains("déjà connecté", error);
        Assert.False(service.IsConnected(MailProvider.Gmail2));
        Assert.True(service.IsConnected(MailProvider.Gmail));
    }

    [Fact]
    public async Task DifferentGmailAccountIsConnected()
    {
        var service = CreateSecondAccountService("second@exemple.fr");
        Assert.Null(await service.ConnectAsync(MailProvider.Gmail2));
        Assert.True(service.IsConnected(MailProvider.Gmail2));
    }

    [Fact]
    public async Task SignInCanBeCancelled()
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"), new FakeTransport(HttpStatusCode.OK, "{}")) { BrowserOpener = _ => true };
        service.Credentials.OutlookClientId = "id";
        using var cancellation = new CancellationTokenSource();

        var connecting = service.ConnectAsync(MailProvider.Outlook, cancellation.Token);
        cancellation.Cancel();

        Assert.Equal("Connexion annulée.", await connecting.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(service.IsConnected(MailProvider.Outlook));
    }

    [Fact]
    public async Task UnavailableBrowserIsReported()
    {
        var service = new MailService(Path.Combine(_folder, "mail.dat"), new FakeTransport(HttpStatusCode.OK, "{}")) { BrowserOpener = _ => false };
        service.Credentials.OutlookClientId = "id";
        Assert.Contains("navigateur", await service.ConnectAsync(MailProvider.Outlook));
    }

    [Fact]
    public async Task RateLimitedTrashDoesNotAskToReconnect()
    {
        var body = """{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}]}}""";
        var error = await CreateService(HttpStatusCode.Forbidden, body).TrashAsync(MailProvider.Gmail, "1");
        Assert.Equal("Suppression impossible", error);
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
