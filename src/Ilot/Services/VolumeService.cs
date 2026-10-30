using System.Runtime.InteropServices;
using Ilot.Interop;

namespace Ilot.Services;

/// <summary>Volume master du périphérique de sortie par défaut. À appeler depuis le thread UI uniquement.</summary>
internal sealed class VolumeService
{
    private const int ERender = 0;
    private const int EMultimedia = 1;
    private const int ClsCtxAll = 23;

    private IMMDeviceEnumerator? _enumerator;

    public bool TryRead(out float level, out bool isMuted)
    {
        var success = false;
        var readLevel = 0f;
        var readMuted = false;
        WithEndpoint(endpoint =>
        {
            success = endpoint.GetMasterVolumeLevelScalar(out readLevel) >= 0 && endpoint.GetMute(out readMuted) >= 0;
        });
        level = readLevel;
        isMuted = readMuted;
        return success;
    }

    /// <summary>Change le volume d'un écart, et renvoie le nouveau niveau. Null s'il n'y a pas de sortie audio.</summary>
    public float? ChangeLevel(float delta)
    {
        if (!TryRead(out var level, out _))
        {
            return null;
        }
        var next = Math.Clamp(level + delta, 0f, 1f);
        SetLevel(next);
        return next;
    }

    public void SetLevel(float level) =>
        WithEndpoint(endpoint => endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), IntPtr.Zero));

    public void SetMute(bool mute) => WithEndpoint(endpoint => endpoint.SetMute(mute, IntPtr.Zero));

    // Le périphérique est résolu à chaque appel pour suivre un changement de sortie (casque, HDMI...).
    private void WithEndpoint(Action<IAudioEndpointVolume> action)
    {
        _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
        if (_enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var device) < 0 || device is null)
        {
            return; // Aucune sortie audio active.
        }

        try
        {
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out var endpoint) < 0 || endpoint is null)
            {
                return;
            }
            try
            {
                action((IAudioEndpointVolume)endpoint);
            }
            finally
            {
                Marshal.ReleaseComObject(endpoint);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }
}
