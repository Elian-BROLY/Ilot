namespace Ilot.Services;

/// <summary>Repère le passage au morceau suivant. Changer de lecteur, ou reprendre après une pause, n'est pas un nouveau morceau.</summary>
internal sealed class TrackChangeDetector
{
    private string? _source;
    private string? _artist;
    private string? _title;

    /// <summary>Renvoie true si un nouveau morceau vient de commencer dans le même lecteur.</summary>
    public bool Update(string? source, string artist, string title)
    {
        // Entre deux morceaux, certains lecteurs publient un titre vide, et Deezer va jusqu'à fermer sa session
        // plus d'une seconde : le morceau précédent reste la référence.
        if (source is null || (title.Length == 0 && source == _source))
        {
            return false;
        }

        var isNewTrack = _source is not null && source == _source && (title != _title || artist != _artist);
        _source = source;
        _artist = artist;
        _title = title;
        return isNewTrack;
    }
}
