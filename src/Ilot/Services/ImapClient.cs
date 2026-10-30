using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Ilot.Services;

internal sealed class ImapException(string message) : Exception(message);

/// <summary>
/// Le strict nécessaire d'IMAP : compter et lister les non lus, marquer comme lu, mettre à la corbeille. Seule une connexion
/// chiffrée (TLS, port 993) est utilisée, et rien n'est jamais supprimé définitivement. Le corps des messages n'est pas lu.
/// </summary>
internal sealed partial class ImapClient : IDisposable
{
    public const string LoginRefused = "Identifiants refusés";
    private const int MaxLineBytes = 1024 * 1024;

    private readonly Stream _stream;
    private int _tag;

    private ImapClient(Stream stream) => _stream = stream;

    /// <summary>Le serveur IMAP des services courants d'après l'adresse. Null si inconnu : il faut l'indiquer à la main.</summary>
    public static string? GuessHost(string address)
    {
        var domain = address[(address.IndexOf('@') + 1)..].Trim().ToLowerInvariant();
        return domain switch
        {
            "gmail.com" or "googlemail.com" => "imap.gmail.com",
            "yahoo.com" or "yahoo.fr" => "imap.mail.yahoo.com",
            "icloud.com" or "me.com" or "mac.com" => "imap.mail.me.com",
            "orange.fr" or "wanadoo.fr" => "imap.orange.fr",
            "free.fr" => "imap.free.fr",
            "sfr.fr" or "neuf.fr" => "imap.sfr.fr",
            "laposte.net" => "imap.laposte.net",
            "gmx.fr" or "gmx.com" or "gmx.net" => "imap.gmx.com",
            "proton.me" or "protonmail.com" => null, // Proton passe par son logiciel Bridge, sur le PC.
            _ => null,
        };
    }

    public static async Task<ImapClient> ConnectAsync(string host, string user, string password, CancellationToken cancellationToken)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, 993, cancellationToken);
            var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(host);
            var client = new ImapClient(tls);
            await client.ReadGreetingAsync(cancellationToken);
            await client.LoginAsync(user, password, cancellationToken);
            return client;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>Pour les tests : parle à un flux quelconque, déjà prêt, sans réseau.</summary>
    internal static async Task<ImapClient> OverStreamAsync(Stream stream, string user, string password, CancellationToken cancellationToken)
    {
        var client = new ImapClient(stream);
        await client.ReadGreetingAsync(cancellationToken);
        await client.LoginAsync(user, password, cancellationToken);
        return client;
    }

    private async Task ReadGreetingAsync(CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(cancellationToken);
        if (!line.StartsWith("* OK", StringComparison.OrdinalIgnoreCase))
        {
            throw new ImapException("Réponse inattendue du serveur");
        }
    }

    private async Task LoginAsync(string user, string password, CancellationToken cancellationToken)
    {
        await RunAsync($"LOGIN {Quote(user)} {Quote(password)}", cancellationToken);
    }

    /// <summary>Le nombre de non lus de la boîte de réception, et les plus récents, du plus récent au plus ancien.</summary>
    public async Task<MailSummary> ReadSummaryAsync(int maxItems, CancellationToken cancellationToken)
    {
        await RunAsync("EXAMINE INBOX", cancellationToken);
        var search = await RunAsync("UID SEARCH UNSEEN", cancellationToken);
        var uids = search.Where(line => line.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase))
            .SelectMany(line => line[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(long.Parse)
            .OrderDescending()
            .ToList();
        var items = new List<MailItem>();
        foreach (var uid in uids.Take(maxItems))
        {
            var lines = await RunAsync($"UID FETCH {uid} (BODY.PEEK[HEADER.FIELDS (FROM SUBJECT)])", cancellationToken);
            var headers = ParseHeaders(string.Join("\r\n", lines));
            items.Add(new MailItem(uid.ToString(System.Globalization.CultureInfo.InvariantCulture), null,
                MailHeaders.CleanSender(headers.GetValueOrDefault("from")), headers.GetValueOrDefault("subject"), null, true));
        }
        return new MailSummary(uids.Count, items, null);
    }

    public async Task MarkAsReadAsync(string uid, CancellationToken cancellationToken)
    {
        await RunAsync("SELECT INBOX", cancellationToken);
        await RunAsync($"UID STORE {RequireUid(uid)} +FLAGS.SILENT (\\Seen)", cancellationToken);
    }

    /// <summary>Déplace vers la corbeille du serveur. Sans corbeille identifiée, échoue plutôt que de supprimer pour de bon.</summary>
    public async Task MoveToTrashAsync(string uid, CancellationToken cancellationToken)
    {
        var folders = await RunAsync("LIST \"\" \"*\"", cancellationToken);
        var trash = FindTrashFolder(folders) ?? throw new ImapException("Corbeille introuvable sur ce serveur");
        await RunAsync("SELECT INBOX", cancellationToken);
        // Pas de repli sur COPY puis EXPUNGE : cela supprimerait aussi les messages marqués ailleurs pour suppression.
        await RunAsync($"UID MOVE {RequireUid(uid)} {Quote(trash)}", cancellationToken);
    }

    /// <summary>Le dossier marqué \Trash, sinon un nom courant.</summary>
    internal static string? FindTrashFolder(IEnumerable<string> listLines)
    {
        var names = new List<(string Name, bool IsMarked)>();
        foreach (var line in listLines)
        {
            var match = ListLinePattern().Match(line);
            if (match.Success)
            {
                names.Add((match.Groups["name"].Value.Trim('"'), match.Groups["flags"].Value.Contains("\\Trash", StringComparison.OrdinalIgnoreCase)));
            }
        }
        var marked = names.FirstOrDefault(name => name.IsMarked).Name;
        return marked ?? names.Select(name => name.Name).FirstOrDefault(name =>
            name.Equals("Trash", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("/Trash", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".Trash", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Corbeille", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Deleted Items", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>En-têtes From et Subject, repliés et décodés. Les clés sont en minuscules.</summary>
    internal static Dictionary<string, string> ParseHeaders(string text)
    {
        var headers = new Dictionary<string, string>();
        // Une ligne qui commence par un blanc continue l'en-tête précédent.
        var unfolded = FoldPattern().Replace(text, " ");
        foreach (Match match in HeaderPattern().Matches(unfolded))
        {
            headers[match.Groups[1].Value.ToLowerInvariant()] = MailHeaders.DecodeEncodedWords(match.Groups[2].Value.Trim());
        }
        return headers;
    }

    internal static string Quote(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private static string RequireUid(string uid) =>
        long.TryParse(uid, out _) ? uid : throw new ImapException("Identifiant de message invalide");

    /// <summary>Envoie une commande et renvoie les lignes de réponse, littéraux compris. Lève une exception si le serveur refuse.</summary>
    private async Task<List<string>> RunAsync(string command, CancellationToken cancellationToken)
    {
        var tag = $"A{++_tag:000}";
        await _stream.WriteAsync(Encoding.UTF8.GetBytes($"{tag} {command}\r\n"), cancellationToken);
        await _stream.FlushAsync(cancellationToken);

        var lines = new List<string>();
        while (true)
        {
            var line = await ReadLineAsync(cancellationToken);
            if (line.StartsWith(tag + " ", StringComparison.Ordinal))
            {
                if (!line[(tag.Length + 1)..].StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ImapException(command.StartsWith("LOGIN", StringComparison.Ordinal) ? LoginRefused : "Commande refusée par le serveur");
                }
                return lines;
            }
            lines.Add(line);
            var literal = LiteralPattern().Match(line);
            if (literal.Success)
            {
                lines.Add(await ReadLiteralAsync(int.Parse(literal.Groups[1].Value), cancellationToken));
            }
        }
    }

    private async Task<string> ReadLiteralAsync(int length, CancellationToken cancellationToken)
    {
        if (length > MaxLineBytes)
        {
            throw new ImapException("Réponse trop longue");
        }
        var buffer = new byte[length];
        await _stream.ReadExactlyAsync(buffer, cancellationToken);
        return Encoding.UTF8.GetString(buffer);
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var line = new MemoryStream();
        var one = new byte[1];
        while (line.Length < MaxLineBytes)
        {
            if (await _stream.ReadAsync(one, cancellationToken) == 0)
            {
                throw new ImapException("Connexion fermée par le serveur");
            }
            if (one[0] == '\n')
            {
                return Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r');
            }
            line.WriteByte(one[0]);
        }
        throw new ImapException("Réponse trop longue");
    }

    public void Dispose() => _stream.Dispose();

    [GeneratedRegex(@"\{(\d+)\}$")]
    private static partial Regex LiteralPattern();

    [GeneratedRegex(@"\r?\n[ \t]+")]
    private static partial Regex FoldPattern();

    [GeneratedRegex(@"^(From|Subject):(.*)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\* LIST \((?<flags>[^)]*)\) (?:""[^""]*""|NIL) (?<name>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ListLinePattern();
}

/// <summary>Mise en forme des en-têtes de mail.</summary>
internal static partial class MailHeaders
{
    // Beaucoup de mails français arrivent encore en windows-1252, absent par défaut de .NET.
    static MailHeaders() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // "Jeanne Martin <jeanne@exemple.fr>" devient "Jeanne Martin".
    public static string? CleanSender(string? from)
    {
        if (from is null)
        {
            return null;
        }
        var bracket = from.IndexOf('<');
        var name = bracket > 0 ? from[..bracket] : from;
        return name.Trim().Trim('"');
    }

    /// <summary>Décode les « =?UTF-8?B?...?= » et « =?UTF-8?Q?...?= » de la RFC 2047.</summary>
    public static string DecodeEncodedWords(string value)
    {
        // Les blancs entre deux mots encodés ne comptent pas.
        var joined = BetweenWordsPattern().Replace(value, "?==?");
        return EncodedWordPattern().Replace(joined, match =>
        {
            try
            {
                var encoding = Encoding.GetEncoding(match.Groups[1].Value);
                var text = match.Groups[3].Value;
                var bytes = match.Groups[2].Value is "B" or "b"
                    ? Convert.FromBase64String(text)
                    : DecodeQuotedPrintable(text);
                return encoding.GetString(bytes);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                return match.Value; // Illisible : mieux vaut l'afficher tel quel que perdre le sujet.
            }
        });
    }

    private static byte[] DecodeQuotedPrintable(string text)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '_')
            {
                bytes.Add((byte)' ');
            }
            else if (text[i] == '=' && i + 2 < text.Length)
            {
                bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                i += 2;
            }
            else
            {
                bytes.Add((byte)text[i]);
            }
        }
        return bytes.ToArray();
    }

    [GeneratedRegex(@"\?=\s+=\?")]
    private static partial Regex BetweenWordsPattern();

    [GeneratedRegex(@"=\?([^?]+)\?([BbQq])\?([^?]*)\?=")]
    private static partial Regex EncodedWordPattern();
}
