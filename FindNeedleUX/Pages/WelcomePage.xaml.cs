using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;
public sealed partial class WelcomePage : Page
{
    public WelcomePage()
    {
        this.InitializeComponent();
    }

    private void OpenLogFileButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        (WindowUtil.GetMainWindow() as MainWindow)?.QuickFileOpen();
    }

    private void OpenFolderButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        (WindowUtil.GetMainWindow() as MainWindow)?.QuickFolderOpen();
    }

    private void OpenLogWithRulesButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        (WindowUtil.GetMainWindow() as MainWindow)?.NavigateToQuickLogWithRules();
    }
}
