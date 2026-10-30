using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Ilot.Services;

internal sealed record SystemNotification(string AppName, string AppId, string Title, string Body, BitmapSource? Icon);

/// <summary>
/// Repère les nouvelles notifications Windows. L'événement NotificationChanged n'est pas disponible pour une
/// application non empaquetée (erreur 0x80070490), et une relecture du centre de notifications coûte environ 50 ms de CPU.
/// On surveille donc les métadonnées du journal de la base de Windows : elles changent en moins de 20 ms, se lisent pour
/// un coût négligeable, contrairement à FileSystemWatcher qui signale ces écritures avec plus de 10 s de retard.
/// Cet emplacement n'étant pas documenté, une relecture périodique reste en place en secours.
/// </summary>
internal sealed class SystemNotificationService
{
    private static readonly TimeSpan SignalCheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SlowPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FastPollInterval = TimeSpan.FromSeconds(3); // Si le journal n'existe pas.
    private const int IconDecodeWidth = 64;

    private readonly DispatcherTimer _pollTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _signalTimer = new(DispatcherPriority.Background) { Interval = SignalCheckInterval };
    private readonly FileInfo _signalFile = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications", "wpndatabase.db-wal"));
    private (long Length, DateTime LastWrite) _lastSignal;
    private HashSet<uint>? _known;
    private bool _isPolling;

    public SystemNotificationService()
    {
        _pollTimer.Tick += async (_, _) => await PollAsync();
        _signalTimer.Tick += async (_, _) => await CheckSignalAsync();
    }

    /// <summary>Levé sur le thread UI, une fois par nouvelle notification, de la plus ancienne à la plus récente.</summary>
    public event Action<SystemNotification>? Received;

    public static async Task<bool> RequestAccessAsync()
    {
        try
        {
            return await UserNotificationListener.Current.RequestAccessAsync() == UserNotificationListenerAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return false;
        }
    }

    public async Task StartAsync()
    {
        if (_pollTimer.IsEnabled || !await RequestAccessAsync())
        {
            return;
        }
        _lastSignal = ReadSignal();
        _pollTimer.Interval = _signalFile.Exists ? SlowPollInterval : FastPollInterval;
        _pollTimer.Start();
        _signalTimer.Start();
        await PollAsync(); // Référence prise tout de suite : une notification reçue juste après ne s'y fondrait pas.
    }

    public void Stop()
    {
        _pollTimer.Stop();
        _signalTimer.Stop();
        _known = null; // À la reprise, celles reçues entre-temps ne sont pas annoncées.
    }

    private (long Length, DateTime LastWrite) ReadSignal()
    {
        try
        {
            _signalFile.Refresh();
            return _signalFile.Exists ? (_signalFile.Length, _signalFile.LastWriteTimeUtc) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    private async Task CheckSignalAsync()
    {
        // Relecture en cours : le changement sera vu au passage suivant, plutôt que perdu.
        if (_isPolling)
        {
            return;
        }
        if (ReadSignal() != _lastSignal)
        {
            await PollAsync();
            // Relevé après la relecture : celle-ci fait elle-même écrire Windows dans ce journal, ce qui bouclerait sinon.
            _lastSignal = ReadSignal();
        }
    }

    private async Task PollAsync()
    {
        if (_isPolling)
        {
            return;
        }
        _isPolling = true;
        try
        {
            var notifications = await UserNotificationListener.Current.GetNotificationsAsync(NotificationKinds.Toast);
            var isFirstRead = _known is null;
            var previous = _known;
            _known = notifications.Select(notification => notification.Id).ToHashSet();
            if (isFirstRead || !_pollTimer.IsEnabled)
            {
                return; // La première lecture sert de référence.
            }

            foreach (var notification in notifications.Where(item => !previous!.Contains(item.Id)).OrderBy(item => item.CreationTime))
            {
                Received?.Invoke(await ConvertAsync(notification));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            // Service de notifications momentanément indisponible : nouvel essai au prochain passage.
        }
        finally
        {
            _isPolling = false;
        }
    }

    private static async Task<SystemNotification> ConvertAsync(UserNotification notification)
    {
        var appName = "Notification";
        var appId = string.Empty;
        BitmapSource? icon = null;
        try
        {
            var displayName = notification.AppInfo.DisplayInfo.DisplayName;
            appName = string.IsNullOrWhiteSpace(displayName) ? appName : displayName; // La bannière affiche sa première lettre.
            appId = notification.AppInfo.AppUserModelId;
            icon = await LoadIconAsync(notification);
        }
        catch (Exception)
        {
            // Applications classiques : GetLogo échoue, le dossier Applications prend le relais ci-dessous.
        }
        if (icon is null && appId.Length > 0)
        {
            icon = AppIconService.Get(appId);
        }

        var texts = notification.Notification.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
        var lines = texts?.Select(text => text.Text).Where(text => !string.IsNullOrWhiteSpace(text)).ToList() ?? [];
        var title = lines.Count > 0 ? lines[0] : appName;
        return new SystemNotification(appName, appId, title, string.Join(" ", lines.Skip(1)), icon);
    }

    private static async Task<BitmapSource> LoadIconAsync(UserNotification notification)
    {
        var reference = notification.AppInfo.DisplayInfo.GetLogo(new Size(IconDecodeWidth, IconDecodeWidth));
        using var winrtStream = await reference.OpenReadAsync();
        using var source = winrtStream.AsStreamForRead();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        buffer.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = IconDecodeWidth;
        image.StreamSource = buffer;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
