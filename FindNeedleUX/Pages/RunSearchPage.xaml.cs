using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;
public sealed partial class RunSearchPage : Page
{
    private CancellationTokenSource _cts;
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
        _cts = new CancellationTokenSource();
        MiddleLayerService.GetProgressEventSink().RegisterForNumericProgress(GetNumberProgress);
        MiddleLayerService.GetProgressEventSink().RegisterForTextProgress(GetTextProgress);
        try
        {
            var r = await Task.Run(() => MiddleLayerService.RunSearch(_shallowSearch, _cts.Token), _cts.Token);
            summary.Text = r;
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            summary.Text = "Search cancelled.";
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
        _cts?.Cancel();
        summary.Text = "Cancelling...";
    }
}
