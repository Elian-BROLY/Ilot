using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class AirPodsWidget : UserControl
{
    private enum Request
    {
        None,
        Connecting,
        Disconnecting,
    }

    private readonly BluetoothAudioService _service = new();
    private readonly AirPodsBatteryMonitor _batteryMonitor;
    // L'état change par rafales (une notification par sortie audio) : on attend la fin de la rafale.
    private readonly DispatcherTimer _refreshDebounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    // Une reconnexion juste après une déconnexion peut dépasser 20 s.
    private readonly DispatcherTimer _requestTimeout = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _periodicCheckTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(5) };
    private readonly DispatcherTimer _periodicCheckStopTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _isPeriodicChecking;
    private readonly Brush _defaultButtonForeground;
    private readonly Brush _defaultButtonBackground;
    private BluetoothAudioDevice? _device;
    private Request _request;
    private string? _transientMessage;
    private bool _isBannerWatching;

    public AirPodsWidget()
    {
        InitializeComponent();
        _defaultButtonForeground = ActionButton.Foreground;
        _defaultButtonBackground = ActionButton.Background;
        _batteryMonitor = new AirPodsBatteryMonitor(Dispatcher);
        _batteryMonitor.BatteryChanged += battery =>
        {
            Battery = battery;
            Rings.Update(battery);
            BatteryChanged?.Invoke();
        };

        _refreshDebounce.Tick += async (_, _) =>
        {
            _refreshDebounce.Stop();
            await RefreshAsync();
        };
        _requestTimeout.Tick += (_, _) =>
        {
            _requestTimeout.Stop();
            ShowMessage(_request == Request.Connecting ? "Introuvables : boîtier ouvert ?" : "Déconnexion impossible");
            _request = Request.None;
            Render();
        };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            _transientMessage = null;
            Render();
        };
        IsVisibleChanged += (_, _) => UpdateBatteryWatching();
        // Une brève écoute régulière suffit pour les alertes : la batterie des AirPods varie lentement.
        _periodicCheckTimer.Tick += (_, _) => BeginPeriodicCheck();
        _periodicCheckStopTimer.Tick += (_, _) =>
        {
            _periodicCheckStopTimer.Stop();
            _isPeriodicChecking = false;
            UpdateBatteryWatching();
        };

        _service.DevicesChanged += () => Dispatcher.InvokeAsync(() =>
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        });
    }

    /// <summary>Levé quand un appareil apparaît ou disparaît de la liste des appareils appairés.</summary>
    public event Action? AvailabilityChanged;

    /// <summary>Levé quand la connexion change, avec le nouvel état. Jamais levé pour l'état initial.</summary>
    public event Action<bool>? ConnectionChanged;

    public event Action? BatteryChanged;

    internal AirPodsBattery? Battery { get; private set; }

    public bool IsAvailable => _device is not null;

    public bool IsConnected => _device?.IsConnected ?? false;

    public string DeviceName => _device?.Name ?? string.Empty;

    /// <summary>La bannière a besoin de la batterie alors que ce widget est masqué.</summary>
    public bool IsBannerWatching
    {
        set
        {
            _isBannerWatching = value;
            UpdateBatteryWatching();
        }
    }

    // Appelée par MainWindow : le panneau replié au démarrage ne déclencherait pas Loaded.
    internal async Task InitializeAsync()
    {
        await _service.StartListeningAsync();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var previous = _device;
        _device = await BluetoothAudioService.FindAirPodsAsync();
        _batteryMonitor.ProductId = _device?.ProductId;

        if ((previous is null) != (_device is null))
        {
            AvailabilityChanged?.Invoke();
        }

        var hasChanged = previous is not null && _device is not null && previous.IsConnected != _device.IsConnected;
        if (hasChanged)
        {
            _request = Request.None;
            _requestTimeout.Stop();
            _transientMessage = null;
        }
        Render();
        UpdateBatteryWatching();
        if (hasChanged)
        {
            ConnectionChanged?.Invoke(_device!.IsConnected);
        }
    }

    private void Render()
    {
        if (_device is not { } device)
        {
            return;
        }

        NameText.Text = device.Name;
        StatusText.Text = _transientMessage ?? _request switch
        {
            Request.Connecting => "Connexion en cours…",
            Request.Disconnecting => "Déconnexion en cours…",
            _ => device.IsConnected ? "Connectés" : "Non connectés",
        };

        var isBusy = _request != Request.None;
        ActionButton.IsEnabled = !isBusy;
        ActionButton.Content = _request switch
        {
            Request.Connecting => "Connexion…",
            Request.Disconnecting => "Déconnexion…",
            _ => device.IsConnected ? "Déconnecter" : "Connecter",
        };

        var isPrimary = !device.IsConnected && !isBusy;
        ActionButton.Background = isPrimary ? (Brush)FindResource("AccentBrush") : _defaultButtonBackground;
        ActionButton.Foreground = isPrimary ? Brushes.Black : _defaultButtonForeground;

        if (isBusy)
        {
            var pulse = new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(pulse, 30);
            ActionButton.BeginAnimation(OpacityProperty, pulse);
        }
        else
        {
            ActionButton.BeginAnimation(OpacityProperty, null);
        }
    }

    private void ShowMessage(string message)
    {
        _transientMessage = message;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void UpdateBatteryWatching()
    {
        // L'écoute BLE consomme de la batterie : uniquement si le résultat est affiché, ou pour un bref contrôle.
        if (_device is not null && (IsVisible || _isBannerWatching || _isPeriodicChecking))
        {
            _batteryMonitor.Start();
        }
        else
        {
            _batteryMonitor.Stop();
        }

        if (IsConnected && !_periodicCheckTimer.IsEnabled)
        {
            _periodicCheckTimer.Start();
            BeginPeriodicCheck();
        }
        else if (!IsConnected)
        {
            _periodicCheckTimer.Stop();
        }
    }

    private void BeginPeriodicCheck()
    {
        _isPeriodicChecking = true;
        _periodicCheckStopTimer.Stop();
        _periodicCheckStopTimer.Start();
        UpdateBatteryWatching();
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_device is not { } device)
        {
            return;
        }

        var connect = !device.IsConnected;
        _request = connect ? Request.Connecting : Request.Disconnecting;
        _transientMessage = null;
        Render();
        _requestTimeout.Start();

        var isAccepted = await BluetoothAudioService.RequestConnectionAsync(device.ContainerId, connect);
        if (!isAccepted && _request != Request.None)
        {
            _requestTimeout.Stop();
            _request = Request.None;
            ShowMessage("Bluetooth indisponible");
            Render();
        }
    }
}
