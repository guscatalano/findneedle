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


        KeyPoint connectHandler = new KeyPoint() { textToMatch = "CTSSession::ConnectToTerminal on session ID " };
        connectHandler.umlTextDelegate = (string msg) =>
        {
            msg = msg.Substring(msg.IndexOf(connectHandler.textToMatch) + connectHandler.textToMatch.Length);
            msg = msg.Substring(0, msg.IndexOf(" "));
            return "LSM -> LSM : Connect Terminal to Session ID " + msg;
        };
        keyHandlers.Add(connectHandler);

        KeyPoint disconnectHandler = new KeyPoint() { textToMatch = "CTSSession::DisconnectSession on session ID " };
        connectHandler.umlTextDelegate = (string msg) =>
        {
            msg = msg.Substring(msg.IndexOf(connectHandler.textToMatch) + connectHandler.textToMatch.Length);
            msg = msg.Substring(0, msg.IndexOf(" "));
            return "LSM -> LSM : Disconnect sessionID: " + msg;
        };
        keyHandlers.Add(connectHandler);

        {
            KeyPoint assignHandler = new KeyPoint() { textToMatch = "Assign session id " };
            assignHandler.umlTextDelegate = (string msg) =>
            {
                msg = msg.Substring(msg.IndexOf(assignHandler.textToMatch) + assignHandler.textToMatch.Length);
                msg = msg.Substring(0, msg.IndexOf(" "));
                return "LSM -> LSM : Assign Terminal to Session ID " + msg;
            };
            keyHandlers.Add(assignHandler);
        }

        {
            KeyPoint fastReconnectHanlder = new KeyPoint() { textToMatch = "msg=Fast reconnect - adding session, SessionId=" };
            fastReconnectHanlder.umlTextDelegate = (string msg) =>
            {
                msg = msg.Substring(msg.IndexOf(fastReconnectHanlder.textToMatch) + fastReconnectHanlder.textToMatch.Length);
                return "Termsrv -> LSM : Fast reconnect to Session ID " + msg;
            };
            keyHandlers.Add(fastReconnectHanlder);
        }
        {
            KeyPoint loggedOnStartedHandler = new KeyPoint() { textToMatch = "msg=pNewTerminal->LoggedOnStarted() took this long, this->CommonData.GetSessionId()=" };
            loggedOnStartedHandler.umlTextDelegate = (string msg) =>
            {
                msg = msg.Substring(msg.IndexOf(loggedOnStartedHandler.textToMatch) + loggedOnStartedHandler.textToMatch.Length);
                return "Termsrv -> LSM : Fast reconnect to Session ID " + msg;
            };
            keyHandlers.Add(loggedOnStartedHandler);
        }

        {
            KeyPoint connectnotify = new KeyPoint() { textToMatch = "msg=LSM sent us ConnectNotify, m_SessionId=" };
            connectnotify.umlTextDelegate = (string msg) =>
            {
                msg = msg.Substring(msg.IndexOf(connectnotify.textToMatch) + connectnotify.textToMatch.Length);
                return "LSM -> Termsrv : ConnectNotify Session ID " + msg;
            };
            keyHandlers.Add(connectnotify);
        }

        {
            KeyPoint disconnectstack = new KeyPoint() { textToMatch = "msg=Trying to call DisconnectNotify, SessionId=" };
            disconnectstack.umlTextDelegate = (string msg) =>
            {
                msg = msg.Substring(msg.IndexOf(disconnectstack.textToMatch) + disconnectstack.textToMatch.Length);
                return "LSM -> Termsrv : DisconnectNotify Session ID " + msg;
            };
            keyHandlers.Add(disconnectstack);
        }

        {
            KeyPoint stackready = new KeyPoint() { textToMatch = "perf=Stack took this long to get ready, StackReadyTime=" };
            stackready.umlTextDelegate = (string msg) =>
            {
                msg = msg.Substring(msg.IndexOf(stackready.textToMatch) + stackready.textToMatch.Length);
                msg = msg.Substring(0, msg.IndexOf(","));
                return "Stack -> Termsrv : Stack is ready for connection (took: " + msg + " ms)";
            };
            keyHandlers.Add(stackready);
        }

        {
            KeyPoint fastreconnectdone = new KeyPoint() { textToMatch = "perf=Fast reconnect time to connect to session, "};
            fastreconnectdone.umlTextDelegate = (string msg) =>
            {
                var sessionid = "";
                msg = msg.Substring(msg.IndexOf(fastreconnectdone.textToMatch) + fastreconnectdone.textToMatch.Length);
                sessionid = msg.Substring(msg.IndexOf(", SessionId="));
                msg = msg.Substring(0, msg.IndexOf(","));
                return "Termsrv -> Termsrv : Fast reconnect finished to session " + sessionid + " (took: " + msg + " ms)";
            };
            keyHandlers.Add(fastreconnectdone);
        }

        {
            KeyPoint connectionstarted = new KeyPoint() { textToMatch = "Listener was notified of a new connection" };
            connectionstarted.umlTextDelegate = (string msg) =>
            {
                msg = msg.Substring(msg.IndexOf("{"));
                return "Stack -> Termsrv : New connection with activityID: " + msg;
            };
            keyHandlers.Add(connectionstarted);
        }

        {
            KeyPoint connectionbroken = new KeyPoint() { textToMatch = "Task started=Broken Connection, Function=CConnectionEx::CRDPCallback::BrokenConnection" };
            connectionbroken.umlTextDelegate = (string msg) =>
            {
                return "Stack -> Termsrv : Connection was broken";
            };
            keyHandlers.Add(connectionbroken);
        }

      
    }

    public string GeneratePlatUML()
    {
        var txt = "@startuml" + Environment.NewLine;
        foreach (var msg in keypoints)
        {
            foreach (KeyPoint key in keyHandlers)
            {
                if (msg.Key.Contains(key.textToMatch))
                {
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
