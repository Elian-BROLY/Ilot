using System.Windows.Threading;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

internal sealed record AirPodsBattery(int? Left, int? Right, int? Case, bool IsLeftCharging, bool IsRightCharging, bool IsCaseCharging);

/// <summary>
/// Lit la batterie dans les annonces BLE « Proximity Pairing » d'Apple. Ce format n'est pas documenté par Apple et
/// les annonces sont anonymes : le filtrage par modèle et par signal écarte la plupart des AirPods voisins, sans garantie.
/// </summary>
internal sealed class AirPodsBatteryMonitor
{
    private const ushort AppleCompanyId = 0x004C;
    private const byte ProximityPairingType = 0x07;
    private const int MinimumPayloadLength = 8;
    private const short MinimumSignalDbm = -75;
    private const int UnknownLevel = 0xF;
    private const byte OrientationFlag = 0x20; // Absent : les écouteurs gauche et droit sont permutés dans la trame.
    private static readonly TimeSpan StrongestSignalWindow = TimeSpan.FromSeconds(5);

    private readonly Dispatcher _dispatcher;
    private BluetoothLEAdvertisementWatcher? _watcher;
    private short _lastSignal = short.MinValue;
    private DateTime _lastAcceptedUtc;

    public AirPodsBatteryMonitor(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public event Action<AirPodsBattery>? BatteryChanged;

    public ushort? ProductId { get; set; }

    public void Start()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _watcher.Received += OnAdvertisementReceived;
        try
        {
            _watcher.Start();
        }
        catch (Exception)
        {
            _watcher = null; // Bluetooth désactivé : la jauge reste simplement masquée.
        }
    }

    public void Stop()
    {
        if (_watcher is { } watcher)
        {
            _watcher = null;
            watcher.Received -= OnAdvertisementReceived;
            watcher.Stop();
        }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (ProductId is not { } productId || args.RawSignalStrengthInDBm < MinimumSignalDbm)
        {
            return;
        }

        foreach (var section in args.Advertisement.ManufacturerData)
        {
            if (section.CompanyId != AppleCompanyId || section.Data.Length < MinimumPayloadLength)
            {
                continue;
            }

            var data = new byte[section.Data.Length];
            using (var reader = DataReader.FromBuffer(section.Data))
            {
                reader.ReadBytes(data);
            }
            if (data[0] != ProximityPairingType || (data[3] | data[4] << 8) != productId)
            {
                continue;
            }

            // Si plusieurs paires du même modèle sont à portée, on garde la plus proche.
            var now = DateTime.UtcNow;
            if (args.RawSignalStrengthInDBm + 10 < _lastSignal && now - _lastAcceptedUtc < StrongestSignalWindow)
            {
                continue;
            }
            _lastSignal = args.RawSignalStrengthInDBm;
            _lastAcceptedUtc = now;

            var battery = Parse(data);
            _dispatcher.InvokeAsync(() => BatteryChanged?.Invoke(battery));
        }
    }

    private static AirPodsBattery Parse(byte[] data)
    {
        var isFlipped = (data[5] & OrientationFlag) == 0;
        var high = data[6] >> 4;
        var low = data[6] & 0xF;
        var chargeFlags = data[7] >> 4;
        var leftChargeFlag = isFlipped ? 0b0010 : 0b0001;
        var rightChargeFlag = isFlipped ? 0b0001 : 0b0010;

        return new AirPodsBattery(
            ToPercent(isFlipped ? high : low),
            ToPercent(isFlipped ? low : high),
            ToPercent(data[7] & 0xF),
            (chargeFlags & leftChargeFlag) != 0,
            (chargeFlags & rightChargeFlag) != 0,
            (chargeFlags & 0b0100) != 0);
    }

    // Niveaux transmis par pas de 10 %.
    private static int? ToPercent(int level) => level == UnknownLevel ? null : Math.Min(level * 10, 100);
}
