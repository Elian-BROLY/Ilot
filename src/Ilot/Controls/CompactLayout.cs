namespace Ilot.Controls;

/// <summary>La capsule compacte centre l'heure, avec des indicateurs à gauche et à droite : tout doit tenir sans se toucher.</summary>
internal static class CompactLayout
{
    private const double MinimumGap = 4;

    /// <param name="left">Largeur à gauche, marge comprise.</param>
    /// <param name="right">Largeur à droite, marge comprise.</param>
    public static bool Fits(double capsuleWidth, double center, double left, double right)
    {
        var freeOnEachSide = (capsuleWidth - center) / 2 - MinimumGap;
        return left <= freeOnEachSide && right <= freeOnEachSide;
    }
}
