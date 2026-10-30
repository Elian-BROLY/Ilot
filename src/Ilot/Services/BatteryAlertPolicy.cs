namespace Ilot.Services;

/// <summary>
/// Les annonces Bluetooth des AirPods sont anonymes : une lecture faible isolée peut venir d'une paire voisine. Elle n'est
/// retenue qu'après plusieurs lectures faibles de suite. Cela réduit les fausses alertes sans pouvoir les exclure.
/// </summary>
internal sealed class ConsecutiveLowReadings(int threshold, int required)
{
    private int _count;

    /// <summary>Faux tant qu'une lecture faible n'est pas confirmée. Une lecture au-dessus du seuil est toujours retenue.</summary>
    public bool IsTrusted(int percent)
    {
        if (percent > threshold)
        {
            _count = 0;
            return true;
        }
        _count++;
        return _count >= required;
    }
}

/// <summary>Décide quand signaler une batterie faible : une seule alerte par seuil franchi, jusqu'à la prochaine recharge.</summary>
internal sealed class BatteryAlertPolicy(params int[] thresholds)
{
    private readonly int[] _thresholds = thresholds.OrderByDescending(threshold => threshold).ToArray();
    private readonly HashSet<int> _alerted = [];

    /// <summary>Renvoie le seuil qui vient d'être franchi, ou null s'il n'y a rien à signaler.</summary>
    public int? Update(int percent, bool isCharging)
    {
        // Remonter au-dessus d'un seuil, ou brancher le chargeur, le réarme pour la prochaine décharge.
        _alerted.RemoveWhere(threshold => isCharging || percent > threshold);
        if (isCharging)
        {
            return null;
        }

        int? crossed = null;
        foreach (var threshold in _thresholds.Where(threshold => percent <= threshold))
        {
            if (_alerted.Add(threshold))
            {
                crossed = threshold; // Le plus bas l'emporte si plusieurs sont franchis d'un coup.
            }
        }
        return crossed;
    }
}
