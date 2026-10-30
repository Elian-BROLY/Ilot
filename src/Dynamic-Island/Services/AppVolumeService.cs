using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

internal sealed record AppVolume(string Key, string Name, float Level, bool IsActive);

/// <summary>Volume de chaque application sur la sortie par défaut, regroupé par programme (Discord ouvre plusieurs sessions).</summary>
internal static class AppVolumeService
{
    private const int ERender = 0;
    private const int EMultimedia = 1;
    private const int ClsCtxAll = 23;
    private const int SessionStateActive = 1;
    private const int SessionStateExpired = 2;
    private const int HResultFalse = 1;

    private static readonly Dictionary<string, string> DisplayNames = [];

    public static Task<List<AppVolume>> ListAsync() => Task.Run(() =>
    {
        var byProgram = new Dictionary<string, AppVolume>();
        ForEachSession((key, session, volume) =>
        {
            session.GetState(out var state);
            volume.GetMasterVolume(out var level);
            var isActive = state == SessionStateActive;
            if (!byProgram.TryGetValue(key.Name, out var existing) || (isActive && !existing.IsActive))
            {
                byProgram[key.Name] = new AppVolume(key.Name, key.Display, level, isActive || (existing?.IsActive ?? false));
            }
        });
        return byProgram.Values.OrderByDescending(app => app.IsActive).ThenBy(app => app.Name).ToList();
    });

    public static Task SetLevelAsync(string key, float level) => Task.Run(() =>
        ForEachSession((session, _, volume) =>
        {
            if (session.Name == key)
            {
                volume.SetMasterVolume(Math.Clamp(level, 0f, 1f), IntPtr.Zero);
            }
        }));

    private static void ForEachSession(Action<(string Name, string Display), IAudioSessionControl2, ISimpleAudioVolume> action)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        object? managerObject = null;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out var device) < 0 || device is null)
            {
                return;
            }
            var iid = typeof(IAudioSessionManager2).GUID;
            var hr = device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out managerObject);
            Marshal.ReleaseComObject(device);
            if (hr < 0 || managerObject is not IAudioSessionManager2 manager
                || manager.GetSessionEnumerator(out var sessions) < 0 || sessions is null)
            {
                return;
            }

            sessions.GetCount(out var count);
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var session) < 0 || session is null)
                {
                    continue;
                }
                session.GetState(out var state);
                session.GetProcessId(out var processId);
                // S_OK signifie « sons système » : ni cette session, ni une session terminée, n'ont leur place ici.
                if (state != SessionStateExpired && session.IsSystemSoundsSession() == HResultFalse
                    && processId != 0 && session is ISimpleAudioVolume volume && Identify(processId) is { } identity)
                {
                    action(identity, session, volume);
                }
                Marshal.ReleaseComObject(session);
            }
            Marshal.ReleaseComObject(sessions);
        }
        finally
        {
            if (managerObject is not null)
            {
                Marshal.ReleaseComObject(managerObject);
            }
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static (string Name, string Display)? Identify(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var name = process.ProcessName;
            lock (DisplayNames)
            {
                if (!DisplayNames.TryGetValue(name, out var display))
                {
                    display = ReadProductName(process) ?? name;
                    DisplayNames[name] = display;
                }
                return (name, display);
            }
        }
        catch (ArgumentException)
        {
            return null; // Processus déjà terminé.
        }
    }

    private static string? ReadProductName(Process process)
    {
        try
        {
            var info = process.MainModule?.FileVersionInfo;
            return !string.IsNullOrWhiteSpace(info?.FileDescription) ? info.FileDescription : info?.ProductName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null; // Processus protégé : son nom technique suffit.
        }
    }
}
