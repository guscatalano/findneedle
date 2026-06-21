using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FindNeedleUX.Pages;

/// <summary>A minimal wrapping panel (WinUI has no built-in WrapPanel): lays children left-to-right and
/// wraps to the next row when the next child would exceed the available width. Used for the welcome
/// page's Quick Actions so they overflow onto new rows instead of being clipped/scrolled.</summary>
public sealed partial class SimpleWrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = 12;
    public double VerticalSpacing { get; set; } = 12;

    protected override Size MeasureOverride(Size availableSize)
    {
        double max = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        double x = 0, y = 0, lineHeight = 0, widest = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > max)   // wrap
            {
                widest = Math.Max(widest, x - HorizontalSpacing);
                x = 0; y += lineHeight + VerticalSpacing; lineHeight = 0;
            }
            x += s.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, s.Height);
        }
        widest = Math.Max(widest, x - HorizontalSpacing);
        return new Size(double.IsInfinity(max) ? Math.Max(0, widest) : availableSize.Width, y + lineHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (var child in Children)
        {
            var s = child.DesiredSize;
            if (x > 0 && x + s.Width > finalSize.Width)
            {
                x = 0; y += lineHeight + VerticalSpacing; lineHeight = 0;
            }
            child.Arrange(new Rect(x, y, s.Width, s.Height));
            x += s.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, s.Height);
        }
        return finalSize;
    }
}
