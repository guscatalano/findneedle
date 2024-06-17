using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;

namespace FindNeedleUX.Services.WizardDef;
public class IWizard
{
    public string w_initialPageName;
    public Type w_initialPageType;
    public UIElement starterElement;
    public Page currentPage;
    public Frame wizFrame;
    public Dictionary<string, Dictionary<string, string>> pages = new();
    public Action<string> callback;

    public string FindPageWithShortName(string name)
    {
        var type = typeof(Page);
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => type.IsAssignableFrom(p));
        foreach (var page in types)
        {
            if (page.ToString().Contains(name))
            {
                return page.ToString(); ;
            }
        }
        throw new Exception("Could not find it :(");
    }

    public IWizard(string initialPageName)
    {

        w_initialPageName = initialPageName;
        w_initialPageType = Type.GetType(FindPageWithShortName(w_initialPageName));
    }

    public void AddPage(string page, Dictionary<string, string> actionToPage)
    {
        pages.Add(FindPageWithShortName(page), actionToPage);
    }

    public void NavigateNextOne(string action)
    {
        if (action.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            WindowUtil.GetWindowForElement(wizFrame)?.Close();
            return;
        }
        var pageName = currentPage.GetType().ToString();
        if (!pages.ContainsKey(pageName))
        {
            throw new Exception("Could not find current page!");
        }

        if (!pages[pageName].ContainsKey(action))
        {
            throw new Exception("Undefined action");
        }
        var newPageStr = pages[pageName][action];
        newPageStr = FindPageWithShortName(newPageStr);
        Type newPage = Type.GetType(newPageStr);
        MiddleLayerService.PageChanged(this, this.currentPage);
        wizFrame.Navigate(newPage);
    }

    public void RegisterCurrentPage(Page page)
    {
        this.currentPage = page;
    }

    public void StartWizard(UIElement sender, Action<string> callback)
    {
        this.callback = callback;
        starterElement = sender;
        BasicWizard newWizard = new BasicWizard();

        WindowUtil.TrackWindow(newWizard);
        var y = WindowUtil.GetWindowForElement(sender);
        var z = y.AppWindow;
        y.SetExtendedWindowStyle(ExtendedWindowStyle.NoActivate);
        //z.Hide();
        wizFrame = newWizard.GetFrame();
        wizFrame.Navigate(w_initialPageType);
        newWizard.SetWindowSize(600, 400);
        newWizard.Activate();
        //newWizard.AppWindow.boun
        newWizard.Closed += X_Closed;
    }


    private void X_Closed(object sender, WindowEventArgs args)
    {
        //Let the parent window update somehow

        this.callback("test");
        WindowUtil.GetWindowForElement(this.starterElement).AppWindow.Show();
    }
}
