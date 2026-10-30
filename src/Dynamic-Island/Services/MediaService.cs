using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services;

/// <summary>
/// Suit une session média du système via GSMTC : celle choisie à la main, sinon le lecteur préféré, sinon la session courante de Windows.
/// <see cref="Changed"/> est levé sur le thread UI.
/// </summary>
internal sealed class MediaService
{
    private const int ThumbnailDecodeWidth = 176; // 88 DIP en 200 %
    private const int HueBuckets = 12;
    private const double MinimumTintWeight = 0.08; // En dessous, la pochette est trop grise pour teinter la capsule.

    // Windows n'indique que l'application : un site ouvert dans un navigateur porte le nom du navigateur.
    private static readonly (string Fragment, string Name)[] KnownSources =
    [
        ("deezer", "Deezer"),
        ("spotify", "Spotify"),
        ("youtube", "YouTube"),
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("308046B0AF4A39CB", "Firefox"),
        ("firefox", "Firefox"),
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("vivaldi", "Vivaldi"),
        ("applemusic", "Apple Music"),
        ("zunemusic", "Lecteur multimédia"),
        ("vlc", "VLC"),
    ];

    private readonly Dispatcher _dispatcher;
    private readonly TrackChangeDetector _trackChanges = new();
    private string _preferredSource = string.Empty;
    private bool _showPlayingOverPausedPreferred;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private List<(string Id, GlobalSystemMediaTransportControlsSession Session)> _sources = [];
    private string? _sourceId;
    private string? _manualSourceId;
    private int _refreshVersion;
    private TimeSpan _reportedPosition;
    private DateTimeOffset _positionTimestamp;
    private TimeSpan _timelineStart;

    public MediaService(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public event Action? Changed;

    /// <summary>Levé quand le lecteur affiché passe à un autre morceau, pas quand on change de lecteur.</summary>
    public event Action? TrackChanged;

    public bool IsPreferredSourceShown => _preferredSource.Length == 0 || (_sourceId is not null && IsPreferred(_sourceId, _preferredSource));
    public bool HasSession { get; private set; }
    public int SourceCount => _sources.Count;
    public string SourceName => _sourceId is null ? string.Empty : GetSourceName(_sourceId);
    public string NextSourceName => _sources.Count == 0 ? string.Empty : GetSourceName(_sources[NextSourceIndex].Id);
    public bool IsPlaying { get; private set; }
    public bool CanGoPrevious { get; private set; }
    public bool CanTogglePlayPause { get; private set; }
    public bool CanGoNext { get; private set; }
    public bool CanSeek { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Artist { get; private set; } = string.Empty;
    public BitmapSource? Thumbnail { get; private set; }
    public Color? DominantColor { get; private set; }
    /// <summary>Zéro si l'application ne publie pas de chronologie (certains sites web, par exemple).</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>Position estimée : les applications ne la publient que de temps en temps.</summary>
    public TimeSpan Position
    {
        get
        {
            var position = _reportedPosition;
            if (IsPlaying)
            {
                position += DateTimeOffset.Now - _positionTimestamp;
            }
            return position < TimeSpan.Zero ? TimeSpan.Zero : position > Duration ? Duration : position;
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return; // GSMTC indisponible : le widget reste sur son état vide.
        }
        _manager.CurrentSessionChanged += (_, _) => _dispatcher.InvokeAsync(AttachPreferredSession);
        _manager.SessionsChanged += (_, _) => _dispatcher.InvokeAsync(AttachPreferredSession);
        AttachPreferredSession();
    }

    /// <summary>Passe au lecteur suivant. Ce choix tient tant que ce lecteur reste ouvert.</summary>
    public void SelectNextSource()
    {
        if (_sources.Count > 1)
        {
            _manualSourceId = _sources[NextSourceIndex].Id;
            AttachPreferredSession();
        }
    }

    public void SetPreferences(string preferredSource, bool showPlayingOverPausedPreferred)
    {
        if (preferredSource == _preferredSource && showPlayingOverPausedPreferred == _showPlayingOverPausedPreferred)
        {
            return;
        }
        _preferredSource = preferredSource;
        _showPlayingOverPausedPreferred = showPlayingOverPausedPreferred;
        if (_manager is not null)
        {
            AttachPreferredSession();
        }
    }

    /// <summary>
    /// Priorité : le choix manuel s'il est encore ouvert, puis le lecteur préféré, puis la session courante de Windows.
    /// Avec <paramref name="playingIds"/>, un lecteur préféré en pause cède sa place à celui qui joue.
    /// </summary>
    internal static string? ChooseSource(IReadOnlyList<string> ids, string? manualId, string? currentId, string preferredSource,
        IReadOnlyCollection<string>? playingIds = null)
    {
        if (manualId is not null && ids.Contains(manualId))
        {
            return manualId;
        }

        var preferred = ids.FirstOrDefault(id => IsPreferred(id, preferredSource));
        if (preferred is not null && (playingIds is not { Count: > 0 } || playingIds.Contains(preferred)))
        {
            return preferred;
        }
        if (playingIds is { Count: > 0 })
        {
            if (currentId is not null && playingIds.Contains(currentId) && ids.Contains(currentId))
            {
                return currentId;
            }
            if (ids.FirstOrDefault(playingIds.Contains) is { } playing)
            {
                return playing;
            }
        }
        return preferred ?? (currentId is not null && ids.Contains(currentId) ? currentId : ids.FirstOrDefault());
    }

    internal static string GetSourceName(string id)
    {
        foreach (var (fragment, name) in KnownSources)
        {
            if (id.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }
        var application = id.Split('!')[0];
        return application.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? application[..^4] : application;
    }

    private static bool IsPreferred(string id, string preferredSource) =>
        preferredSource.Length > 0 && id.Contains(preferredSource, StringComparison.OrdinalIgnoreCase);

    private int NextSourceIndex => (_sources.FindIndex(source => source.Id == _sourceId) + 1) % _sources.Count;

    public Task PreviousAsync() => SendAsync(s => s.TrySkipPreviousAsync().AsTask());

    public Task TogglePlayPauseAsync() => SendAsync(s => s.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync() => SendAsync(s => s.TrySkipNextAsync().AsTask());

    public Task SeekAsync(TimeSpan position)
    {
        _reportedPosition = position;
        _positionTimestamp = DateTimeOffset.Now;
        return SendAsync(s => s.TryChangePlaybackPositionAsync((_timelineStart + position).Ticks).AsTask());
    }

    private async Task SendAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        if (_session is { } session)
        {
            try
            {
                await command(session);
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
                // La session a pu disparaître entre le clic et l'envoi : CurrentSessionChanged mettra l'état à jour.
            }
        }
    }

    private void AttachPreferredSession()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        foreach (var (_, session) in _sources)
        {
            session.PlaybackInfoChanged -= OnAnySourcePlaybackChanged;
        }

        _sources = ReadSources();
        var ids = _sources.ConvertAll(source => source.Id);
        if (_manualSourceId is not null && !ids.Contains(_manualSourceId))
        {
            _manualSourceId = null; // Le lecteur choisi a été fermé : retour au choix par défaut.
        }
        _sourceId = ChooseCurrentSource(ids);
        _session = _sourceId is null ? null : _sources.Find(source => source.Id == _sourceId).Session;
        foreach (var (_, session) in _sources)
        {
            session.PlaybackInfoChanged += OnAnySourcePlaybackChanged;
        }
        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }

        UpdatePlaybackInfo();
        UpdateTimeline();
        _ = RefreshMediaPropertiesAsync();
    }

    // Ordre stable : la bascule parcourt toujours les lecteurs dans le même sens.
    private List<(string Id, GlobalSystemMediaTransportControlsSession Session)> ReadSources()
    {
        var sources = new List<(string Id, GlobalSystemMediaTransportControlsSession Session)>();
        try
        {
            if (_manager is not null)
            {
                foreach (var session in _manager.GetSessions())
                {
                    if (sources.TrueForAll(source => source.Id != session.SourceAppUserModelId))
                    {
                        sources.Add((session.SourceAppUserModelId, session));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            // Session fermée pendant la lecture de la liste : SessionsChanged relancera la lecture.
        }
        sources.Sort((a, b) => IsPreferred(a.Id, _preferredSource) != IsPreferred(b.Id, _preferredSource)
            ? IsPreferred(a.Id, _preferredSource) ? -1 : 1
            : string.CompareOrdinal(a.Id, b.Id));
        return sources;
    }

    private string? ChooseCurrentSource(IReadOnlyList<string> ids) =>
        ChooseSource(ids, _manualSourceId, ReadCurrentSourceId(), _preferredSource, _showPlayingOverPausedPreferred ? ReadPlayingIds() : null);

    private List<string> ReadPlayingIds()
    {
        var playing = new List<string>();
        foreach (var (id, session) in _sources)
        {
            try
            {
                if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    playing.Add(id);
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
                // Session fermée pendant la lecture de son état.
            }
        }
        return playing;
    }

    private void OnAnySourcePlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (_showPlayingOverPausedPreferred && ChooseCurrentSource(_sources.ConvertAll(source => source.Id)) != _sourceId)
            {
                AttachPreferredSession();
            }
        });

    private string? ReadCurrentSourceId()
    {
        try
        {
            return _manager?.GetCurrentSession()?.SourceAppUserModelId;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            return null; // Session fermée pendant la lecture de son état.
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _dispatcher.InvokeAsync(() => _ = RefreshMediaPropertiesAsync());

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        _dispatcher.InvokeAsync(UpdatePlaybackInfo);

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) =>
        _dispatcher.InvokeAsync(UpdateTimeline);

    private void UpdatePlaybackInfo()
    {
        // Fige la position estimée avant que IsPlaying ne change.
        _reportedPosition = Position;
        _positionTimestamp = DateTimeOffset.Now;

        HasSession = _session is not null;
        IsPlaying = false;
        CanGoPrevious = CanTogglePlayPause = CanGoNext = CanSeek = false;
        try
        {
            if (_session?.GetPlaybackInfo() is { } info)
            {
                IsPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                CanGoPrevious = info.Controls.IsPreviousEnabled;
                CanTogglePlayPause = info.Controls.IsPlayPauseToggleEnabled;
                CanGoNext = info.Controls.IsNextEnabled;
                CanSeek = info.Controls.IsPlaybackPositionEnabled;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            // Session fermée pendant la lecture de son état.
        }
        Changed?.Invoke();
    }

    private void UpdateTimeline()
    {
        Duration = TimeSpan.Zero;
        try
        {
            if (_session?.GetTimelineProperties() is { } timeline && timeline.EndTime > timeline.StartTime)
            {
                _timelineStart = timeline.StartTime;
                Duration = timeline.EndTime - timeline.StartTime;
                _reportedPosition = timeline.Position - timeline.StartTime;
                _positionTimestamp = timeline.LastUpdatedTime;
            }
        }
        catch (Exception ex)
        {
            AppLog.Write(ex);
            // Session fermée pendant la lecture de son état.
        }
        Changed?.Invoke();
    }

    private async Task RefreshMediaPropertiesAsync()
    {
        var version = ++_refreshVersion;
        var title = string.Empty;
        var artist = string.Empty;
        BitmapSource? thumbnail = null;
        Color? dominantColor = null;

        if (_session is { } session)
        {
            try
            {
                var properties = await session.TryGetMediaPropertiesAsync();
                title = properties.Title ?? string.Empty;
                artist = properties.Artist ?? string.Empty;
                if (properties.Thumbnail is { } reference)
                {
                    (thumbnail, dominantColor) = await Task.Run(() => DecodeThumbnailAsync(reference));
                }
            }
            catch (Exception ex)
            {
                AppLog.Write(ex);
                // Certaines applications ferment la session pendant la requête : on garde l'état vide.
            }
        }

        if (version != _refreshVersion)
        {
            return; // Une requête plus récente a démarré entre-temps.
        }
        Title = title;
        Artist = artist;
        Thumbnail = thumbnail;
        DominantColor = dominantColor;
        var isNewTrack = _trackChanges.Update(_sourceId, artist, title);
        Changed?.Invoke();
        if (isNewTrack)
        {
            TrackChanged?.Invoke();
        }
    }

    private static async Task<(BitmapSource Image, Color? DominantColor)> DecodeThumbnailAsync(IRandomAccessStreamReference reference)
    {
        using var winrtStream = await reference.OpenReadAsync();
        using var source = winrtStream.AsStreamForRead();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        buffer.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = ThumbnailDecodeWidth;
        image.StreamSource = buffer;
        image.EndInit();
        image.Freeze();
        return (image, FindDominantColor(image));
    }

    /// <summary>Moyenne de la teinte la plus représentée, en privilégiant les pixels vifs aux gris et aux noirs.</summary>
    private static Color? FindDominantColor(BitmapSource image)
    {
        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = pixels.PixelWidth * 4;
        var data = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(data, stride, 0);

        var weights = new double[HueBuckets];
        var reds = new double[HueBuckets];
        var greens = new double[HueBuckets];
        var blues = new double[HueBuckets];
        var total = 0.0;
        for (var i = 0; i < data.Length; i += 16) // Un pixel sur quatre suffit.
        {
            double blue = data[i] / 255.0, green = data[i + 1] / 255.0, red = data[i + 2] / 255.0;
            var max = Math.Max(red, Math.Max(green, blue));
            var delta = max - Math.Min(red, Math.Min(green, blue));
            total += 1;
            if (delta <= 0)
            {
                continue;
            }

            var hue = max == red ? (green - blue) / delta : max == green ? 2 + (blue - red) / delta : 4 + (red - green) / delta;
            var bucket = (int)((hue + 6) % 6 / 6 * HueBuckets) % HueBuckets;
            var weight = delta * max; // Saturation × luminosité.
            weights[bucket] += weight;
            reds[bucket] += red * weight;
            greens[bucket] += green * weight;
            blues[bucket] += blue * weight;
        }

        var best = Array.IndexOf(weights, weights.Max());
        if (total == 0 || weights[best] / total < MinimumTintWeight)
        {
            return null;
        }
        return Color.FromRgb((byte)(reds[best] / weights[best] * 255), (byte)(greens[best] / weights[best] * 255), (byte)(blues[best] / weights[best] * 255));
    }
}
