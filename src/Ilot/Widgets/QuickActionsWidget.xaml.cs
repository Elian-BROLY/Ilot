using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ilot.Interop;
using Ilot.Services;
using Windows.Devices.Radios;

namespace Ilot.Widgets;

/// <summary>Les réglages qu'on change le plus souvent, à un clic : Wi-Fi, Bluetooth, mode avion, capture, verrouillage.</summary>
public partial class QuickActionsWidget : UserControl
{
    private Radio? _wifi;
    private Radio? _bluetooth;
    private bool _isLoaded;

    public QuickActionsWidget()
    {
        InitializeComponent();
        // Chargé au premier affichage : la page est masquée par défaut, elle n'a rien à interroger avant.
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible && !_isLoaded)
            {
                _isLoaded = true;
                await LoadRadiosAsync();
            }
            Render();
        };
    }

    /// <summary>Levé avant une capture : la capsule se referme pour ne pas figurer sur l'image.</summary>
    public event Action? CaptureRequested;

    private async Task LoadRadiosAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            {
                return;
            }
            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind == RadioKind.WiFi)
                {
                    _wifi ??= radio;
                }
                else if (radio.Kind == RadioKind.Bluetooth)
                {
                    _bluetooth ??= radio;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex); // Les tuiles restent grisées.
            return;
        }
        foreach (var radio in new[] { _wifi, _bluetooth })
        {
            if (radio is not null)
            {
                radio.StateChanged += (_, _) => Dispatcher.BeginInvoke(Render);
            }
        }
    }

    private void Render()
    {
        RenderTile(WifiButton, _wifi);
        RenderTile(BluetoothButton, _bluetooth);
    }

    private void RenderTile(Button button, Radio? radio)
    {
        button.IsEnabled = radio is not null;
        var isOn = radio?.State == RadioState.On;
        button.Background = (Brush)FindResource(isOn ? "AccentBrush" : "SurfaceBrush");
        button.Foreground = isOn ? Brushes.Black : (Brush)FindResource("TextPrimaryBrush");
    }

    private async void Wifi_Click(object sender, RoutedEventArgs e) => await ToggleAsync(_wifi);

    private async void Bluetooth_Click(object sender, RoutedEventArgs e) => await ToggleAsync(_bluetooth);

    private async Task ToggleAsync(Radio? radio)
    {
        if (radio is null)
        {
            return;
        }
        try
        {
            await radio.SetStateAsync(radio.State == RadioState.On ? RadioState.Off : RadioState.On);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
        }
        Render();
    }

    private void Airplane_Click(object sender, RoutedEventArgs e) => Launch("ms-settings:network-airplanemode");

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        CaptureRequested?.Invoke();
        // Laisse à la capsule le temps de se refermer avant que l'outil fige l'écran.
        _ = Task.Delay(400).ContinueWith(_ => Launch("ms-screenclip:"), TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Lock_Click(object sender, RoutedEventArgs e) => NativeMethods.LockWorkStation();

    private static void Launch(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppLog.Write(ex);
        }
    }
}
