using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations.EventLogQueryLocation;
using FindNeedlePluginLib;

namespace findneedle.Implementations.Locations.EventLogQueryLocation;

public class EventRecordResult : ISearchResult
{
    readonly EventRecord entry;
    readonly IEventLogQueryLocation location;
    readonly string eventdata = "";
    readonly string systemdata = "";
    readonly string formattedevent = "";


    public EventRecordResult(EventRecord entry, IEventLogQueryLocation location)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        this.entry = entry;
        this.location = location;

        var xmlStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var doc = entry.ToXml();
        xmlStopwatch.Stop();
        Logger.Instance.Log($"[PERF] EventRecordResult.ToXml() took {xmlStopwatch.Elapsed.TotalMilliseconds:F0} ms");

        //Parse eventdata
        var first = doc.IndexOf("<EventData>") + "<EventData>".Length;
        var last = doc.IndexOf("</EventData>");
        if (first > 0 && last > 0)
        {
            eventdata = doc.Substring(first, last - first);
        }

        //Parse system data
        first = doc.IndexOf("<System>") + "<System>".Length;
        last = doc.IndexOf("</System>");
        if (first > 0 && last > 0)
        {
            systemdata = doc.Substring(first, last - first);
        }

        // Try to get formatted event, fallback to details if it fails
        string? formatted;
        var formatStopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            formatted = entry.FormatDescription(); //Consider passing in the locale
        }
        catch (Exception ex)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[FormatDescription unavailable: {ex.Message}]");
            sb.AppendLine($"Provider: {entry.ProviderName}");
            sb.AppendLine($"EventId: {entry.Id}");

            string levelString;
            try
            {
                levelString = entry.LevelDisplayName ?? entry.Level?.ToString() ?? "Unknown";
            }
            catch
            {
                levelString = entry.Level?.ToString() ?? "Unknown*";
            }
            sb.AppendLine($"Level: {levelString}");

            sb.AppendLine($"TimeCreated: {entry.TimeCreated}");
            sb.AppendLine($"Raw XML: {doc}");
            formatted = sb.ToString();
        }
        formatStopwatch.Stop();
        Logger.Instance.Log($"[PERF] EventRecordResult.FormatDescription() took {formatStopwatch.Elapsed.TotalMilliseconds:F0} ms");
        formattedevent = formatted;
        stopwatch.Stop();
        Logger.Instance.Log($"[PERF] EventRecordResult constructor took {stopwatch.Elapsed.TotalMilliseconds:F0} ms");
    }


    public Level GetLevel()
    {
        try { 
            if(entry.LevelDisplayName == null)
            {
                return Level.Unknown;
            }
            switch (entry.LevelDisplayName.ToLower())
            {
                case "warning":
                    return Level.Warning;
                case "error":
                    return Level.Error;
                case "information":
                    return Level.Info;
                default:
                    return Level.Verbose;

            }
        }
        catch (Exception)
        {
            switch (entry.Level)
            {
                case 1:
                    return Level.Error;
                case 2:
                    return Level.Warning;
                case 3:
                    return Level.Info;
                default:
                    return Level.Verbose;
            }
            throw; // If it still doesnt work, throw it
        }
    }

    public string GetResultSource()
    {
        return "LocalEventLogRecord-" + location.GetName();
    }

    public DateTime GetLogTime()
    {
        if (entry.TimeCreated == null)
        {
            throw new NotSupportedException("Log time can be empty");
        }
        return (DateTime)entry.TimeCreated;
    }

    public string GetMachineName()
    {
        return entry.MachineName;
    }

    public string GetOpCode()
    {
        try { return entry.OpcodeDisplayName ?? ""; } catch { return ""; }
    }

    public string GetSource()
    {
        return entry.ProviderName;
    }

    public string GetTaskName()
    {
        try
        {
            return entry.TaskDisplayName ?? "";
        }
        catch (Exception)
        {
            // Many providers have no task metadata — TaskDisplayName throws; treat as "no task".
            return "";
        }
    }

    public string GetUsername()
    {

        if(entry.UserId == null) { 
            return "Unknown";
        }
        var sid = entry.UserId.ToString();
        try
        {
            var account = new System.Security.Principal.SecurityIdentifier(sid).Translate(typeof(System.Security.Principal.NTAccount)).ToString();
            return account.ToString();
        }
        catch (Exception)
        {
            //return sid if we couldn't translate it (from another machine?)
            return sid;
        }
    }

    public string GetProcessId()
    {
        try { return entry.ProcessId?.ToString() ?? ""; } catch { return ""; }
    }

    public string GetEventId()
    {
        try { return entry.Id.ToString(); } catch { return ""; }
    }

    public string GetKeywords()
    {
        try { return entry.KeywordsDisplayNames != null ? string.Join(", ", entry.KeywordsDisplayNames) : ""; }
        catch { return ""; }
    }

    public string GetRelatedActivityId()
    {
        try { return entry.RelatedActivityId?.ToString() ?? ""; } catch { return ""; }
    }

    public string GetChannel()
    {
        try { return entry.LogName ?? ""; } catch { return ""; }
    }

    public string GetProviderGuid()
    {
        try { return entry.ProviderId?.ToString() ?? ""; } catch { return ""; }
    }

    public string GetRecordId()
    {
        try { return entry.RecordId?.ToString() ?? ""; } catch { return ""; }
    }

    /// <summary>Parse the captured &lt;EventData&gt; XML into a JSON object of Name→value pairs (unnamed
    /// Data elements get Data1, Data2, …). Empty for events with no EventData (e.g. UserData-only).</summary>
    public string GetStructuredData()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(eventdata)) return "";
            var root = System.Xml.Linq.XElement.Parse("<EventData>" + eventdata + "</EventData>");
            var pairs = new Dictionary<string, string>();
            int i = 0;
            foreach (var d in root.Elements())
            {
                i++;
                var name = d.Attribute("Name")?.Value;
                if (string.IsNullOrEmpty(name)) name = "Data" + i;
                if (!pairs.ContainsKey(name)) pairs[name] = d.Value;
            }
            return pairs.Count == 0 ? "" : System.Text.Json.JsonSerializer.Serialize(pairs);
        }
        catch { return ""; }
    }

    public string GetThreadId()
    {
        try { return entry.ThreadId?.ToString() ?? ""; } catch { return ""; }
    }

    public string GetActivityId()
    {
        try { return entry.ActivityId?.ToString() ?? ""; } catch { return ""; }
    }

    public string GetMessage()
    {
        // Prefer the rendered, human-readable description (FormatDescription). Fall back to the raw
        // <EventData> and then <System> XML — many providers use <UserData> (no <EventData>), so
        // returning eventdata alone dropped their message entirely.
        if (!string.IsNullOrWhiteSpace(formattedevent)) return formattedevent;
        if (!string.IsNullOrWhiteSpace(eventdata)) return eventdata;
        return systemdata;
    }

    //This is usually the message, but it can be more. This is likely not readable
    public string GetSearchableData()
    {

        return string.Join(' ', eventdata, systemdata, formattedevent);
    }

    public void WriteToConsole()
    {
        Console.WriteLine(eventdata);
    }
}

































































