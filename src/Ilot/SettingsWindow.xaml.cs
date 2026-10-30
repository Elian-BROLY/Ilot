using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ilot.Services;
using Ilot.Settings;

namespace Ilot;

public partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, string> PageLabels = new()
    {
        [PageIds.Media] = "Lecteur média",
        [PageIds.Weather] = "Météo",
        [PageIds.Mail] = "Mails",
        [PageIds.Audio] = "Sortie audio",
        [PageIds.Timer] = "Minuteur",
        [PageIds.Note] = "Note rapide",
        [PageIds.AppVolume] = "Volume par application",
        [PageIds.SystemStats] = "Système : CPU, mémoire, réseau",
    };

    private const string FollowCursorChoice = "follow-cursor";

    private sealed record ScreenChoice(string? Device, string Label);

    private readonly SettingsStore _store = App.Settings;
    private readonly DispatcherTimer _delaySaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly Dictionary<MailProvider, CancellationTokenSource> _signIns = [];
    private bool _isLoading = true;

    public SettingsWindow()
    {
        _delaySaveTimer.Tick += (_, _) => SaveDelays();
        Closed += (_, _) =>
        {
            SaveDelays();
            foreach (var signIn in _signIns.Values)
            {
                signIn.Cancel();
            }
        };
        var settings = _store.Current;
        // Fenêtre classique : elle suit le thème de la capsule avec les contrôles natifs de Windows 11.
#pragma warning disable WPF0001
        ThemeMode = ThemeService.IsLight(settings) ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        InitializeComponent();

        PlayerCombo.SelectedItem = PlayerCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == settings.PreferredPlayer)
            ?? PlayerCombo.Items[0];
        PlayingOverPausedCheck.IsChecked = settings.ShowPlayingOverPausedPreferred;
        TrackBannerCheck.IsChecked = settings.TrackBannerEnabled;
        AutoUpdateCheck.IsChecked = settings.AutoUpdateEnabled;
        VersionText.Text = $"Version {App.Updates.CurrentVersion}";
        UpdateStatusText.Text = App.Updates.IsAvailable ? string.Empty : "Cette copie n'est pas installée : pas de mise à jour.";
        CheckUpdateButton.IsEnabled = App.Updates.IsAvailable;

        ExpandDelaySlider.Value = settings.ExpandDelayMs;
        CollapseDelaySlider.Value = settings.CollapseDelayMs;
        RenderDelayTexts();
        AnimationCombo.SelectedIndex = settings.AnimationDurationFactor switch { 0 => 2, < 1 => 1, _ => 0 };

        LoadScreens();
        AutostartCheck.IsChecked = AutostartService.IsEnabled;
        FullscreenCheck.IsChecked = settings.HideInFullscreen;
        TintCheck.IsChecked = settings.TintEnabled;
        CityBox.Text = settings.City ?? string.Empty;

        var credentials = App.Mail.Credentials;
        OutlookIdBox.Text = credentials.OutlookClientId ?? string.Empty;
        GmailIdBox.Text = credentials.GmailClientId ?? string.Empty;
        GmailSecretBox.Password = credentials.GmailClientSecret ?? string.Empty;
        OutlookEnabledCheck.IsChecked = settings.OutlookEnabled;
        GmailEnabledCheck.IsChecked = settings.GmailEnabled;
        Gmail2EnabledCheck.IsChecked = settings.Gmail2Enabled;
        MailBannerCheck.IsChecked = settings.MailBannerEnabled;
        SystemNotificationsCheck.IsChecked = settings.SystemNotificationsEnabled;
        _ = RenderSystemNotificationAccessAsync();
        RenderMailAccounts();
        CompactTemperatureCheck.IsChecked = settings.CompactTemperatureEnabled;
        PrivacyIndicatorsCheck.IsChecked = settings.PrivacyIndicatorsEnabled;
        BatteryAlertsCheck.IsChecked = settings.BatteryAlertsEnabled;
        GlobalHotkeyCheck.IsChecked = settings.GlobalHotkeyEnabled;
        FocusWithTimerCheck.IsChecked = settings.FocusWithTimerEnabled;
        NetworkAlertsCheck.IsChecked = settings.NetworkAlertsEnabled;
        RenderHotkeyStatus();

        ThemeCombo.SelectedIndex = settings.FollowSystemTheme ? 2 : settings.LightTheme ? 1 : 0;
        AccentCombo.ItemsSource = ThemeService.Accents.Select(accent => accent.Name).ToList();
        AccentCombo.SelectedItem = settings.AccentName;
        SizeCombo.SelectedIndex = settings.CapsuleScale switch { < 1 => 0, > 1 => 2, _ => 1 };

        RenderPages();
        _isLoading = false;
    }

    private void LoadScreens()
    {
        var choices = new List<ScreenChoice> { new(null, "Écran principal"), new(FollowCursorChoice, "L'écran où se trouve la souris") };
        var number = 1;
        foreach (var screen in ScreenService.GetScreens())
        {
            var suffix = screen.IsPrimary ? " (principal)" : string.Empty;
            choices.Add(new ScreenChoice(screen.Device, $"Écran {number++} : {screen.Width} × {screen.Height}{suffix}"));
        }
        ScreenCombo.ItemsSource = choices;
        var selected = _store.Current.ScreenFollowsCursor ? FollowCursorChoice : _store.Current.MonitorDevice;
        ScreenCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Device == selected) ?? choices[0];
    }

    private void RenderPages()
    {
        PagesList.Children.Clear();
        var pages = _store.Current.Pages;
        for (var i = 0; i < pages.Count; i++)
        {
            PagesList.Children.Add(CreatePageRow(pages[i], i, pages.Count));
        }
    }

    private DockPanel CreatePageRow(PageEntry page, int index, int count)
    {
        var label = PageLabels[page.Id];
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

        var down = new Button { Content = "▼", IsEnabled = index < count - 1, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        down.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"Descendre {label}");
        down.Click += (_, _) => MovePage(index, index + 1);
        DockPanel.SetDock(down, Dock.Right);
        row.Children.Add(down);

        var up = new Button { Content = "▲", IsEnabled = index > 0, Padding = new Thickness(8, 2, 8, 2) };
        up.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, $"Monter {label}");
        up.Click += (_, _) => MovePage(index, index - 1);
        DockPanel.SetDock(up, Dock.Right);
        row.Children.Add(up);

        var check = new CheckBox { Content = label, IsChecked = page.IsEnabled };
        RoutedEventHandler onChanged = (_, _) =>
        {
            _store.Update(_ => page.IsEnabled = check.IsChecked == true);
            // Le magasin réactive la dernière page si tout est décoché : la case doit le refléter.
            RenderPages();
        };
        check.Checked += onChanged;
        check.Unchecked += onChanged;
        row.Children.Add(check);
        return row;
    }

    private void MovePage(int from, int to)
    {
        _store.Update(settings =>
        {
            var page = settings.Pages[from];
            settings.Pages.RemoveAt(from);
            settings.Pages.Insert(to, page);
        });
        RenderPages();
    }

    private void RenderDelayTexts()
    {
        ExpandDelayText.Text = FormatMs(ExpandDelaySlider.Value);
        CollapseDelayText.Text = FormatMs(CollapseDelaySlider.Value);
    }

    private static string FormatMs(double value) => value.ToString("0", CultureInfo.InvariantCulture) + " ms";

    private void Delay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoading)
        {
            return;
        }
        RenderDelayTexts();
        // Enregistré une fois le curseur relâché : chaque cran écrirait sur le disque et réappliquerait tous les réglages.
        _delaySaveTimer.Stop();
        _delaySaveTimer.Start();
    }

    private void SaveDelays()
    {
        if (!_delaySaveTimer.IsEnabled)
        {
            return;
        }
        _delaySaveTimer.Stop();
        _store.Update(settings =>
        {
            settings.ExpandDelayMs = (int)ExpandDelaySlider.Value;
            settings.CollapseDelayMs = (int)CollapseDelaySlider.Value;
        });
    }

    private void Animation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading && AnimationCombo.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            _store.Update(settings => settings.AnimationDurationFactor = double.Parse(tag, CultureInfo.InvariantCulture));
        }
    }

    private void Screen_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading && ScreenCombo.SelectedItem is ScreenChoice choice)
        {
            _store.Update(settings =>
            {
                settings.ScreenFollowsCursor = choice.Device == FollowCursorChoice;
                settings.MonitorDevice = settings.ScreenFollowsCursor ? null : choice.Device;
            });
        }
    }

    private void Recenter_Click(object sender, RoutedEventArgs e) =>
        _store.Update(settings => settings.HorizontalPosition = 0.5);

    private void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
        {
            AutostartService.IsEnabled = AutostartCheck.IsChecked == true;
        }
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        _store.Update(settings =>
        {
            settings.HideInFullscreen = FullscreenCheck.IsChecked == true;
            settings.TintEnabled = TintCheck.IsChecked == true;
        });
    }

    private void CapsuleOptions_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        _store.Update(settings =>
        {
            settings.CompactTemperatureEnabled = CompactTemperatureCheck.IsChecked == true;
            settings.PrivacyIndicatorsEnabled = PrivacyIndicatorsCheck.IsChecked == true;
            settings.BatteryAlertsEnabled = BatteryAlertsCheck.IsChecked == true;
            settings.GlobalHotkeyEnabled = GlobalHotkeyCheck.IsChecked == true;
            settings.FocusWithTimerEnabled = FocusWithTimerCheck.IsChecked == true;
            settings.NetworkAlertsEnabled = NetworkAlertsCheck.IsChecked == true;
        });
        RenderHotkeyStatus();
    }

    private void Appearance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        _store.Update(settings =>
        {
            settings.FollowSystemTheme = ThemeCombo.SelectedIndex == 2;
            settings.LightTheme = ThemeCombo.SelectedIndex == 1;
            settings.AccentName = AccentCombo.SelectedItem as string ?? settings.AccentName;
            if (SizeCombo.SelectedItem is ComboBoxItem { Tag: string tag })
            {
                settings.CapsuleScale = double.Parse(tag, CultureInfo.InvariantCulture);
            }
        });
    }

    private void Player_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        _store.Update(settings =>
        {
            settings.PreferredPlayer = PlayerCombo.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : settings.PreferredPlayer;
            settings.ShowPlayingOverPausedPreferred = PlayingOverPausedCheck.IsChecked == true;
            settings.TrackBannerEnabled = TrackBannerCheck.IsChecked == true;
        });
    }

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoading)
        {
            _store.Update(settings => settings.AutoUpdateEnabled = AutoUpdateCheck.IsChecked == true);
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Recherche…";
        UpdateStatusText.Text = await App.Updates.CheckAsync();
        CheckUpdateButton.IsEnabled = true;
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(AppLog.LogPath))
            {
                AppLog.Write("Journal créé : aucune erreur enregistrée jusqu'ici.");
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLog.LogPath) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            AppLog.Write(ex);
        }
    }

    private void RenderHotkeyStatus() =>
        HotkeyStatusText.Visibility = _store.Current.GlobalHotkeyEnabled && !App.IsHotkeyRegistered ? Visibility.Visible : Visibility.Collapsed;

    private async Task RenderSystemNotificationAccessAsync()
    {
        var isAllowed = !_store.Current.SystemNotificationsEnabled || await SystemNotificationService.RequestAccessAsync();
        SystemNotificationsStatusText.Text = "Accès refusé : autorisez Ilot dans Paramètres > Confidentialité > Notifications.";
        SystemNotificationsStatusText.Visibility = isAllowed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SystemNotifications_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        _store.Update(settings => settings.SystemNotificationsEnabled = SystemNotificationsCheck.IsChecked == true);
        _ = RenderSystemNotificationAccessAsync();
    }

    private void OpenNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:notifications") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Paramètres indisponibles : le texte d'aide indique aussi le raccourci Win + N.
        }
    }

    private void MailEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        _store.Update(settings =>
        {
            settings.OutlookEnabled = OutlookEnabledCheck.IsChecked == true;
            settings.GmailEnabled = GmailEnabledCheck.IsChecked == true;
            settings.Gmail2Enabled = Gmail2EnabledCheck.IsChecked == true;
            settings.MailBannerEnabled = MailBannerCheck.IsChecked == true;
        });
    }

    private void RenderMailAccounts()
    {
        OutlookButton.Content = MailButtonLabel(MailProvider.Outlook);
        GmailButton.Content = MailButtonLabel(MailProvider.Gmail);
        Gmail2Button.Content = MailButtonLabel(MailProvider.Gmail2);
    }

    private string MailButtonLabel(MailProvider provider) =>
        _signIns.ContainsKey(provider) ? "Annuler" : App.Mail.IsConnected(provider) ? "Déconnecter" : "Connecter";

    private async void OutlookButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleMailAccountAsync(MailProvider.Outlook, OutlookButton, OutlookStatusText);

    private async void GmailButton_Click(object sender, RoutedEventArgs e) =>
        await ToggleMailAccountAsync(MailProvider.Gmail, GmailButton, GmailStatusText);

    private async void Gmail2Button_Click(object sender, RoutedEventArgs e) =>
        await ToggleMailAccountAsync(MailProvider.Gmail2, Gmail2Button, Gmail2StatusText);

    private async Task ToggleMailAccountAsync(MailProvider provider, Button button, TextBlock status)
    {
        var mail = App.Mail;
        if (_signIns.TryGetValue(provider, out var running))
        {
            running.Cancel(); // La page du navigateur a pu être fermée : rien ne reviendrait avant 3 minutes.
            return;
        }
        if (mail.IsConnected(provider))
        {
            mail.Disconnect(provider);
            status.Text = "Compte déconnecté.";
            RenderMailAccounts();
            return;
        }

        mail.SaveClientCredentials(OutlookIdBox.Text.Trim(), GmailIdBox.Text.Trim(), GmailSecretBox.Password.Trim());
        using var cancellation = new CancellationTokenSource();
        _signIns[provider] = cancellation;
        button.Content = MailButtonLabel(provider);
        status.Text = "Connectez-vous dans la page ouverte par votre navigateur…";
        status.Text = await mail.ConnectAsync(provider, cancellation.Token) ?? "Compte connecté.";
        _signIns.Remove(provider);
        RenderMailAccounts();
    }

    private async void ValidateCity_Click(object sender, RoutedEventArgs e)
    {
        var city = CityBox.Text.Trim();
        if (city.Length == 0)
        {
            _store.Update(settings =>
            {
                settings.City = null;
                settings.Latitude = null;
                settings.Longitude = null;
            });
            CityChoicesCombo.Visibility = Visibility.Collapsed;
            CityStatusText.Text = "Ville retirée.";
            return;
        }

        CityStatusText.Text = "Recherche…";
        IReadOnlyList<GeocodedCity> results = [];
        try
        {
            results = await WeatherService.GeocodeAsync(city);
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            // Une erreur imprévue ne doit pas fermer l'application : même message qu'une ville introuvable.
        }
        CityChoicesCombo.Visibility = results.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count == 0)
        {
            CityStatusText.Text = "Ville introuvable, ou service inaccessible.";
            return;
        }

        // Plusieurs villes portent parfois ce nom : la plus importante d'abord, et le choix des autres juste en dessous.
        CityChoicesCombo.ItemsSource = results;
        CityChoicesCombo.SelectedIndex = 0;
        SaveCity(results[0]);
    }

    private void CityChoice_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CityChoicesCombo.SelectedItem is GeocodedCity city && city.Name.Length > 0 && e.RemovedItems.Count > 0)
        {
            SaveCity(city);
        }
    }

    private void SaveCity(GeocodedCity city)
    {
        CityBox.Text = city.Name;
        CityStatusText.Text = $"Ville enregistrée : {city.Label}.";
        _store.Update(settings =>
        {
            settings.City = city.Name;
            settings.Latitude = city.Latitude;
            settings.Longitude = city.Longitude;
        });
    }
}
