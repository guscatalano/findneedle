using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using Microsoft.Diagnostics.Tracing.Session;

namespace ETWPlugin.Locations;

public class LiveCollector
{

    private List<string> providersToCollect = new();
    private TimeSpan timeLimit = TimeSpan.Zero;
    private int eventLimit = 0;
    private TraceEventSession? currentSession = null;
    private Thread? collectorThread = null;
    private readonly List<ISearchResult> results = [];
    private readonly ManualResetEvent stopEvent = new(false);
    private string sessionName = "";
    private List<string> providersFailedToEnable = [];

    public void Setup(List<string> providersToCollect, TimeSpan? timeLimit = null, int eventLimit = 0, string sessionName = "")
    {
        this.providersToCollect = providersToCollect;
        

        if ((timeLimit == null || timeLimit == TimeSpan.Zero) && eventLimit == 0)
        {
            throw new Exception("Time limit or Event limit must be set");
        }
        this.timeLimit = timeLimit ?? TimeSpan.Zero;
        this.eventLimit = eventLimit;
        this.sessionName = sessionName;
        if (string.IsNullOrEmpty(this.sessionName))
        {
            this.sessionName = "FindNeedle_LiveETW";
        }
    }

    public void GetOutputFile()
    {

    }

    public void StartCollecting()
    {
        Thread x = new Thread(CollectorThread);
        x.Start();
        collectorThread = x;

        Thread y = new Thread(CounterWatcher);
        y.Start();
    }

    private void CounterWatcher()
    {
        stopEvent.WaitOne(timeLimit);
        StopCollecting();
    }

    private void CollectorThread()
    {
        using var KS = new TraceEventSession(sessionName);

        currentSession = KS;
        foreach (var provider in providersToCollect)
        {
            try
            {
                KS.EnableProvider(provider);
            } catch(UnauthorizedAccessException e)
            {
                providersFailedToEnable.Add(provider);
            }
        }
        KS.Source.Dynamic.All += Dynamic_All; //Listen to everything
        KS.Source.Process();
    }

    public void StopCollecting()
    {
        if (currentSession != null)
        {
            currentSession.Dispose();
            currentSession = null;
        }
       
        if(collectorThread != null)
        {
            var count = 100;
            while (collectorThread.IsAlive)
            {
                count--;
                Thread.Sleep(100);
            }
            if(count < 0)
            {
                throw new Exception("failed to stop collector thread");
            }
        }
    }

    public bool IsCollecting()
    {
        return currentSession != null;
    }

    public List<ISearchResult> GetResultsInMemory()
    {
        return results;
    }

    private void Dynamic_All(Microsoft.Diagnostics.Tracing.TraceEvent obj)
    {
        results.Add(new ETLLogLine(obj));
        if (eventLimit != 0)
        {
            if (results.Count() > eventLimit)
            {
                stopEvent.Set();
            }
        }
        
    }
}
