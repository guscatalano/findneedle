using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FindNeedleUX.Pages;

/// <summary>
/// One "Rules" hub: a tabbed shell that hosts the existing rule pages in a content Frame, so all rule
/// configuration lives behind a single Configure → Rules entry instead of five separate menu items.
/// Each tab just navigates the inner Frame to the corresponding existing page (their logic/stores are
/// unchanged). Navigation parameter is a tab tag (e.g. "fields") so deep-links open the right tab.
/// </summary>
public sealed partial class RulesPage : Page
{
    // Default to a config tab ("Rule files"), not the runtime "Active" status tab which is empty until a
    // search has run. A nav param (from a specific menu item) still overrides this.
    private string _initialTag = "files";

    public RulesPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string tag && !string.IsNullOrEmpty(tag))
            _initialTag = tag;
        SelectTab(_initialTag);
    }

    private void SelectTab(string tag)
    {
        var items = RulesNav.MenuItems.OfType<NavigationViewItem>().ToList();
        // Setting SelectedItem (from null) fires SelectionChanged, which navigates the Frame.
        RulesNav.SelectedItem =
            items.FirstOrDefault(i => (i.Tag as string) == tag) ?? items.FirstOrDefault();
    }

    private void RulesNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // Guard: SelectionChanged can fire during initial XAML parse before the Frame exists.
        if (RulesContent == null) return;
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        switch (tag)
        {
            case "files":   RulesContent.Navigate(typeof(SearchRulesPage)); break;
            case "autoadd": RulesContent.Navigate(typeof(AutoAddRulesPage)); break;
            case "fields":  RulesContent.Navigate(typeof(ReformatRulesPage)); break;
            case "uml":     RulesContent.Navigate(typeof(DiagramToolsPage)); break;
            default:        RulesContent.Navigate(typeof(SearchProcessorsPage)); break; // "active"
        }
    }
}
