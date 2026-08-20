using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Size = System.Windows.Size;

namespace DynamicIsland.Windows.Infrastructure;

/// <summary>
/// A one-line title that stays still when it fits and continuously loops leftward only when it overflows.
/// The loop uses a second copy inside a clipped viewport, making the wrap visually seamless.
/// </summary>
public sealed class MarqueeText : Decorator
{
    private const double LoopGap = 14d;
    private const double ScrollPixelsPerSecond = 32d;

    private readonly TextBlock _text;
    private readonly TextBlock _duplicate;
    private readonly Grid _viewport;
    private readonly Canvas _track;
    private readonly TranslateTransform _offset = new();
    private bool _refreshQueued;
    private bool _animating;
    private bool _shouldScroll;
    private double _availableWidth;
    private double _naturalWidth;
    private string _animationKey = string.Empty;

    public MarqueeText()
    {
        ClipToBounds = true;
        _text = CreateTextBlock();
        _duplicate = CreateTextBlock();
        _duplicate.Visibility = Visibility.Collapsed;

        _track = new Canvas
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
            RenderTransform = _offset,
        };
        _track.Children.Add(_text);
        _track.Children.Add(_duplicate);

        _viewport = new Grid
        {
            ClipToBounds = true,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
        };
        _viewport.Children.Add(_track);
        Child = _viewport;
    }

    private static TextBlock CreateTextBlock() => new()
    {
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
    };

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new PropertyMetadata(string.Empty, (d, e) =>
        {
            var m = (MarqueeText)d;
            var value = (string?)e.NewValue ?? string.Empty;
            m._text.Text = value;
            m._duplicate.Text = value;
            m.RequestLayoutRefresh();
        }));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(
        nameof(Active), typeof(bool), typeof(MarqueeText),
        new PropertyMetadata(true, (d, _) => ((MarqueeText)d).RequestLayoutRefresh()));
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    public static readonly DependencyProperty TextForegroundProperty = DependencyProperty.Register(
        nameof(TextForeground), typeof(Brush), typeof(MarqueeText),
        new PropertyMetadata(Brushes.White, (d, e) =>
        {
            var m = (MarqueeText)d;
            m._text.Foreground = (Brush)e.NewValue;
            m._duplicate.Foreground = (Brush)e.NewValue;
        }));
    public Brush TextForeground { get => (Brush)GetValue(TextForegroundProperty); set => SetValue(TextForegroundProperty, value); }

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(MarqueeText),
        new PropertyMetadata(13d, (d, e) =>
        {
            var m = (MarqueeText)d;
            m._text.FontSize = (double)e.NewValue;
            m._duplicate.FontSize = (double)e.NewValue;
            m.RequestLayoutRefresh();
        }));
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(MarqueeText),
        new PropertyMetadata(System.Windows.SystemFonts.MessageFontFamily, (d, e) =>
        {
            var m = (MarqueeText)d;
            m._text.FontFamily = (FontFamily)e.NewValue;
            m._duplicate.FontFamily = (FontFamily)e.NewValue;
            m.RequestLayoutRefresh();
        }));
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public static readonly DependencyProperty FontWeightProperty = DependencyProperty.Register(
        nameof(FontWeight), typeof(FontWeight), typeof(MarqueeText),
        new PropertyMetadata(FontWeights.Normal, (d, e) =>
        {
            var m = (MarqueeText)d;
            m._text.FontWeight = (FontWeight)e.NewValue;
            m._duplicate.FontWeight = (FontWeight)e.NewValue;
            m.RequestLayoutRefresh();
        }));
    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        _text.Measure(new Size(double.PositiveInfinity, constraint.Height));
        _naturalWidth = _text.DesiredSize.Width;
        var width = double.IsInfinity(constraint.Width) ? _naturalWidth : Math.Min(_naturalWidth, constraint.Width);
        return new Size(width, _text.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        _availableWidth = Math.Max(0d, arrangeSize.Width);
        _shouldScroll = Active && _availableWidth > 0d && _naturalWidth > _availableWidth + 2d;
        _text.TextTrimming = _shouldScroll ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        _duplicate.TextTrimming = TextTrimming.None;

        if (_shouldScroll)
        {
            _text.Width = _naturalWidth;
            _duplicate.Width = _naturalWidth;
            _duplicate.Visibility = Visibility.Visible;
            Canvas.SetLeft(_text, 0d);
            Canvas.SetLeft(_duplicate, _naturalWidth + LoopGap);
            _track.Width = _naturalWidth * 2d + LoopGap;
        }
        else
        {
            _text.Width = _availableWidth;
            _duplicate.Visibility = Visibility.Collapsed;
            Canvas.SetLeft(_text, 0d);
            _track.Width = _availableWidth;
        }

        _track.Height = arrangeSize.Height;
        _viewport.Arrange(new Rect(0, 0, arrangeSize.Width, arrangeSize.Height));
        QueueRefresh();
        return arrangeSize;
    }

    private void RequestLayoutRefresh()
    {
        InvalidateMeasure();
        InvalidateArrange();
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_refreshQueued || Dispatcher.HasShutdownStarted) return;
        _refreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _refreshQueued = false;
            RefreshAnimation();
        }));
    }

    private void RefreshAnimation()
    {
        var key = $"{Text}|{Active}|{_availableWidth:F1}|{_naturalWidth:F1}";
        if (!_shouldScroll)
        {
            StopAnimation();
            _duplicate.Visibility = Visibility.Collapsed;
            return;
        }

        if (_animating && string.Equals(key, _animationKey, StringComparison.Ordinal)) return;

        StopAnimation();
        _duplicate.Visibility = Visibility.Visible;
        var distance = _naturalWidth + LoopGap;
        var duration = TimeSpan.FromSeconds(Math.Max(1.2d, distance / ScrollPixelsPerSecond));
        var pause = TimeSpan.FromSeconds(0.9d);
        var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(pause)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(pause + duration)));
        _offset.BeginAnimation(TranslateTransform.XProperty, animation);
        _animationKey = key;
        _animating = true;
    }

    private void StopAnimation()
    {
        if (!_animating && Math.Abs(_offset.X) < 0.01d) return;
        _offset.BeginAnimation(TranslateTransform.XProperty, null);
        _offset.X = 0d;
        _animating = false;
        _animationKey = string.Empty;
    }
}
