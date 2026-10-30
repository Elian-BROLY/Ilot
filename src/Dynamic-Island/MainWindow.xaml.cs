using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Interop;
using DynamicIsland.Services;
using DynamicIsland.Settings;

namespace DynamicIsland;

public partial class MainWindow : Window
{
    private const double CompactWidth = 180;
    private const double CompactHeight = 32;
    private const double CompactRadius = 16;
    private const double ExpandedWidth = 420;
    private const double ExpandedHeight = 160;
    private const double ExpandedHeightWithAirPods = 208;
    private const double ExpandedRadius = 24;
    private const double BannerWidth = 360;
    private const int MaxQueuedBanners = 5;
    private const double BannerHeight = 60;
    private const double BannerRadius = 30;
    private const double CompactTintStrength = 0.2;
    private const double ExpandedTintStrength = 0.28;
    private const int DragThresholdPixels = 6;
    private static readonly TimeSpan AirPodsBannerDuration = TimeSpan.FromSeconds(3.5);
    private static readonly TimeSpan MailBannerDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MailBannerDurationAfterHover = TimeSpan.FromSeconds(2);
    private const int HotkeyId = 1;
    private const uint HotkeyVirtualKey = 0x4E; // N
    private const int WheelGestureGapMs = 200;
    private const int WheelGestureThreshold = 40; // Un cran de molette vaut 120.

    private const double BaseWindowWidth = 468;
    private const double BaseWindowHeight = 240;
    private static readonly TimeSpan NetworkAlertDelay = TimeSpan.FromSeconds(4);
    private static readonly Thickness CompactBorder = new(0);
    private static readonly Thickness ExpandedBorder = new(1);
    // Fenêtres de Windows qui couvrent tout l'écran sans être du plein écran : Bureau, barre des tâches, Alt+Tab.
    private static readonly string[] ShellWindowClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "XamlExplorerHostIslandWindow"];

    private static readonly DependencyProperty CapsuleRadiusProperty = DependencyProperty.Register(
        nameof(CapsuleRadius), typeof(double), typeof(MainWindow),
        new PropertyMetadata(CompactRadius, (d, e) =>
            ((MainWindow)d).Capsule.CornerRadius = new CornerRadius(Math.Max(0, (double)e.NewValue))));

    private readonly DispatcherTimer _clockTimer = new(DispatcherPriority.Background);
    private readonly DispatcherTimer _expandTimer = new();
    private readonly DispatcherTimer _collapseTimer = new();
    private readonly DispatcherTimer _trimTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _bannerTimer = new();
    private readonly DispatcherTimer _fullscreenTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _trackBannerTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<string, FrameworkElement> _pageViews;
    private readonly List<string> _visiblePages = [];
    private readonly SettingsStore _settings = App.Settings;
    private IntPtr _hwnd;
    private string? _currentPage;
    private bool _isExpanded;
    private bool _isBannerVisible;
    private bool _isTrackBannerVisible;
    private bool _isBannerPressed;
    private Action? _bannerClick; // Non nul : le survol maintient la bannière, et le clic exécute cette action.
    private readonly Queue<Action> _bannerQueue = new();
    private readonly SystemNotificationService _systemNotifications = new();
    private bool _isPinned;
    private bool _isHiddenForFullscreen;
    private bool _pressStartedOnCapsule;
    private bool _isDraggingWindow;
    private NativeMethods.POINT _dragStartCursor;
    private int _dragStartLeft;
    private Color? _tint;
    private long _lastWheelEventTime;
    private int _wheelGestureDelta;
    private bool _isWheelGestureConsumed;
    private bool _isTyping;
    private IntPtr _foregroundBeforeTyping;
    private bool _isHotkeyRegistered;
    private int _missedDuringFocus;
    private bool _wasTimerActive;
    private bool _isNetworkUp = NetworkInterface.GetIsNetworkAvailable();
    private readonly DispatcherTimer _networkAlertTimer = new() { Interval = NetworkAlertDelay };
    private bool? _wasOnAcPower;
    private readonly PrivacyIndicatorService _privacyIndicators = new();
    private readonly BatteryAlertPolicy _pcBatteryPolicy = new(20, 10);
    private readonly BatteryAlertPolicy _airPodsBatteryPolicy = new(20);

    public MainWindow()
    {
        InitializeComponent();
        _pageViews = new Dictionary<string, FrameworkElement>
        {
            [PageIds.Media] = MediaPanel,
            [PageIds.Weather] = WeatherPanel,
            [PageIds.Mail] = MailPanel,
            [PageIds.Audio] = AudioPanel,
            [PageIds.Timer] = TimerPanel,
            [PageIds.Note] = NotePanel,
            [PageIds.AppVolume] = AppVolumePanel,
            [PageIds.Lyrics] = LyricsPanel,
            [PageIds.SystemStats] = SystemStatsPanel,
        };
        LyricsPanel.Attach(MediaPanel.Service);

        _clockTimer.Tick += (_, _) => UpdateClock();
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            Expand();
        };
        _collapseTimer.Tick += (_, _) => TryCollapse();
        _trimTimer.Tick += (_, _) =>
        {
            _trimTimer.Stop();
            // Au repos, rend au système les pages qui n'ont servi qu'au démarrage ou à l'animation.
            if (!_isExpanded)
            {
                // Sans cela, le tas .NET reste gonflé longtemps : au repos, plus rien ne l'oblige à se réduire.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);
            }
        };
        _trimTimer.Start();
        _bannerTimer.Tick += (_, _) =>
        {
            _bannerTimer.Stop();
            if (_isBannerVisible)
            {
                Collapse();
            }
        };
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreenVisibility();

        AirPodsPanel.AvailabilityChanged += UpdateAirPodsLayout;
        AirPodsPanel.ConnectionChanged += OnAirPodsConnectionChanged;
        AirPodsPanel.BatteryChanged += OnAirPodsBatteryChanged;
        _ = AirPodsPanel.InitializeAsync();
        TimerPanel.Service.Changed += OnTimerChanged;
        TimerPanel.Service.Completed += OnTimerCompleted;
        TimerPanel.Service.Stopped += OnTimerStopped;
        TimerPanel.TypingRequested += () => BeginTyping(TimerPanel.FocusInput);
        TimerPanel.TypingFinished += EndTyping;
        NotePanel.TypingRequested += () => BeginTyping(NotePanel.FocusNote);
        NetworkChange.NetworkAvailabilityChanged += (_, e) => Dispatcher.BeginInvoke(() =>
        {
            // Un Wi-Fi qui change de borne coupe un instant : seule une coupure qui dure mérite une alerte.
            _networkAlertTimer.Stop();
            _networkAlertTimer.Start();
        });
        _networkAlertTimer.Tick += (_, _) => CheckNetworkAvailability();
        WeatherPanel.TemperatureChanged += UpdateCompactIndicators;
        _privacyIndicators.Changed += UpdatePrivacyDots;
        Deactivated += (_, _) => EndTyping();
        Closed += (_, _) => NotePanel.Save();
        MediaPanel.Service.Changed += OnMediaChanged;
        MediaPanel.Service.TrackChanged += OnTrackChanged;
        _trackBannerTimer.Tick += (_, _) => ShowTrackBanner();
        App.Updates.UpdateReady += OnUpdateReady;
        MailPanel.UnreadChanged += () =>
        {
            UpdateCompactIndicators();
            ScheduleMemoryTrim(); // La relecture périodique ne doit pas faire dériver la mémoire au repos.
        };
        MailPanel.NewMailReceived += OnNewMailReceived;
        _systemNotifications.Received += OnSystemNotificationReceived;
        _settings.Changed += ApplySettings;

        ApplySettings();
        UpdateClock();
    }

    private double CapsuleRadius
    {
        get => (double)GetValue(CapsuleRadiusProperty);
        set => SetValue(CapsuleRadiusProperty, value);
    }

    private double CurrentExpandedHeight => AirPodsPanel.IsAvailable ? ExpandedHeightWithAirPods : ExpandedHeight;

    private void ApplySettings()
    {
        var settings = _settings.Current;
        if (ThemeService.NeedsRestart(settings))
        {
            App.RestartToApplyTheme();
            return;
        }
        CapsuleFill.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(_isExpanded ? ExpandedFill : CompactFill, Scaled(TimeSpan.FromMilliseconds(250))));
        RootGrid.LayoutTransform = new ScaleTransform(settings.CapsuleScale, settings.CapsuleScale);
        Width = BaseWindowWidth * settings.CapsuleScale;
        Height = BaseWindowHeight * settings.CapsuleScale;
        _expandTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, settings.ExpandDelayMs));
        _collapseTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, settings.CollapseDelayMs));

        RebuildPages();
        UpdateTint();
        if (_hwnd != IntPtr.Zero)
        {
            PositionOnTargetScreen();
        }

        OutputButton.Visibility = _visiblePages.Contains(PageIds.Audio) ? Visibility.Visible : Visibility.Collapsed;
        UpdateCompactIndicators();
        UpdateHotkeyRegistration();
        CheckPowerStatus();
        App.Updates.SetAutomatic(settings.AutoUpdateEnabled);
        if (settings.PrivacyIndicatorsEnabled)
        {
            _privacyIndicators.Start();
        }
        else
        {
            _privacyIndicators.Stop();
        }

        if (settings.SystemNotificationsEnabled)
        {
            _ = _systemNotifications.StartAsync();
        }
        else
        {
            _systemNotifications.Stop();
        }

        if (settings.HideInFullscreen)
        {
            _fullscreenTimer.Start();
        }
        else
        {
            _fullscreenTimer.Stop();
            SetHiddenForFullscreen(false);
        }
    }

    // --- Pages ---

    private void RebuildPages()
    {
        _visiblePages.Clear();
        _visiblePages.AddRange(_settings.Current.Pages.Where(page => page.IsEnabled).Select(page => page.Id));

        PageDots.Children.Clear();
        if (_visiblePages.Count > 1)
        {
            foreach (var id in _visiblePages)
            {
                PageDots.Children.Add(CreatePageDot(id));
            }
        }

        var page = _currentPage is not null && _visiblePages.Contains(_currentPage) ? _currentPage : _visiblePages[0];
        ShowPage(page, direction: 0);
    }

    private Border CreatePageDot(string id)
    {
        // La zone cliquable dépasse largement le point, trop petit pour être visé.
        var dot = new Border
        {
            Width = 14,
            Height = 11, // Toutes les pages actives doivent tenir dans la hauteur du panneau.
            Background = Brushes.Transparent,
            Tag = id,
            Child = new Ellipse { Width = 5, Height = 5, Fill = (Brush)FindResource("TextPrimaryBrush") },
        };
        dot.MouseLeftButtonDown += (_, e) => e.Handled = true; // Ne doit pas verrouiller la capsule.
        dot.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ShowPage(id, Math.Sign(_visiblePages.IndexOf(id) - _visiblePages.IndexOf(_currentPage ?? id)));
        };
        return dot;
    }

    private void ShowPage(string id, int direction)
    {
        var previous = _currentPage;
        _currentPage = id;
        foreach (var (pageId, view) in _pageViews)
        {
            view.Visibility = pageId == id ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var dot in PageDots.Children.OfType<Border>())
        {
            dot.Child.Opacity = (string)dot.Tag == id ? 1 : 0.3;
        }

        if (direction == 0 || previous == id)
        {
            return;
        }
        // La nouvelle page glisse depuis le bas ou le haut, dans l'axe des points de navigation.
        var view2 = _pageViews[id];
        var offset = new TranslateTransform(0, direction * 24);
        view2.RenderTransform = offset;
        var duration = Scaled(TimeSpan.FromMilliseconds(220));
        offset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        view2.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
    }

    private void OutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPage is not null && _visiblePages.Contains(PageIds.Audio))
        {
            ShowPage(PageIds.Audio, Math.Sign(_visiblePages.IndexOf(PageIds.Audio) - _visiblePages.IndexOf(_currentPage)));
        }
    }

    // En Preview : la zone de texte de la note marquerait sinon l'événement comme traité, même sans rien à faire défiler.
    private void Capsule_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_isExpanded || _visiblePages.Count < 2 || _currentPage is null
            || (_currentPage == PageIds.Note && NotePanel.IsContentScrollable)
            || (_currentPage == PageIds.Lyrics && LyricsPanel.IsContentScrollable))
        {
            return;
        }
        e.Handled = true;

        // Un pavé tactile envoie une rafale de petits événements, inertie comprise : tant qu'ils se suivent,
        // ils forment un seul geste, qui ne change qu'une seule page.
        var now = Environment.TickCount64;
        if (now - _lastWheelEventTime > WheelGestureGapMs)
        {
            _wheelGestureDelta = 0;
            _isWheelGestureConsumed = false;
        }
        _lastWheelEventTime = now;

        _wheelGestureDelta += e.Delta;
        if (_isWheelGestureConsumed || Math.Abs(_wheelGestureDelta) < WheelGestureThreshold)
        {
            return;
        }
        _isWheelGestureConsumed = true;

        ShowRelativePage(_wheelGestureDelta < 0 ? 1 : -1);
    }

    // Les pages forment une boucle dans les deux sens.
    private void ShowRelativePage(int direction)
    {
        if (!_isExpanded || _visiblePages.Count < 2 || _currentPage is null)
        {
            return;
        }
        var index = (_visiblePages.IndexOf(_currentPage) + direction + _visiblePages.Count) % _visiblePages.Count;
        ShowPage(_visiblePages[index], direction);
    }

    // --- Média ---

    private void OnMediaChanged()
    {
        UpdateCompactIndicators();
        UpdateTint();
    }

    private void UpdateTint()
    {
        var tint = _settings.Current.TintEnabled ? MediaPanel.Service.DominantColor : null;
        if (tint == _tint)
        {
            return;
        }
        _tint = tint;
        CapsuleFill.BeginAnimation(SolidColorBrush.ColorProperty,
            new ColorAnimation(_isExpanded ? ExpandedFill : CompactFill, Scaled(TimeSpan.FromMilliseconds(500))));
    }

    private Color CompactFill => Blend(ThemeService.CompactFill, _tint, CompactTintStrength);

    private Color ExpandedFill => Blend(ThemeService.ExpandedFill, _tint, ExpandedTintStrength);

    private static Color Blend(Color baseColor, Color? tint, double strength)
    {
        if (tint is not { } color)
        {
            return baseColor;
        }
        return Color.FromArgb(baseColor.A,
            (byte)(baseColor.R + (color.R - baseColor.R) * strength),
            (byte)(baseColor.G + (color.G - baseColor.G) * strength),
            (byte)(baseColor.B + (color.B - baseColor.B) * strength));
    }

    // --- AirPods ---

    private void UpdateAirPodsLayout()
    {
        AirPodsPanel.Visibility = AirPodsPanel.IsAvailable ? Visibility.Visible : Visibility.Collapsed;
        ExpandedPanel.Height = CurrentExpandedHeight;
        if (_isExpanded)
        {
            Animate(Capsule, HeightProperty, CurrentExpandedHeight, TimeSpan.FromMilliseconds(260), new CubicEase { EasingMode = EasingMode.EaseInOut });
        }
        UpdateCompactIndicators();
    }

    private void OnAirPodsConnectionChanged(bool isConnected)
    {
        UpdateCompactIndicators();
        // Panneau ouvert : la ligne AirPods affiche déjà le changement.
        if (!_isExpanded && !_isHiddenForFullscreen)
        {
            ShowBanner(isConnected);
        }
    }


    private bool IsInFocusMode => _settings.Current.FocusWithTimerEnabled && TimerPanel.Service.IsActive;

    private void OnTimerChanged()
    {
        var isActive = TimerPanel.Service.IsActive;
        if (isActive && !_wasTimerActive && _settings.Current.FocusWithTimerEnabled)
        {
            _missedDuringFocus = 0;
            _bannerQueue.Clear(); // Ce qui attendait encore ne doit pas interrompre la concentration.
        }
        _wasTimerActive = isActive;
        UpdateCompactIndicators();
    }

    private void OnTimerCompleted(TimeSpan duration)
    {
        System.Media.SystemSounds.Exclamation.Play();
        var minutes = duration.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture);
        ShowInfoBanner("", "Minuteur terminé", $"{minutes} min écoulées{DescribeMissed()}");
    }

    private void OnTimerStopped()
    {
        if (_missedDuringFocus > 0)
        {
            ShowInfoBanner("", "Concentration terminée", DescribeMissed().TrimStart(' ', '·'));
        }
    }

    private string DescribeMissed()
    {
        var count = _missedDuringFocus;
        _missedDuringFocus = 0;
        return count switch
        {
            0 => string.Empty,
            1 => " · 1 notification reçue (Win + N)",
            _ => $" · {count} notifications reçues (Win + N)",
        };
    }

    private void CheckNetworkAvailability()
    {
        _networkAlertTimer.Stop();
        var isUp = NetworkInterface.GetIsNetworkAvailable();
        if (isUp == _isNetworkUp)
        {
            return;
        }
        _isNetworkUp = isUp;
        if (_settings.Current.NetworkAlertsEnabled && !IsInFocusMode)
        {
            ShowInfoBanner("", isUp ? "Connexion rétablie" : "Connexion perdue", isUp ? "Vous êtes de nouveau en ligne" : "Aucun réseau disponible");
        }
    }

    // Aussi appelé chaque minute : tous les PC ne signalent pas chaque variation du niveau de batterie.
    private void CheckPowerStatus()
    {
        if (!NativeMethods.GetSystemPowerStatus(out var status)
            || (status.BatteryFlag & 128) != 0 // Pas de batterie.
            || status.BatteryLifePercent == 255)
        {
            return;
        }

        var isOnAcPower = status.ACLineStatus == 1;
        var percent = status.BatteryLifePercent.ToString(CultureInfo.InvariantCulture) + " %";
        var crossed = _pcBatteryPolicy.Update(status.BatteryLifePercent, isOnAcPower);
        var hasPlugChanged = _wasOnAcPower is { } wasOn && wasOn != isOnAcPower;
        _wasOnAcPower = isOnAcPower;

        if (!_settings.Current.BatteryAlertsEnabled)
        {
            return;
        }
        if (hasPlugChanged)
        {
            ShowInfoBanner(isOnAcPower ? "" : "", isOnAcPower ? "Chargeur branché" : "Chargeur débranché", "Batterie à " + percent);
        }
        if (crossed is not null)
        {
            ShowInfoBanner("", "Batterie faible", "Plus que " + percent + " : pensez à brancher le chargeur");
        }
    }

    private void OnAirPodsBatteryChanged()
    {
        BannerRings.Update(AirPodsPanel.Battery);
        if (AirPodsPanel.Battery is not { } battery || !AirPodsPanel.IsConnected)
        {
            return;
        }
        // Le plus faible des deux écouteurs décide. Le boîtier n'est pas concerné : il n'a pas besoin d'être en poche.
        int?[] levels = [battery.Left, battery.Right];
        if (levels.Min() is not { } lowest)
        {
            return;
        }
        var crossed = _airPodsBatteryPolicy.Update(lowest, battery.IsLeftCharging || battery.IsRightCharging);
        if (crossed is not null && _settings.Current.BatteryAlertsEnabled)
        {
            ShowInfoBanner("", "Batterie des AirPods faible", $"Environ {lowest} %");
        }
    }

    // --- Micro et caméra ---

    private void UpdatePrivacyDots()
    {
        var mic = _privacyIndicators.IsMicrophoneInUse ? Visibility.Visible : Visibility.Collapsed;
        var camera = _privacyIndicators.IsCameraInUse ? Visibility.Visible : Visibility.Collapsed;
        CompactMicDot.Visibility = mic;
        ExpandedMicDot.Visibility = mic;
        CompactCameraDot.Visibility = camera;
        ExpandedCameraDot.Visibility = camera;
    }

    // --- Note rapide : la capsule accepte le focus clavier uniquement pendant la saisie ---

    private void BeginTyping(Action focusInput)
    {
        _isTyping = true;
        _collapseTimer.Stop();
        _foregroundBeforeTyping = NativeMethods.GetForegroundWindow();
        SetActivatable(true);
        NativeMethods.SetForegroundWindow(_hwnd);
        Activate();
        focusInput();
    }

    private void EndTyping()
    {
        if (!_isTyping)
        {
            return;
        }
        _isTyping = false;
        Keyboard.ClearFocus();
        NotePanel.Save();
        SetActivatable(false);
        // Si l'utilisateur a validé sans cliquer ailleurs, le clavier revient à la fenêtre qu'il utilisait.
        if (NativeMethods.GetForegroundWindow() == _hwnd && _foregroundBeforeTyping != IntPtr.Zero)
        {
            NativeMethods.SetForegroundWindow(_foregroundBeforeTyping);
        }
        if (_isExpanded && !_isPinned)
        {
            _collapseTimer.Start();
        }
    }

    private void SetActivatable(bool isActivatable)
    {
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style = isActivatable ? style & ~NativeMethods.WS_EX_NOACTIVATE : style | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }



    private void UpdateCompactIndicators()
    {
        var isCompact = !_isExpanded && !_isBannerVisible;
        AirPodsIndicator.Visibility = AirPodsPanel.IsConnected ? Visibility.Visible : Visibility.Collapsed;
        MailIndicator.Visibility = MailPanel.TotalUnread > 0 ? Visibility.Visible : Visibility.Collapsed;
        MailCountText.Text = MailPanel.TotalUnread > 99 ? "99+" : MailPanel.TotalUnread.ToString(CultureInfo.InvariantCulture);
        // Le minuteur prend la place de l'égaliseur : la capsule compacte est trop étroite pour les deux.
        var timer = TimerPanel.Service;
        CompactTimerText.Visibility = timer.IsActive ? Visibility.Visible : Visibility.Collapsed;
        CompactTimerText.Text = timer.IsActive ? TimerService.Format(timer.Remaining) : string.Empty;
        // Masqué dès que le mode compact n'est plus affiché, pour arrêter l'animation.
        PlayingIndicator.Visibility = MediaPanel.Service.IsPlaying && isCompact && !timer.IsActive ? Visibility.Visible : Visibility.Collapsed;

        var temperature = WeatherPanel.CurrentTemperature;
        var showTemperature = _settings.Current.CompactTemperatureEnabled && temperature is not null;
        CompactTemperatureText.Visibility = showTemperature ? Visibility.Visible : Visibility.Collapsed;
        CompactTemperatureText.Text = showTemperature ? temperature!.Value.ToString(CultureInfo.InvariantCulture) + "°" : string.Empty;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // --- Souris : survol, verrouillage et déplacement ---

    private void Capsule_MouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        if (_bannerClick is not null)
        {
            // Survoler la bannière la maintient ouverte : le panneau la remplacerait avant le clic.
            _bannerTimer.Stop();
        }
        else if (!_isExpanded)
        {
            _expandTimer.Start();
        }
    }

    private void Capsule_MouseLeave(object sender, MouseEventArgs e)
    {
        _expandTimer.Stop();
        if (_bannerClick is not null)
        {
            _isBannerPressed = false;
            _bannerTimer.Interval = MailBannerDurationAfterHover;
            _bannerTimer.Start();
        }
        else if (_isExpanded && !_isPinned)
        {
            _collapseTimer.Start();
        }
    }

    private void Capsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressStartedOnCapsule = true;
        _isDraggingWindow = false;
        NativeMethods.GetCursorPos(out _dragStartCursor);
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        _dragStartLeft = rect.Left;
        Capsule.CaptureMouse();
    }

    private void Capsule_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressStartedOnCapsule)
        {
            return;
        }

        NativeMethods.GetCursorPos(out var cursor);
        var delta = cursor.X - _dragStartCursor.X;
        if (!_isDraggingWindow && Math.Abs(delta) < DragThresholdPixels)
        {
            return;
        }
        _isDraggingWindow = true;

        if (ScreenService.Resolve(_settings.Current.MonitorDevice) is { } screen)
        {
            NativeMethods.GetWindowRect(_hwnd, out var rect);
            var maxLeft = screen.Bounds.Right - (rect.Right - rect.Left);
            var left = Math.Clamp(_dragStartLeft + delta, screen.Bounds.Left, Math.Max(screen.Bounds.Left, maxLeft));
            // Y reste au bord supérieur de l'écran : seul X change.
            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, left, screen.Bounds.Top, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }
    }

    private void Capsule_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressStartedOnCapsule)
        {
            return;
        }
        _pressStartedOnCapsule = false;
        var wasDragging = _isDraggingWindow;
        _isDraggingWindow = false;
        Capsule.ReleaseMouseCapture();

        if (wasDragging)
        {
            SaveHorizontalPosition();
        }
        else
        {
            TogglePinned();
        }
    }

    private void Capsule_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDraggingWindow)
        {
            SaveHorizontalPosition();
        }
        _pressStartedOnCapsule = false;
        _isDraggingWindow = false;
    }

    private void SaveHorizontalPosition()
    {
        if (ScreenService.Resolve(_settings.Current.MonitorDevice) is not { } screen)
        {
            return;
        }
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        var range = screen.Width - (rect.Right - rect.Left);
        var position = range > 0 ? (rect.Left - screen.Bounds.Left) / (double)range : 0.5;
        _settings.UpdateSilently(settings => settings.HorizontalPosition = Math.Clamp(position, 0, 1));
    }

    private void TogglePinned()
    {
        _isPinned = !_isPinned;
        PinGlyph.Text = _isPinned ? "" : "";
        PinGlyph.Foreground = (Brush)FindResource(_isPinned ? "TextPrimaryBrush" : "TextSecondaryBrush");
        if (_isPinned)
        {
            _expandTimer.Stop();
            _collapseTimer.Stop();
            Expand();
        }
    }

    private void TryCollapse()
    {
        _collapseTimer.Stop();
        if (_isPinned || !_isExpanded)
        {
            return;
        }
        // Filet de sécurité : ignore un MouseLeave parasite si le curseur est encore sur la capsule.
        if (IsCursorOverCapsule() || Mouse.Captured is not null || _isTyping)
        {
            _collapseTimer.Start();
            return;
        }
        Collapse();
    }

    private bool IsCursorOverCapsule()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var topLeft = Capsule.PointToScreen(default);
        var bottomRight = Capsule.PointToScreen(new Point(Capsule.ActualWidth, Capsule.ActualHeight));
        return cursor.X >= topLeft.X && cursor.X < bottomRight.X && cursor.Y >= topLeft.Y && cursor.Y < bottomRight.Y;
    }

    // --- Animations ---

    private void Expand()
    {
        if (_isExpanded)
        {
            return;
        }
        _isExpanded = true;
        _trimTimer.Stop();
        SetPageHotkeys(true);
        if (_isBannerVisible)
        {
            HideBannerContent();
        }
        UpdateCompactIndicators();

        ExpandedPanel.Visibility = Visibility.Visible;
        CapsuleShadow.Visibility = Visibility.Visible;
        Capsule.BorderBrush = (Brush)FindResource("HoverBrush");
        Capsule.BorderThickness = ExpandedBorder;

        var duration = TimeSpan.FromMilliseconds(380);
        var spring = new SpringEase();
        Animate(Capsule, WidthProperty, ExpandedWidth, duration, spring);
        Animate(Capsule, HeightProperty, CurrentExpandedHeight, duration, spring);
        Animate(this, CapsuleRadiusProperty, ExpandedRadius, duration, null);
        CapsuleFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(ExpandedFill, Scaled(TimeSpan.FromMilliseconds(200))));
        Animate(CompactContent, OpacityProperty, 0, TimeSpan.FromMilliseconds(90), null);
        Animate(ExpandedPanel, OpacityProperty, 1, TimeSpan.FromMilliseconds(220), null, TimeSpan.FromMilliseconds(90));
        Animate(CapsuleShadow, OpacityProperty, 1, TimeSpan.FromMilliseconds(220), null);
    }

    private void Collapse()
    {
        _isExpanded = false;
        SetPageHotkeys(false);
        if (_isBannerVisible)
        {
            HideBannerContent();
        }
        UpdateCompactIndicators();

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var sizeAnimation = new DoubleAnimation(CompactHeight, Scaled(duration)) { EasingFunction = ease };
        sizeAnimation.Completed += (_, _) =>
        {
            // Une réouverture ou une bannière a pu survenir pendant l'animation.
            if (!_isExpanded && !_isBannerVisible)
            {
                BannerPanel.Visibility = Visibility.Collapsed;
                ExpandedPanel.Visibility = Visibility.Collapsed;
                CapsuleShadow.Visibility = Visibility.Hidden;
                Capsule.BorderThickness = CompactBorder;
                _trimTimer.Start();
                ShowNextQueuedBanner();
            }
        };
        Capsule.BeginAnimation(HeightProperty, sizeAnimation);
        Animate(Capsule, WidthProperty, CompactWidth, duration, ease);
        Animate(this, CapsuleRadiusProperty, CompactRadius, duration, null);
        CapsuleFill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(CompactFill, Scaled(duration)));
        Animate(ExpandedPanel, OpacityProperty, 0, TimeSpan.FromMilliseconds(100), null);
        Animate(CompactContent, OpacityProperty, 1, TimeSpan.FromMilliseconds(160), null, TimeSpan.FromMilliseconds(100));
        Animate(CapsuleShadow, OpacityProperty, 0, duration, null);
    }

    private TimeSpan Scaled(TimeSpan duration) => duration * _settings.Current.AnimationDurationFactor;

    private void Animate(IAnimatable target, DependencyProperty property, double to, TimeSpan duration,
        IEasingFunction? ease, TimeSpan delay = default)
    {
        target.BeginAnimation(property, new DoubleAnimation(to, Scaled(duration)) { EasingFunction = ease, BeginTime = Scaled(delay) });
    }

    // --- Fenêtre et écran ---

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Pas de vol de focus au clic, et invisible dans Alt+Tab.
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));

        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        PositionOnTargetScreen();
        UpdateHotkeyRegistration();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DISPLAYCHANGE or NativeMethods.WM_DPICHANGED)
        {
            // Différé : WPF applique d'abord sa propre mise à l'échelle.
            Dispatcher.BeginInvoke(PositionOnTargetScreen, DispatcherPriority.Loaded);
        }
        else if (msg == NativeMethods.WM_HOTKEY)
        {
            OnHotkey(wParam.ToInt32());
            handled = true;
        }
        else if (msg == NativeMethods.WM_SETTINGCHANGE && lParam != IntPtr.Zero
            && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet" && ThemeService.NeedsRestart(_settings.Current))
        {
            App.RestartToApplyTheme(reopenSettings: false); // Le thème de Windows vient de changer.
        }
        else if (msg == NativeMethods.WM_POWERBROADCAST && wParam.ToInt32() == NativeMethods.PBT_APMPOWERSTATUSCHANGE)
        {
            CheckPowerStatus();
        }
        return IntPtr.Zero;
    }

    /// <summary>Place la fenêtre en haut de l'écran choisi, en pixels physiques (High-DPI).</summary>
    private void PositionOnTargetScreen()
    {
        if (_isDraggingWindow || ScreenService.Resolve(_settings.Current.MonitorDevice) is not { } screen)
        {
            return;
        }
        NativeMethods.GetDpiForMonitor(screen.Handle, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _);

        var width = (int)Math.Round(Width * dpi / 96.0);
        var height = (int)Math.Round(Height * dpi / 96.0);
        var left = screen.Bounds.Left + (int)((screen.Width - width) * _settings.Current.HorizontalPosition);

        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, left, screen.Bounds.Top, width, height,
            NativeMethods.SWP_NOACTIVATE);
    }

    private void UpdateFullscreenVisibility() => SetHiddenForFullscreen(IsFullscreenAppActive());

    private bool IsFullscreenAppActive()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _hwnd
            || ScreenService.Resolve(_settings.Current.MonitorDevice) is not { } screen
            || !NativeMethods.GetWindowRect(foreground, out var rect))
        {
            return false;
        }

        // Une fenêtre agrandie ne recouvre pas la barre des tâches : seul un vrai plein écran couvre tout le moniteur.
        if (rect.Left > screen.Bounds.Left || rect.Top > screen.Bounds.Top
            || rect.Right < screen.Bounds.Right || rect.Bottom < screen.Bounds.Bottom)
        {
            return false;
        }

        // Le Bureau couvre aussi tout l'écran.
        var className = new StringBuilder(64);
        NativeMethods.GetClassName(foreground, className, className.Capacity);
        return !ShellWindowClasses.Contains(className.ToString());
    }

    private void SetHiddenForFullscreen(bool hide)
    {
        if (hide == _isHiddenForFullscreen)
        {
            return;
        }
        _isHiddenForFullscreen = hide;

        if (hide)
        {
            _bannerQueue.Clear();
            _expandTimer.Stop();
            _collapseTimer.Stop();
            if ((_isExpanded && !_isPinned) || _isBannerVisible)
            {
                Collapse();
            }
            Hide();
        }
        else
        {
            Show();
            PositionOnTargetScreen();
        }
        // L'égaliseur s'arrête seul : IsVisible devient faux quand la fenêtre est masquée.
        ScheduleMemoryTrim();
    }

    /// <summary>Demande une libération de mémoire dans quelques secondes, si la capsule est alors au repos.</summary>
    internal void ScheduleMemoryTrim()
    {
        _trimTimer.Stop();
        _trimTimer.Start();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        ExpandedClockText.Text = ClockText.Text;
        CheckPowerStatus();

        // Un seul réveil par minute, calé sur le changement de minute.
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond);
        _clockTimer.Start();
    }
}
