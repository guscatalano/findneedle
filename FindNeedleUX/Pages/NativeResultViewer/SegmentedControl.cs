using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.UI;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// A compact single-choice segmented control for toolbars: a label segment on a subtle fill, then
/// one segment per option; the selected one is accent-tinted. 32px tall, 1px stroke, 4px corners —
/// the same silhouette as the neighbouring toolbar buttons, so "Filters: Left | Top | Hide" reads as
/// one first-level control instead of a menu the user has to open to discover.
/// <para>
/// Built in code (no XAML) so it stays a drop-in: set <see cref="Label"/> and <see cref="Items"/>
/// (comma-separated) from XAML, listen to <see cref="SelectionChanged"/> for USER clicks only;
/// setting <see cref="SelectedIndex"/> from code reflects state without raising the event.
/// </para>
/// </summary>
public sealed partial class SegmentedControl : UserControl
{
    /// <summary>Corner radius of the pill. The outer segments (label on the left, last option on the
    /// right) carry it too — a Border does not round its children, so without that a selected segment's
    /// fill squares off the ends and the control stops reading as one control.</summary>
    private const double SegmentRadius = 4;

    private readonly Border _root;
    private readonly StackPanel _strip;
    private readonly TextBlock _labelText;
    private readonly Border _labelSegment;
    private readonly Border _badge;
    private readonly TextBlock _badgeText;
    private readonly List<Button> _segments = new();
    private readonly List<Border> _dividers = new();
    private string _label = "";
    private string _items = "";
    private string _toolTips = "";
    private int _selectedIndex = -1;
    private int _badgeCount;

    /// <summary>Raised when the USER picks a segment (not when <see cref="SelectedIndex"/> is set from code).</summary>
    public event EventHandler<int> SelectionChanged;

    public SegmentedControl()
    {
        _labelText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        _badgeText = new TextBlock { FontSize = 10, Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center };
        _badge = new Border
        {
            CornerRadius = new CornerRadius(8), Padding = new Thickness(5, 0, 5, 0), MinWidth = 16,
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Child = _badgeText,
        };
        var labelInner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        labelInner.Children.Add(_labelText);
        labelInner.Children.Add(_badge);
        _labelSegment = new Border
        {
            Padding = new Thickness(9, 0, 9, 0),
            BorderThickness = new Thickness(0, 0, 1, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
            // The label is the LEFT end of the pill, so it carries the left corners. Without this the
            // square-cornered fills inside painted over the rounded outline and the control read as a
            // row of square buttons rather than one rounded segmented control.
            CornerRadius = new CornerRadius(SegmentRadius, 0, 0, SegmentRadius),
            Child = labelInner,
        };
        _strip = new StackPanel { Orientation = Orientation.Horizontal };
        _strip.Children.Add(_labelSegment);
        _root = new Border
        {
            Height = 32,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(SegmentRadius),
            Child = _strip,
        };
        Content = _root;
        MinHeight = 0;
        Padding = new Thickness(0);
        ActualThemeChanged += (_, _) => ApplyVisuals();
        Loaded += (_, _) => ApplyVisuals();
    }

    /// <summary>The label segment's text (e.g. "Filters").</summary>
    public string Label
    {
        get => _label;
        set { _label = value ?? ""; _labelText.Text = _label; }
    }

    /// <summary>Comma-separated option captions, in order (e.g. "Left,Top,Hide").</summary>
    public string Items
    {
        get => _items;
        set { _items = value ?? ""; Rebuild(); }
    }

    /// <summary>Optional comma-separated tooltips, one per option (same order as <see cref="Items"/>).</summary>
    public string ItemToolTips
    {
        get => _toolTips;
        set { _toolTips = value ?? ""; ApplyToolTips(); }
    }

    /// <summary>Selected option (−1 = none). Setting it repaints without raising <see cref="SelectionChanged"/>.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set { _selectedIndex = value; ApplyVisuals(); }
    }

    /// <summary>A small accent count badge inside the label segment (0 hides it) — e.g. active filters.</summary>
    public int BadgeCount
    {
        get => _badgeCount;
        set
        {
            _badgeCount = Math.Max(0, value);
            _badgeText.Text = _badgeCount.ToString();
            _badge.Visibility = _badgeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>The option captions as parsed from <see cref="Items"/>.</summary>
    public IReadOnlyList<string> Options => _items.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

    private void Rebuild()
    {
        foreach (var b in _segments) _strip.Children.Remove(b);
        foreach (var d in _dividers) _strip.Children.Remove(d);
        _segments.Clear();
        _dividers.Clear();

        var options = Options;
        for (int i = 0; i < options.Count; i++)
        {
            if (i > 0)
            {
                var divider = new Border { Width = 1, VerticalAlignment = VerticalAlignment.Stretch };
                _dividers.Add(divider);
                _strip.Children.Add(divider);
            }
            int index = i;
            var btn = new Button
            {
                Content = options[i],
                Tag = index,
                MinWidth = 0,
                MinHeight = 0,
                Height = 30,
                Padding = new Thickness(9, 0, 9, 0),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(btn, $"{_label}: {options[i]}");
            btn.Click += (_, _) =>
            {
                if (_selectedIndex == index) return;
                SelectedIndex = index;
                SelectionChanged?.Invoke(this, index);
            };
            _segments.Add(btn);
            _strip.Children.Add(btn);
        }
        // The last segment is the RIGHT end of the pill: round its outer corners so a selected fill
        // follows the outline instead of squaring it off.
        if (_segments.Count > 0)
            _segments[^1].CornerRadius = new CornerRadius(0, SegmentRadius, SegmentRadius, 0);
        ApplyToolTips();
        ApplyVisuals();
    }

    private void ApplyToolTips()
    {
        var tips = _toolTips.Split(',', StringSplitOptions.None);
        for (int i = 0; i < _segments.Count; i++)
        {
            var tip = i < tips.Length ? tips[i].Trim() : "";
            ToolTipService.SetToolTip(_segments[i], string.IsNullOrEmpty(tip) ? null : tip);
        }
    }

    private static Brush Res(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b ? b : new SolidColorBrush(Colors.Transparent);

    private static Color AccentColor() =>
        Application.Current.Resources.TryGetValue("SystemAccentColor", out var v) && v is Color c ? c : Colors.DodgerBlue;

    private void ApplyVisuals()
    {
        var stroke = Res("CardStrokeColorDefaultBrush");
        _root.BorderBrush = stroke;
        _root.Background = Res("CardBackgroundFillColorDefaultBrush");
        _labelSegment.BorderBrush = stroke;
        _labelSegment.Background = Res("SubtleFillColorSecondaryBrush");
        _labelText.Foreground = Res("TextFillColorSecondaryBrush");
        _badge.Background = new SolidColorBrush(AccentColor());
        foreach (var d in _dividers) d.Background = stroke;

        var accentTint = new SolidColorBrush(AccentColor()) { Opacity = 0.16 };
        var accentText = Res("AccentTextFillColorPrimaryBrush");
        var normalText = Res("TextFillColorPrimaryBrush");
        for (int i = 0; i < _segments.Count; i++)
        {
            bool selected = i == _selectedIndex;
            _segments[i].Background = selected ? accentTint : new SolidColorBrush(Colors.Transparent);
            _segments[i].Foreground = selected ? accentText : normalText;
        }
    }
}
