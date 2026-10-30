using System.Windows.Input;
using Ilot.Interop;

namespace Ilot.Services;

/// <summary>Un raccourci clavier, écrit « Ctrl+Alt+N » dans les réglages.</summary>
internal sealed record HotkeyGesture(uint Modifiers, Key Key)
{
    private const uint NoRepeat = 0x4000;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public uint NativeModifiers => Modifiers | NoRepeat;

    /// <summary>Faux sans Ctrl ni Alt ni Win : une simple lettre ou Maj + lettre empêcherait d'écrire partout ailleurs.</summary>
    public static bool IsAllowed(uint modifiers, Key key) =>
        (modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | ModWin)) != 0 && !IsModifierKey(key) && key != Key.None;

    public static HotkeyGesture? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        uint modifiers = 0;
        Key? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;
                case "ALT":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;
                case "MAJ":
                case "SHIFT":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;
                case "WIN":
                    modifiers |= ModWin;
                    break;
                default:
                    if (key is not null || !Enum.TryParse<Key>(part, ignoreCase: true, out var parsed))
                    {
                        return null;
                    }
                    key = parsed;
                    break;
            }
        }
        return key is { } found && IsAllowed(modifiers, found) ? new HotkeyGesture(modifiers, found) : null;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((Modifiers & NativeMethods.MOD_ALT) != 0)
        {
            parts.Add("Alt");
        }
        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0)
        {
            parts.Add("Maj");
        }
        if ((Modifiers & ModWin) != 0)
        {
            parts.Add("Win");
        }
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }

    private const uint ModWin = 0x0008;

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;
}

/// <summary>Les actions au clavier et leur raccourci d'origine.</summary>
internal static class HotkeyActions
{
    public const string TogglePanel = "toggle";
    public const string PlayPause = "playpause";
    public const string NextTrack = "next";
    public const string PreviousTrack = "previous";
    public const string PreviousPage = "pageprevious";
    public const string NextPage = "pagenext";

    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [TogglePanel] = "Ctrl+Alt+N",
        [PlayPause] = "Ctrl+Alt+Maj+Space",
        [NextTrack] = "Ctrl+Alt+Maj+Right",
        [PreviousTrack] = "Ctrl+Alt+Maj+Left",
        [PreviousPage] = "Ctrl+Alt+Up",
        [NextPage] = "Ctrl+Alt+Down",
    };

    /// <summary>Le raccourci choisi, ou celui d'origine si le réglage est absent ou illisible.</summary>
    public static HotkeyGesture Resolve(string action, IReadOnlyDictionary<string, string> custom) =>
        (custom.TryGetValue(action, out var text) ? HotkeyGesture.TryParse(text) : null)
        ?? HotkeyGesture.TryParse(Defaults[action])!;
}
