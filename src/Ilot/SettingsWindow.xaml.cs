using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Ilot.Services;
using Ilot.Settings;

namespace Ilot;

public partial class SettingsWindow : Window
{
    private static readonly (string Action, string Label)[] HotkeyLabels =
    [
        (HotkeyActions.TogglePanel, "Ouvrir ou réduire"),
        (HotkeyActions.PlayPause, "Lecture ou pause"),
        (HotkeyActions.NextTrack, "Morceau suivant"),
        (HotkeyActions.PreviousTrack, "Morceau précédent"),
        (HotkeyActions.PreviousPage, "Page précédente, panneau ouvert"),
        (HotkeyActions.NextPage, "Page suivante, panneau ouvert"),
    ];

    private static readonly Dictionary<string, string> PageLabels = new()
    {
        [PageIds.Calendar] = "Agenda (compte Google ou Outlook)",
        [PageIds.Files] = "Dépôt de fichiers",
        [PageIds.Media] = "Lecteur média",
        [PageIds.Weather] = "Météo",
        [PageIds.Mail] = "Mails",
        [PageIds.Audio] = "Sortie audio",
        [PageIds.Timer] = "Minuteur",
        [PageIds.Note] = "Note rapide",
        [PageIds.AppVolume] = "Volume par application",
        [PageIds.SystemStats] = "Système : CPU, mémoire, réseau",
    };

    internal static string PageLabelFor(string id) => PageLabels[id];

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
        Loc.Translate(this);

        PlayerCombo.SelectedItem = PlayerCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == settings.PreferredPlayer)
            ?? PlayerCombo.Items[0];
        PlayingOverPausedCheck.IsChecked = settings.ShowPlayingOverPausedPreferred;
        TrackBannerCheck.IsChecked = settings.TrackBannerEnabled;
        AutoUpdateCheck.IsChecked = settings.AutoUpdateEnabled;
        VersionText.Text = Loc.T("Version {0}", App.Updates.CurrentVersion);
        UpdateStatusText.Text = App.Updates.IsAvailable ? string.Empty : Loc.T("Cette copie n'est pas installée : pas de mise à jour.");
        CheckUpdateButton.IsEnabled = App.Updates.IsAvailable;
        InstallUpdateButton.Visibility = App.Updates.IsPending ? Visibility.Visible : Visibility.Collapsed;

        ExpandDelaySlider.Value = settings.ExpandDelayMs;
        CollapseDelaySlider.Value = settings.CollapseDelayMs;
        RenderDelayTexts();
        AnimationCombo.SelectedIndex = settings.AnimationDurationFactor switch { 0 => 2, < 1 => 1, _ => 0 };
        AnimationStyleCombo.SelectedItem = AnimationStyleCombo.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == settings.AnimationStyle);

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
        AccentCombo.ItemsSource = ThemeService.Accents.Select(accent => new ComboBoxItem { Content = Loc.T(accent.Name), Tag = accent.Name }).ToList();
        AccentCombo.SelectedItem = AccentCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == settings.AccentName);
        SizeCombo.SelectedIndex = settings.CapsuleScale switch { < 1 => 0, > 1 => 2, _ => 1 };
        LanguageCombo.SelectedItem = LanguageCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == settings.Language) ?? LanguageCombo.Items[0];

        ContextualPageCheck.IsChecked = settings.ContextualPageEnabled;
        WheelVolumeCheck.IsChecked = settings.WheelVolumeEnabled;
        PomodoroMusicCheck.IsChecked = settings.PomodoroControlsMusic;
        PomodoroButtonCheck.IsChecked = settings.PomodoroButtonEnabled;
        HeadsetRowCheck.IsChecked = settings.HeadsetRowEnabled;
        QuietHoursCheck.IsChecked = settings.QuietHoursEnabled;
        QuietStartBox.Text = NotificationFilter.FormatTime(settings.QuietHoursStart);
        QuietEndBox.Text = NotificationFilter.FormatTime(settings.QuietHoursEnd);
        ImapEnabledCheck.IsChecked = settings.ImapEnabled;
        ImapAddressBox.Text = credentials.ImapAddress ?? string.Empty;
        ImapHostBox.Text = credentials.ImapHost ?? string.Empty;
        ImapPasswordBox.Password = credentials.ImapPassword ?? string.Empty;
        RenderNotificationApps();
        RenderHotkeys();
        _ = LoadHeadsetsAsync();

        RenderPages();
        _isLoading = false;
    }

    // --- Casque ---

    private async Task LoadHeadsetsAsync()
    {
        List<BluetoothAudioDevice> devices = [];
        try
        {
            devices = await BluetoothAudioService.FindDevicesAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex); // Le choix reste « Automatique ».
        }
        var wasLoading = _isLoading;
        _isLoading = true;
        HeadsetCombo.Items.Clear();
        HeadsetCombo.Items.Add(new ComboBoxItem { Content = Loc.T("Automatique : les AirPods, sinon le casque connecté"), Tag = string.Empty });
        foreach (var device in devices)
        {
            HeadsetCombo.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Name });
        }
        // Un casque choisi puis retiré de Windows reste dans la liste : le réglage ne doit pas changer tout seul.
        var current = _store.Current.HeadsetName;
        if (current.Length > 0 && devices.TrueForAll(device => device.Name != current))
        {
            HeadsetCombo.Items.Add(new ComboBoxItem { Content = current, Tag = current });
        }
        HeadsetCombo.SelectedItem = HeadsetCombo.Items.Cast<ComboBoxItem>().First(item => (string)item.Tag == current || ((string)item.Tag).Length == 0 && current.Length == 0);
        _isLoading = wasLoading;
    }

    private void Headset_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        _store.Update(settings =>
        {
            settings.HeadsetRowEnabled = HeadsetRowCheck.IsChecked == true;
            settings.HeadsetName = HeadsetCombo.SelectedItem is ComboBoxItem { Tag: string name } ? name : settings.HeadsetName;
        });
    }

    // --- Langue ---

    private void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading && LanguageCombo.SelectedItem is ComboBoxItem { Tag: string language })
        {
            _store.Update(settings => settings.Language = language);
        }
    }

    // --- Raccourcis ---

    private void RenderHotkeys()
    {
        HotkeysList.Children.Clear();
        var custom = _store.Current.Hotkeys;
        foreach (var (action, label) in HotkeyLabels)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var box = new TextBox
            {
                Width = 180,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = false,
                TextAlignment = TextAlignment.Center,
                Text = HotkeyActions.Resolve(action, custom).ToString(),
            };
            System.Windows.Automation.AutomationProperties.SetName(box, Loc.T(label));
            box.PreviewKeyDown += (_, e) => CaptureHotkey(action, box, e);
            DockPanel.SetDock(box, Dock.Right);
            row.Children.Add(box);
            row.Children.Add(new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = Loc.T(label) });
            HotkeysList.Children.Add(row);
        }
        var used = HotkeyLabels.Select(item => HotkeyActions.Resolve(item.Action, custom).ToString()).ToList();
        HotkeysConflictText.Text = used.Distinct().Count() < used.Count ? Loc.T("Deux actions utilisent le même raccourci.") : string.Empty;
    }

    private void CaptureHotkey(string action, TextBox box, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape)
        {
            return; // Laisse sortir du champ au clavier.
        }
        e.Handled = true;
        uint modifiers = 0;
        modifiers |= (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? Interop.NativeMethods.MOD_CONTROL : 0;
        modifiers |= (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? Interop.NativeMethods.MOD_ALT : 0;
        modifiers |= (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? Interop.NativeMethods.MOD_SHIFT : 0;
        modifiers |= (Keyboard.Modifiers & ModifierKeys.Windows) != 0 ? 0x0008u : 0;
        if (!HotkeyGesture.IsAllowed(modifiers, key))
        {
            return; // Combinaison en cours, ou touche seule.
        }
        var gesture = new HotkeyGesture(modifiers, key).ToString();
        box.Text = gesture;
        _store.Update(settings => settings.Hotkeys[action] = gesture);
        RenderHotkeys();
        RenderHotkeyStatus();
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _store.Update(settings => settings.Hotkeys.Clear());
        RenderHotkeys();
        RenderHotkeyStatus();
    }

    // --- Notifications par application et heures de silence ---

    private void RenderNotificationApps()
    {
        NotificationAppsList.Children.Clear();
        var settings = _store.Current;
        foreach (var (id, name) in settings.KnownNotificationApps.OrderBy(app => app.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            var check = new CheckBox { Content = name, IsChecked = !NotificationFilter.IsMuted(id, settings.MutedNotificationApps) };
            RoutedEventHandler onChanged = (_, _) => _store.Update(current =>
            {
                current.MutedNotificationApps.RemoveAll(app => string.Equals(app, id, StringComparison.OrdinalIgnoreCase));
                if (check.IsChecked != true)
                {
                    current.MutedNotificationApps.Add(id);
                }
            });
            check.Checked += onChanged;
            check.Unchecked += onChanged;
            NotificationAppsList.Children.Add(check);
        }
        NoNotificationAppsText.Visibility = settings.KnownNotificationApps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void QuietHours_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }
        var start = NotificationFilter.ParseTime(QuietStartBox.Text);
        var end = NotificationFilter.ParseTime(QuietEndBox.Text);
        QuietStatusText.Text = start is null || end is null ? Loc.T("Heure à écrire ainsi : 22:00") : string.Empty;
        _store.Update(settings =>
        {
            settings.QuietHoursEnabled = QuietHoursCheck.IsChecked == true;
            settings.QuietHoursStart = start ?? settings.QuietHoursStart;
            settings.QuietHoursEnd = end ?? settings.QuietHoursEnd;
        });
    }

    private async void ImapButton_Click(object sender, RoutedEventArgs e)
    {
        var mail = App.Mail;
        if (mail.IsConnected(MailProvider.Imap))
        {
            mail.Disconnect(MailProvider.Imap);
            ImapPasswordBox.Password = string.Empty;
            ImapStatusText.Text = Loc.T("Compte déconnecté.");
            RenderMailAccounts();
            return;
        }
        ImapButton.IsEnabled = false;
        ImapStatusText.Text = Loc.T("Connexion…");
        ImapStatusText.Text = await mail.ConnectImapAsync(ImapAddressBox.Text, ImapPasswordBox.Password, ImapHostBox.Text) ?? Loc.T("Compte connecté.");
        ImapButton.IsEnabled = true;
        ImapHostBox.Text = mail.Credentials.ImapHost ?? ImapHostBox.Text;
        RenderMailAccounts();
    }

    private void LoadScreens()
    {
        var choices = new List<ScreenChoice> { new(null, Loc.T("Écran principal")), new(FollowCursorChoice, Loc.T("L'écran où se trouve la souris")) };
        var number = 1;
        foreach (var screen in ScreenService.GetScreens())
        {
            var suffix = screen.IsPrimary ? Loc.T(" (principal)") : string.Empty;
            choices.Add(new ScreenChoice(screen.Device, Loc.T("Écran {0} : {1} × {2}{3}", number++, screen.Width, screen.Height, suffix)));
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
        var label = Loc.T(PageLabels[page.Id]);
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };

        var down = new Button { Content = "▼", IsEnabled = index < count - 1, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0) };
        down.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Loc.T("Descendre {0}", label));
        down.Click += (_, _) => MovePage(index, index + 1);
        DockPanel.SetDock(down, Dock.Right);
        row.Children.Add(down);

        var up = new Button { Content = "▲", IsEnabled = index > 0, Padding = new Thickness(8, 2, 8, 2) };
        up.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, Loc.T("Monter {0}", label));
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

    // Choisir un style le montre tout de suite sur la capsule.
    private void AnimationStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading && AnimationStyleCombo.SelectedItem is ComboBoxItem { Tag: string style })
        {
            _store.Update(settings => settings.AnimationStyle = style);
            App.PreviewAnimation();
        }
    }

    private void PreviewAnimation_Click(object sender, RoutedEventArgs e) => App.PreviewAnimation();

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
            settings.ContextualPageEnabled = ContextualPageCheck.IsChecked == true;
            settings.WheelVolumeEnabled = WheelVolumeCheck.IsChecked == true;
            settings.PomodoroControlsMusic = PomodoroMusicCheck.IsChecked == true;
            settings.PomodoroButtonEnabled = PomodoroButtonCheck.IsChecked == true;
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
            settings.AccentName = AccentCombo.SelectedItem is ComboBoxItem { Tag: string accent } ? accent : settings.AccentName;
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
        UpdateStatusText.Text = Loc.T("Recherche…");
        UpdateStatusText.Text = await App.Updates.CheckAsync();
        CheckUpdateButton.IsEnabled = true;
        InstallUpdateButton.Visibility = App.Updates.IsPending ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InstallUpdate_Click(object sender, RoutedEventArgs e) => App.Updates.ApplyAndRestart();

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!System.IO.File.Exists(AppLog.LogPath))
            {
                AppLog.Write(Loc.T("Journal créé : aucune erreur enregistrée jusqu'ici."));
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
        SystemNotificationsStatusText.Text = Loc.T("Accès refusé : autorisez Ilot dans Paramètres > Confidentialité > Notifications.");
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
            settings.ImapEnabled = ImapEnabledCheck.IsChecked == true;
            settings.MailBannerEnabled = MailBannerCheck.IsChecked == true;
        });
    }

    private void RenderMailAccounts()
    {
        OutlookButton.Content = MailButtonLabel(MailProvider.Outlook);
        GmailButton.Content = MailButtonLabel(MailProvider.Gmail);
        Gmail2Button.Content = MailButtonLabel(MailProvider.Gmail2);
        ImapButton.Content = MailButtonLabel(MailProvider.Imap);
    }

    private string MailButtonLabel(MailProvider provider) =>
        _signIns.ContainsKey(provider) ? Loc.T("Annuler") : App.Mail.IsConnected(provider) ? Loc.T("Déconnecter") : Loc.T("Connecter");

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
            status.Text = Loc.T("Compte déconnecté.");
            RenderMailAccounts();
            return;
        }

        mail.SaveClientCredentials(OutlookIdBox.Text.Trim(), GmailIdBox.Text.Trim(), GmailSecretBox.Password.Trim());
        using var cancellation = new CancellationTokenSource();
        _signIns[provider] = cancellation;
        button.Content = MailButtonLabel(provider);
        status.Text = Loc.T("Connectez-vous dans la page ouverte par votre navigateur…");
        status.Text = await mail.ConnectAsync(provider, cancellation.Token) ?? Loc.T("Compte connecté.");
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
            CityStatusText.Text = Loc.T("Ville retirée.");
            return;
        }

        CityStatusText.Text = Loc.T("Recherche…");
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
            CityStatusText.Text = Loc.T("Ville introuvable, ou service inaccessible.");
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
        CityStatusText.Text = Loc.T("Ville enregistrée : {0}.", city.Label);
        _store.Update(settings =>
        {
            settings.City = city.Name;
            settings.Latitude = city.Latitude;
            settings.Longitude = city.Longitude;
        });
    }
}
