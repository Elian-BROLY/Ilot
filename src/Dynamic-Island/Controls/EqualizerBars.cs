using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DynamicIsland.Controls;

/// <summary>Égaliseur décoratif en mode compact. Ne tourne que lorsqu'il est visible.</summary>
internal sealed class EqualizerBars : StackPanel
{
    private const int FramesPerSecond = 15; // La fenêtre étant rendue en logiciel, chaque image a un coût.

    // Hauteurs relatives de chaque barre : des cycles de longueurs différentes évitent un motif répétitif.
    private static readonly double[][] Patterns =
    [
        [0.35, 0.9, 0.5, 1.0, 0.4],
        [0.8, 0.4, 1.0, 0.55],
        [0.5, 1.0, 0.35, 0.75, 0.9, 0.45],
        [0.9, 0.5, 0.7, 0.35],
    ];

    private readonly List<(ScaleTransform Scale, DoubleAnimationUsingKeyFrames Animation)> _bars = [];

    public EqualizerBars()
    {
        Orientation = Orientation.Horizontal;
        Height = 12;
        foreach (var pattern in Patterns)
        {
            var scale = new ScaleTransform(1, pattern[0]);
            Children.Add(new Rectangle
            {
                Width = 2.5,
                Height = 12,
                RadiusX = 1.25,
                RadiusY = 1.25,
                Margin = new Thickness(0.75, 0, 0.75, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
                Fill = (Brush)Application.Current.FindResource("AccentBrush"),
            });
            _bars.Add((scale, CreateAnimation(pattern)));
        }
        IsVisibleChanged += (_, _) => UpdateAnimations();
    }

    private static DoubleAnimationUsingKeyFrames CreateAnimation(double[] pattern)
    {
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var step = TimeSpan.FromMilliseconds(260);
        var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
        for (var i = 0; i <= pattern.Length; i++)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(pattern[i % pattern.Length], KeyTime.FromTimeSpan(step * i), ease));
        }
        Timeline.SetDesiredFrameRate(animation, FramesPerSecond);
        animation.Freeze();
        return animation;
    }

    private void UpdateAnimations()
    {
        foreach (var (scale, animation) in _bars)
        {
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, IsVisible ? animation : null);
        }
    }
}
