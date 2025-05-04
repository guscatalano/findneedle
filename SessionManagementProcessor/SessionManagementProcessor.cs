using System.Runtime.CompilerServices;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;
using Windows.System;

namespace SessionManagementProcessor;


public struct KeyPoint
{
    public string textToMatch;
    public string? textToUnmatch;
    public ISearchResult? matchedLine;
    public string? umlText;
    public Func<string, string>? umlTextDelegate;
    public Func<Tuple<string, string>, string>? umlTextDelegateComplex;
}

public struct ProcessLifeTime
{
    public int processID;
    public DateTime? startTime;
    public DateTime? endTime;
}

public class ProcessLifeTimeTracker
{
    public string processName;
    public Dictionary<int, ProcessLifeTime> lives = new();


    public ProcessLifeTimeTracker(string processName)
    {
        this.processName = processName;
    }

    public string GetUMLAtTime(DateTime currentLogTime, DateTime lastLogTime)
    {
        var ret = "";
        foreach (var life in lives)
        {
            if (life.Value.startTime != null && life.Value.endTime != null)
            {
                //The last time we did not get it, and now we passed it.
                if (lastLogTime < life.Value.startTime && currentLogTime > life.Value.startTime)
                {
                    ret += "== LSM started pid: " + life.Value.processID + " == " + Environment.NewLine;
                }

                if (lastLogTime <= life.Value.endTime && currentLogTime > life.Value.endTime)
                {
                    ret += "== LSM ended pid: " + life.Value.processID + " == " + Environment.NewLine;
                }
            }
        }
        return ret;
    }


    public void NewEvent(DateTime time, int pid)
    {
        if (lives.ContainsKey(pid))
        {
            // Create a copy of the struct, modify it, and then assign it back to the dictionary
            var processLifeTime = lives[pid];

            if (processLifeTime.startTime > time)
            {
                processLifeTime.startTime = time;
            }
            if (processLifeTime.endTime < time)
            {
                processLifeTime.endTime = time;
            }

            lives[pid] = processLifeTime; // Assign the modified struct back to the dictionary
        }
        else
        {
            lives.Add(pid, new ProcessLifeTime()
            {
                processID = pid,
                startTime = time,
                endTime = time
            });
        }
    }
}

public class SessionManagementProcessor : IResultProcessor, IPluginDescription
{
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }
    public string GetPluginFriendlyName() => "Session Management Processor";
    public string GetPluginTextDescription() => "This plugin manages the session of the search query.";
    public void Dispose()
    {
    }

    public string GetDescription() {
        return "Processes session management logs into UML";
    }
    public string GetOutputFile(string optionalOutputFolder = "") {
        var path = Path.Combine(optionalOutputFolder, "session.pu");
        path = Path.GetFullPath(path);
        File.WriteAllText(path, GeneratePlatUML());
        PlantUMLGenerator x = new PlantUMLGenerator();
        var outputpath = x.GenerateUML(path);
        return outputpath;
    }
    

    private readonly List<KeyPoint> keyHandlers = new();
    private ProcessLifeTimeTracker lsmlife = new("LSM");


    public void GenerateKeyPoints()
    {
        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "- WMsgMessageHandler: ",
            textToUnmatch = "dwMessage",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> Winlogon : " + msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "StateFn:",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> Winlogon : " + msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
            }
        });
        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Begin session arbitration:",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> LSM : " + "Start session arbitration";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "End session arbitration:",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> LSM : " + "Stop session arbitration";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services: Session logon succeeded",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> LSM : " + "User logged on";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Local multi-user session manager received system shutdown message",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "svchost -> LSM : " + "System shutting down";
            }
        });

        

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services: Shell start notification received",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> LSM : " + "Shell started";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services is not accepting logons because setup is running.",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> LSM : " + "OOBE is running, no remote connections";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services: Session logoff succeeded:",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                return "LSM -> LSM : Session logoff";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "CTSSession::ConnectToTerminal on session ID " ,
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(" "));
                return "LSM -> LSM : Connect Terminal to Session ID " + msg;
            }
        });


        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "CTSSession::DisconnectSession on session ID ",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(" "));
                return "LSM -> LSM : Disconnect sessionID: " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Assign session id ",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(" "));
                return "LSM -> LSM : Assign Terminal to Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=Fast reconnect - adding session, SessionId=",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "Termsrv -> LSM : Fast reconnect to Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=pNewTerminal->LoggedOnStarted() took this long, this->CommonData.GetSessionId()=",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "Termsrv -> LSM : LoggedOnStarted finished to Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=LSM sent us ConnectNotify, m_SessionId=",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "LSM -> Termsrv : ConnectNotify Session ID " + msg;
            }
        });


        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=Trying to call DisconnectNotify, SessionId=",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "LSM -> Termsrv : DisconnectNotify Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "perf=Stack took this long to get ready, StackReadyTime=",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(","));
                return "Stack -> Termsrv : Stack is ready for connection (took: " + msg + " ms)";
            }
        });


        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "perf=Fast reconnect time to connect to session, ",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                var sessionid = "";
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                sessionid = msg.Substring(msg.IndexOf(", SessionId="));
                msg = msg.Substring(0, msg.IndexOf(","));
                return "Termsrv -> Termsrv : Fast reconnect finished to session " + sessionid + " (took: " + msg + " ms)";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Listener was notified of a new connection",
            umlTextDelegateComplex = (Tuple<string, string> input) =>
            {
                var msg = input.Item1;
                var matchedText = input.Item2;
                msg = msg.Substring(msg.IndexOf("{"));
                return "Stack -> Termsrv : New connection with activityID: " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Task started=Broken Connection, Function=CConnectionEx::CRDPCallback::BrokenConnection",
            umlTextDelegate = (string input) =>
            {
                return "Stack -> Termsrv : Connection was broken";
            }
        });


      
    }

    public string GeneratePlatUML()
    {
        var txt = "@startuml" + Environment.NewLine;

        DateTime lastLogLine = DateTime.MinValue;
        foreach (var msg in keypoints)
        {
            foreach (KeyPoint key in keyHandlers)
            {
                if (msg.Key.Contains(key.textToMatch))
                {
                    txt += lsmlife.GetUMLAtTime(msg.Value.GetLogTime(), lastLogLine);


                    // Ensure that `umlTextDelegate` is not null before invoking it
                    if (key.umlTextDelegate != null)
                    {
                        txt += key.umlTextDelegate(msg.Value.GetMessage()) + Environment.NewLine;
                    }
                    else if (key.umlTextDelegateComplex != null)
                    {
                        var result = key.umlTextDelegateComplex(new Tuple<string, string>(msg.Value.GetMessage(), key.textToMatch));
                        txt += result + Environment.NewLine;
                    }
                    else
                    {
                        throw new Exception(":(");
                    }
                    lastLogLine = msg.Value.GetLogTime();
                }
            }
        }
        txt += "@enduml" + Environment.NewLine;
        return txt;
    }

    public string GetOutputText()
    {
        return GeneratePlatUML();
    }

    public List<KeyValuePair<string, ISearchResult>> keypoints = new();

    public void ProcessResults(List<ISearchResult> results)
    {
        GenerateKeyPoints();
        foreach (var ret in results)
        {

            if(ret.GetSource().ToLower().Contains("Microsoft-Windows-TerminalServices-LocalSessionManager".ToLower()))
            {
                if (ret.GetSearchableData().Contains("Execution ProcessID"))
                {
                    var pid = ret.GetSearchableData().Substring(ret.GetSearchableData().IndexOf("Execution ProcessID") + "Execution ProcessID".Length);
                    pid = pid.Substring(0, pid.IndexOf(" ")).Replace("=", "").Replace("'", "").Replace("\"", "").Trim();
                    lsmlife.NewEvent(ret.GetLogTime(), int.Parse(pid));
                }
            }   
            

            foreach(KeyPoint key in keyHandlers)
            {
                if (ret.GetMessage().Contains(key.textToMatch) || ret.GetSearchableData().Contains(key.textToMatch))
                {
                    if (!string.IsNullOrEmpty(key.textToUnmatch) && ret.GetMessage().Contains(key.textToUnmatch))
                    {
                        continue;
                    }
                    keypoints.Add(new KeyValuePair<string, ISearchResult>(key.textToMatch, ret));
                }
            }


           
        }
    }
}
