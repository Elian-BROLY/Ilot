namespace Ilot.Services;

/// <summary>Le mail le plus récent à annoncer, et le nombre d'autres mails arrivés en même temps.</summary>
internal sealed record MailNotification(MailProvider Provider, MailItem Item, int OtherCount);

/// <summary>Compare deux lectures successives de la boîte de réception pour repérer les mails vraiment nouveaux.</summary>
internal sealed class NewMailDetector
{
    private readonly Dictionary<MailProvider, HashSet<string>> _known = [];

    /// <summary>
    /// Renvoie les nouveaux mails, du plus récent au plus ancien. La première lecture sert de référence et n'annonce rien.
    /// Les listes étant triées du plus récent au plus ancien, un nouveau mail ne peut être qu'en tête : un ancien non lu
    /// qui remonte en fin de liste après une suppression n'est donc pas pris pour un nouveau.
    /// </summary>
    public IReadOnlyList<MailItem> Update(MailProvider provider, IReadOnlyList<MailItem> items)
    {
        var isFirstRead = !_known.TryGetValue(provider, out var known);
        _known[provider] = items.Select(item => item.Id).ToHashSet();
        if (isFirstRead)
        {
            return [];
        }

        var added = items.TakeWhile(item => !known!.Contains(item.Id)).ToList();
        // Liste pleine sans aucun mail connu : d'anciens non lus ont remplacé ceux qui viennent d'être lus.
        return added.Count >= MailService.MaxItems ? [] : added;
    }

    /// <summary>À appeler quand un compte est masqué ou déconnecté : son retour ne doit rien annoncer.</summary>
    public void Reset(MailProvider provider) => _known.Remove(provider);
}
