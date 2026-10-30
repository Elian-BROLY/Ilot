using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Ilot.Controls;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class WeatherWidget : UserControl
{
    private const int ForecastDayCount = 3;

    private static readonly string[] DayNames = ["Dim.", "Lun.", "Mar.", "Mer.", "Jeu.", "Ven.", "Sam."];
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);

    private readonly DispatcherTimer _refreshTimer = new() { Interval = RefreshInterval };
    private DateTime _lastSuccessUtc = DateTime.MinValue;
    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private bool _isLoading;
    private string? _loadedCity = App.Settings.Current.City;

    public WeatherWidget()
    {
        InitializeComponent();
        // Toujours actif : la température est aussi affichée dans la capsule compacte.
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                await RefreshIfStaleAsync();
            }
        };
        App.Settings.Changed += OnSettingsChanged;
        // Chargement dès le démarrage : la page est prête à la première ouverture.
        _ = RefreshAsync();
    }

    /// <summary>Levé après chaque lecture réussie. Null si aucune ville n'est configurée.</summary>
    public event Action? TemperatureChanged;

    public int? CurrentTemperature { get; private set; }

    private void OnSettingsChanged()
    {
        // Seul un changement de ville justifie une requête : les curseurs de réglage en envoient en rafale.
        var city = App.Settings.Current.City;
        if (city == _loadedCity)
        {
            return;
        }
        _loadedCity = city;
        _lastSuccessUtc = DateTime.MinValue;
        _lastAttemptUtc = DateTime.MinValue;
        CurrentTemperature = null;
        TemperatureChanged?.Invoke();
        _ = RefreshAsync();
    }

    private async Task RefreshIfStaleAsync()
    {
        var now = DateTime.UtcNow;
        if (now - _lastSuccessUtc >= RefreshInterval && now - _lastAttemptUtc >= RetryInterval)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        var settings = App.Settings.Current;
        if (settings.Latitude is not { } latitude || settings.Longitude is not { } longitude)
        {
            ReportPanel.Visibility = Visibility.Collapsed;
            ShowMessage("Indiquez votre ville dans les réglages\n(clic droit sur l'icône Ilot)");
            return;
        }
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        _lastAttemptUtc = DateTime.UtcNow;
        if (_lastSuccessUtc == DateTime.MinValue)
        {
            ReportPanel.Visibility = Visibility.Collapsed;
            ShowMessage("Chargement de la météo…");
        }

        var city = settings.City;
        var report = await WeatherService.FetchAsync(latitude, longitude);
        _isLoading = false;
        if (settings.City != city)
        {
            await RefreshAsync(); // La ville a changé pendant le chargement : ce résultat est celui de l'ancienne.
            return;
        }
        if (report is not null)
        {
            _lastSuccessUtc = DateTime.UtcNow;
            CurrentTemperature = report.Temperature;
            TemperatureChanged?.Invoke();
            Render(report, settings.City ?? string.Empty);
        }
        else if (_lastSuccessUtc == DateTime.MinValue)
        {
            ShowMessage("Météo indisponible pour le moment");
        }
        // Sinon, on garde les dernières données affichées.
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
    }

    private void Render(WeatherReport report, string city)
    {
        MessageText.Visibility = Visibility.Collapsed;
        ReportPanel.Visibility = Visibility.Visible;
        CurrentIcon.Data = WeatherIcons.ForCode(report.WeatherCode);
        TemperatureText.Text = FormatDegrees(report.Temperature);
        DescriptionText.Text = WeatherService.Describe(report.WeatherCode);
        CityText.Text = city;

        ForecastPanel.Children.Clear();
        // Le premier jour renvoyé est aujourd'hui, déjà résumé à gauche.
        foreach (var day in report.Days.Skip(1).Take(ForecastDayCount))
        {
            ForecastPanel.Children.Add(CreateDayColumn(day));
        }
    }

    private FrameworkElement CreateDayColumn(DailyForecast day)
    {
        var column = new StackPanel { Width = 52 };
        column.Children.Add(CreateCenteredText(DayNames[(int)day.Date.DayOfWeek], 11, "TextSecondaryBrush"));
        column.Children.Add(new Path
        {
            Width = 24,
            Height = 24,
            Margin = new Thickness(0, 4, 0, 4),
            Fill = (Brush)FindResource("TextPrimaryBrush"),
            Stretch = Stretch.Uniform,
            Data = WeatherIcons.ForCode(day.WeatherCode),
        });
        column.Children.Add(CreateCenteredText(FormatDegrees(day.Max), 12, "TextPrimaryBrush"));
        column.Children.Add(CreateCenteredText(FormatDegrees(day.Min), 11, "TextSecondaryBrush"));
        return column;
    }

    private TextBlock CreateCenteredText(string text, double size, string brushKey) => new()
    {
        Text = text,
        FontSize = size,
        HorizontalAlignment = HorizontalAlignment.Center,
        Foreground = (Brush)FindResource(brushKey),
    };

    private static string FormatDegrees(int value) => value.ToString(CultureInfo.InvariantCulture) + "°";
}
