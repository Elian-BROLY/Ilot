using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ilot.Services;
using Ilot.Settings;

namespace Ilot.Widgets;

/// <summary>Les rendez-vous d'aujourd'hui et de demain. Page masquée dans les réglages : aucune requête, ni rappel, ni décompte.</summary>
public partial class CalendarWidget : UserControl
{
    private const int MaxLines = 4;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);
    private static readonly MailProvider[] Providers = [MailProvider.Outlook, MailProvider.Gmail, MailProvider.Gmail2];

    private readonly DispatcherTimer _pollTimer = new(DispatcherPriority.Background) { Interval = PollInterval };
    private readonly DispatcherTimer _reminderTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(30) };
    private readonly HashSet<string> _announced = [];
    private List<CalendarEvent> _events = [];
    private bool _isRefreshing;
    private bool _wasEnabled;

    public CalendarWidget()
    {
        InitializeComponent();
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _reminderTimer.Tick += (_, _) => CheckReminder();
        App.Settings.Changed += ApplyEnabled;
        App.Mail.AccountsChanged += () => _ = RefreshAsync();
        ApplyEnabled();
    }

    /// <summary>Levé 5 minutes avant un rendez-vous.</summary>
    internal event Action<CalendarEvent>? ReminderDue;

    /// <summary>Levé quand le décompte de la capsule peut avoir changé.</summary>
    public event Action? CountdownChanged;

    /// <summary>Minutes avant le prochain rendez-vous s'il est proche, sinon null.</summary>
    public int? CountdownMinutes => _wasEnabled ? CalendarEvents.CountdownMinutesLeft(_events, DateTime.UtcNow) : null;

    private static bool IsPageEnabled => App.Settings.Current.Pages.Any(page => page.Id == PageIds.Calendar && page.IsEnabled);

    private void ApplyEnabled()
    {
        var isEnabled = IsPageEnabled;
        App.Mail.RequestCalendarAccess = isEnabled;
        if (isEnabled == _wasEnabled)
        {
            return;
        }
        _wasEnabled = isEnabled;
        if (isEnabled)
        {
            _pollTimer.Start();
            _reminderTimer.Start();
            _ = RefreshAsync();
        }
        else
        {
            _pollTimer.Stop();
            _reminderTimer.Stop();
            _events = [];
            CountdownChanged?.Invoke();
        }
    }

    private async Task RefreshAsync()
    {
        if (_isRefreshing || !_wasEnabled)
        {
            return;
        }
        _isRefreshing = true;
        try
        {
            var events = new List<CalendarEvent>();
            var anyConnected = false;
            var needsPermission = false;
            var hasFailed = false;
            foreach (var provider in Providers.Where(App.Mail.IsConnected))
            {
                anyConnected = true;
                var result = await App.Mail.GetCalendarAsync(provider, DateTime.UtcNow);
                events.AddRange(result.Events);
                needsPermission |= result.NeedsPermission;
                hasFailed |= result.HasFailed;
            }
            _events = events;
            Render(anyConnected, needsPermission, hasFailed);
            CheckReminder();
            CountdownChanged?.Invoke();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex); // Une erreur imprévue ne doit pas arrêter les relectures suivantes.
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void CheckReminder()
    {
        if (CalendarEvents.DueForReminder(_events, DateTime.UtcNow, _announced) is { } due)
        {
            ReminderDue?.Invoke(due);
        }
        CountdownChanged?.Invoke();
    }

    private void Render(bool anyConnected, bool needsPermission, bool hasFailed)
    {
        Lines.Children.Clear();
        var upcoming = CalendarEvents.Upcoming(_events, DateTime.UtcNow);
        foreach (var item in upcoming.Take(MaxLines))
        {
            Lines.Children.Add(CreateLine(item));
        }
        MessageText.Visibility = upcoming.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MessageText.Text =
            !anyConnected ? Loc.T("Connectez un compte Google ou Outlook dans les réglages")
            : needsPermission ? Loc.T("Reconnectez le compte pour autoriser l'agenda (Réglages, onglet Mails)")
            : hasFailed ? Loc.T("Agenda indisponible pour le moment")
            : Loc.T("Rien de prévu aujourd'hui ni demain");
    }

    private Border CreateLine(CalendarEvent item)
    {
        var start = item.StartUtc.ToLocalTime();
        var isToday = start.Date == DateTime.Now.Date;
        var time = item.IsAllDay ? Loc.T("Journée") : start.ToString("HH:mm", CultureInfo.InvariantCulture);
        var day = isToday ? string.Empty : Loc.T("Demain") + " ";

        var timeText = new TextBlock
        {
            Width = 92,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            Text = day + time,
        };
        var titleText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = item.Title,
        };
        var content = new DockPanel { Margin = new Thickness(6, 0, 6, 0) };
        DockPanel.SetDock(timeText, Dock.Left);
        content.Children.Add(timeText);
        if (item.JoinUrl is not null)
        {
            var join = new TextBlock
            {
                Margin = new Thickness(8, 0, 0, 0),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)FindResource("AccentBrush"),
                Text = Loc.T("Rejoindre"),
            };
            DockPanel.SetDock(join, Dock.Right);
            content.Children.Add(join);
        }
        content.Children.Add(titleText);

        var line = new Border { Height = 25, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = content };
        line.MouseEnter += (_, _) => line.Background = (Brush)FindResource("SurfaceBrush");
        line.MouseLeave += (_, _) => line.Background = Brushes.Transparent;
        line.MouseLeftButtonDown += (_, e) => e.Handled = true; // Ouvrir un rendez-vous ne doit ni verrouiller ni déplacer la capsule.
        line.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if ((item.JoinUrl ?? item.OpenUrl) is { } url)
            {
                App.Mail.OpenInBrowser(url);
            }
        };
        return line;
    }
}
