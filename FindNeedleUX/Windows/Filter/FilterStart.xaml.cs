using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows.Filter;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class FilterStart : Page
{
    public FilterStart()
    {
        this.InitializeComponent();
        WizardSelectionService.GetCurrentWizard().RegisterCurrentPage(this);
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        WizardSelectionService.GetCurrentWizard().NavigateNextOne("Keyword");
    }

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        //Control1Output.Text = string.Format("You selected {0}", (sender as RadioButton).Content.ToString());
    }
}