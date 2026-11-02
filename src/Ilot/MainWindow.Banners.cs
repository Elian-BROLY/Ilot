using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Ilot.Controls;
using Ilot.Services;

namespace Ilot;

public partial class MainWindow
{
    private const double TrackBannerWidth = 290;
    private const double TrackBannerHeight = 46;
    private const double BannerMarkSize = 32;
    private const double TrackBannerMarkSize = 30;
    private const string HeadsetGlyph = "";
    private const string MusicGlyph = "";
    private const string UpdateGlyph = "";
    private static readonly TimeSpan TrackBannerDuration = TimeSpan.FromSeconds(3.5);

    /// <summary>Remplit la bannière. Une bannière avec <paramref name="click"/> reste ouverte au survol ; sans, le survol ouvre le panneau.</summary>
    private void SetBannerContent(string title, string status, Action? click, string letter = "", bool isGlyph = false,
        ImageSource? icon = null, ImageSource? artwork = null, string extra = "")
    {
        BannerNameText.Text = title;
        BannerStatusText.Text = status;
        BannerMailLetter.Text = letter;
        BannerMailLetter.FontFamily = isGlyph ? (FontFamily)FindResource("IconFont") : FontFamily;
        BannerMailLetter.Visibility = icon is null && artwork is null ? Visibility.Visible : Visibility.Collapsed;
        BannerAppIcon.Source = icon;
        BannerAirPodsIcon.Visibility = Visibility.Collapsed;
        BannerMailMark.Visibility = Visibility.Visible;
        BannerMailMark.Width = BannerMailMark.Height = BannerMarkSize;
        BannerMailMark.Background = artwork is null ? (Brush)FindResource("HoverBrush") : new ImageBrush(artwork) { Stretch = Stretch.UniformToFill };
        BannerRings.Visibility = Visibility.Collapsed;
        BannerExtraText.Text = extra;
        BannerExtraText.Visibility = extra.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        AirPodsPanel.IsBannerWatching = false;
        _bannerClick = click;
        _isTransientBannerVisible = false;
    }

    private void ShowBanner(bool isConnected)
    {
        var isAirPods = AirPodsPanel.IsAirPods;
        var status = isAirPods ? (isConnected ? Loc.T("Connectés") : Loc.T("Déconnectés")) : (isConnected ? Loc.T("Connecté") : Loc.T("Déconnecté"));
        SetBannerContent(AirPodsPanel.DeviceName, status, click: null, HeadsetGlyph, isGlyph: true);
        BannerRings.Update(isConnected ? AirPodsPanel.Battery : null);
        BannerRings.Visibility = isConnected ? Visibility.Visible : Visibility.Collapsed;
        if (isAirPods)
        {
            BannerAirPodsIcon.Visibility = Visibility.Visible;
            BannerMailMark.Visibility = Visibility.Collapsed;
        }
        AirPodsPanel.IsBannerWatching = isConnected;
        PresentBanner(AirPodsBannerDuration);
    }

    /// <summary>Affiche la bannière tout de suite si la capsule est libre, sinon après les précédentes.</summary>
    private void ShowOrQueueBanner(Action show)
    {
        if (!_isExpanded && !_isBannerVisible && !_isHiddenForFullscreen)
        {
            show();
            return;
        }
        if (_bannerQueue.Count >= MaxQueuedBanners)
        {
            _bannerQueue.Dequeue(); // Tout reste consultable dans le centre de notifications (Win + N).
        }
        _bannerQueue.Enqueue(show);
    }

    private void ShowNextQueuedBanner()
    {
        if (!_isExpanded && !_isBannerVisible && !_isHiddenForFullscreen && _bannerQueue.TryDequeue(out var show))
        {
            show();
        }
    }

    // --- Bannières d'information : minuteur, batterie, mise à jour ---

    /// <param name="keepAfterFullscreen">
    /// Vrai : en plein écran, la bannière attend la sortie au lieu d'être abandonnée. C'est le cas de ce que l'utilisateur a
    /// lui-même lancé, comme un minuteur : il passe aussi les heures de silence.
    /// </param>
    /// <param name="evenInQuietHours">Vrai : assez important pour passer les heures de silence, comme une batterie presque vide.</param>
    private void ShowInfoBanner(string glyph, string title, string status, Action? click = null, bool keepAfterFullscreen = false,
        bool evenInQuietHours = false)
    {
        if ((_isHiddenForFullscreen && !keepAfterFullscreen) || (IsQuietTime && !keepAfterFullscreen && !evenInQuietHours))
        {
            return;
        }
        ShowOrQueueBanner(() =>
        {
            SetBannerContent(title, status, click ?? (() => { }), glyph, isGlyph: true);
            PresentBanner(MailBannerDuration);
        });
    }

    private void OnUpdateReady(string version)
    {
        if (!IsInFocusMode)
        {
            ShowInfoBanner(UpdateGlyph, Loc.T("Version {0} prête", version), Loc.T("Cliquez pour l'installer et redémarrer"), App.Updates.ApplyAndRestart);
        }
    }

    // --- Bannière de changement de morceau ---

    // Différé : certains lecteurs publient le titre, l'artiste et la pochette en plusieurs fois.
    private void OnTrackChanged()
    {
        _trackBannerTimer.Stop();
        _trackBannerTimer.Start();
    }

    private void ShowTrackBanner()
    {
        _trackBannerTimer.Stop();
        var media = MediaPanel.Service;
        // Moins importante que les autres bannières : elle est abandonnée plutôt que mise en attente, le titre serait périmé.
        if (!_settings.Current.TrackBannerEnabled || !media.IsPlaying || !media.IsPreferredSourceShown || media.Title.Length == 0
            || _isExpanded || _isHiddenForFullscreen || IsInFocusMode || (_isBannerVisible && !_isTransientBannerVisible))
        {
            return;
        }

        SetBannerContent(media.Title, media.Artist.Length > 0 ? media.Artist : media.SourceName, click: null, MusicGlyph, isGlyph: true, artwork: media.Thumbnail);
        BannerMailMark.Width = BannerMailMark.Height = TrackBannerMarkSize;
        _isTransientBannerVisible = true;
        PresentBanner(TrackBannerDuration, TrackBannerWidth, TrackBannerHeight);
    }

    // --- Bannière de notification Windows ---

    private bool IsQuietTime => _settings.Current.QuietHoursEnabled && NotificationFilter.IsQuietTime(
        TimeOnly.FromDateTime(DateTime.Now), _settings.Current.QuietHoursStart, _settings.Current.QuietHoursEnd);

    private void OnSystemNotificationReceived(SystemNotification notification)
    {
        // Retient l'application pour pouvoir la couper dans les réglages, même si sa notification n'est pas affichée.
        if (notification.AppId.Length > 0 && !_settings.Current.KnownNotificationApps.ContainsKey(notification.AppId))
        {
            _settings.UpdateSilently(settings => settings.KnownNotificationApps[notification.AppId] = notification.AppName);
        }
        if (NotificationFilter.IsMuted(notification.AppId, _settings.Current.MutedNotificationApps) || IsQuietTime)
        {
            return;
        }
        if (IsInFocusMode)
        {
            _missedDuringFocus++;
            return;
        }
        // Plein écran : ne pas déranger. La notification reste dans le centre de notifications.
        if (!_isHiddenForFullscreen)
        {
            ShowOrQueueBanner(() => ShowSystemNotificationBanner(notification));
        }
    }

    private void ShowSystemNotificationBanner(SystemNotification notification)
    {
        var status = notification.Body.Length > 0 ? $"{notification.AppName} · {notification.Body}" : notification.AppName;
        // Windows ne donne pas accès aux boutons de la notification : le clic ouvre l'application.
        SetBannerContent(notification.Title, status, () => AppIconService.OpenApp(notification.AppId),
            notification.AppName[..1].ToUpperInvariant(), icon: notification.Icon);
        PresentBanner(MailBannerDuration);
    }

    // --- Bannière de nouveau mail ---

    private void OnNewMailReceived(MailNotification notification)
    {
        if (IsInFocusMode)
        {
            _missedDuringFocus += 1 + notification.OtherCount;
            return;
        }
        // Panneau ouvert : la page Mails et le compteur affichent déjà l'information.
        if (_settings.Current.MailBannerEnabled && !_isExpanded && !_isHiddenForFullscreen && !IsQuietTime)
        {
            ShowOrQueueBanner(() => ShowMailBanner(notification));
        }
    }

    private void ShowMailBanner(MailNotification notification)
    {
        var item = notification.Item;
        var label = notification.Provider.Label();
        var url = item.OpenUrl;
        SetBannerContent(
            string.IsNullOrWhiteSpace(item.Sender) ? label : item.Sender,
            string.IsNullOrWhiteSpace(item.Subject) ? Loc.T("(sans objet)") : item.Subject,
            () =>
            {
                if (url is not null)
                {
                    App.Mail.OpenInBrowser(url);
                }
            },
            label[..1],
            extra: notification.OtherCount > 0 ? "+" + notification.OtherCount.ToString(CultureInfo.InvariantCulture) : string.Empty);
        PresentBanner(MailBannerDuration);
    }

    private void BannerPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Géré ici : ce clic agit sur la bannière, il ne doit ni verrouiller ni déplacer la capsule.
        if (_bannerClick is not null)
        {
            _isBannerPressed = true;
            e.Handled = true;
        }
    }

    private void BannerPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isBannerPressed && _bannerClick is { } click)
        {
            e.Handled = true;
            click();
            Collapse();
        }
        _isBannerPressed = false;
    }

    private void PresentBanner(TimeSpan displayDuration, double width = BannerWidth, double height = BannerHeight)
    {
        _trimTimer.Stop();
        _bannerTimer.Stop();
        _bannerTimer.Interval = displayDuration;
        _bannerTimer.Start();

        // Une fermeture du panneau a pu être interrompue : même invisible, il intercepterait les clics destinés à la bannière.
        ExpandedPanel.Visibility = Visibility.Collapsed;
        CapsuleShadow.Visibility = Visibility.Hidden;
        Capsule.BorderThickness = CompactBorder;

        // Aussi si une bannière est déjà affichée : elle n'a pas forcément la même taille.
        BannerPanel.Width = width;
        BannerPanel.Height = height;
        var motion = CapsuleMotions.Open(_settings.Current.AnimationStyle);
        Animate(Capsule, WidthProperty, width, motion.Width);
        Animate(Capsule, HeightProperty, height, motion.Height);
        Animate(this, CapsuleRadiusProperty, height / 2, motion.Radius);
        FadeWindowIfNeeded(motion);
        if (_isBannerVisible)
        {
            return;
        }
        _isBannerVisible = true;
        UpdateCompactIndicators();

        BannerPanel.Visibility = Visibility.Visible;
        Animate(CompactContent, OpacityProperty, 0, TimeSpan.FromMilliseconds(90), null);
        Animate(BannerPanel, OpacityProperty, 1, TimeSpan.FromMilliseconds(220), null, TimeSpan.FromMilliseconds(90));
    }

    private void HideBannerContent()
    {
        _isBannerVisible = false;
        _isTransientBannerVisible = false;
        _isBannerPressed = false;
        _bannerClick = null;
        _bannerTimer.Stop();
        AirPodsPanel.IsBannerWatching = false;
        Animate(BannerPanel, OpacityProperty, 0, TimeSpan.FromMilliseconds(100), null);
    }
}
