using System.Runtime.InteropServices;
using System.Windows.Interop;
using Ilot.Interop;

namespace Ilot.Services;

/// <summary>Icône de zone de notification en Win32 pur, pour éviter le coût mémoire de WinForms.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = NativeMethods.WM_APP + 1;
    private const uint QuitCommand = 1;
    private const uint SettingsCommand = 2;

    private readonly Action _onSettings;
    private readonly Action _onQuit;
    private readonly HwndSource _source;
    private readonly uint _taskbarCreatedMessage;
    private readonly IntPtr _icon;
    private NativeMethods.NOTIFYICONDATA _data;

    public TrayIcon(Action onSettings, Action onQuit)
    {
        _onSettings = onSettings;
        _onQuit = onQuit;
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _source = new HwndSource(new HwndSourceParameters("IlotTray") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);

        var small = new IntPtr[1];
        NativeMethods.ExtractIconEx(Environment.ProcessPath!, 0, null, small, 1);
        _icon = small[0];

        _data = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
            szTip = "Ilot",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _data);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage && lParam.ToInt32() == NativeMethods.WM_RBUTTONUP)
        {
            ShowMenu();
            handled = true;
        }
        else if (msg == _taskbarCreatedMessage)
        {
            // L'Explorateur a redémarré : l'icône doit être recréée.
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _data);
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(SettingsCommand), "Réglages…");
        NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(QuitCommand), "Quitter");

        NativeMethods.GetCursorPos(out var cursor);
        // Sans ces deux appels, le menu ne se ferme pas quand on clique ailleurs.
        NativeMethods.SetForegroundWindow(_source.Handle);
        var command = NativeMethods.TrackPopupMenu(menu, NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
            cursor.X, cursor.Y, 0, _source.Handle, IntPtr.Zero);
        NativeMethods.PostMessage(_source.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);

        if (command == QuitCommand)
        {
            _onQuit();
        }
        else if (command == SettingsCommand)
        {
            _onSettings();
        }
    }

    public void Dispose()
    {
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _data);
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }
        _source.Dispose();
    }
}
