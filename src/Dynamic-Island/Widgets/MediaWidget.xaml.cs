using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Services;

namespace DynamicIsland.Widgets;

public partial class MediaWidget : UserControl
{
    private readonly Brush _emptyArtwork;
    // N'avance la barre que si elle est à l'écran.
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _isRenderingProgress;

    public MediaWidget()
    {
        InitializeComponent();
        _emptyArtwork = Artwork.Background;
        Service = new MediaService(Dispatcher);
        Service.Changed += Render;
        _progressTimer.Tick += (_, _) => RenderProgress();
        IsVisibleChanged += (_, _) => UpdateProgressTimer();
        Render();
        App.Settings.Changed += ApplyPreferences;
        ApplyPreferences();
        _ = Service.InitializeAsync();
    }

    private void ApplyPreferences()
    {
        var settings = App.Settings.Current;
        Service.SetPreferences(settings.PreferredPlayer, settings.ShowPlayingOverPausedPreferred);
    }

    internal MediaService Service { get; }

    private void Render()
    {
        var hasTrack = Service.HasSession && Service.Title.Length > 0;
        TitleText.Text = hasTrack ? Service.Title : "Aucune lecture en cours";
        ArtistText.Text = hasTrack ? Service.Artist : string.Empty;

        PreviousButton.IsEnabled = Service.CanGoPrevious;
        PlayPauseButton.IsEnabled = Service.CanTogglePlayPause;
        NextButton.IsEnabled = Service.CanGoNext;

        SourceButton.Visibility = Service.SourceCount > 1 ? Visibility.Visible : Visibility.Collapsed;
        SourceText.Text = Service.SourceName;
        var switchLabel = $"Passer à {Service.NextSourceName}";
        SourceButton.ToolTip = switchLabel;
        AutomationProperties.SetName(SourceButton, switchLabel);

        PlayPauseButton.Content = Service.IsPlaying ? "" : "";

        if (Service.Thumbnail is { } thumbnail)
        {
            var brush = new ImageBrush(thumbnail) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            Artwork.Background = brush;
            ArtworkPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            Artwork.Background = _emptyArtwork;
            ArtworkPlaceholder.Visibility = Visibility.Visible;
        }

        ProgressRow.Visibility = Service.Duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
        ProgressSlider.IsEnabled = Service.CanSeek;
        RenderProgress();
        UpdateProgressTimer();
    }

    private void UpdateProgressTimer()
    {
        if (IsVisible && Service.IsPlaying && Service.Duration > TimeSpan.Zero)
        {
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }
    }

    private void RenderProgress()
    {
        if (Service.Duration <= TimeSpan.Zero)
        {
            return;
        }
        DurationText.Text = FormatTime(Service.Duration);

        // Ne pas faire reculer le curseur pendant que l'utilisateur le déplace.
        if (ProgressSlider.IsMouseCaptureWithin)
        {
            return;
        }
        var position = Service.Position;
        PositionText.Text = FormatTime(position);
        _isRenderingProgress = true;
        ProgressSlider.Value = position / Service.Duration;
        _isRenderingProgress = false;
    }

    private static string FormatTime(TimeSpan time) => time.ToString(
        time.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isRenderingProgress)
        {
            PositionText.Text = FormatTime(Service.Duration * e.NewValue);
        }
    }

    private void ProgressSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Service.CanSeek)
        {
            _ = Service.SeekAsync(Service.Duration * ProgressSlider.Value);
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e) => _ = Service.PreviousAsync();

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _ = Service.TogglePlayPauseAsync();

    private void Next_Click(object sender, RoutedEventArgs e) => _ = Service.NextAsync();

    private void Source_Click(object sender, RoutedEventArgs e) => Service.SelectNextSource();
}
