namespace Ilot.Services;

/// <summary>Enchaînement travail et pauses : 25 min de travail, 5 min de pause, et une pause de 15 min toutes les 4 séances.</summary>
internal static class PomodoroCycle
{
    public static readonly TimeSpan Work = TimeSpan.FromMinutes(25);
    public static readonly TimeSpan ShortBreak = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LongBreak = TimeSpan.FromMinutes(15);
    private const int SessionsBeforeLongBreak = 4;

    /// <summary>Les étapes paires sont du travail, les impaires des pauses.</summary>
    public static bool IsBreak(int step) => step % 2 == 1;

    public static TimeSpan Duration(int step) =>
        !IsBreak(step) ? Work
        : (step + 1) / 2 % SessionsBeforeLongBreak == 0 ? LongBreak
        : ShortBreak;

    /// <summary>Numéro de la séance de travail en cours ou qui vient de finir, à partir de 1.</summary>
    public static int Session(int step) => step / 2 + 1;
}
