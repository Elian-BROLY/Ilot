using System.Windows.Media.Animation;

namespace Ilot.Controls;

/// <summary>Un mouvement de la capsule : sa durée et son départ en millisecondes, avant le multiplicateur de vitesse.</summary>
internal sealed record MotionStep(double DurationMs, double DelayMs, IEasingFunction? Ease);

/// <summary>Comment la capsule s'ouvre ou se ferme. FadesWindow : elle disparaît le temps de changer de taille.</summary>
internal sealed record CapsuleMotion(MotionStep Width, MotionStep Height, MotionStep Radius, bool FadesWindow = false);

/// <summary>Les styles d'animation au choix dans les réglages.</summary>
internal static class CapsuleMotions
{
    public const string Classic = "classic";
    public const string Bounce = "bounce";
    public const string Smooth = "smooth";
    public const string Slide = "slide";
    public const string Fade = "fade";

    public static readonly string[] All = [Classic, Bounce, Smooth, Slide, Fade];

    private const double InstantMs = 1;
    /// <summary>Le temps de disparition du style Fondu, avant le changement de taille.</summary>
    public const double FadeOutMs = 90;
    public const double FadeInMs = 260;

    public static CapsuleMotion Open(string style) => style switch
    {
        // Elle rebondit vers l'intérieur : un dépassement serait coupé par les bords de la fenêtre.
        Bounce => Together(620, new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 3 }, 300),
        Smooth => Together(420, new QuinticEase { EasingMode = EasingMode.EaseOut }, 420),
        // Le panneau prend d'abord sa largeur, puis descend depuis le haut de l'écran.
        Slide => new CapsuleMotion(
            new MotionStep(150, 0, new CubicEase { EasingMode = EasingMode.EaseOut }),
            new MotionStep(380, 100, new QuinticEase { EasingMode = EasingMode.EaseOut }),
            new MotionStep(380, 0, null)),
        Fade => Instant(),
        _ => Together(380, new SpringEase(), 380),
    };

    public static CapsuleMotion Close(string style) => style switch
    {
        Bounce => Together(320, new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.3 }, 320),
        Smooth => Together(340, new QuinticEase { EasingMode = EasingMode.EaseInOut }, 340),
        // L'inverse de l'ouverture : il remonte, puis reprend sa petite largeur.
        Slide => new CapsuleMotion(
            new MotionStep(140, 220, new CubicEase { EasingMode = EasingMode.EaseIn }),
            new MotionStep(280, 0, new CubicEase { EasingMode = EasingMode.EaseIn }),
            new MotionStep(320, 0, null)),
        Fade => Instant(),
        _ => Together(260, new CubicEase { EasingMode = EasingMode.EaseInOut }, 260),
    };

    private static CapsuleMotion Together(double durationMs, IEasingFunction ease, double radiusMs) =>
        new(new MotionStep(durationMs, 0, ease), new MotionStep(durationMs, 0, ease), new MotionStep(radiusMs, 0, null));

    private static CapsuleMotion Instant() => new(
        new MotionStep(InstantMs, FadeOutMs, null),
        new MotionStep(InstantMs, FadeOutMs, null),
        new MotionStep(InstantMs, FadeOutMs, null),
        FadesWindow: true);
}
