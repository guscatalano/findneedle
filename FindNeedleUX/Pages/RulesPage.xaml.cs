using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FindNeedleUX.Pages;

/// <summary>
/// One "Rules" hub: a tabbed shell that hosts the existing rule pages in a content Frame, so all rule
/// configuration lives behind one hub (Workspace ▸ Rule files / Auto rules / Field extraction open its tabs).
/// Each tab just navigates the inner Frame to the corresponding existing page (their logic/stores are
/// unchanged). Navigation parameter is a tab tag (e.g. "fields") so deep-links open the right tab.
/// </summary>
public sealed partial class RulesPage : Page
{
    // Default tab is "Rule files". A nav param (from a specific menu item) still overrides this.
    // (Runtime "which rules did the last run apply" status lives in the viewer's Sources dialog, not here.)
    private string _initialTag = "files";

    /// <summary>The page currently hosted in the hub's frame — the shell breadcrumb names THAT page
    /// ("Workspace ▸ Auto rules"), not the hub.</summary>
    public System.Type ActivePageType => RulesContent?.Content?.GetType();

    /// <summary>Raised after the hub switches tabs, so the shell can refresh its breadcrumb.</summary>
    public static event System.Action ActiveTabChanged;

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
            case "autoadd": RulesContent.Navigate(typeof(AutoAddRulesPage)); break;
            case "fields":  RulesContent.Navigate(typeof(ReformatRulesPage)); break;
            default:        RulesContent.Navigate(typeof(SearchRulesPage)); break; // "files"
        }
        try { ActiveTabChanged?.Invoke(); } catch { /* breadcrumb is cosmetic */ }
    }
}
