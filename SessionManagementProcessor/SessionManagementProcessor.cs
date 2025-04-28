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
        return path;
    }
    private readonly string msgheader = "- WMsgMessageHandler: ";
    private readonly string stateheader = "StateFn:";
    private readonly string disconnectheader = "CTSSession::DisconnectSession on session ID ";
    private readonly string connectheader = "CTSSession::ConnectToTerminal on session ID ";
    private readonly string assignheader = "Assign session id ";
    private readonly string loggedonstarted = "msg=pNewTerminal->LoggedOnStarted() took this long, this->CommonData.GetSessionId()=";

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

            if (msg.Key.Equals("loggedonstarted"))
            {
                var wmsg = msg.Value.GetMessage();
                wmsg = wmsg.Substring(wmsg.IndexOf(loggedonstarted) + loggedonstarted.Length);
                wmsg = wmsg.Substring(0, wmsg.IndexOf(","));
                txt += "LSM -> LSM : Logged on started Session ID " + wmsg + Environment.NewLine;
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

            if (ret.GetMessage().Contains(assignheader))
            {
                keypoints.Add(new KeyValuePair<string, ISearchResult>("assign", ret));
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
