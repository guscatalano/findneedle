using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;

namespace FindNeedleUX.Pages;

/// <summary>
/// System Check — a health view of the environment and the external tools FindNeedle depends on
/// (tracefmt, WDK, PlantUML), shown as pass/fail rows. App version + links live on the About page.
/// </summary>
public sealed partial class SystemInfoPage : Page
{
    public SystemInfoPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => Render();
    }

    private void Render()
    {
        HealthList.Children.Clear();
        foreach (var item in SystemInfoMiddleware.GetHealthChecks())
            HealthList.Children.Add(Row(item));
    }

    private FrameworkElement Row(SystemInfoMiddleware.HealthItem item)
    {
        var grid = new Grid { Padding = new Thickness(8, 6, 8, 6), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // fix action

        // glyph: ✓ ok, ✗ missing, • informational
        var (glyph, brushKey) = item.Ok switch
        {
            true => ("✓", "SystemFillColorSuccessBrush"),
            false => ("✗", "SystemFillColorCriticalBrush"),
            null => ("•", "TextFillColorSecondaryBrush"),
        };
        var icon = new TextBlock
        {
            Text = glyph,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources[brushKey],
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var name = new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var detail = new TextBlock
        {
            Text = item.Detail,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        Grid.SetColumn(detail, 2);
        grid.Children.Add(detail);

        // Remediation: for a failing check with an in-app fix, offer a button that jumps to the page
        // that fixes it — so the user can act from where the problem is reported.
        var remedy = Remedy(item);
        if (remedy != null)
        {
            var fix = new Button
            {
                Content = remedy.Value.label,
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            };
            var page = remedy.Value.page;
            fix.Click += (_, _) => this.Frame?.Navigate(page);
            Grid.SetColumn(fix, 3);
            grid.Children.Add(fix);
        }

        return grid;
    }

    /// <summary>The in-app fix for a failing check, or null if there isn't one (e.g. cdb — the detail
    /// text explains the external install).</summary>
    private static (string label, System.Type page)? Remedy(SystemInfoMiddleware.HealthItem item)
    {
        if (item.Ok != false) return null;
        var n = item.Name.ToLowerInvariant();
        if (n.Contains("uml") || n.Contains("diagram"))
            return ("Install…", typeof(DiagramToolsPage));
        if (n.Contains("tracefmt") || n.Contains("wdk") || n.Contains("symbol"))
            return ("Decoding settings…", typeof(ResultsViewerSettingsPage));
        return null;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Render();
}
