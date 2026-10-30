using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ilot.Services;

namespace Ilot.Widgets;

/// <summary>Le dépôt de fichiers : on y glisse un fichier pendant qu'on change de fenêtre, puis on le reprend en le glissant ailleurs.</summary>
public partial class FilesWidget : UserControl
{
    private const int DragThreshold = 4;

    private readonly FileShelf _shelf = new();
    private Point? _pressPoint;

    public FilesWidget()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _shelf.RemoveMissing())
            {
                Render();
            }
        };
        Render();
    }

    /// <summary>Si la liste déborde, la molette la fait défiler plutôt que de changer de page.</summary>
    public bool IsContentScrollable => Scroller.Visibility == Visibility.Visible && Scroller.ScrollableHeight > 0;

    public void AddFiles(IEnumerable<string> paths)
    {
        _shelf.Add(paths);
        Render();
    }

    private void Render()
    {
        Items.Children.Clear();
        foreach (var path in _shelf.Files)
        {
            Items.Children.Add(CreateRow(path));
        }
        EmptyText.Visibility = _shelf.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Scroller.Visibility = _shelf.Files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border CreateRow(string path)
    {
        var isFolder = Directory.Exists(path);
        var icon = new TextBlock
        {
            Width = 22,
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Text = isFolder ? "" : "",
        };
        var name = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
            ToolTip = path,
        };
        var remove = new Button
        {
            Width = 20,
            Height = 20,
            FontSize = 9,
            Style = (Style)FindResource("IconButton"),
            Content = "",
            ToolTip = Loc.T("Retirer de la liste"),
        };
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            _shelf.Remove(path);
            Render();
        };

        var content = new DockPanel { Margin = new Thickness(6, 0, 4, 0) };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        content.Children.Add(icon);
        content.Children.Add(remove);
        content.Children.Add(name);

        var row = new Border { Height = 26, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = content };
        row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("SurfaceBrush");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonDown += (_, e) =>
        {
            // Géré ici : un clic sur le fichier ne doit ni verrouiller ni déplacer la capsule.
            e.Handled = true;
            _pressPoint = e.GetPosition(row);
        };
        row.MouseMove += (_, e) =>
        {
            if (_pressPoint is not { } start || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }
            var delta = e.GetPosition(row) - start;
            if (Math.Abs(delta.X) >= DragThreshold || Math.Abs(delta.Y) >= DragThreshold)
            {
                _pressPoint = null;
                // Copie : c'est le geste le moins risqué, le fichier d'origine ne bouge pas.
                DragDrop.DoDragDrop(row, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
            }
        };
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (_pressPoint is not null)
            {
                e.Handled = true;
                _pressPoint = null;
                Open(path);
            }
        };
        return row;
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            AppLog.Write(ex); // Fichier déplacé entre-temps : il sortira de la liste au prochain affichage.
        }
    }
}
