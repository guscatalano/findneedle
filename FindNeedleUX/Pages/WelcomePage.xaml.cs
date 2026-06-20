using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using FindNeedleUX.Services;

namespace FindNeedleUX.Pages;
public sealed partial class WelcomePage : Page
{
    public WelcomePage()
    {
        this.InitializeComponent();
        LoadHeroAnimation();
    }

    /// <summary>Show the app's themed loader as an animated hero. Uses the loader theme chosen in
    /// Settings; falls back to "robot" if the user's pick is a non-animated mode (Spinner/Bar).</summary>
    private void LoadHeroAnimation()
    {
        try
        {
            var theme = ResultsViewerSettings.LoadingAnimation;
            if (!RobotLoader.IsAnimated(theme)) theme = "robot";
            // Frame index 1 = the "scan"-style second frame for most themes.
            HeroGif.Source = new BitmapImage(new Uri(RobotLoader.Uri(1, theme, wide: false)));
        }
        catch { /* hero art is decorative — never block the page */ }
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
