using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DayNew.Core;

/// <summary>
/// 统一动效常量与轻量化动画辅助（遵循 nord-frost-ui：无回弹、时长不超过 225ms）。
/// </summary>
public static class Motion
{
    public static readonly TimeSpan Xs = TimeSpan.FromMilliseconds(90);
    public static readonly TimeSpan S = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan M = TimeSpan.FromMilliseconds(225);

    public static readonly CubicEase EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly CubicEase EaseIn = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
    public static readonly CubicEase EaseInOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze) freezable.Freeze();
        return freezable;
    }

    public static void AnimateOpacity(UIElement element, double to, TimeSpan duration,
        IEasingFunction? easing = null, Action? onCompleted = null)
    {
        var anim = new DoubleAnimation(to, duration)
        {
            EasingFunction = easing ?? EaseOut,
            FillBehavior = FillBehavior.HoldEnd
        };

        if (onCompleted is not null)
        {
            anim.Completed += (_, _) => onCompleted();
        }

        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    public static (ScaleTransform Scale, TranslateTransform Translate) EnsureTransformGroup(UIElement element,
        bool centerOrigin = true)
    {
        if (centerOrigin) element.RenderTransformOrigin = new Point(0.5, 0.5);

        if (element.RenderTransform is TransformGroup group &&
            !group.IsFrozen &&
            group.Children.Count >= 2 &&
            group.Children[0] is ScaleTransform existingScale && !existingScale.IsFrozen &&
            group.Children[1] is TranslateTransform existingTranslate && !existingTranslate.IsFrozen)
        {
            return (existingScale, existingTranslate);
        }

        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var newGroup = new TransformGroup();
        newGroup.Children.Add(scale);
        newGroup.Children.Add(translate);
        element.RenderTransform = newGroup;
        return (scale, translate);
    }

    public static ScaleTransform EnsureScale(UIElement element)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        if (element.RenderTransform is ScaleTransform existing && !existing.IsFrozen) return existing;
        if (element.RenderTransform is TransformGroup group &&
            !group.IsFrozen &&
            group.Children.FirstOrDefault() is ScaleTransform grouped &&
            !grouped.IsFrozen)
        {
            return grouped;
        }

        var scale = new ScaleTransform(1, 1);
        element.RenderTransform = scale;
        return scale;
    }

    public static void AnimateScale(UIElement element, double to, TimeSpan duration, IEasingFunction? easing = null)
    {
        var scale = EnsureScale(element);
        var curve = easing ?? EaseOut;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(to, duration) { EasingFunction = curve });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(to, duration) { EasingFunction = curve });
    }

    public static void AnimateOffsetY(TranslateTransform transform, double to, TimeSpan duration,
        IEasingFunction? easing = null)
    {
        transform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(to, duration) { EasingFunction = easing ?? EaseOut });
    }

    public static DropShadowEffect CreateFloatingShadow(double blur = 14, double depth = 4, double opacity = 0.22)
    {
        return Freeze(new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = blur,
            ShadowDepth = depth,
            Direction = 270,
            Opacity = opacity
        });
    }
}
