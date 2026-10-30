using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Ilot.Services;

/// <summary>
/// Détecte l'usage du micro et de la caméra via le registre tenu à jour par Windows : une application
/// les utilise tant que sa valeur LastUsedTimeStop vaut 0. Cet emplacement n'est pas documenté par Microsoft.
/// </summary>
internal sealed class PrivacyIndicatorService
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
    private const string NonPackaged = "NonPackaged";

    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };

    public PrivacyIndicatorService() => _timer.Tick += (_, _) => Refresh();

    public event Action? Changed;

    public bool IsMicrophoneInUse { get; private set; }

    public bool IsCameraInUse { get; private set; }

    public void Start()
    {
        _timer.Start();
        Refresh();
    }

    public void Stop()
    {
        _timer.Stop();
        Apply(false, false);
    }

    private void Refresh() => Apply(IsInUse("microphone"), IsInUse("webcam"));

    private void Apply(bool microphone, bool camera)
    {
        if (microphone != IsMicrophoneInUse || camera != IsCameraInUse)
        {
            IsMicrophoneInUse = microphone;
            IsCameraInUse = camera;
            Changed?.Invoke();
        }
    }

    private static bool IsInUse(string capability)
    {
        using var root = Registry.CurrentUser.OpenSubKey(ConsentStore + capability);
        if (root is null)
        {
            return false;
        }

        // Applications du Store directement sous la clé, applications classiques sous NonPackaged.
        foreach (var name in root.GetSubKeyNames())
        {
            using var app = root.OpenSubKey(name);
            if (app is null)
            {
                continue;
            }
            if (name == NonPackaged)
            {
                foreach (var classicName in app.GetSubKeyNames())
                {
                    using var classic = app.OpenSubKey(classicName);
                    if (IsActive(classic) && IsProgramRunning(classicName))
                    {
                        return true;
                    }
                }
            }
            else if (IsActive(app))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsActive(RegistryKey? app) =>
        app?.GetValue("LastUsedTimeStop") is long stop && app.GetValue("LastUsedTimeStart") is long start
        && IsActive(start, stop, DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64));

    /// <summary>Une application qui plante laisse sa fin à 0 : un début antérieur au démarrage du PC ne compte donc pas.</summary>
    internal static bool IsActive(long start, long stop, DateTime bootUtc) =>
        stop == 0 && start > 0 && DateTime.FromFileTimeUtc(start) >= bootUtc;

    // Une application classique qui a planté n'a plus de processus. Vérifié seulement si le registre la dit active.
    private static bool IsProgramRunning(string keyName)
    {
        var processes = Process.GetProcessesByName(ProgramNameFromKey(keyName));
        var isRunning = processes.Length > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }
        return isRunning;
    }

    /// <summary>« C:#Program Files#Discord#Discord.exe » devient « Discord ».</summary>
    internal static string ProgramNameFromKey(string keyName) =>
        Path.GetFileNameWithoutExtension(keyName[(keyName.LastIndexOf('#') + 1)..]);
}
