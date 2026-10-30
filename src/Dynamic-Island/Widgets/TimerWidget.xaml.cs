using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Services;

namespace DynamicIsland.Widgets;

public partial class TimerWidget : UserControl
{
    private const int MaxMinutes = 1440;
    private static readonly int[] PresetMinutes = [5, 25, 45];

    public TimerWidget()
    {
        InitializeComponent();
        foreach (var minutes in PresetMinutes)
        {
            Presets.Children.Add(CreatePreset(minutes));
        }
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
        AutomationProperties.SetName(button, "Minuteur " + label);
        button.Click += (_, _) => Service.Start(TimeSpan.FromMinutes(minutes));
        return button;
    }

    private void Render()
    {
        var isActive = Service.IsActive;
        RemainingText.Text = isActive ? TimerService.Format(Service.Remaining) : "Minuteur";
        StatusText.Text = !isActive
            ? "Choisissez une durée"
            : Service.IsPaused ? "En pause" : App.Settings.Current.FocusWithTimerEnabled ? "Concentration : notifications masquées" : "En cours";
        SetupPanel.Visibility = isActive ? Visibility.Collapsed : Visibility.Visible;
        RunningPanel.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Content = Service.IsPaused ? "Reprendre" : "Pause";
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
            StatusText.Text = "Entre 1 et 1440 minutes";
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
