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

    private void SetControlsTo(bool enable)
    {
        busybar.ShowPaused = enable;
        busybar2.ShowPaused = enable;
        this.IsEnabled = enable;
        if (!enable)
        {
            MainWindowActions.DisableNavBar();
        } else {
            MainWindowActions.EnableNavBar();
        }
    }

    private void GetNumberProgress(int count)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            busybar2.Value = count;
        });
    }

    private void GetTextProgress(string text)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            progresstext.Text = text;
        });
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        SetControlsTo(false);
        MiddleLayerService.GetProgressEventSink().RegisterForNumericProgress(GetNumberProgress);
        MiddleLayerService.GetProgressEventSink().RegisterForTextProgress(GetTextProgress);
        var r = await Task.Run(() => MiddleLayerService.RunSearch());
        summary.Text = r;
        SetControlsTo(true);

    }

    private async void Button2_Click(object sender, RoutedEventArgs e)
    {
        SetControlsTo(false);
        MiddleLayerService.GetProgressEventSink().RegisterForNumericProgress(GetNumberProgress);
        MiddleLayerService.GetProgressEventSink().RegisterForTextProgress(GetTextProgress);
        var r = await Task.Run(() => MiddleLayerService.RunSearch(true));
        summary.Text = r;
        SetControlsTo(true);
    }
}
