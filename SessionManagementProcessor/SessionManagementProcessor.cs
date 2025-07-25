﻿using System.Runtime.CompilerServices;
using FindNeedlePluginLib;
using FindNeedleCoreUtils;
using Windows.System;
using FindNeedlePluginUtils;
using static FindNeedlePluginUtils.LogToPlantUML;

namespace SessionManagementProcessor;


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
        var x = new PlantUMLGenerator();
        var outputpath = x.GenerateUML(path);
        return outputpath;
    }
    

    private readonly List<KeyPoint> keyHandlers = new();
    private readonly ProcessLifeTimeTracker lsmlife = new("LSM");
    private readonly ProcessLifeTimeTracker winlogonlife = new("winlogon");


    public void GenerateKeyPoints()
    {
        keyHandlers.Clear();
        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "- WMsgMessageHandler: ",
            textToUnmatch = "dwMessage",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> Winlogon : " + msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "StateFn:",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> Winlogon : " + msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
            }
        });
        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Begin session arbitration:",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> LSM : " + "Start session arbitration";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "End session arbitration:",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> LSM : " + "Stop session arbitration";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services: Session logon succeeded",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> LSM : " + "User logged on";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Local multi-user session manager received system shutdown message",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "svchost -> LSM : " + "System shutting down";
            }
        });

        

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services: Shell start notification received",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> LSM : " + "Shell started";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services is not accepting logons because setup is running.",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> LSM : " + "OOBE is running, no remote connections";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Remote Desktop Services: Session logoff succeeded:",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                return "LSM -> LSM : Session logoff";
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "CTSSession::ConnectToTerminal on session ID " ,
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(" "));
                return "LSM -> LSM : Connect Terminal to Session ID " + msg;
            }
        });


        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "CTSSession::DisconnectSession on session ID ",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(" "));
                return "LSM -> LSM : Disconnect sessionID: " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Assign session id ",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(" "));
                return "LSM -> LSM : Assign Terminal to Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=Fast reconnect - adding session, SessionId=",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "Termsrv -> LSM : Fast reconnect to Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=pNewTerminal->LoggedOnStarted() took this long, this->CommonData.GetSessionId()=",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "Termsrv -> LSM : LoggedOnStarted finished to Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=LSM sent us ConnectNotify, m_SessionId=",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "LSM -> Termsrv : ConnectNotify Session ID " + msg;
            }
        });


        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "msg=Trying to call DisconnectNotify, SessionId=",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                return "LSM -> Termsrv : DisconnectNotify Session ID " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "perf=Stack took this long to get ready, StackReadyTime=",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                msg = msg.Substring(msg.IndexOf(matchedText) + matchedText.Length);
                msg = msg.Substring(0, msg.IndexOf(","));
                return "Stack -> Termsrv : Stack is ready for connection (took: " + msg + " ms)";
            }
        });


        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "perf=Fast reconnect time to connect to session, ",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
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
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                var msg = input.msg;
                var matchedText = input.matchedText;
                var starsub = msg.IndexOf("{");
                if (starsub > 0)
                {
                    //fix when activityid doesnt have {
                    msg = msg.Substring(msg.IndexOf("{"));
                }
                
                return "Stack -> Termsrv : New connection with activityID: " + msg;
            }
        });

        keyHandlers.Add(new KeyPoint()
        {
            textToMatch = "Task started=Broken Connection, Function=CConnectionEx::CRDPCallback::BrokenConnection",
            umlTextDelegateComplex = (UmlGenerationParams input) =>
            {
                return "Stack -> Termsrv : Connection was broken";
            }
        });


      
    }

    public string GeneratePlatUML()
    {
        var txt = "@startuml" + Environment.NewLine;

        var lastLogLine = DateTime.MinValue;
        foreach (var msg in keypoints)
        {
            foreach (var key in keyHandlers)
            {
                if (msg.Key.Contains(key.textToMatch))
                {
                    txt += lsmlife.GetUMLAtTime(msg.Value.GetLogTime(), lastLogLine);
                    txt += winlogonlife.GetUMLAtTime(msg.Value.GetLogTime(), lastLogLine);

                    if (key.umlTextDelegateComplex != null)
                    {
                        var result = key.umlTextDelegateComplex(new UmlGenerationParams
                        {
                            msg = msg.Value.GetMessage(),
                            matchedText = key.textToMatch,
                            includeTime = false
                        });
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

        keypoints = new(); // reset 
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
            if (ret.GetSource().ToLower().Contains("Microsoft-Windows-Winlogon".ToLower()))
            {
                if (ret.GetSearchableData().Contains("Execution ProcessID"))
                {
                    var pid = ret.GetSearchableData().Substring(ret.GetSearchableData().IndexOf("Execution ProcessID") + "Execution ProcessID".Length);
                    pid = pid.Substring(0, pid.IndexOf(" ")).Replace("=", "").Replace("'", "").Replace("\"", "").Trim();
                    winlogonlife.NewEvent(ret.GetLogTime(), int.Parse(pid));
                }
            }


            foreach (var key in keyHandlers)
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
