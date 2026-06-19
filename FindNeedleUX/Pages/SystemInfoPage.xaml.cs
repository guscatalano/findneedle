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

    private static FrameworkElement Row(SystemInfoMiddleware.HealthItem item)
    {
        var grid = new Grid { Padding = new Thickness(8, 6, 8, 6), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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

        return grid;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Render();
}
