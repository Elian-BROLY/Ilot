using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Ilot.Interop;

namespace Ilot.Services;

internal sealed record BluetoothAudioDevice(Guid ContainerId, string Name, bool IsConnected, ushort? ProductId);

/// <summary>
/// Connecte et déconnecte un appareil audio Bluetooth déjà appairé, via les requêtes KS documentées par Microsoft
/// (celles qu'emploient les Paramètres Windows). Tout s'exécute sur le pool de threads, car ces appels peuvent bloquer.
/// </summary>
internal sealed partial class BluetoothAudioService
{
    private const int EAll = 2;
    private const int DeviceStateActive = 1;
    private const int DeviceStateUnplugged = 8;
    private const int ClsCtxAll = 23;
    private const uint KsPropertyTypeGet = 1;
    private const uint OneShotReconnect = 0;
    private const uint OneShotDisconnect = 1;
    private const ushort VtLpwstr = 31;
    private const ushort VtClsid = 72;
    private const string BluetoothFilterPrefix = @"{2}.\\?\bth"; // Couvre bthenum (A2DP) et bthhfenum (mains libres).

    private static readonly Guid BtAudioPropertySet = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private static readonly PROPERTYKEY FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly PROPERTYKEY ContainerIdKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    private IMMDeviceEnumerator? _notificationEnumerator;
    private NotificationClient? _notificationClient;

    /// <summary>Levé sur un thread quelconque quand l'état d'un périphérique audio change.</summary>
    public event Action? DevicesChanged;

    private sealed record Endpoint(Guid ContainerId, string FriendlyName, int State, string FilterId);

    public Task StartListeningAsync() => Task.Run(() =>
    {
        _notificationClient = new NotificationClient(() => DevicesChanged?.Invoke());
        _notificationEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _notificationEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
    });

    /// <summary>Renvoie les AirPods s'ils sont appairés. Les autres appareils Bluetooth sont volontairement ignorés.</summary>
    public static Task<BluetoothAudioDevice?> FindAirPodsAsync() => Task.Run(() =>
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            return ListEndpoints(enumerator)
                .GroupBy(endpoint => endpoint.ContainerId)
                .Select(ToDevice)
                .FirstOrDefault(device => device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    });

    /// <summary>Renvoie true si le pilote a accepté la demande, ce qui ne garantit pas que la connexion aboutisse.</summary>
    public static Task<bool> RequestConnectionAsync(Guid containerId, bool connect) => Task.Run(() =>
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            var filters = ListEndpoints(enumerator)
                .Where(endpoint => endpoint.ContainerId == containerId)
                .Select(endpoint => endpoint.FilterId)
                .Distinct();

            var isAccepted = false;
            foreach (var filterId in filters)
            {
                isAccepted |= SendOneShot(enumerator, filterId, connect ? OneShotReconnect : OneShotDisconnect);
            }
            return isAccepted;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    });

    private static BluetoothAudioDevice ToDevice(IGrouping<Guid, Endpoint> endpoints)
    {
        var productMatch = endpoints.Select(endpoint => ProductIdPattern().Match(endpoint.FilterId)).FirstOrDefault(match => match.Success);
        return new BluetoothAudioDevice(
            endpoints.Key,
            CleanName(endpoints.First().FriendlyName),
            endpoints.Any(endpoint => endpoint.State == DeviceStateActive),
            productMatch is null ? null : Convert.ToUInt16(productMatch.Groups[1].Value, 16));
    }

    // "Casque (AirPods de Elian  - Find My Hands-Free)" devient "AirPods de Elian".
    internal static string ReadFriendlyName(IPropertyStore store) => ReadString(store, FriendlyNameKey);

    internal static string CleanName(string friendlyName)
    {
        var start = friendlyName.IndexOf('(');
        var end = friendlyName.LastIndexOf(')');
        var name = start >= 0 && end > start ? friendlyName[(start + 1)..end] : friendlyName;
        name = NameSuffixPattern().Replace(name, string.Empty);
        return WhitespacePattern().Replace(name, " ").Trim();
    }

    private static List<Endpoint> ListEndpoints(IMMDeviceEnumerator enumerator)
    {
        var endpoints = new List<Endpoint>();
        if (enumerator.EnumAudioEndpoints(EAll, DeviceStateActive | DeviceStateUnplugged, out var collection) < 0 || collection is null)
        {
            return endpoints;
        }

        collection.GetCount(out var count);
        for (uint i = 0; i < count; i++)
        {
            if (collection.Item(i, out var device) < 0 || device is null)
            {
                continue;
            }
            if (FindBluetoothFilter(device) is { } filterId)
            {
                device.GetState(out var state);
                device.OpenPropertyStore(0, out var store);
                if (store is not null)
                {
                    endpoints.Add(new Endpoint(ReadGuid(store, ContainerIdKey), ReadString(store, FriendlyNameKey), state, filterId));
                    Marshal.ReleaseComObject(store);
                }
            }
            Marshal.ReleaseComObject(device);
        }
        Marshal.ReleaseComObject(collection);
        return endpoints;
    }

    private static string? FindBluetoothFilter(IMMDevice device)
    {
        var iid = typeof(IDeviceTopology).GUID;
        if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var topologyObject) < 0 || topologyObject is null)
        {
            return null;
        }

        try
        {
            var topology = (IDeviceTopology)topologyObject;
            topology.GetConnectorCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (topology.GetConnector(i, out var connector) < 0 || connector is null)
                {
                    continue;
                }
                // Échoue sur un connecteur non relié : ce n'est pas une erreur ici.
                var hr = connector.GetDeviceIdConnectedTo(out var id);
                Marshal.ReleaseComObject(connector);
                if (hr >= 0 && id is not null && id.StartsWith(BluetoothFilterPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
            }
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(topologyObject);
        }
    }

    private static bool SendOneShot(IMMDeviceEnumerator enumerator, string filterId, uint propertyId)
    {
        if (enumerator.GetDevice(filterId, out var filter) < 0 || filter is null)
        {
            return false;
        }

        try
        {
            var iid = typeof(IKsControl).GUID;
            if (filter.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var controlObject) < 0 || controlObject is null)
            {
                return false;
            }
            try
            {
                var property = new KSPROPERTY { Set = BtAudioPropertySet, Id = propertyId, Flags = KsPropertyTypeGet };
                return ((IKsControl)controlObject).KsProperty(ref property, (uint)Marshal.SizeOf<KSPROPERTY>(), IntPtr.Zero, 0, out _) >= 0;
            }
            finally
            {
                Marshal.ReleaseComObject(controlObject);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(filter);
        }
    }

    private static string ReadString(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out var value) < 0)
        {
            return string.Empty;
        }
        var text = value.VarType == VtLpwstr ? Marshal.PtrToStringUni(value.Pointer) ?? string.Empty : string.Empty;
        NativeMethods.PropVariantClear(ref value);
        return text;
    }

    private static Guid ReadGuid(IPropertyStore store, PROPERTYKEY key)
    {
        if (store.GetValue(ref key, out var value) < 0)
        {
            return Guid.Empty;
        }
        var guid = value.VarType == VtClsid ? Marshal.PtrToStructure<Guid>(value.Pointer) : Guid.Empty;
        NativeMethods.PropVariantClear(ref value);
        return guid;
    }

    [GeneratedRegex("pid&([0-9a-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex ProductIdPattern();

    [GeneratedRegex(@"\s*(-\s*Find My)?\s*(Hands-Free|Stereo)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NameSuffixPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    private sealed class NotificationClient(Action onChanged) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, int newState)
        {
            onChanged();
            return 0;
        }

        public int OnDeviceAdded(string deviceId)
        {
            onChanged();
            return 0;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            onChanged();
            return 0;
        }

        public int OnDefaultDeviceChanged(int flow, int role, string? deviceId) => 0;

        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;
    }
}
