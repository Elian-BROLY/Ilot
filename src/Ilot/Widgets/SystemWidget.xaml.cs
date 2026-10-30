using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ilot.Interop;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class SystemWidget : UserControl
{
    private const byte BatteryFlagNoSystemBattery = 128;
    private const byte BatteryPercentUnknown = 255;
    private const byte AcOnline = 1;

    private readonly VolumeService _volume = new();
    // N'interroge le système que lorsque le panneau est visible : aucun coût à l'état compact.
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _isRendering;
    private bool _isMuted;

    public SystemWidget()
    {
        InitializeComponent();
        _pollTimer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Refresh();
                _pollTimer.Start();
            }
            else
            {
                _pollTimer.Stop();
            }
        };
    }

    private void Refresh()
    {
        RefreshVolume();
        RefreshBattery();
    }

    private void RefreshVolume()
    {
        var isAvailable = _volume.TryRead(out var level, out _isMuted);
        MuteButton.IsEnabled = isAvailable;
        VolumeSlider.IsEnabled = isAvailable;
        MuteButton.Content = _isMuted || !isAvailable ? "" : "";

        // Ne pas faire reculer le curseur pendant que l'utilisateur le déplace.
        if (!VolumeSlider.IsMouseCaptureWithin)
        {
            _isRendering = true;
            VolumeSlider.Value = level;
            _isRendering = false;
        }
        VolumeText.Text = isAvailable ? FormatPercent(VolumeSlider.Value) : "—";
    }

    private void RefreshBattery()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status)
            || (status.BatteryFlag & BatteryFlagNoSystemBattery) != 0
            || status.BatteryLifePercent == BatteryPercentUnknown)
        {
            BatteryPanel.Visibility = Visibility.Collapsed;
            return;
        }

        BatteryPanel.Visibility = Visibility.Visible;
        BatteryText.Text = status.BatteryLifePercent.ToString(CultureInfo.InvariantCulture) + " %";
        BatteryChargingGlyph.Visibility = status.ACLineStatus == AcOnline ? Visibility.Visible : Visibility.Collapsed;
        BatteryGlyph.Text = GetBatteryGlyph(status.BatteryLifePercent);
    }

    // Segoe : Battery0 à Battery9 occupent E850-E859, Battery10 est à part (E83F).
    private static string GetBatteryGlyph(int percent)
    {
        var tenth = Math.Clamp(percent / 10, 0, 10);
        return tenth == 10 ? "" : ((char)(0xE850 + tenth)).ToString();
    }

    private static string FormatPercent(double level) =>
        Math.Round(level * 100).ToString(CultureInfo.InvariantCulture) + " %";

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRendering)
        {
            return;
        }
        _volume.SetLevel((float)e.NewValue);
        VolumeText.Text = FormatPercent(e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _volume.SetMute(!_isMuted);
        RefreshVolume();
    }
}
