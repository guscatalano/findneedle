using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;

namespace FindNeedlePluginLib.Implementations.SearchNotifications;


public enum SearchStep
{
    AtLoad,
    AtSearch,
    AtLaunch,
    AtProcessor,
    AtOutput,
    Total
}
public class SearchStepNotificationSink
{
    private SearchProgressSink searchProgressSink = new();
    public List<Action<SearchStep>> genericStep = new();
    
    public SearchProgressSink progressSink
    {
        get
        {
            searchProgressSink ??= new SearchProgressSink();
            return searchProgressSink;
        }
    }

    public SearchStepNotificationSink()
    {
    }

    public void RegisterForStepNotification(Action<SearchStep> register)
    {
        genericStep.Add(register);
    }

    public void NotifyStep(SearchStep step)
    {
        foreach (var action in genericStep)
        {
            action.Invoke(step);
        }
    }

}
