using System.Windows.Media;
using Ilot.Interop;

namespace Ilot;

public partial class MainWindow
{
    private const int PlayPauseHotkeyId = 2;
    private const int NextTrackHotkeyId = 3;
    private const int PreviousTrackHotkeyId = 4;
    private const int PreviousPageHotkeyId = 5;
    private const int NextPageHotkeyId = 6;
    private const uint VirtualKeySpace = 0x20;
    private const uint VirtualKeyLeft = 0x25;
    private const uint VirtualKeyUp = 0x26;
    private const uint VirtualKeyRight = 0x27;
    private const uint VirtualKeyDown = 0x28;
    private const uint OpenModifiers = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
    // Avec Maj : Ctrl + Alt + flèches sert déjà dans bien des logiciels, comme les éditeurs de code.
    private const uint MediaModifiers = OpenModifiers | NativeMethods.MOD_SHIFT;

    private static readonly (int Id, uint Modifiers, uint Key)[] GlobalHotkeys =
    [
        (HotkeyId, OpenModifiers, HotkeyVirtualKey),
        (PlayPauseHotkeyId, MediaModifiers, VirtualKeySpace),
        (NextTrackHotkeyId, MediaModifiers, VirtualKeyRight),
        (PreviousTrackHotkeyId, MediaModifiers, VirtualKeyLeft),
    ];

    // --- Raccourcis clavier globaux ---

    private void UpdateHotkeyRegistration()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return; // Refait dans OnSourceInitialized.
        }
        var isWanted = _settings.Current.GlobalHotkeyEnabled;
        if (isWanted != _isHotkeyRegistered)
        {
            foreach (var (id, _, _) in GlobalHotkeys)
            {
                NativeMethods.UnregisterHotKey(_hwnd, id);
            }
            _isHotkeyRegistered = isWanted;
            if (isWanted)
            {
                foreach (var (id, modifiers, key) in GlobalHotkeys)
                {
                    _isHotkeyRegistered &= NativeMethods.RegisterHotKey(_hwnd, id, modifiers, key);
                }
            }
        }
        App.IsHotkeyRegistered = _isHotkeyRegistered;
    }

    /// <summary>Le changement de page n'occupe Ctrl + Alt + flèches que le temps où le panneau est ouvert.</summary>
    private void SetPageHotkeys(bool isActive)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }
        if (isActive && _settings.Current.GlobalHotkeyEnabled)
        {
            NativeMethods.RegisterHotKey(_hwnd, PreviousPageHotkeyId, OpenModifiers, VirtualKeyUp);
            NativeMethods.RegisterHotKey(_hwnd, NextPageHotkeyId, OpenModifiers, VirtualKeyDown);
        }
        else
        {
            NativeMethods.UnregisterHotKey(_hwnd, PreviousPageHotkeyId);
            NativeMethods.UnregisterHotKey(_hwnd, NextPageHotkeyId);
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
