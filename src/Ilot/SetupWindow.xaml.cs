using System.Windows;
using System.Windows.Controls;
using Ilot.Services;
using Ilot.Settings;

namespace Ilot;

/// <summary>L'assistant du premier lancement : il explique les gestes et remplace les réglages par défaut par les vôtres.</summary>
public partial class SetupWindow : Window
{
    private readonly SettingsStore _store = App.Settings;
    private readonly FrameworkElement[] _steps;
    private int _step;
    private bool _isLoading = true;

    public SetupWindow()
    {
#pragma warning disable WPF0001
        ThemeMode = ThemeService.IsLight(_store.Current) ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        InitializeComponent();
        Loc.Translate(this);
        _steps = [WelcomeStep, PlaceStep, PagesStep];
        LanguageCombo.SelectedItem = LanguageCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == _store.Current.Language) ?? LanguageCombo.Items[0];
        PlayerCombo.SelectedIndex = 0;
        CityBox.Text = _store.Current.City ?? string.Empty;
        foreach (var page in _store.Current.Pages)
        {
            var check = new CheckBox { Content = Loc.T(SettingsWindow.PageLabelFor(page.Id)), IsChecked = page.IsEnabled, Tag = page.Id, Margin = new Thickness(0, 2, 0, 2) };
            PagesList.Children.Add(check);
        }
        ShowStep(0);
        _isLoading = false;
        Closed += (_, _) => _store.Update(settings => settings.SetupCompleted = true);
    }

    private void ShowStep(int step)
    {
        _step = step;
        for (var i = 0; i < _steps.Length; i++)
        {
            _steps[i].Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        }
        BackButton.Visibility = step > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = step == _steps.Length - 1 ? Loc.T("Terminer") : Loc.T("Suivant");
        StepText.Text = $"{step + 1} / {_steps.Length}";
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < _steps.Length - 1)
        {
            ShowStep(_step + 1);
            return;
        }
        Apply();
        Close();
    }

    private void Apply()
    {
        var city = CityCombo.SelectedItem as GeocodedCity;
        _store.Update(settings =>
        {
            settings.PreferredPlayer = PlayerCombo.SelectedItem is ComboBoxItem { Tag: string player } ? player : string.Empty;
            if (city is not null)
            {
                settings.City = city.Name;
                settings.Latitude = city.Latitude;
                settings.Longitude = city.Longitude;
            }
            foreach (var check in PagesList.Children.OfType<CheckBox>())
            {
                settings.Pages.First(page => page.Id == (string)check.Tag).IsEnabled = check.IsChecked == true;
            }
        });
    }

    private async void SearchCity_Click(object sender, RoutedEventArgs e)
    {
        var name = CityBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }
        CityStatusText.Text = Loc.T("Recherche…");
        var results = await WeatherService.GeocodeAsync(name);
        CityCombo.ItemsSource = results;
        CityCombo.SelectedIndex = results.Count > 0 ? 0 : -1;
        CityCombo.Visibility = results.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        CityStatusText.Text = results.Count > 0
            ? Loc.T("Ville retenue : {0}.", results[0].Label)
            : Loc.T("Ville introuvable, ou service inaccessible.");
    }

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        // L'application redémarre dans la langue choisie, et l'assistant se rouvre : il n'est pas encore terminé.
        if (!_isLoading && LanguageCombo.SelectedItem is ComboBoxItem { Tag: string language })
        {
            _store.Update(settings => settings.Language = language);
        }
    }
}
