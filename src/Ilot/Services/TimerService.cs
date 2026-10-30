using System.Windows.Threading;

namespace Ilot.Services;

/// <summary>L'état d'un minuteur à enregistrer. PausedRemainingSeconds n'est renseigné qu'en pause.</summary>
internal sealed record TimerSnapshot(DateTime EndUtc, double DurationSeconds, double? PausedRemainingSeconds);

/// <summary>Minuteur basé sur l'heure de fin : il reste juste même après une mise en veille du PC ou un redémarrage de l'application.</summary>
internal sealed class TimerService
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _endUtc;
    private TimeSpan _remainingWhenPaused;

    public TimerService() => _timer.Tick += (_, _) => OnTick();

    /// <summary>Levé chaque seconde pendant le décompte, et à chaque changement d'état.</summary>
    public event Action? Changed;

    /// <summary>Levé quand le décompte arrive à zéro, avec la durée initiale.</summary>
    public event Action<TimeSpan>? Completed;

    /// <summary>Levé quand l'utilisateur arrête le minuteur avant la fin.</summary>
    public event Action? Stopped;

    public bool IsPaused { get; private set; }

    /// <summary>Vrai du lancement à la fin du décompte, pause comprise.</summary>
    public bool IsActive => _timer.IsEnabled || IsPaused;

    public TimeSpan Duration { get; private set; }

    /// <summary>Levé à chaque lancement, pause, reprise ou arrêt : de quoi enregistrer l'état sans écrire chaque seconde.</summary>
    public event Action? StateChanged;

    /// <summary>Null si aucun minuteur n'est en cours.</summary>
    public TimerSnapshot? Snapshot => IsActive
        ? new TimerSnapshot(_endUtc, Duration.TotalSeconds, IsPaused ? _remainingWhenPaused.TotalSeconds : null)
        : null;

    /// <summary>Reprend un minuteur enregistré. S'il a expiré entre-temps, Completed sera levé au premier passage.</summary>
    public void Restore(TimerSnapshot snapshot)
    {
        Duration = TimeSpan.FromSeconds(snapshot.DurationSeconds);
        _endUtc = snapshot.EndUtc;
        if (snapshot.PausedRemainingSeconds is { } remaining)
        {
            _remainingWhenPaused = TimeSpan.FromSeconds(remaining);
            IsPaused = true;
        }
        else
        {
            _timer.Start();
        }
        Changed?.Invoke();
    }

    public TimeSpan Remaining
    {
        get
        {
            if (IsPaused)
            {
                return _remainingWhenPaused;
            }
            var remaining = _endUtc - DateTime.UtcNow;
            return !_timer.IsEnabled || remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    public void Start(TimeSpan duration)
    {
        Duration = duration;
        IsPaused = false;
        _endUtc = DateTime.UtcNow + duration;
        _timer.Start();
        Changed?.Invoke();
        StateChanged?.Invoke();
    }

    public void TogglePause()
    {
        if (IsPaused)
        {
            IsPaused = false;
            _endUtc = DateTime.UtcNow + _remainingWhenPaused;
            _timer.Start();
        }
        else if (_timer.IsEnabled)
        {
            _remainingWhenPaused = Remaining;
            IsPaused = true;
            _timer.Stop();
        }
        Changed?.Invoke();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _timer.Stop();
        IsPaused = false;
        Changed?.Invoke();
        StateChanged?.Invoke();
        Stopped?.Invoke();
    }

    /// <summary>Format commun à la page et à la capsule compacte. Arrondi au supérieur : "0:01" reste affiché jusqu'à la fin.</summary>
    public static string Format(TimeSpan time)
    {
        var seconds = (int)Math.Ceiling(time.TotalSeconds);
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }

    private void OnTick()
    {
        if (DateTime.UtcNow < _endUtc)
        {
            Changed?.Invoke();
            return;
        }
        _timer.Stop();
        Changed?.Invoke();
        StateChanged?.Invoke();
        Completed?.Invoke(Duration);
    }
}
