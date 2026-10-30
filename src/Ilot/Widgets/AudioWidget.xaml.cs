using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class AudioWidget : UserControl
{
    private const int MaxOutputs = 6; // Ce qui tient dans la page, sans barre de défilement.

    // Une sortie peut être branchée ou choisie ailleurs : la liste reste à jour tant qu'elle est visible.
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private string _lastSignature = string.Empty;

    public AudioWidget()
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
        List<AudioOutput> outputs;
        try
        {
            outputs = await AudioOutputService.ListAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            ShowMessage(Loc.T("Sorties audio indisponibles"));
            return;
        }

        // La sortie active reste affichée même au-delà de la limite.
        var visible = outputs.OrderByDescending(output => output.IsDefault).Take(MaxOutputs).OrderBy(output => output.Name).ToList();
        var signature = string.Join("|", visible.Select(output => output.Id + output.IsDefault));
        if (signature == _lastSignature)
        {
            return;
        }
        _lastSignature = signature;

        Outputs.Children.Clear();
        foreach (var output in visible)
        {
            Outputs.Children.Add(CreateRow(output));
        }
    }

    private Button CreateRow(AudioOutput output)
    {
        var icon = new TextBlock
        {
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = output.IsDefault ? Brushes.Black : (Brush)FindResource("TextPrimaryBrush"),
            Text = GlyphFor(output.FullName),
        };
        var label = new TextBlock
        {
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            FontWeight = output.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = output.IsDefault ? Brushes.Black : (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = output.Name,
        };
        var content = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(label);

        var button = new Button
        {
            Height = 28,
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(10, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Style = (Style)FindResource("PillRowButton"),
            Background = output.IsDefault ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("SurfaceBrush"),
            ToolTip = output.FullName,
            Content = content,
        };
        AutomationProperties.SetName(button, Loc.T("Sortie {0}", output.Name));
        button.Click += async (_, _) => await SelectAsync(output);
        return button;
    }

    private async Task SelectAsync(AudioOutput output)
    {
        if (output.IsDefault)
        {
            return;
        }

        var isDone = false;
        try
        {
            isDone = await AudioOutputService.SetDefaultAsync(output.Id);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            // Interface non documentée de Windows : elle peut disparaître avec une mise à jour.
        }
        if (isDone)
        {
            MessageText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShowMessage(Loc.T("Windows a refusé de changer de sortie"));
        }
        await RefreshAsync();
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }

    private static string GlyphFor(string fullName)
    {
        if (fullName.Contains("AirPods", StringComparison.OrdinalIgnoreCase)
            || fullName.Contains("Casque", StringComparison.OrdinalIgnoreCase)
            || fullName.Contains("Headphone", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }
        // Les écrans passent par une sortie HDMI ou DisplayPort, sans mot-clé fiable : le pilote les distingue.
        return fullName.Contains("High Definition Audio Device", StringComparison.OrdinalIgnoreCase) ? "" : "";
    }
}
