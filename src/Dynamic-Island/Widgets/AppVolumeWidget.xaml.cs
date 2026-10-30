using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Widgets;

public partial class AppVolumeWidget : UserControl
{
    private const int MaxRows = 4; // Ce qui tient dans la page, sans barre de défilement.

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<string, (Slider Slider, TextBlock Percent)> _rows = [];
    private string _layoutSignature = string.Empty;
    private bool _isRendering;

    public AppVolumeWidget()
    {
        InitializeComponent();
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                _refreshTimer.Start();
                await RefreshAsync();
            }
            else
            {
                _refreshTimer.Stop();
            }
        };
    }

    private async Task RefreshAsync()
    {
        List<AppVolume> apps;
        try
        {
            apps = (await AppVolumeService.ListAsync()).Take(MaxRows).ToList();
        }
        catch (Exception)
        {
            return; // Sortie audio en train de changer : nouvel essai au prochain passage.
        }

        var signature = string.Join("|", apps.Select(app => app.Key));
        if (signature != _layoutSignature)
        {
            _layoutSignature = signature;
            Rows.Children.Clear();
            _rows.Clear();
            foreach (var app in apps)
            {
                Rows.Children.Add(CreateRow(app));
            }
        }
        EmptyText.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var app in apps)
        {
            var (slider, percent) = _rows[app.Key];
            // Ne pas faire reculer le curseur pendant que l'utilisateur le déplace.
            if (!slider.IsMouseCaptureWithin)
            {
                _isRendering = true;
                slider.Value = app.Level;
                _isRendering = false;
                percent.Text = FormatPercent(app.Level);
            }
        }
    }

    private Grid CreateRow(AppVolume app)
    {
        var grid = new Grid { Height = 25 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var name = new TextBlock
        {
            Margin = new Thickness(4, 0, 8, 0),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = app.Name,
            ToolTip = app.Name,
        };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            IsMoveToPointEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("ThinSlider"),
        };
        AutomationProperties.SetName(slider, "Volume " + app.Key);
        var percent = new TextBlock
        {
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        };
        slider.ValueChanged += (_, e) =>
        {
            if (!_isRendering)
            {
                percent.Text = FormatPercent(e.NewValue);
                _ = AppVolumeService.SetLevelAsync(app.Key, (float)e.NewValue);
            }
        };

        Grid.SetColumn(slider, 1);
        Grid.SetColumn(percent, 2);
        grid.Children.Add(name);
        grid.Children.Add(slider);
        grid.Children.Add(percent);
        _rows[app.Key] = (slider, percent);
        return grid;
    }

    private static string FormatPercent(double level) =>
        Math.Round(level * 100).ToString(CultureInfo.InvariantCulture) + " %";
}
