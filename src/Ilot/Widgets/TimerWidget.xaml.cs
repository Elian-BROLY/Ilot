using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class TimerWidget : UserControl
{
    private const int MaxMinutes = 1440;
    private static readonly int[] PresetMinutes = [5, 25, 45];

    private readonly Button _pomodoroButton;

    public TimerWidget()
    {
        InitializeComponent();
        foreach (var minutes in PresetMinutes)
        {
            Presets.Children.Add(CreatePreset(minutes));
        }
        // Sur sa propre ligne : à côté des durées, il couperait le titre de la page.
        _pomodoroButton = CreatePomodoroButton();
        SetupPanel.Children.Add(_pomodoroButton);
        App.Settings.Changed += ApplyPomodoroVisibility;
        ApplyPomodoroVisibility();
        Service.Changed += Render;
    }

    /// <summary>La capsule ne prend jamais le focus d'elle-même : elle doit l'autoriser pour la saisie.</summary>
    public event Action? TypingRequested;

    public event Action? TypingFinished;

    internal TimerService Service { get; } = new();

    public void FocusInput() => CustomMinutesBox.Focus();

    private Button CreatePreset(int minutes)
    {
        var label = minutes.ToString(CultureInfo.InvariantCulture) + " min";
        var button = new Button
        {
            Width = 56,
            Height = 30,
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 11,
            Style = (Style)FindResource("PillRowButton"),
            Background = (Brush)FindResource("SurfaceBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Content = label,
        };
        AutomationProperties.SetName(button, Loc.T("Minuteur ") + label);
        button.Click += (_, _) =>
        {
            TypingFinished?.Invoke(); // Le champ a pu être en cours de saisie : il disparaît au lancement.
            Service.Start(TimeSpan.FromMinutes(minutes));
        };
        return button;
    }

    private void ApplyPomodoroVisibility() =>
        _pomodoroButton.Visibility = App.Settings.Current.PomodoroButtonEnabled ? Visibility.Visible : Visibility.Collapsed;

    private Button CreatePomodoroButton()
    {
        var button = new Button
        {
            Width = 180,
            Height = 26,
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 11,
            Style = (Style)FindResource("PillRowButton"),
            Background = (Brush)FindResource("SurfaceBrush"),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Content = "Pomodoro",
            ToolTip = Loc.T("25 min de travail, 5 min de pause, et 15 min toutes les 4 séances"),
        };
        AutomationProperties.SetName(button, "Pomodoro");
        button.Click += (_, _) =>
        {
            TypingFinished?.Invoke();
            Service.StartPomodoro();
        };
        return button;
    }

    private void Render()
    {
        var isActive = Service.IsActive;
        RemainingText.Text = isActive ? TimerService.Format(Service.Remaining) : Loc.T("Minuteur");
        StatusText.Text = !isActive
            ? Loc.T("Choisissez une durée")
            : Service.IsPaused ? Loc.T("En pause")
            : Service.PomodoroStep is { } step ? (PomodoroCycle.IsBreak(step) ? Loc.T("Pause · séance {0}", PomodoroCycle.Session(step)) : Loc.T("Travail · séance {0}", PomodoroCycle.Session(step)))
            : App.Settings.Current.FocusWithTimerEnabled ? Loc.T("Concentration : notifications masquées") : Loc.T("En cours");
        SetupPanel.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
        RunningPanel.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Content = Service.IsPaused ? Loc.T("Reprendre") : Loc.T("Pause");
    }

    private void StartCustom()
    {
        if (int.TryParse(CustomMinutesBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            && minutes is > 0 and <= MaxMinutes)
        {
            TypingFinished?.Invoke();
            Service.Start(TimeSpan.FromMinutes(minutes));
        }
        else
        {
            StatusText.Text = Loc.T("Entre 1 et 1440 minutes");
        }
    }

    private void CustomMinutesBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CustomMinutesBox.IsKeyboardFocused)
        {
            TypingRequested?.Invoke();
        }
    }

    // Sur un clavier AZERTY, la rangée du haut donne « é », « & »… sans Maj : ici, seul un chiffre a du sens.
    private void CustomMinutesBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is >= Key.D0 and <= Key.D9 && Keyboard.Modifiers == ModifierKeys.None)
        {
            var digit = ((int)e.Key - (int)Key.D0).ToString(CultureInfo.InvariantCulture);
            if (CustomMinutesBox.Text.Length - CustomMinutesBox.SelectionLength < CustomMinutesBox.MaxLength)
            {
                CustomMinutesBox.SelectedText = digit;
                CustomMinutesBox.CaretIndex = CustomMinutesBox.SelectionStart + CustomMinutesBox.SelectionLength;
                CustomMinutesBox.SelectionLength = 0;
            }
            e.Handled = true;
        }
    }

    private void CustomMinutesBox_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void CustomMinutesBox_TextChanged(object sender, TextChangedEventArgs e) =>
        MinutesHint.Visibility = CustomMinutesBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void CustomMinutesBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            StartCustom();
            e.Handled = true;
        }
    }

    private void StartCustom_Click(object sender, RoutedEventArgs e) => StartCustom();

    private void Pause_Click(object sender, RoutedEventArgs e) => Service.TogglePause();

    private void Stop_Click(object sender, RoutedEventArgs e) => Service.Stop();
}
