using Microsoft.UI.Xaml.Controls;
using FindNeedleUX.Pages;

namespace FindNeedleUX.Pages;
public sealed partial class WelcomePage : Page
{
    public WelcomePage()
    {
        this.InitializeComponent();
    }

    private void NewSearchButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var rootFrame = Microsoft.UI.Xaml.Window.Current.Content as Frame;
        rootFrame?.Navigate(typeof(RunSearchPage));
    }

    private void RuleDSLConfigButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var rootFrame = Microsoft.UI.Xaml.Window.Current.Content as Frame;
        rootFrame?.Navigate(typeof(RuleDSLHomePage));
    }

    private void UMLDiagramButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var rootFrame = Microsoft.UI.Xaml.Window.Current.Content as Frame;
        rootFrame?.Navigate(typeof(DiagramToolsPage));
    }
}
