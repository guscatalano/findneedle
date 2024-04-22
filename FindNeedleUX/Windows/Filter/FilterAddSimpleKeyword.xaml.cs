using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows.Filter;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class FilterAddSimpleKeyword : Page
{
    public FilterAddSimpleKeyword()
    {
        this.InitializeComponent();
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {

        MiddleLayerService.AddKeywordFilter(keywordtxt.Text);
        WizardSelectionService.GetCurrentWizard().NavigateNextOne("Quit");
    }
}
