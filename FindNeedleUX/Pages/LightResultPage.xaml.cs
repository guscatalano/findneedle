using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using FindNeedleUX.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LightResultPage : Page
{

    private Random random = new Random();
    private int MaxLength = 425;

   
    public MyItemsSource filteredRecipeData = new MyItemsSource(null);
    public List<LogLine> staticRecipeData;
    private bool IsSortDescending = false;

    private Button LastSelectedColorButton;
    private int PreviouslyFocusedAnimatedScrollRepeaterIndex = -1;
    public LightResultPage()
    {
        this.InitializeComponent();
        List<LogLine> LogLineList = MiddleLayerService.GetLogLines();
        filteredRecipeData.InitializeCollection(LogLineList);
        // Save a static copy to compare to while filtering
        staticRecipeData = LogLineList;
        VariedImageSizeRepeater.ItemsSource = filteredRecipeData;
    }

   

    // ==========================  Data source class ==========================
    /* To hold the recipe items, a data source class was created called MyItemsSource. The class
       inherits from IList and IKeyIndexMapping interfaces, basically creating a collection class
       that can easily filter and sort its items. Important methods are shown below, but full source
       code can be found in WinUI Gallery repo. See the linked ItemsRepeater guidance documentation as
       well for a full tutorial on how to implement this type of class. */
    // Custom data source class that assigns elements unique IDs, making filtering easier
    public class MyItemsSource : IList, Microsoft.UI.Xaml.Controls.IKeyIndexMapping, INotifyCollectionChanged
    {
        private List<Recipe> inner = new List<Recipe>();
        public List<LogLine> innerLines = new List<LogLine>();

        public MyItemsSource(IEnumerable<LogLine> collection)
        {
            InitializeCollection(collection);

        }

        public void InitializeCollection(IEnumerable<LogLine> collection)
        {
            if(collection == null)
            {
                return;
            }
            innerLines.Clear();
           

            innerLines.AddRange(collection);

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

     

        #region IReadOnlyList<T>
        public int Count => this.innerLines != null ? this.innerLines.Count : 0;

        public object this[int index]
        {
            get
            {
                return innerLines[index] as LogLine;
            }

            set
            {
                innerLines[index] = (LogLine)value;
            }
        }

        public IEnumerator<LogLine> GetEnumerator() => this.innerLines.GetEnumerator();

        #endregion

        #region INotifyCollectionChanged
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        #endregion

        #region IKeyIndexMapping
        public string KeyFromIndex(int index)
        {
            return innerLines[index].Index.ToString();
        }

        public int IndexFromKey(string key)
        {
            foreach (LogLine item in innerLines)
            {
                if (item.Index.ToString() == key)
                {
                    return innerLines.IndexOf(item);
                }
            }
            return -1;
        }

        #endregion

        #region Unused List methods
        IEnumerator IEnumerable.GetEnumerator()
        {
            throw new NotImplementedException();
        }

        public int Add(object value)
        {
            throw new NotImplementedException();
        }

        public void Clear()
        {
            throw new NotImplementedException();
        }

        public bool Contains(object value)
        {
            throw new NotImplementedException();
        }

        public int IndexOf(object value)
        {
            throw new NotImplementedException();
        }

        public void Insert(int index, object value)
        {
            throw new NotImplementedException();
        }

        public void Remove(object value)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }

        public bool IsFixedSize => throw new NotImplementedException();

        public bool IsReadOnly => throw new NotImplementedException();

        public bool IsSynchronized => throw new NotImplementedException();

        public object SyncRoot => throw new NotImplementedException();

        #endregion
    }


    public void FilterRecipes_FilterChanged(object sender, RoutedEventArgs e)
    {
        UpdateSortAndFilter();
    }

   
    private void UpdateSortAndFilter()
    {
        // Find all recipes that ingredients include what was typed into the filtering text box
        var filteredTypes = staticRecipeData.Where(i => i.Message.Contains(FilterRecipes.Text, StringComparison.InvariantCultureIgnoreCase));
        // Sort the recipes by whichever sorting mode was last selected (least to most ingredients by default)
       /* var sortedFilteredTypes = IsSortDescending ?
            filteredTypes.OrderByDescending(i => i.IngList.Count()) :
            filteredTypes.OrderBy(i => i.IngList.Count());*/
        // Re-initialize MyItemsSource object with this newly filtered data
        filteredRecipeData.InitializeCollection(filteredTypes);

        var peer = FrameworkElementAutomationPeer.FromElement(VariedImageSizeRepeater);

        peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, $"Filtered recipes, {filteredTypes.Count()} results.", "RecipesFilteredNotificationActivityId");
    }

    private void TextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        LogLine.GlobalMessageColumnWidth = Int32.Parse(((TextBox)sender).Text);
       
    }
}
