using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;

public class SearchProgressSink
{
    public List<Action<int>> numericProgress = new();
    public List<Action<string>> textProgress = new();


    public void RegisterForNumericProgress(Action<int> register)
    {
        numericProgress.Add(register);
    }

    public void RegisterForTextProgress(Action<string> register)
    {    
        textProgress.Add(register); 
    }

    //Just change the text
    public void NotifyProgress( string description)
    {
        foreach (var action in textProgress)
        {
            action.Invoke(description);
        }
    }

    public void NotifyProgress(int percentGuess, string description)
    {
        foreach(var action in numericProgress)
        {
            action.Invoke(percentGuess);
        }
        foreach(var action in textProgress)
        {
            action.Invoke(description);
        }
    }
}
