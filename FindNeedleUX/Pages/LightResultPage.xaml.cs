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
    public List<Recipe> staticRecipeData;
    private bool IsSortDescending = false;

    private Button LastSelectedColorButton;
    private int PreviouslyFocusedAnimatedScrollRepeaterIndex = -1;
    public LightResultPage()
    {
        this.InitializeComponent();
        List<Recipe> RecipeList = GetRecipeList();
        filteredRecipeData.InitializeCollection(RecipeList);
        // Save a static copy to compare to while filtering
        staticRecipeData = RecipeList;
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

        public MyItemsSource(IEnumerable<Recipe> collection)
        {
            InitializeCollection(collection);

        }

        public void InitializeCollection(IEnumerable<Recipe> collection)
        {
            inner.Clear();
            if (collection != null)
            {
                inner.AddRange(collection);
            }

            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        #region IReadOnlyList<T>
        public int Count => this.inner != null ? this.inner.Count : 0;

        public object this[int index]
        {
            get
            {
                return inner[index] as Recipe;
            }

            set
            {
                inner[index] = (Recipe)value;
            }
        }

        public IEnumerator<Recipe> GetEnumerator() => this.inner.GetEnumerator();

        #endregion

        #region INotifyCollectionChanged
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        #endregion

        #region IKeyIndexMapping
        public string KeyFromIndex(int index)
        {
            return inner[index].Num.ToString();
        }

        public int IndexFromKey(string key)
        {
            foreach (Recipe item in inner)
            {
                if (item.Num.ToString() == key)
                {
                    return inner.IndexOf(item);
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

    // ========================== Initialization code ==========================
    public List<String> ColorList = new List<String>()
        {
                "Blue",
                "BlueViolet",
                "Crimson",
                "DarkCyan",
                "DarkGoldenrod",
                "DarkMagenta",
                "DarkOliveGreen",
                "DarkRed",
                "DarkSlateBlue",
                "DeepPink",
                "IndianRed",
                "MediumSlateBlue",
                "Maroon",
                "MidnightBlue",
                "Peru",
                "SaddleBrown",
                "SteelBlue",
                "OrangeRed",
                "Firebrick",
                "DarkKhaki"
        };


    private ObservableCollection<string> GetFruits()
    {
        return new ObservableCollection<string> { "Apricots", "Bananas", "Grapes", "Strawberries", "Watermelon", "Plums", "Blueberries" };
    }

    private ObservableCollection<string> GetVegetables()
    {
        return new ObservableCollection<string> { "Broccoli", "Spinach", "Sweet potato", "Cauliflower", "Onion", "Brussels sprouts", "Carrots" };
    }
    private ObservableCollection<string> GetGrains()
    {
        return new ObservableCollection<string> { "Rice", "Quinoa", "Pasta", "Bread", "Farro", "Oats", "Barley" };
    }
    private ObservableCollection<string> GetProteins()
    {
        return new ObservableCollection<string> { "Steak", "Chicken", "Tofu", "Salmon", "Pork", "Chickpeas", "Eggs" };
    }

    // ==========================================================================
    // VariedImageSize Layout with Filtering/Sorting
    // ==========================================================================
    private List<Recipe> GetRecipeList()
    {
        // Initialize list of recipes for varied image size layout sample
        var rnd = new Random();
        List<Recipe> tempList = new List<Recipe>(
                                    Enumerable.Range(0, 1000).Select(k =>
                                        new Recipe
                                        {
                                            Num = k,
                                            Name = "Recipe " + k.ToString(),
                                            Color = ColorList[rnd.Next(0, 19)]
                                        }));

        foreach (Recipe rec in tempList)
        {
            // Add one food from each option into the recipe's ingredient list and ingredient string
            string fruitOption = GetFruits()[rnd.Next(0, 6)];
            string vegOption = GetVegetables()[rnd.Next(0, 6)];
            string grainOption = GetGrains()[rnd.Next(0, 6)];
            string proteinOption = GetProteins()[rnd.Next(0, 6)];
            rec.Ingredients = "\n" + fruitOption + "\n" + vegOption + "\n" + grainOption + "\n" + proteinOption;
            rec.IngList = new List<string>() { fruitOption, vegOption, grainOption, proteinOption };

            // Add extra ingredients so items have varied heights in the layout
            rec.RandomizeIngredients();
        }

        return tempList;
    }

    private void InitializeData()
    {
        // Initialize custom MyItemsSource object with new recipe data
        List<Recipe> RecipeList = GetRecipeList();
        filteredRecipeData.InitializeCollection(RecipeList);
        // Save a static copy to compare to while filtering
        staticRecipeData = RecipeList;
        VariedImageSizeRepeater.ItemsSource = filteredRecipeData;
    }
    private void OnEnableAnimationsChanged(object sender, RoutedEventArgs e)
    {
#if WINUI_PRERELEASE
            VariedImageSizeRepeater.Animator = EnableAnimations.IsChecked.GetValueOrDefault() ? new DefaultElementAnimator() : null;
#endif
    }

    public void FilterRecipes_FilterChanged(object sender, RoutedEventArgs e)
    {
        UpdateSortAndFilter();
    }

    private void OnSortAscClick(object sender, RoutedEventArgs e)
    {
        if (IsSortDescending == true)
        {
            IsSortDescending = false;
            UpdateSortAndFilter();
        }
    }


    private void OnSortDesClick(object sender, RoutedEventArgs e)
    {
        if (!IsSortDescending == true)
        {
            IsSortDescending = true;
            UpdateSortAndFilter();
        }
    }

    private void UpdateSortAndFilter()
    {
        // Find all recipes that ingredients include what was typed into the filtering text box
        var filteredTypes = staticRecipeData.Where(i => i.Ingredients.Contains(FilterRecipes.Text, StringComparison.InvariantCultureIgnoreCase));
        // Sort the recipes by whichever sorting mode was last selected (least to most ingredients by default)
        var sortedFilteredTypes = IsSortDescending ?
            filteredTypes.OrderByDescending(i => i.IngList.Count()) :
            filteredTypes.OrderBy(i => i.IngList.Count());
        // Re-initialize MyItemsSource object with this newly filtered data
        filteredRecipeData.InitializeCollection(sortedFilteredTypes);

        var peer = FrameworkElementAutomationPeer.FromElement(VariedImageSizeRepeater);

        peer.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, $"Filtered recipes, {sortedFilteredTypes.Count()} results.", "RecipesFilteredNotificationActivityId");
    }
}
