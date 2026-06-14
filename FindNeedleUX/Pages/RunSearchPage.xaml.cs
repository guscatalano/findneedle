using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindNeedleUX.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;
public sealed partial class RunSearchPage : Page
{
    private readonly SearchOrchestrator _orchestrator = new();
    public RunSearchPage()
    {
        this.InitializeComponent();
    }

    private void SetControlsTo(bool enable)
    {
        busybar.ShowPaused = enable;
        busybar2.ShowPaused = enable;
        this.IsEnabled = enable;
        CancelButton.IsEnabled = !enable;
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

        try
        {
            // The orchestrator owns the streaming run/grace/cancel flow; the page just supplies
            // the two UI side effects (open viewer, show status). Both callbacks run on the UI
            // thread because RunAsync resumes on the captured WinUI context.
            await _orchestrator.RunAsync(
                _shallowSearch,
                onOpenViewer: MainWindowActions.NavigateToNativeResultsPage,
                onStatus: text => summary.Text = text);
        }
        finally
        {
            SetControlsTo(true);
        }
    }

    private bool _shallowSearch;

    private void ShallowSearch_Click(object sender, RoutedEventArgs e) => _shallowSearch = true;
    private void NormalSearch_Click(object sender, RoutedEventArgs e) => _shallowSearch = false;
    private void DeepSearch_Click(object sender, RoutedEventArgs e) => _shallowSearch = false;

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _orchestrator.Cancel();
        summary.Text = "Cancelling...";
    }
}
