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
}

internal static class MailProviderExtensions
{
    public static bool IsGmail(this MailProvider provider) => provider is MailProvider.Gmail or MailProvider.Gmail2;

    public static string Label(this MailProvider provider) => provider switch
    {
        MailProvider.Gmail => "Gmail",
        MailProvider.Gmail2 => "Gmail 2",
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

    /// <summary>Levé quand un compte est connecté ou déconnecté.</summary>
    public event Action? AccountsChanged;

    public MailCredentials Credentials { get; }

    public bool IsConnected(MailProvider provider) => GetRefreshToken(provider) is not null;

    public void SaveClientCredentials(string outlookId, string gmailId, string gmailSecret)
    {
        Credentials.OutlookClientId = outlookId;
        Credentials.GmailClientId = gmailId;
        Credentials.GmailClientSecret = gmailSecret;
        Save();
    }

    public void Disconnect(MailProvider provider)
    {
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
        try
        {
            var token = await GetAccessTokenAsync(provider);
            if (token is null)
            {
                return "Reconnexion nécessaire (Réglages)";
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
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return "Reconnectez le compte pour autoriser la suppression";
            }
            return response.IsSuccessStatusCode ? null : "Suppression impossible";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "Suppression impossible pour le moment";
        }
    }

    /// <summary>Marque le message comme lu. Renvoie null en cas de succès, sinon un message d'erreur.</summary>
    public async Task<string?> MarkAsReadAsync(MailProvider provider, string messageId)
    {
        try
        {
            var token = await GetAccessTokenAsync(provider);
            if (token is null)
            {
                return "Reconnexion nécessaire (Réglages)";
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
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return "Reconnectez le compte pour autoriser cette action";
            }
            return response.IsSuccessStatusCode ? null : "Action impossible";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "Action impossible pour le moment";
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
    public async Task<string?> ConnectAsync(MailProvider provider)
    {
        var clientId = provider.IsGmail() ? Credentials.GmailClientId : Credentials.OutlookClientId;
        if (string.IsNullOrEmpty(clientId))
        {
            return "Indiquez d'abord l'identifiant.";
        }
        if (provider.IsGmail() && string.IsNullOrEmpty(Credentials.GmailClientSecret))
        {
            return "Indiquez aussi le code secret.";
        }

        try
        {
            var error = await SignInAsync(provider, clientId);
            if (error is null)
            {
                AccountsChanged?.Invoke();
            }
            return error;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or HttpListenerException or InvalidOperationException or Win32Exception)
        {
            return "Connexion impossible. Vérifiez l'identifiant et le réseau.";
        }
    }

    public async Task<MailSummary> GetSummaryAsync(MailProvider provider)
    {
        try
        {
            var token = await GetAccessTokenAsync(provider);
            if (token is null)
            {
                return new MailSummary(0, [], "Reconnexion nécessaire (Réglages)", NeedsReconnect: true);
            }
            return provider.IsGmail() ? await GetGmailSummaryAsync(provider, token) : await GetOutlookSummaryAsync(token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _accessTokens.Remove(provider);
            return new MailSummary(0, [], "Accès refusé : reconnectez le compte", NeedsReconnect: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            AppLog.Write(ex);
            return new MailSummary(0, [], "Lecture impossible pour le moment");
        }
    }

    private async Task<string?> SignInAsync(MailProvider provider, string clientId)
    {
        var port = FindFreePort();
        var redirectUri = $"http://localhost:{port}/";
        var state = CreateRandomString();
        var verifier = CreateRandomString();
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var parameters = $"client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&state={state}&code_challenge={challenge}&code_challenge_method=S256";
        var url = provider.IsGmail()
            ? $"{GmailAuthorizeUrl}?{parameters}&scope={Uri.EscapeDataString(GmailScope)}&access_type=offline&prompt=select_account%20consent"
            : $"{OutlookAuthorizeUrl}?{parameters}&scope={Uri.EscapeDataString(OutlookScope)}&prompt=select_account";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        var pending = listener.GetContextAsync();
        if (await Task.WhenAny(pending, Task.Delay(SignInTimeout)) != pending)
        {
            return "Délai dépassé : connexion annulée.";
        }

        var context = await pending;
        var query = context.Request.QueryString;
        var isValid = query["state"] == state && !string.IsNullOrEmpty(query["code"]);
        await RespondAsync(context, isValid);
        if (!isValid)
        {
            return "Connexion refusée ou annulée.";
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
            form["scope"] = OutlookScope;
        }

        using var response = await _http.PostAsync(TokenUrl(provider), new FormUrlEncodedContent(form));
        if (!response.IsSuccessStatusCode)
        {
            return "Le service a refusé la connexion. Vérifiez les identifiants.";
        }
        StoreTokens(provider, await response.Content.ReadAsStringAsync());
        return IsConnected(provider) ? null : "Le service n'a pas accordé d'accès durable.";
    }

    private static async Task RespondAsync(HttpListenerContext context, bool isValid)
    {
        var message = isValid ? "Connexion réussie. Vous pouvez fermer cet onglet." : "Connexion annulée. Vous pouvez fermer cet onglet.";
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
        else
        {
            form["scope"] = OutlookScope;
        }

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
                sender = CleanSender(value);
            }
            else if (string.Equals(header.GetProperty("name").GetString(), "Subject", StringComparison.OrdinalIgnoreCase))
            {
                subject = value;
            }
        }

        // L'adresse dans le lien ouvre le bon compte quand plusieurs sont connectés dans le navigateur.
        if (!_gmailAddresses.TryGetValue(provider, out var address))
        {
            using var profile = await GetJsonAsync($"{GmailApi}/profile", token);
            address = profile.RootElement.GetProperty("emailAddress").GetString();
            _gmailAddresses[provider] = address;
        }

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

    private async Task<JsonDocument> GetJsonAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    // "Jeanne Martin <jeanne@exemple.fr>" devient "Jeanne Martin".
    private static string? CleanSender(string? from)
    {
        if (from is null)
        {
            return null;
        }
        var bracket = from.IndexOf('<');
        var name = bracket > 0 ? from[..bracket] : from;
        return name.Trim().Trim('"');
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
