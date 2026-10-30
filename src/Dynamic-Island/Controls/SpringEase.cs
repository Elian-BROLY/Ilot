using System.Windows;
using System.Windows.Media.Animation;

namespace DynamicIsland.Controls;

/// <summary>Ressort amorti : léger dépassement de la cible (~6 %) puis stabilisation.</summary>
internal sealed class SpringEase : EasingFunctionBase
{
    private const double Damping = 7.0;
    private const double Frequency = 8.0;

    public SpringEase() => EasingMode = EasingMode.EaseIn; // EaseIn applique la formule telle quelle.

    protected override double EaseInCore(double t) =>
        t >= 1.0 ? 1.0 : 1.0 - Math.Exp(-Damping * t) * Math.Cos(Frequency * t);

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
