using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ilot.Services;

internal enum MailProvider
{
    Gmail,
    Outlook,
    Gmail2,
    Imap,
}

internal static class MailProviderExtensions
{
    public static bool IsGmail(this MailProvider provider) => provider is MailProvider.Gmail or MailProvider.Gmail2;

    public static string Label(this MailProvider provider) => provider switch
    {
        MailProvider.Gmail => "Gmail",
        MailProvider.Gmail2 => "Gmail 2",
        MailProvider.Imap => "Mail",
        _ => "Outlook",
    };
}

internal sealed class MailCredentials
{
    public string? OutlookClientId { get; set; }
    public string? OutlookRefreshToken { get; set; }
    public string? GmailClientId { get; set; }
    public string? GmailClientSecret { get; set; }
    public string? GmailRefreshToken { get; set; }
    /// <summary>Second compte Gmail : il passe par le même client OAuth que le premier.</summary>
    public string? Gmail2RefreshToken { get; set; }
    /// <summary>Compte IMAP : pour les services qui acceptent un mot de passe d'application, sans projet Google ou Azure.</summary>
    public string? ImapHost { get; set; }
    public string? ImapAddress { get; set; }
    public string? ImapPassword { get; set; }
}

/// <summary>IsLoaded est faux si seul l'identifiant est connu : Gmail exige une requête par message pour le reste.</summary>
internal sealed record MailItem(string Id, string? ThreadId, string? Sender, string? Subject, string? OpenUrl, bool IsLoaded);

/// <summary>
/// Items : les derniers non lus, du plus récent au plus ancien. Error est null si la lecture a réussi.
/// NeedsReconnect : l'accès est perdu, contrairement à une simple coupure réseau.
/// </summary>
internal sealed record MailSummary(int Unread, IReadOnlyList<MailItem> Items, string? Error, bool NeedsReconnect = false)
{
    /// <summary>Après une lecture ratée, garde les derniers mails et le dernier compteur : une coupure réseau ne vide pas la boîte.</summary>
    public MailSummary KeepingContentOf(MailSummary? last) =>
        Error is null || NeedsReconnect || last is null ? this : last with { Error = Error };
}

/// <summary>
/// Accès à Gmail et Outlook via OAuth 2 (PKCE et redirection locale). L'application n'utilise que les compteurs,
/// l'expéditeur et le sujet, et ne met un message à la corbeille que sur demande. La mise à la corbeille impose
/// toutefois des portées plus larges que ces seuls usages : les deux services n'en proposent pas de plus étroites.
/// </summary>
internal sealed class MailService
{
    private const string OutlookScope = "offline_access Mail.ReadWrite";
    private const string GmailScope = "https://www.googleapis.com/auth/gmail.modify";
    private const string GmailApi = "https://gmail.googleapis.com/gmail/v1/users/me";
    private const string GraphApi = "https://graph.microsoft.com/v1.0/me";
    private const string GoogleCalendarScope = "https://www.googleapis.com/auth/calendar.readonly";
    private const string OutlookCalendarScope = "Calendars.Read";
    private const string GoogleCalendarApi = "https://www.googleapis.com/calendar/v3/calendars/primary/events";
    private const string OutlookAuthorizeUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
    private const string OutlookTokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    private const string GmailAuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string GmailTokenUrl = "https://oauth2.googleapis.com/token";
    public const int MaxItems = 20;
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(3);

    private readonly HttpClient _http;
    private readonly string _path;
    private readonly Dictionary<MailProvider, (string Token, DateTime ExpiresUtc)> _accessTokens = [];
    private readonly Dictionary<MailProvider, Dictionary<string, MailItem>> _gmailItems = new() { [MailProvider.Gmail] = [], [MailProvider.Gmail2] = [] };
    private readonly Dictionary<MailProvider, string?> _gmailAddresses = [];

    /// <param name="dataPath">Fichier chiffré des identifiants. Par défaut, %APPDATA%\Ilot\mail.dat.</param>
    /// <param name="handler">Transport HTTP de remplacement, pour tester sans toucher à une vraie boîte mail.</param>
    public MailService(string? dataPath = null, HttpMessageHandler? handler = null)
    {
        _path = dataPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ilot", "mail.dat");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(15);
        Credentials = Load();
    }

    /// <summary>Remplaçable pour tester sans ouvrir de navigateur.</summary>
    internal Func<string, bool> BrowserOpener { get; set; } = StartBrowser;

    /// <summary>Remplaçable pour tester sans serveur IMAP.</summary>
    internal Func<string, string, string, CancellationToken, Task<ImapClient>> ImapConnector { get; set; } = ImapClient.ConnectAsync;

    /// <summary>Vrai : une nouvelle connexion demande aussi l'accès en lecture à l'agenda. Faux si la page Agenda est masquée.</summary>
    public bool RequestCalendarAccess { get; set; }

    /// <summary>Levé quand un compte est connecté ou déconnecté.</summary>
    public event Action? AccountsChanged;

    public MailCredentials Credentials { get; }

    public bool IsConnected(MailProvider provider) => provider == MailProvider.Imap
        ? !string.IsNullOrEmpty(Credentials.ImapPassword)
        : GetRefreshToken(provider) is not null;

    /// <summary>Vérifie les identifiants IMAP puis les garde. Renvoie null en cas de succès, sinon un message d'erreur.</summary>
    public async Task<string?> ConnectImapAsync(string address, string password, string host, CancellationToken cancellationToken = default)
    {
        var server = string.IsNullOrWhiteSpace(host) ? ImapClient.GuessHost(address) : host.Trim();
        // Un mot de passe collé emporte souvent un espace ou un retour à la ligne, que le serveur refuserait.
        password = password.Trim();
        if (string.IsNullOrWhiteSpace(address) || string.IsNullOrEmpty(password))
        {
            return Loc.T("Indiquez l'adresse et le mot de passe d'application.");
        }
        if (server is null)
        {
            return Loc.T("Serveur inconnu : indiquez-le (imap.exemple.fr).");
        }
        // Même délai que la relecture : un serveur qui accepte la connexion puis se tait ne doit pas bloquer les réglages.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_http.Timeout);
        try
        {
            using var client = await ImapConnector(server, address.Trim(), password, timeout.Token);
        }
        catch (ImapException ex)
        {
            return Loc.T(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or SocketException or System.Security.Authentication.AuthenticationException or OperationCanceledException)
        {
            return Loc.T("Serveur injoignable. Vérifiez son nom et le réseau.");
        }

        Credentials.ImapHost = server;
        Credentials.ImapAddress = address.Trim();
        Credentials.ImapPassword = password;
        Save();
        AccountsChanged?.Invoke();
        return null;
    }

    /// <summary>Les rendez-vous d'aujourd'hui et de demain pour ce compte. IMAP n'a pas d'agenda.</summary>
    public async Task<CalendarResult> GetCalendarAsync(MailProvider provider, DateTime nowUtc)
    {
        if (provider == MailProvider.Imap)
        {
            return new CalendarResult([]);
        }
        try
        {
            var token = await GetAccessTokenAsync(provider);
            if (token is null)
            {
                return new CalendarResult([], NeedsPermission: true);
            }
            var today = nowUtc.ToLocalTime().Date;
            var from = Uri.EscapeDataString(today.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            var to = Uri.EscapeDataString(today.AddDays(2).ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            if (provider.IsGmail())
            {
                var json = await GetBodyAsync($"{GoogleCalendarApi}?singleEvents=true&orderBy=startTime&maxResults=10&timeMin={from}&timeMax={to}", token);
                return new CalendarResult(CalendarEvents.ParseGoogle(json));
            }
            var graph = await GetBodyAsync($"{GraphApi}/calendarView?startDateTime={from}&endDateTime={to}&$top=10&$orderby=start/dateTime&$select=id,subject,start,isAllDay,onlineMeeting,webLink", token, utcTimes: true);
            return new CalendarResult(CalendarEvents.ParseGraph(graph));
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return new CalendarResult([], NeedsPermission: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new CalendarResult([], HasFailed: true);
        }
    }

    private async Task<ImapClient> OpenImapAsync(CancellationToken cancellationToken) =>
        await ImapConnector(Credentials.ImapHost ?? string.Empty, Credentials.ImapAddress ?? string.Empty, Credentials.ImapPassword ?? string.Empty, cancellationToken);

    private async Task<MailSummary> GetImapSummaryAsync()
    {
        using var timeout = new CancellationTokenSource(_http.Timeout);
        try
        {
            using var client = await OpenImapAsync(timeout.Token);
            var summary = await client.ReadSummaryAsync(MaxItems, timeout.Token);
            var webmail = ImapClient.GuessWebmail(Credentials.ImapAddress);
            return summary with { Items = summary.Items.Select(item => item with { OpenUrl = webmail }).ToList() };
        }
        catch (ImapException ex) when (ex.Message == ImapClient.LoginRefused)
        {
            return new MailSummary(0, [], Loc.T("Accès refusé : reconnectez le compte"), NeedsReconnect: true);
        }
        catch (Exception ex) when (ex is ImapException or IOException or SocketException or System.Security.Authentication.AuthenticationException or OperationCanceledException or FormatException)
        {
            AppLog.Write(ex);
            return new MailSummary(0, [], Loc.T("Lecture impossible pour le moment"));
        }
    }

    private async Task<string?> RunImapAsync(Func<ImapClient, CancellationToken, Task> action, string failure)
    {
        using var timeout = new CancellationTokenSource(_http.Timeout);
        try
        {
            using var client = await OpenImapAsync(timeout.Token);
            await action(client, timeout.Token);
            return null;
        }
        catch (Exception ex) when (ex is ImapException or IOException or SocketException or System.Security.Authentication.AuthenticationException or OperationCanceledException)
        {
            AppLog.Write(ex);
            return failure;
        }
    }

    public void SaveClientCredentials(string outlookId, string gmailId, string gmailSecret)
    {
        Credentials.OutlookClientId = outlookId;
        Credentials.GmailClientId = gmailId;
        Credentials.GmailClientSecret = gmailSecret;
        Save();
    }

    public void Disconnect(MailProvider provider)
    {
        if (provider == MailProvider.Imap)
        {
            Credentials.ImapPassword = null;
            Save();
            AccountsChanged?.Invoke();
            return;
        }
        SetRefreshToken(provider, null);
        _accessTokens.Remove(provider);
        if (provider.IsGmail())
        {
            _gmailAddresses.Remove(provider);
            _gmailItems[provider].Clear();
        }
        Save();
        AccountsChanged?.Invoke();
    }

    /// <summary>Met le message à la corbeille. Renvoie null en cas de succès, sinon un message d'erreur.</summary>
    public async Task<string?> TrashAsync(MailProvider provider, string messageId)
    {
        if (provider == MailProvider.Imap)
        {
            return await RunImapAsync((client, token) => client.MoveToTrashAsync(messageId, token), Loc.T("Suppression impossible"));
        }
        try
        {
            var token = await GetAccessTokenAsync(provider);
            if (token is null)
            {
                return Loc.T("Reconnexion nécessaire (Réglages)");
            }

            var id = Uri.EscapeDataString(messageId);
            using var request = provider.IsGmail()
                ? new HttpRequestMessage(HttpMethod.Post, $"{GmailApi}/messages/{id}/trash") { Content = new StringContent(string.Empty) }
                : new HttpRequestMessage(HttpMethod.Post, $"{GraphApi}/messages/{id}/move")
                {
                    Content = new StringContent("{\"destinationId\":\"deleteditems\"}", Encoding.UTF8, "application/json"),
                };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request);
            if (await IsAccessDeniedAsync(provider, response))
            {
                return Loc.T("Reconnectez le compte pour autoriser la suppression");
            }
            return response.IsSuccessStatusCode ? null : Loc.T("Suppression impossible");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Loc.T("Suppression impossible pour le moment");
        }
    }

    /// <summary>Marque le message comme lu. Renvoie null en cas de succès, sinon un message d'erreur.</summary>
    public async Task<string?> MarkAsReadAsync(MailProvider provider, string messageId)
    {
        if (provider == MailProvider.Imap)
        {
            return await RunImapAsync((client, token) => client.MarkAsReadAsync(messageId, token), Loc.T("Action impossible"));
        }
        try
        {
            var token = await GetAccessTokenAsync(provider);
            if (token is null)
            {
                return Loc.T("Reconnexion nécessaire (Réglages)");
            }

            var id = Uri.EscapeDataString(messageId);
            using var request = provider.IsGmail()
                ? new HttpRequestMessage(HttpMethod.Post, $"{GmailApi}/messages/{id}/modify")
                {
                    Content = new StringContent("{\"removeLabelIds\":[\"UNREAD\"]}", Encoding.UTF8, "application/json"),
                }
                : new HttpRequestMessage(HttpMethod.Patch, $"{GraphApi}/messages/{id}")
                {
                    Content = new StringContent("{\"isRead\":true}", Encoding.UTF8, "application/json"),
                };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request);
            if (await IsAccessDeniedAsync(provider, response))
            {
                return Loc.T("Reconnectez le compte pour autoriser cette action");
            }
            return response.IsSuccessStatusCode ? null : Loc.T("Action impossible");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Loc.T("Action impossible pour le moment");
        }
    }

    /// <summary>Ouvre le message dans le navigateur. Renvoie false en cas d'échec.</summary>
    public bool OpenInBrowser(string url) => BrowserOpener(url);

    /// <summary>Complète un message dont seul l'identifiant est connu. Renvoie le message inchangé en cas d'échec.</summary>
    public async Task<MailItem> LoadItemAsync(MailProvider provider, MailItem item)
    {
        if (item.IsLoaded || !provider.IsGmail())
        {
            return item;
        }
        try
        {
            var token = await GetAccessTokenAsync(provider);
            return token is null ? item : await LoadGmailItemAsync(provider, token, item);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return item;
        }
    }

    // Seules les adresses https sont acceptées : l'adresse vient d'une réponse réseau.
    private static bool StartBrowser(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Ouvre la page de connexion du service. Renvoie null en cas de succès, sinon un message d'erreur.</summary>
    public async Task<string?> ConnectAsync(MailProvider provider, CancellationToken cancellationToken = default)
    {
        var clientId = provider.IsGmail() ? Credentials.GmailClientId : Credentials.OutlookClientId;
        if (string.IsNullOrEmpty(clientId))
        {
            return Loc.T("Indiquez d'abord l'identifiant.");
        }
        if (provider.IsGmail() && string.IsNullOrEmpty(Credentials.GmailClientSecret))
        {
            return Loc.T("Indiquez aussi le code secret.");
        }

        try
        {
            var error = await SignInAsync(provider, clientId, cancellationToken);
            if (error is null && await IsSameAsOtherGmailAccountAsync(provider))
            {
                SetRefreshToken(provider, null);
                _accessTokens.Remove(provider);
                _gmailAddresses.Remove(provider);
                Save();
                return Loc.T("Ce compte Gmail est déjà connecté. Choisissez-en un autre.");
            }
            if (error is null)
            {
                AccountsChanged?.Invoke();
            }
            return error;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or HttpListenerException or InvalidOperationException or Win32Exception)
        {
            return Loc.T("Connexion impossible. Vérifiez l'identifiant et le réseau.");
        }
    }

    public async Task<MailSummary> GetSummaryAsync(MailProvider provider)
    {
        if (provider == MailProvider.Imap)
        {
            return await GetImapSummaryAsync();
        }
        try
        {
            try
            {
                return await ReadSummaryAsync(provider);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Le jeton en mémoire a pu être invalidé plus tôt que prévu : un nouveau jeton tranche.
                _accessTokens.Remove(provider);
                return await ReadSummaryAsync(provider);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _accessTokens.Remove(provider);
            return new MailSummary(0, [], Loc.T("Accès refusé : reconnectez le compte"), NeedsReconnect: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            AppLog.Write(ex);
            return new MailSummary(0, [], Loc.T("Lecture impossible pour le moment"));
        }
    }

    // Le même compte deux fois doublerait le compteur et les bannières. En cas de doute (réseau), la connexion est gardée.
    private async Task<bool> IsSameAsOtherGmailAccountAsync(MailProvider provider)
    {
        var other = provider == MailProvider.Gmail ? MailProvider.Gmail2 : MailProvider.Gmail;
        if (!provider.IsGmail() || !IsConnected(other))
        {
            return false;
        }
        try
        {
            var address = await GetGmailAddressAsync(provider, await GetAccessTokenAsync(provider));
            var otherAddress = await GetGmailAddressAsync(other, await GetAccessTokenAsync(other));
            return address is not null && string.Equals(address, otherAddress, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }

    private async Task<string?> GetGmailAddressAsync(MailProvider provider, string? token)
    {
        if (!_gmailAddresses.TryGetValue(provider, out var address) && token is not null)
        {
            using var profile = await GetJsonAsync($"{GmailApi}/profile", token);
            address = profile.RootElement.GetProperty("emailAddress").GetString();
            _gmailAddresses[provider] = address;
        }
        return address;
    }

    private async Task<MailSummary> ReadSummaryAsync(MailProvider provider)
    {
        var token = await GetAccessTokenAsync(provider);
        if (token is null)
        {
            return new MailSummary(0, [], Loc.T("Reconnexion nécessaire (Réglages)"), NeedsReconnect: true);
        }
        return provider.IsGmail() ? await GetGmailSummaryAsync(provider, token) : await GetOutlookSummaryAsync(token);
    }

    /// <summary>Google répond aussi 403 quand une limite de débit est atteinte : là, il suffit de patienter.</summary>
    internal static bool IsRateLimitBody(string body) =>
        body.Contains("RateLimitExceeded", StringComparison.OrdinalIgnoreCase)
        || body.Contains("quotaExceeded", StringComparison.OrdinalIgnoreCase)
        || body.Contains("dailyLimitExceeded", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsAccessDeniedAsync(MailProvider provider, HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _accessTokens.Remove(provider); // Le prochain essai demandera un nouveau jeton.
            return true;
        }
        return response.StatusCode == HttpStatusCode.Forbidden && !IsRateLimitBody(await response.Content.ReadAsStringAsync());
    }

    private async Task<string?> SignInAsync(MailProvider provider, string clientId, CancellationToken cancellationToken)
    {
        var port = FindFreePort();
        var redirectUri = $"http://localhost:{port}/";
        var state = CreateRandomString();
        var verifier = CreateRandomString();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var parameters = $"client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&state={state}&code_challenge={challenge}&code_challenge_method=S256";
        var outlookScope = RequestCalendarAccess ? $"{OutlookScope} {OutlookCalendarScope}" : OutlookScope;
        var gmailScope = RequestCalendarAccess ? $"{GmailScope} {GoogleCalendarScope}" : GmailScope;
        var url = provider.IsGmail()
            ? $"{GmailAuthorizeUrl}?{parameters}&scope={Uri.EscapeDataString(gmailScope)}&access_type=offline&prompt=select_account%20consent"
            : $"{OutlookAuthorizeUrl}?{parameters}&scope={Uri.EscapeDataString(outlookScope)}&prompt=select_account";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        var pending = listener.GetContextAsync();
        // Sans réponse, la fermeture de l'écoute fera échouer cette attente : ce n'est pas une erreur à journaliser.
        _ = pending.ContinueWith(task => task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        if (!BrowserOpener(url))
        {
            return Loc.T("Ouverture du navigateur impossible.");
        }

        if (await Task.WhenAny(pending, Task.Delay(SignInTimeout, cancellationToken)) != pending)
        {
            return cancellationToken.IsCancellationRequested ? Loc.T("Connexion annulée.") : Loc.T("Délai dépassé : connexion annulée.");
        }

        var context = await pending;
        var query = context.Request.QueryString;
        var isValid = query["state"] == state && !string.IsNullOrEmpty(query["code"]);
        await RespondAsync(context, isValid);
        if (!isValid)
        {
            return Loc.T("Connexion refusée ou annulée.");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = query["code"]!,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = verifier,
        };
        if (provider.IsGmail())
        {
            form["client_secret"] = Credentials.GmailClientSecret!;
        }
        else
        {
            form["scope"] = outlookScope;
        }

        using var response = await _http.PostAsync(TokenUrl(provider), new FormUrlEncodedContent(form));
        if (!response.IsSuccessStatusCode)
        {
            return Loc.T("Le service a refusé la connexion. Vérifiez les identifiants.");
        }
        StoreTokens(provider, await response.Content.ReadAsStringAsync());
        return IsConnected(provider) ? null : Loc.T("Le service n'a pas accordé d'accès durable.");
    }

    private static async Task RespondAsync(HttpListenerContext context, bool isValid)
    {
        var message = isValid ? Loc.T("Connexion réussie. Vous pouvez fermer cet onglet.") : Loc.T("Connexion annulée. Vous pouvez fermer cet onglet.");
        var body = Encoding.UTF8.GetBytes($"<!doctype html><html lang=\"fr\"><meta charset=\"utf-8\"><title>Ilot</title><body style=\"font-family:sans-serif;text-align:center;margin-top:20vh\"><h2>{message}</h2></body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        await context.Response.OutputStream.WriteAsync(body);
        context.Response.Close();
    }

    private async Task<string?> GetAccessTokenAsync(MailProvider provider)
    {
        if (_accessTokens.TryGetValue(provider, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
        {
            return cached.Token;
        }
        if (GetRefreshToken(provider) is not { } refreshToken)
        {
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = (provider.IsGmail() ? Credentials.GmailClientId : Credentials.OutlookClientId) ?? string.Empty,
        };
        if (provider.IsGmail())
        {
            form["client_secret"] = Credentials.GmailClientSecret ?? string.Empty;
        }
        // Pas de portée pour Outlook : sans elle, il redonne celles du compte, avec ou sans agenda selon sa connexion.

        using var response = await _http.PostAsync(TokenUrl(provider), new FormUrlEncodedContent(form));
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            return null; // Autorisation expirée ou révoquée : cas normal avec Gmail en mode test.
        }
        response.EnsureSuccessStatusCode();
        StoreTokens(provider, await response.Content.ReadAsStringAsync());
        return _accessTokens[provider].Token;
    }

    private void StoreTokens(MailProvider provider, string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var lifetime = root.GetProperty("expires_in").GetInt32();
        _accessTokens[provider] = (root.GetProperty("access_token").GetString()!, DateTime.UtcNow.AddSeconds(lifetime - 60));
        if (root.TryGetProperty("refresh_token", out var refreshToken))
        {
            SetRefreshToken(provider, refreshToken.GetString());
            Save();
        }
    }

    private async Task<MailSummary> GetGmailSummaryAsync(MailProvider provider, string token)
    {
        using var inbox = await GetJsonAsync($"{GmailApi}/labels/INBOX", token);
        var unread = inbox.RootElement.GetProperty("messagesUnread").GetInt32();

        // Filtre par libellés : fonctionne aussi avec un compte encore connecté avec l'ancienne portée « metadata ».
        using var list = await GetJsonAsync($"{GmailApi}/messages?labelIds=INBOX&labelIds=UNREAD&maxResults={MaxItems}", token);
        var items = new List<MailItem>();
        if (list.RootElement.TryGetProperty("messages", out var messages))
        {
            foreach (var message in messages.EnumerateArray())
            {
                var id = message.GetProperty("id").GetString()!;
                // Une lecture par message serait trop coûteuse : le reste est chargé à l'affichage, puis conservé.
                items.Add(_gmailItems[provider].TryGetValue(id, out var known)
                    ? known
                    : new MailItem(id, message.GetProperty("threadId").GetString(), null, null, null, false));
            }
        }

        foreach (var staleId in _gmailItems[provider].Keys.Except(items.Select(item => item.Id)).ToList())
        {
            _gmailItems[provider].Remove(staleId);
        }
        if (items.Count > 0)
        {
            items[0] = await LoadGmailItemAsync(provider, token, items[0]);
        }
        return new MailSummary(unread, items, null);
    }

    private async Task<MailItem> LoadGmailItemAsync(MailProvider provider, string token, MailItem item)
    {
        if (item.IsLoaded)
        {
            return item;
        }

        using var message = await GetJsonAsync($"{GmailApi}/messages/{item.Id}?format=metadata&metadataHeaders=From&metadataHeaders=Subject", token);
        string? sender = null;
        string? subject = null;
        foreach (var header in message.RootElement.GetProperty("payload").GetProperty("headers").EnumerateArray())
        {
            var value = header.GetProperty("value").GetString();
            if (string.Equals(header.GetProperty("name").GetString(), "From", StringComparison.OrdinalIgnoreCase))
            {
                sender = MailHeaders.CleanSender(value);
            }
            else if (string.Equals(header.GetProperty("name").GetString(), "Subject", StringComparison.OrdinalIgnoreCase))
            {
                subject = value;
            }
        }

        // L'adresse dans le lien ouvre le bon compte quand plusieurs sont connectés dans le navigateur.
        var address = await GetGmailAddressAsync(provider, token);

        // Gmail ouvre des conversations : l'identifiant d'un message n'est valable que s'il est le premier de la sienne.
        var openUrl = $"https://mail.google.com/mail/?authuser={Uri.EscapeDataString(address ?? string.Empty)}#all/{item.ThreadId}";
        var loaded = item with { Sender = sender, Subject = subject, OpenUrl = openUrl, IsLoaded = true };
        _gmailItems[provider][item.Id] = loaded;
        return loaded;
    }

    private async Task<MailSummary> GetOutlookSummaryAsync(string token)
    {
        const string Base = GraphApi + "/mailFolders/inbox";
        using var inbox = await GetJsonAsync($"{Base}?$select=unreadItemCount", token);
        var unread = inbox.RootElement.GetProperty("unreadItemCount").GetInt32();

        // Sans $orderby : Graph trie déjà du plus récent au plus ancien, et refuse ce filtre combiné à un tri.
        using var list = await GetJsonAsync($"{Base}/messages?$filter=isRead%20eq%20false&$top={MaxItems}&$select=subject,from,webLink", token);
        var items = new List<MailItem>();
        foreach (var message in list.RootElement.GetProperty("value").EnumerateArray())
        {
            string? sender = null;
            if (message.TryGetProperty("from", out var from) && from.TryGetProperty("emailAddress", out var address))
            {
                sender = address.GetProperty("name").GetString();
            }
            items.Add(new MailItem(message.GetProperty("id").GetString()!, null, sender,
                message.GetProperty("subject").GetString(), message.GetProperty("webLink").GetString(), true));
        }
        return new MailSummary(unread, items, null);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string token) => JsonDocument.Parse(await GetBodyAsync(url, token));

    private async Task<string> GetBodyAsync(string url, string token, bool utcTimes = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (utcTimes)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
        }
        using var response = await _http.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.Forbidden && IsRateLimitBody(body))
        {
            throw new HttpRequestException("Limite de débit atteinte", null, HttpStatusCode.TooManyRequests);
        }
        response.EnsureSuccessStatusCode();
        return body;
    }

    private static string TokenUrl(MailProvider provider) => provider.IsGmail() ? GmailTokenUrl : OutlookTokenUrl;

    private string? GetRefreshToken(MailProvider provider) => provider switch
    {
        MailProvider.Gmail => Credentials.GmailRefreshToken,
        MailProvider.Gmail2 => Credentials.Gmail2RefreshToken,
        _ => Credentials.OutlookRefreshToken,
    };

    private void SetRefreshToken(MailProvider provider, string? token)
    {
        switch (provider)
        {
            case MailProvider.Gmail:
                Credentials.GmailRefreshToken = token;
                break;
            case MailProvider.Gmail2:
                Credentials.Gmail2RefreshToken = token;
                break;
            default:
                Credentials.OutlookRefreshToken = token;
                break;
        }
    }

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static string CreateRandomString() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // Chiffré avec DPAPI : seule cette session Windows peut relire le fichier.
    private MailCredentials Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<MailCredentials>(bytes) ?? new MailCredentials();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException)
        {
            // Fichier illisible : il faudra simplement reconnecter les comptes.
        }
        return new MailCredentials();
    }

    private void Save()
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(Credentials);
            AtomicFile.WriteAllBytes(_path, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            // Les identifiants restent en mémoire pour cette session.
        }
    }
}
