using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ilot.Services;

namespace Ilot.Widgets;

/// <summary>Les derniers éléments copiés : un clic les remet dans le presse-papiers, prêts à être collés.</summary>
public partial class ClipboardWidget : UserControl
{
    private const int PreviewLength = 120;

    private readonly ClipboardHistory _history = new();
    private readonly DispatcherTimer _copiedTimer = new() { Interval = TimeSpan.FromSeconds(1.2) };
    private ClipboardEntry? _justCopied;
    private bool _isWritingOwnEntry;

    public ClipboardWidget()
    {
        InitializeComponent();
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            _justCopied = null;
            Render();
        };
        Render();
    }

    /// <summary>Si la liste déborde, la molette la fait défiler plutôt que de changer de page.</summary>
    public bool IsContentScrollable => Scroller.Visibility == Visibility.Visible && Scroller.ScrollableHeight > 0;

    /// <summary>Appelé quand le presse-papiers de Windows change.</summary>
    public void OnClipboardChanged()
    {
        if (_isWritingOwnEntry)
        {
            // Une image relue après coup ne donne pas forcément le même PNG : elle apparaîtrait deux fois.
            _isWritingOwnEntry = false;
            return;
        }
        if (ClipboardReader.ReadCurrent() is { } entry)
        {
            _history.Add(entry);
            Render();
        }
    }

    /// <summary>Page masquée dans les réglages : ce qui a été copié n'a plus à rester en mémoire.</summary>
    public void Clear()
    {
        _history.Clear();
        Render();
    }

    private void Render()
    {
        Items.Children.Clear();
        foreach (var entry in _history.Entries)
        {
            Items.Children.Add(CreateRow(entry));
        }
        EmptyText.Visibility = _history.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Scroller.Visibility = _history.Entries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border CreateRow(ClipboardEntry entry)
    {
        FrameworkElement mark = entry.Thumbnail is { } thumbnail
            ? new Image { Source = thumbnail, Height = 20, MaxWidth = 36, Margin = new Thickness(0, 0, 8, 0), Stretch = Stretch.Uniform }
            : new TextBlock
            {
                Width = 22,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Text = "",
            };
        var label = entry == _justCopied ? Loc.T("Copié : collez-le où vous voulez")
            : entry.Text is { } text ? OneLine(text)
            : Loc.T("Image {0} × {1}", entry.Width.ToString(CultureInfo.InvariantCulture), entry.Height.ToString(CultureInfo.InvariantCulture));
        var name = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource(entry == _justCopied ? "AccentBrush" : "TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = label,
        };
        var remove = new Button
        {
            Width = 20,
            Height = 20,
            FontSize = 9,
            Style = (Style)FindResource("IconButton"),
            Content = "",
            ToolTip = Loc.T("Retirer de la liste"),
        };
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            _history.Remove(entry);
            Render();
        };

        var content = new DockPanel { Margin = new Thickness(6, 0, 4, 0) };
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        content.Children.Add(mark);
        content.Children.Add(remove);
        content.Children.Add(name);

        var row = new Border { Height = 26, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = content };
        row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("SurfaceBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        // Géré ici : un clic sur un élément ne doit ni verrouiller ni déplacer la capsule.
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        row.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            _isWritingOwnEntry = true;
            _isWritingOwnEntry = ClipboardReader.TryWrite(entry);
            if (_isWritingOwnEntry)
            {
                _history.Add(entry);
                _justCopied = entry;
                _copiedTimer.Stop();
                _copiedTimer.Start();
                Render();
            }
        };
        return row;
    }

    private static string OneLine(string text)
    {
        var line = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length > PreviewLength ? line[..PreviewLength] : line;
    }
}
