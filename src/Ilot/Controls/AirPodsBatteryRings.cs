using System.Windows;
using System.Windows.Controls;
using Ilot.Services;

namespace Ilot.Controls;

/// <summary>Les trois jauges : gauche, droite et boîtier. Chacune se masque si son niveau est inconnu.</summary>
internal sealed class AirPodsBatteryRings : StackPanel
{
    private readonly BatteryRing _left = new("G", "Écouteur gauche");
    private readonly BatteryRing _right = new("D", "Écouteur droit") { Margin = new Thickness(5, 0, 0, 0) };
    private readonly BatteryRing _case = new("B", "Boîtier") { Margin = new Thickness(5, 0, 0, 0) };

    public AirPodsBatteryRings()
    {
        Orientation = Orientation.Horizontal;
        Children.Add(_left);
        Children.Add(_right);
        Children.Add(_case);
    }

    public void Update(AirPodsBattery? battery)
    {
        _left.Update(battery?.Left, battery?.IsLeftCharging ?? false);
        _right.Update(battery?.Right, battery?.IsRightCharging ?? false);
        _case.Update(battery?.Case, battery?.IsCaseCharging ?? false);
    }
}
