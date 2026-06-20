using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using FindNeedleUX.Services;

namespace FindNeedleUX.Pages;
public sealed partial class WelcomePage : Page
{
    private bool _editMode;

    public WelcomePage()
    {
        this.InitializeComponent();
        LoadHeroAnimation();
        Loaded += (_, _) => RenderQuickActions();
    }

    /// <summary>Show the app's themed loader as an animated hero. Uses the loader theme chosen in
    /// Settings; falls back to "robot" if the user's pick is a non-animated mode (Spinner/Bar).</summary>
    private void LoadHeroAnimation()
    {
        try
        {
            var theme = ResultsViewerSettings.LoadingAnimation;
            if (!RobotLoader.IsAnimated(theme)) theme = "robot";
            HeroGif.Source = new BitmapImage(new Uri(RobotLoader.Uri(1, theme, wide: false)));
        }
        catch { /* hero art is decorative — never block the page */ }
    }

    private MainWindow Main => WindowUtil.GetMainWindow() as MainWindow;

    private void EditQuickActions_Click(object sender, RoutedEventArgs e)
    {
        _editMode = !_editMode;
        EditQuickActionsLabel.Text = _editMode ? "Done" : "Edit";
        EditQuickActionsIcon.Symbol = _editMode ? Symbol.Accept : Symbol.Edit;
        RenderQuickActions();
    }

    private void RenderQuickActions()
    {
        if (QuickActionsHost == null) return;
        try
        {
            QuickActionsHost.Children.Clear();

            var ids = QuickActionCatalog.GetSelectedIds();
            for (int i = 0; i < ids.Count; i++)
            {
                var action = QuickActionCatalog.Find(ids[i]);
                if (action == null) continue;
                QuickActionsHost.Children.Add(_editMode
                    ? BuildEditTile(action, i, ids.Count)
                    : BuildActionTile(action));
            }

            if (_editMode) QuickActionsHost.Children.Add(BuildAddTile(ids));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RenderQuickActions failed: {ex}");
        }
    }

    /// <summary>Normal tile: click runs the action.</summary>
    private Button BuildActionTile(QuickAction action)
    {
        var content = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = action.Emoji, FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = action.Label, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });

        var btn = new Button
        {
            Content = content,
            Height = 88,
            MinWidth = 150,
            Background = new SolidColorBrush(AccentTint(0.10)),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, action.Label);
        btn.Click += (_, _) => Main?.RunQuickAction(action.Id);
        return btn;
    }

    /// <summary>Edit tile: label plus reorder (← →) and remove (×) controls.</summary>
    private FrameworkElement BuildEditTile(QuickAction action, int index, int count)
    {
        var stack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = action.Emoji, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = action.Label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        controls.Children.Add(MiniButton(Symbol.Back, "Move left",  () => Move(action.Id, -1), enabled: index > 0));
        controls.Children.Add(MiniButton(Symbol.Cancel, "Remove",     () => Remove(action.Id)));
        controls.Children.Add(MiniButton(Symbol.Forward, "Move right", () => Move(action.Id, +1), enabled: index < count - 1));
        stack.Children.Add(controls);

        return new Border
        {
            Height = 88,
            MinWidth = 150,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(AccentTint(0.4)),
            Background = new SolidColorBrush(AccentTint(0.06)),
            Padding = new Thickness(8),
            Child = stack,
        };
    }

    /// <summary>The "+ Add" tile in edit mode: a flyout of catalog actions not already shown.</summary>
    private FrameworkElement BuildAddTile(List<string> selected)
    {
        var content = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = "+", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = "Add", HorizontalAlignment = HorizontalAlignment.Center });

        var btn = new Button { Content = content, Height = 88, MinWidth = 90 };

        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
        var available = QuickActionCatalog.Available();
        if (available.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = "All actions added", IsEnabled = false });
        else
            foreach (var a in available)
            {
                var item = new MenuFlyoutItem { Text = $"{a.Emoji}  {a.Label}" };
                item.Click += (_, _) => Add(a.Id);
                menu.Items.Add(item);
            }
        btn.Flyout = menu;
        return btn;
    }

    private static Button MiniButton(Symbol symbol, string tip, Action onClick, bool enabled = true)
    {
        var b = new Button
        {
            Content = new SymbolIcon { Symbol = symbol, RenderTransform = new ScaleTransform { ScaleX = 0.65, ScaleY = 0.65 }, RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5) },
            Padding = new Thickness(4),
            MinWidth = 0, MinHeight = 0,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            IsEnabled = enabled,
        };
        ToolTipService.SetToolTip(b, tip);
        b.Click += (_, _) => onClick();
        return b;
    }

    private static Color AccentTint(double opacity)
    {
        Color a;
        try { a = new global::Windows.UI.ViewManagement.UISettings().GetColorValue(global::Windows.UI.ViewManagement.UIColorType.Accent); }
        catch { a = Color.FromArgb(255, 0, 120, 215); } // Windows default accent
        return Color.FromArgb((byte)(opacity * 255), a.R, a.G, a.B);
    }

    // --- mutations (persist via the catalog, then re-render) ---
    private void Move(string id, int delta) { QuickActionCatalog.Move(id, delta); RenderQuickActions(); }
    private void Remove(string id) { QuickActionCatalog.Remove(id); RenderQuickActions(); }
    private void Add(string id) { QuickActionCatalog.Add(id); RenderQuickActions(); }
}
