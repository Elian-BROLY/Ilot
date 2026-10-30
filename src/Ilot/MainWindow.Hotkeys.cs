using System.Windows.Media;
using Ilot.Interop;
using Ilot.Services;

namespace Ilot;

public partial class MainWindow
{
    private const int PlayPauseHotkeyId = 2;
    private const int NextTrackHotkeyId = 3;
    private const int PreviousTrackHotkeyId = 4;
    private const int PreviousPageHotkeyId = 5;
    private const int NextPageHotkeyId = 6;

    private static readonly (int Id, string Action)[] GlobalHotkeys =
    [
        (HotkeyId, HotkeyActions.TogglePanel),
        (PlayPauseHotkeyId, HotkeyActions.PlayPause),
        (NextTrackHotkeyId, HotkeyActions.NextTrack),
        (PreviousTrackHotkeyId, HotkeyActions.PreviousTrack),
    ];

    private string _hotkeySignature = string.Empty;

    // --- Raccourcis clavier globaux ---

    private void UpdateHotkeyRegistration()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return; // Refait dans OnSourceInitialized.
        }
        var isWanted = _settings.Current.GlobalHotkeyEnabled;
        var custom = _settings.Current.Hotkeys;
        var signature = string.Join("|", HotkeyActions.Defaults.Keys.Select(action => HotkeyActions.Resolve(action, custom).ToString()));
        // Si l'enregistrement a échoué en partie, certains raccourcis restent actifs : ils doivent aussi être retirés.
        if (!isWanted || !_isHotkeyRegistered || signature != _hotkeySignature)
        {
            _hotkeySignature = signature;
            foreach (var (id, _) in GlobalHotkeys)
            {
                NativeMethods.UnregisterHotKey(_hwnd, id);
            }
            _isHotkeyRegistered = isWanted;
            if (isWanted)
            {
                foreach (var (id, action) in GlobalHotkeys)
                {
                    var gesture = HotkeyActions.Resolve(action, custom);
                    _isHotkeyRegistered &= NativeMethods.RegisterHotKey(_hwnd, id, gesture.NativeModifiers, gesture.VirtualKey);
                }
            }
        }
        App.IsHotkeyRegistered = _isHotkeyRegistered;
        SetPageHotkeys(_isExpanded); // Le réglage a pu changer alors que le panneau est ouvert.
    }

    /// <summary>Le changement de page n'occupe ses raccourcis que le temps où le panneau est ouvert.</summary>
    private void SetPageHotkeys(bool isActive)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        NativeMethods.UnregisterHotKey(_hwnd, PreviousPageHotkeyId);
        NativeMethods.UnregisterHotKey(_hwnd, NextPageHotkeyId);
        if (isActive && _settings.Current.GlobalHotkeyEnabled)
        {
            var custom = _settings.Current.Hotkeys;
            var previous = HotkeyActions.Resolve(HotkeyActions.PreviousPage, custom);
            var next = HotkeyActions.Resolve(HotkeyActions.NextPage, custom);
            NativeMethods.RegisterHotKey(_hwnd, PreviousPageHotkeyId, previous.NativeModifiers, previous.VirtualKey);
            NativeMethods.RegisterHotKey(_hwnd, NextPageHotkeyId, next.NativeModifiers, next.VirtualKey);
        }
    }

    private void OnHotkey(int id)
    {
        var media = MediaPanel.Service;
        switch (id)
        {
            case HotkeyId:
                ToggleFromHotkey();
                break;
            case PlayPauseHotkeyId:
                _ = media.TogglePlayPauseAsync();
                break;
            case NextTrackHotkeyId:
                _ = media.NextAsync();
                break;
            case PreviousTrackHotkeyId:
                _ = media.PreviousAsync();
                break;
            case PreviousPageHotkeyId:
                ShowRelativePage(-1);
                break;
            case NextPageHotkeyId:
                ShowRelativePage(1);
                break;
        }
    }

    private void ToggleFromHotkey()
    {
        if (_isHiddenForFullscreen)
        {
            return;
        }
        _expandTimer.Stop();
        _collapseTimer.Stop();
        if (_isExpanded)
        {
            _isPinned = false;
            PinGlyph.Text = "";
            PinGlyph.Foreground = (Brush)FindResource("TextSecondaryBrush");
            EndTyping();
            Collapse();
        }
        else
        {
            // Reste ouverte sans la souris : aucun MouseLeave ne viendra la refermer.
            Expand();
        }
    }
}
