using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Ilot.Interop;
using Ilot.Services;
using Ilot.Settings;

namespace Ilot;

public partial class App : Application
{
    private const string ReopenSettingsArgument = "--settings";
    private const string ShowSettingsEventName = @"Local\Ilot.ShowSettings";

    private bool _isRestarting;
    private bool _reopenSettingsAfterRestart;
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSettingsEvent;
    private RegisteredWaitHandle? _showSettingsWait;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private MainWindow? _mainWindow;

    internal static SettingsStore Settings { get; } = new();

    internal static MailService Mail { get; set; } = new();

    internal static UpdateService Updates { get; } = new();

    internal static bool IsHotkeyRegistered { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            // Une capsule qui disparaît sans prévenir est pire qu'une page qui ne répond plus : on note l'erreur et on continue.
            AppLog.Write(args.Exception, "Erreur non gérée");
            args.Handled = true;
        };

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Ilot.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            AskFirstInstanceToShowSettings();
            Shutdown();
            return;
        }

        try
        {
            ListenForOtherInstances();

            // Une fenêtre aussi petite n'a pas besoin du GPU, et éviter Direct3D économise beaucoup de mémoire.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            Loc.SetLanguage(Settings.Current.Language);
            ThemeService.ApplyAtStartup(Settings.Current);
            AutostartService.RepointToInstalledCopy();
            _tray = new TrayIcon(ShowSettings, Shutdown);
            _mainWindow = new MainWindow();
            _mainWindow.Show();
            if (Settings.Current.SetupCompleted == false)
            {
                new SetupWindow().Show(); // Prioritaire : un changement de langue depuis l'assistant y revient.
            }
            else if (e.Args.Contains(ReopenSettingsArgument))
            {
                ShowSettings();
            }
        }
        catch (Exception ex)
        {
            // Sans capsule, le processus resterait invisible et l'instance unique empêcherait toute relance.
            AppLog.Write(ex, "Démarrage");
            MessageBox.Show(Loc.T("Ilot n'a pas pu démarrer. Le détail de l'erreur se trouve dans %APPDATA%\\Ilot\\app.log."),
                "Ilot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // Relancer l'application alors qu'elle tourne déjà ouvre ses réglages : sinon, rien ne se passerait à l'écran.
    private static void AskFirstInstanceToShowSettings()
    {
        if (EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var showSettings))
        {
            using (showSettings)
            {
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                showSettings.Set();
            }
        }
    }

    private void ListenForOtherInstances()
    {
        _showSettingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        _showSettingsWait = ThreadPool.RegisterWaitForSingleObject(_showSettingsEvent,
            (_, _) => Dispatcher.BeginInvoke(ShowSettings), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Relance l'application. Les réglages sont rouverts si le changement de thème vient de leur fenêtre.</summary>
    internal static void RestartToApplyTheme(bool reopenSettings = true)
    {
        var app = (App)Current;
        if (!app._isRestarting)
        {
            app._isRestarting = true;
            app._reopenSettingsAfterRestart = reopenSettings;
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
        _showSettingsWait?.Unregister(null);
        _showSettingsEvent?.Dispose();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        // Après la libération de l'instance unique : sinon, la nouvelle copie se fermerait aussitôt.
        if (_isRestarting && Environment.ProcessPath is { } path)
        {
            System.Diagnostics.Process.Start(path, _reopenSettingsAfterRestart ? ReopenSettingsArgument : string.Empty);
        }
        base.OnExit(e);
    }
}
