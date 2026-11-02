using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Ilot.Interop;
using Ilot.Services;

namespace Ilot;

/// <summary>Alertes de la capsule : réseau, batterie, chargeur, téléchargements, Verr. Maj et micro.</summary>
public partial class MainWindow
{
    private const string DownloadGlyph = "\uE896";
    private const string KeyboardGlyph = "\uE765";
    private const string MicrophoneGlyph = "\uE720";
    private const string MicrophoneOffGlyph = "\uEC54";
    private static readonly TimeSpan KeyBannerDuration = TimeSpan.FromSeconds(1.5);

    private readonly VolumeService _microphone = new(isMicrophone: true);
    private readonly DispatcherTimer _microphoneTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private DownloadWatcher? _downloads;
    private bool? _wasMicrophoneMuted;
    private bool _isRawKeyboardRegistered;
    private bool _isCapsLockOn;
    private bool _isCapsLockDown;

    // --- Réseau et batterie ---

    private void CheckNetworkAvailability()
    {
        _networkAlertTimer.Stop();
        var isUp = NetworkStatus.IsOnline();
        if (isUp == _isNetworkUp)
        {
            return;
        }
        _isNetworkUp = isUp;
        if (_settings.Current.NetworkAlertsEnabled && !IsInFocusMode)
        {
            ShowInfoBanner("", isUp ? Loc.T("Connexion rétablie") : Loc.T("Connexion perdue"), isUp ? Loc.T("Vous êtes de nouveau en ligne") : Loc.T("Aucun réseau disponible"));
        }
    }

    // Aussi appelé chaque minute : tous les PC ne signalent pas chaque variation du niveau de batterie.
    private void CheckPowerStatus()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status)
            || (status.BatteryFlag & 128) != 0 // Pas de batterie.
            || status.BatteryLifePercent == 255)
        {
            return;
        }

        var isOnAcPower = status.ACLineStatus == 1;
        var percent = status.BatteryLifePercent.ToString(CultureInfo.InvariantCulture) + " %";
        var crossed = _pcBatteryPolicy.Update(status.BatteryLifePercent, isOnAcPower);
        var hasPlugChanged = _wasOnAcPower is { } wasOn && wasOn != isOnAcPower;
        _wasOnAcPower = isOnAcPower;

        if (!_settings.Current.BatteryAlertsEnabled)
        {
            return;
        }
        if (hasPlugChanged)
        {
            ShowInfoBanner(isOnAcPower ? "" : "", isOnAcPower ? Loc.T("Chargeur branché") : Loc.T("Chargeur débranché"), Loc.T("Batterie à {0}", percent));
        }
        if (crossed is not null)
        {
            ShowInfoBanner("", Loc.T("Batterie faible"), Loc.T("Plus que {0} : pensez à brancher le chargeur", percent), evenInQuietHours: true);
        }
    }

    private void OnAirPodsBatteryChanged()
    {
        BannerRings.Update(AirPodsPanel.Battery);
        if (AirPodsPanel.Battery is not { } battery || !AirPodsPanel.IsConnected)
        {
            return;
        }
        // Le plus faible des deux écouteurs décide. Le boîtier n'est pas concerné : il n'a pas besoin d'être en poche.
        int?[] levels = [battery.Left, battery.Right];
        if (levels.Min() is not { } lowest)
        {
            return;
        }
        if (!_airPodsLowReadings.IsTrusted(lowest))
        {
            return; // Une annonce isolée peut venir d'une paire voisine.
        }
        var crossed = _airPodsBatteryPolicy.Update(lowest, battery.IsLeftCharging || battery.IsRightCharging);
        if (crossed is not null && _settings.Current.BatteryAlertsEnabled)
        {
            ShowInfoBanner("", AirPodsPanel.IsAirPods ? Loc.T("Batterie des AirPods faible") : Loc.T("Batterie faible : {0}", AirPodsPanel.DeviceName), Loc.T("Environ {0} %", lowest), evenInQuietHours: true);
        }
    }

    // --- Téléchargements ---

    private bool IsDownloading => _downloads?.IsActive == true;

    private void UpdateDownloadWatching()
    {
        if (!_settings.Current.DownloadAlertsEnabled)
        {
            _downloads?.Stop();
            return;
        }
        if (_downloads is null)
        {
            _downloads = new DownloadWatcher(Dispatcher);
            _downloads.Completed += OnDownloadCompleted;
            _downloads.ActivityChanged += UpdateCompactIndicators;
        }
        _downloads.Start();
    }

    private void OnDownloadCompleted(string path)
    {
        if (!IsInFocusMode)
        {
            ShowInfoBanner(DownloadGlyph, Loc.T("Téléchargement terminé"), Path.GetFileName(path), () => RevealInExplorer(path));
        }
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    // --- Verr. Maj et micro ---

    private void UpdateKeyIndicators()
    {
        var isWanted = _settings.Current.KeyIndicatorsEnabled;
        if (isWanted)
        {
            _microphoneTimer.Start();
        }
        else
        {
            _microphoneTimer.Stop();
            _wasMicrophoneMuted = null;
        }
        if (_hwnd == IntPtr.Zero || isWanted == _isRawKeyboardRegistered)
        {
            return; // Refait dans OnSourceInitialized.
        }
        // Les touches arrivent ici même quand une autre fenêtre a le focus, sans ralentir la frappe comme un crochet clavier.
        var device = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = 1, // Commandes génériques.
            usUsage = 6, // Clavier.
            dwFlags = isWanted ? NativeMethods.RIDEV_INPUTSINK : NativeMethods.RIDEV_REMOVE,
            hwndTarget = isWanted ? _hwnd : IntPtr.Zero,
        };
        if (NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>()))
        {
            _isRawKeyboardRegistered = isWanted;
            _isCapsLockOn = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 1) != 0;
        }
    }

    private void OnRawInput(IntPtr rawInput)
    {
        var headerSize = (uint)Marshal.SizeOf<NativeMethods.RAWINPUTHEADER>();
        uint size = 0;
        NativeMethods.GetRawInputData(rawInput, NativeMethods.RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size < headerSize + Marshal.SizeOf<NativeMethods.RAWKEYBOARD>() || size > 1024)
        {
            return;
        }
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (NativeMethods.GetRawInputData(rawInput, NativeMethods.RID_INPUT, buffer, ref size, headerSize) != size
                || Marshal.PtrToStructure<NativeMethods.RAWINPUTHEADER>(buffer).dwType != NativeMethods.RIM_TYPEKEYBOARD)
            {
                return;
            }
            var keyboard = Marshal.PtrToStructure<NativeMethods.RAWKEYBOARD>(buffer + (int)headerSize);
            if (keyboard.VKey == NativeMethods.VK_CAPITAL)
            {
                OnCapsLockKey(keyboard.Message);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // L'état est suivi ici plutôt que relu : pour une fenêtre sans le focus, Windows ne tient pas l'état des touches à jour.
    private void OnCapsLockKey(uint message)
    {
        if (message == NativeMethods.WM_KEYUP)
        {
            _isCapsLockDown = false;
            return;
        }
        if (message != NativeMethods.WM_KEYDOWN || _isCapsLockDown)
        {
            return; // Touche maintenue : la répétition ne change rien.
        }
        _isCapsLockDown = true;
        _isCapsLockOn = !_isCapsLockOn;
        ShowKeyBanner(KeyboardGlyph, _isCapsLockOn ? Loc.T("Majuscules activées") : Loc.T("Majuscules désactivées"), Loc.T("Verr. Maj"));
    }

    private void CheckMicrophoneMute()
    {
        if (!_microphone.TryRead(out _, out var isMuted))
        {
            _wasMicrophoneMuted = null; // Aucun micro : son retour ne doit pas passer pour un changement.
            return;
        }
        var hasChanged = _wasMicrophoneMuted is { } wasMuted && wasMuted != isMuted;
        _wasMicrophoneMuted = isMuted;
        if (hasChanged)
        {
            ShowKeyBanner(isMuted ? MicrophoneOffGlyph : MicrophoneGlyph, isMuted ? Loc.T("Micro coupé") : Loc.T("Micro réactivé"), Loc.T("Micro par défaut de Windows"));
        }
    }

    /// <summary>
    /// Réponse immédiate à un geste : courte, jamais mise en attente, et sans effacer une bannière plus importante, comme celle
    /// d'un mail.
    /// </summary>
    private void ShowKeyBanner(string glyph, string title, string status)
    {
        if (_isExpanded || _isHiddenForFullscreen || (_isBannerVisible && !_isTransientBannerVisible))
        {
            return;
        }
        SetBannerContent(title, status, click: null, glyph, isGlyph: true);
        BannerMailMark.Width = BannerMailMark.Height = TrackBannerMarkSize;
        _isTransientBannerVisible = true;
        PresentBanner(KeyBannerDuration, TrackBannerWidth, TrackBannerHeight);
    }
}
