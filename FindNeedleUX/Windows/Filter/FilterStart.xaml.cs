using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows.Filter;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class FilterStart : Page
{

    private string selectedItem = "";
    public FilterStart()
    {
        this.InitializeComponent();
        WizardSelectionService.GetCurrentWizard().RegisterCurrentPage(this);
    }

  

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        WizardSelectionService.GetCurrentWizard().NavigateNextOne(selectedItem);
    }

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        selectedItem = (sender as RadioButton).Name.ToString();
    }
}
