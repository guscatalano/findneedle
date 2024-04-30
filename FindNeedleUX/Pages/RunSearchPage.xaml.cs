using System.Threading.Tasks;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class RunSearchPage : Page
{
    public RunSearchPage()
    {
        this.InitializeComponent();
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        busybar.ShowPaused = false;
        string r = await Task.Run(() => MiddleLayerService.RunSearch());
        summary.Text = r;
        busybar.ShowPaused = true;
    }

    private async void Button2_Click(object sender, RoutedEventArgs e)
    {

        busybar.ShowPaused = false;
        string r = await Task.Run(() => MiddleLayerService.RunSearch(true));
        summary.Text = r;
        busybar.ShowPaused = true;
    }
}
