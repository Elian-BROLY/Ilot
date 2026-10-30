using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ilot.Controls;

/// <summary>Texte qui défile en aller-retour s'il déborde. N'anime rien tant qu'il est invisible.</summary>
internal sealed class MarqueeText : Canvas
{
    private const double ScrollSpeed = 30; // DIP par seconde
    private static readonly TimeSpan EdgePause = TimeSpan.FromSeconds(1.5);

    private readonly TextBlock _text = new() { TextWrapping = TextWrapping.NoWrap };
    private readonly TranslateTransform _offset = new();

    public MarqueeText()
    {
        ClipToBounds = true;
        _text.RenderTransform = _offset;
        Children.Add(_text);
        SizeChanged += (_, _) => UpdateAnimation();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    public string Text
    {
        get => _text.Text;
        set
        {
            if (_text.Text != value)
            {
                _text.Text = value;
                InvalidateMeasure();
                UpdateAnimation();
            }
        }
    }

    public double TextFontSize { set => _text.FontSize = value; }

    public FontWeight TextFontWeight { set => _text.FontWeight = value; }

    public Brush TextForeground { set => _text.Foreground = value; }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        return new Size(0, _text.DesiredSize.Height);
    }

    private void UpdateAnimation()
    {
        _offset.BeginAnimation(TranslateTransform.XProperty, null);
        _offset.X = 0;
        if (!IsVisible || ActualWidth <= 0)
        {
            return;
        }

        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var overflow = _text.DesiredSize.Width - ActualWidth;
        if (overflow <= 0)
        {
            return;
        }

        var travel = TimeSpan.FromSeconds(overflow / ScrollSpeed);
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        var time = EdgePause;
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(time)));
        time += travel;
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(time)));
        time += EdgePause;
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-overflow, KeyTime.FromTimeSpan(time)));
        time += travel;
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(time)));
        Timeline.SetDesiredFrameRate(animation, 30);
        _offset.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
