using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// Minimal horizontal wrap panel (the CommunityToolkit WrapPanel isn't in the referenced package).
/// Lays children left-to-right and wraps to the next line when the available width runs out, with
/// configurable spacing. Used as the level-chips ItemsPanel so the chips reflow in the narrow
/// left-docked filter pane instead of overflowing. With unbounded width (e.g. inside a horizontal
/// StackPanel for the top dock) it stays on one line, matching the prior behavior.
/// </summary>
public sealed partial class WrapPanelLite : Panel
{
    public double HorizontalSpacing { get; set; } = 6;
    public double VerticalSpacing { get; set; } = 6;

    protected override Size MeasureOverride(Size availableSize)
    {
        double avail = availableSize.Width;
        bool bounded = !double.IsInfinity(avail) && avail > 0;
        double x = 0, rowHeight = 0, widest = 0, totalHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = child.DesiredSize;
            if (bounded && x > 0 && x + d.Width > avail)
            {
                totalHeight += rowHeight + VerticalSpacing;
                widest = Math.Max(widest, x - HorizontalSpacing);
                x = 0; rowHeight = 0;
            }
            x += d.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, d.Height);
        }
        totalHeight += rowHeight;
        widest = Math.Max(widest, x - HorizontalSpacing);

        return new Size(bounded ? avail : Math.Max(0, widest), totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        foreach (var child in Children)
        {
            var d = child.DesiredSize;
            if (x > 0 && x + d.Width > finalSize.Width)
            {
                y += rowHeight + VerticalSpacing;
                x = 0; rowHeight = 0;
            }
            child.Arrange(new Rect(x, y, d.Width, d.Height));
            x += d.Width + HorizontalSpacing;
            rowHeight = Math.Max(rowHeight, d.Height);
        }
        return finalSize;
    }
}
