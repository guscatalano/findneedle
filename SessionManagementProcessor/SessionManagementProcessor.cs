using System.Runtime.CompilerServices;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

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
        return "Djfodjsf"; 
    }
    public string GetOutputFile(string optionalOutputFolder = "") {
        var path = Path.Combine(optionalOutputFolder, "session.pu");
        path = Path.GetFullPath(path);
        File.WriteAllText(path, GeneratePlatUML());
        PlantUMLGenerator x = new PlantUMLGenerator();
        var outputpath = x.GenerateUML(path);
        return outputpath;
    }
    private readonly string msgheader = "- WMsgMessageHandler: ";
    private readonly string stateheader = "StateFn:";
    private readonly string disconnectheader = "CTSSession::DisconnectSession on session ID ";
    private readonly string connectheader = "CTSSession::ConnectToTerminal on session ID ";
    private readonly string assignheader = "Assign session id ";
    private readonly string loggedonstarted = "msg=pNewTerminal->LoggedOnStarted() took this long, this->CommonData.GetSessionId()=";
    private readonly string connectionstarted = "Listener was notified of a new connection";
    private readonly string connectionbroken = "Task started=Broken Connection, Function=CConnectionEx::CRDPCallback::BrokenConnection";
    private readonly string stackready = "perf=Stack took this long to get ready, StackReadyTime=";
    private readonly string fastreconnect = "msg=Fast reconnect - adding session, SessionId=";
    private readonly string connectnotify = "msg=LSM sent us ConnectNotify, m_SessionId=";
    private readonly string fastreconnectdone = "perf=Fast reconnect time to connect to session, ";
    private readonly string disconnectstack = "msg=Trying to call DisconnectNotify, SessionId=";

    public string GeneratePlatUML()
    {

        var txt = "@startuml" + Environment.NewLine;
        foreach (var msg in keypoints)
        {
            if (msg.Key.Equals("wmsg"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(msgheader) + msgheader.Length);
                txt += "LSM -> Winlogon : " + wmsg + Environment.NewLine;
            }
            if (msg.Key.Equals("state"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(stateheader) + stateheader.Length);
                txt += "Winlogon -> Winlogon : " + wmsg + Environment.NewLine;
            }

            if (msg.Key.Equals("connect"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(connectheader) + connectheader.Length);
                wmsg = wmsg.Substring(0, wmsg.IndexOf(" "));
                txt += "LSM -> LSM : Connect Terminal to Session ID " + wmsg + Environment.NewLine;
            }

            if (msg.Key.Equals("assign"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(assignheader) + assignheader.Length);
                wmsg = wmsg.Substring(0, wmsg.IndexOf(" "));
                txt += "LSM -> LSM : Assign Terminal to Session ID " + wmsg + Environment.NewLine;
            }

            if (msg.Key.Equals("fastreconnect"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(fastreconnect) + fastreconnect.Length);
                txt += "Termsrv -> LSM : Fast reconnect to Session ID " + wmsg + Environment.NewLine;
            }

            if (msg.Key.Equals("loggedonstarted"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(loggedonstarted) + loggedonstarted.Length);
                wmsg = wmsg.Substring(0, wmsg.IndexOf(","));
                txt += "LSM -> LSM : Logged on started Session ID " + wmsg + Environment.NewLine;
            }

            if (msg.Key.Equals("connectnotify"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(connectnotify) + connectnotify.Length);
                txt += "LSM -> Termsrv : ConnectNotify Session ID " + wmsg + Environment.NewLine;
            }

            if (msg.Key.Equals("disconnectstack"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(disconnectstack) + disconnectstack.Length);
                txt += "LSM -> Termsrv : DisconnectNotify Session ID " + wmsg + Environment.NewLine;
            }

            if (msg.Key.Equals("stackready"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(stackready) + stackready.Length);
                wmsg = wmsg.Substring(0, wmsg.IndexOf(","));
                txt += "Stack -> Termsrv : Stack is ready for connection (took: " + wmsg + " ms)" + Environment.NewLine;
            }

            if (msg.Key.Equals("fastreconnectdone"))
            {
                var wmsg = msg.Value.GetMessage();
                var sessionid = "";
                wmsg = wmsg.Substring(wmsg.IndexOf(fastreconnectdone) + fastreconnectdone.Length);
                sessionid = wmsg.Substring(wmsg.IndexOf(", SessionId="));
                wmsg = wmsg.Substring(0, wmsg.IndexOf(","));
                
                txt += "Termsrv -> Termsrv : Fast reconnect finished to session "+ sessionid +" (took: " + wmsg + " ms)" + Environment.NewLine;
            }

            if (msg.Key.Equals("connectionstarted"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf("{"));
                txt += "Stack -> Termsrv : New connection with activityID: " + wmsg + Environment.NewLine;
            }



            if (msg.Key.Equals("connectionbroken"))
            {
                txt += "Stack -> Termsrv : Connection was broken" + Environment.NewLine;
            }

            if (msg.Key.Equals("disconnect"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(disconnectheader) + disconnectheader.Length); //just get the sessionid
                wmsg = wmsg.Substring(0, wmsg.IndexOf(" "));
                txt += "LSM -> LSM : " + "Disconnect sessionID: " + wmsg + Environment.NewLine;
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
        foreach(var ret in results)
        {
            if (ret.GetMessage().Contains(disconnectheader))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("disconnect", ret));
            }

            if (ret.GetMessage().Contains(loggedonstarted))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("loggedonstarted", ret));
            }

            if (ret.GetMessage().Contains(connectheader))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("connect", ret));
            }

            if (ret.GetMessage().Contains(disconnectstack))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("disconnectstack", ret));
            }

            if (ret.GetMessage().Contains(assignheader))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("assign", ret));
            }

            if (ret.GetMessage().Contains(connectnotify))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("connectnotify", ret));
            }

            if (ret.GetMessage().Contains(connectionstarted))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("connectionstarted", ret));
            }

            if (ret.GetMessage().Contains(stackready))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("stackready", ret));
            }

            if(ret.GetMessage().Contains(fastreconnect))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("fastreconnect", ret));
            }

            if (ret.GetMessage().Contains(connectionbroken))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("connectionbroken", ret));
            }

            if (ret.GetMessage().Contains(fastreconnectdone))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("fastreconnectdone", ret));
            }

            if (ret.GetMessage().Contains(msgheader))
            {
                if (ret.GetMessage().Contains("dwMessage"))
                {
                    continue; //bahhh
                }
                keypoints.Add(new KeyValuePair<string, ISearchResult>("wmsg", ret));
            }
            if (ret.GetMessage().Contains(stateheader))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("state", ret));
            }
        }
    }
}
