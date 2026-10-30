using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ilot.Services;

namespace Ilot.Widgets;

public partial class LyricsWidget : UserControl
{
    private readonly DispatcherTimer _syncTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private MediaService? _media;
    private Lyrics? _lyrics;
    private string? _loadedTrack;
    private int _requestVersion;

    public LyricsWidget()
    {
        InitializeComponent();
        _syncTimer.Tick += (_, _) => RenderCurrentLine();
        IsVisibleChanged += (_, _) => OnContextChanged();
    }

    /// <summary>Si le texte brut déborde, la molette le fait défiler plutôt que de changer de page.</summary>
    public bool IsContentScrollable => PlainScroller.Visibility == Visibility.Visible && PlainScroller.ScrollableHeight > 0;

    internal void Attach(MediaService media)
    {
        _media = media;
        media.Changed += OnContextChanged;
    }

    // Aucune requête tant que la page n'est pas affichée : l'artiste et le titre ne partent que si on les demande.
    private void OnContextChanged()
    {
        if (!IsVisible || _media is null)
        {
            _syncTimer.Stop();
            return;
        }

        var track = $"{_media.Artist}|{_media.Title}";
        if (_media.Title.Length == 0)
        {
            _loadedTrack = null;
            _lyrics = null;
            ShowMessage("Aucune lecture en cours");
        }
        else if (track != _loadedTrack)
        {
            _loadedTrack = track;
            _ = LoadAsync(_media.Artist, _media.Title);
        }
        UpdateSyncTimer();
    }

    private async Task LoadAsync(string artist, string title)
    {
        var version = ++_requestVersion;
        _lyrics = null;
        ShowMessage("Recherche des paroles…");
        var lyrics = await LyricsService.FindAsync(artist, title);
        if (version != _requestVersion)
        {
            return; // Le morceau a changé pendant la recherche.
        }

        _lyrics = lyrics;
        if (lyrics is null)
        {
            ShowMessage("Paroles introuvables pour ce morceau");
        }
        else if (lyrics.Synced.Count > 0)
        {
            MessageText.Visibility = Visibility.Collapsed;
            PlainScroller.Visibility = Visibility.Collapsed;
            SyncedPanel.Visibility = Visibility.Visible;
            RenderCurrentLine();
        }
        else
        {
            MessageText.Visibility = Visibility.Collapsed;
            SyncedPanel.Visibility = Visibility.Collapsed;
            PlainText.Text = lyrics.Plain;
            PlainScroller.ScrollToTop();
            PlainScroller.Visibility = Visibility.Visible;
        }
        UpdateSyncTimer();
    }

    private void UpdateSyncTimer()
    {
        if (IsVisible && _lyrics is { Synced.Count: > 0 } && _media is { IsPlaying: true })
        {
            _syncTimer.Start();
        }
        else
        {
            _syncTimer.Stop();
        }
    }

    private void RenderCurrentLine()
    {
        if (_lyrics is not { Synced.Count: > 0 } lyrics || _media is null)
        {
            return;
        }

        var lines = lyrics.Synced;
        var position = _media.Position;
        var index = -1;
        while (index + 1 < lines.Count && lines[index + 1].Time <= position)
        {
            index++;
        }
        PreviousLine.Text = index > 0 ? lines[index - 1].Text : string.Empty;
        CurrentLine.Text = index >= 0 ? lines[index].Text : "♪";
        NextLine.Text = index + 1 < lines.Count ? lines[index + 1].Text : string.Empty;
    }

    private void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
        SyncedPanel.Visibility = Visibility.Collapsed;
        PlainScroller.Visibility = Visibility.Collapsed;
    }
}
