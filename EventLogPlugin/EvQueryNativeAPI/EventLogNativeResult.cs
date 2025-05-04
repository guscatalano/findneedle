using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using findneedle;

namespace EventLogPlugin.EvQueryNativeAPI;
public class EventLogNativeResult : ISearchResult
{
    public readonly string xml;
    public readonly string logname;
    public readonly string query;
    public readonly string eventdata = "";
    public readonly string systemdata = "";

    public EventLogNativeResult(string xml, string logname, string query)
    {
        this.xml = xml;
        this.logname = logname;
        this.query = query;


        //Parse eventdata
        var first = xml.IndexOf("<EventData>") + "<EventData>".Length;
        var last = xml.IndexOf("</EventData>");
        if (first > 0 && last > 0)
        {
            eventdata = xml.Substring(first, last - first);
        }


        //Parse system data
        first = xml.IndexOf("<System>") + "<System>".Length;
        last = xml.IndexOf("</System>");
        if (first > 0 && last > 0)
        {
            systemdata = xml.Substring(first, last - first);
        }
    }
    public Level GetLevel() => throw new NotImplementedException();
    public DateTime GetLogTime() => throw new NotImplementedException();
    public string GetMachineName() => throw new NotImplementedException();
    public string GetMessage() => throw new NotImplementedException();
    public string GetOpCode() => throw new NotImplementedException();
    public string GetResultSource() => throw new NotImplementedException();
    public string GetSearchableData() => throw new NotImplementedException();
    public string GetSource() => throw new NotImplementedException();
    public string GetTaskName() => throw new NotImplementedException();
    public string GetUsername() => throw new NotImplementedException();
    public void WriteToConsole() => throw new NotImplementedException();

}
