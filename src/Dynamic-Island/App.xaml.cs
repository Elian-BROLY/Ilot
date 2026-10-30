using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Services;
using DynamicIsland.Settings;

namespace DynamicIsland;

public partial class App : Application
{
    private const string ReopenSettingsArgument = "--settings";

    private bool _isRestarting;
    private Mutex? _singleInstance;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private MainWindow? _mainWindow;

    internal static SettingsStore Settings { get; } = new();

    internal static MailService Mail { get; set; } = new();

    internal static bool IsHotkeyRegistered { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Dynamic-Island.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        // Une fenêtre aussi petite n'a pas besoin du GPU, et éviter Direct3D économise beaucoup de mémoire.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        ThemeService.ApplyAtStartup(Settings.Current);
        AutostartService.RepointToInstalledCopy();
        _tray = new TrayIcon(ShowSettings, Shutdown);
        _mainWindow = new MainWindow();
        _mainWindow.Show();
        if (e.Args.Contains(ReopenSettingsArgument))
        {
            ShowSettings();
        }
    }

    /// <summary>Relance l'application, réglages rouverts : le changement de thème vient toujours de cette fenêtre.</summary>
    internal static void RestartToApplyTheme()
    {
        var app = (App)Current;
        if (!app._isRestarting)
        {
            app._isRestarting = true;
            app.Dispatcher.BeginInvoke(() => app.Shutdown());
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) =>
            {
                _settingsWindow = null;
                _mainWindow?.ScheduleMemoryTrim();
            };
            _settingsWindow.Show();
        }
        _settingsWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _singleInstance?.Dispose();
        // Après la libération de l'instance unique : sinon, la nouvelle copie se fermerait aussitôt.
        if (_isRestarting && Environment.ProcessPath is { } path)
        {
            System.Diagnostics.Process.Start(path, ReopenSettingsArgument);
        }
        base.OnExit(e);
    }
}
