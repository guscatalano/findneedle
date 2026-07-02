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
        // Disable ONLY the input controls while running — never the whole Page. A disabled WinUI
        // ancestor disables every descendant regardless of its own IsEnabled, which previously made
        // the Cancel button (and any escape) unclickable for the entire run. (A StackPanel has no
        // IsEnabled, so disable its child controls directly.)
        foreach (var child in DepthInputs.Children)
            if (child is Control c) c.IsEnabled = enable;
        RunButton.IsEnabled = enable;
        CancelButton.IsEnabled = !enable;
        if (!enable)
        {
            MainWindowActions.DisableNavBar();
        } else {
            MainWindowActions.EnableNavBar();
        }
    }

    private void AddSource_Click(object sender, RoutedEventArgs e)
        => MainWindowActions.NavigateToSearchLocations();

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
        // Don't run over nothing — a fresh profile has no sources. Guide the user instead of
        // producing a confusing "No results".
        if ((MiddleLayerService.Locations?.Count ?? 0) == 0)
        {
            NoSourcesBar.IsOpen = true;
            return;
        }
        NoSourcesBar.IsOpen = false;
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
