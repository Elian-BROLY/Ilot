using System.Runtime.InteropServices;
using Ilot.Interop;

namespace Ilot.Services;

internal sealed record AudioOutput(string Id, string Name, string FullName, bool IsDefault);

/// <summary>Sorties audio actives et changement de sortie par défaut. Sur le pool de threads : ces appels peuvent bloquer.</summary>
internal static class AudioOutputService
{
    private const int ERender = 0;
    private const int EMultimedia = 1;
    private const int DeviceStateActive = 1;
    private static readonly int[] AllRoles = [0, 1, 2]; // Console, multimédia, communications.

    public static Task<List<AudioOutput>> ListAsync() => Task.Run(() =>
    {
        var outputs = new List<AudioOutput>();
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            var defaultId = string.Empty;
            if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var defaultDevice) >= 0 && defaultDevice is not null)
            {
                defaultDevice.GetId(out var id);
                defaultId = id ?? string.Empty;
                Marshal.ReleaseComObject(defaultDevice);
            }

            if (enumerator.EnumAudioEndpoints(ERender, DeviceStateActive, out var collection) < 0 || collection is null)
            {
                return outputs;
            }
            collection.GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) < 0 || device is null)
                {
                    continue;
                }
                device.GetId(out var id);
                device.OpenPropertyStore(0, out var store);
                if (id is not null && store is not null)
                {
                    var fullName = BluetoothAudioService.ReadFriendlyName(store);
                    outputs.Add(new AudioOutput(id, ShortName(fullName), fullName, id == defaultId));
                    Marshal.ReleaseComObject(store);
                }
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(collection);
            return outputs;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    });

    public static Task<bool> SetDefaultAsync(string deviceId) => Task.Run(() =>
    {
        var policy = (IPolicyConfig)new PolicyConfigClient();
        try
        {
            return AllRoles.All(role => policy.SetDefaultEndpoint(deviceId, role) >= 0);
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    });

    // "Haut-parleurs (Realtek(R) Audio)" devient "Haut-parleurs" ; "Casque (AirPods de Elian  - Find My)", "AirPods de Elian".
    private static string ShortName(string fullName)
    {
        var start = fullName.IndexOf('(');
        if (start <= 0)
        {
            return fullName;
        }
        if (fullName.Contains("AirPods", StringComparison.OrdinalIgnoreCase))
        {
            return BluetoothAudioService.CleanName(fullName);
        }
        return fullName[..start].Trim();
    }
}
