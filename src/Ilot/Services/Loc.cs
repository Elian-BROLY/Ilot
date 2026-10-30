using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Ilot.Services;

/// <summary>
/// Traduction de l'interface. Le texte français sert de clé : il reste lisible dans le code et dans le XAML, et un texte
/// oublié s'affiche en français plutôt que de disparaître. La liste des traductions est dans LocEnglish.cs.
/// </summary>
internal static partial class Loc
{
    private static bool _isEnglish;

    /// <summary>« fr » ou « en ». Une langue vide suit celle de Windows : anglais si Windows n'est pas en français.</summary>
    public static string Resolve(string language) =>
        language is "fr" or "en" ? language
        : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";

    public static void SetLanguage(string language) => _isEnglish = Resolve(language) == "en";

    /// <summary>« fr » ou « en », pour les services qui renvoient du texte dans une langue donnée.</summary>
    public static string Language => _isEnglish ? "en" : "fr";

    public static string T(string french) => _isEnglish ? ToEnglish(french) : french;

    public static string T(string french, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, T(french), values);

    internal static string ToEnglish(string french) => English.GetValueOrDefault(french, french);

    /// <summary>Traduit les textes écrits en XAML d'une fenêtre ou d'un contrôle, et de tout ce qu'il contient.</summary>
    public static void Translate(DependencyObject root)
    {
        if (!_isEnglish)
        {
            return;
        }
        switch (root)
        {
            case Window window:
                window.Title = T(window.Title);
                break;
            case TextBlock { Text.Length: > 0 } text:
                text.Text = T(text.Text);
                break;
            case HeaderedContentControl { Header: string header } headered:
                headered.Header = T(header);
                break;
        }
        if (root is ContentControl { Content: string content } control)
        {
            control.Content = T(content);
        }
        if (root is FrameworkElement { ToolTip: string toolTip } element)
        {
            element.ToolTip = T(toolTip);
        }
        if (AutomationProperties.GetName(root) is { Length: > 0 } name)
        {
            AutomationProperties.SetName(root, T(name));
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            Translate(child);
        }
    }
}
