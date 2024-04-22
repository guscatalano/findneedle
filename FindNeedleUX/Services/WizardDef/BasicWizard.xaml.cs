using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Services.WizardDef;
/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public partial class BasicWizard : Window
{
    public BasicWizard()
    {
        this.InitializeComponent();
    }

    public Frame GetFrame()
    {
        return wizframe;
    }
}
