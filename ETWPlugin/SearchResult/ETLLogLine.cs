global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
using findneedle;
using Microsoft.Diagnostics.Tracing.Etlx;
using Newtonsoft.Json;
using FindNeedlePluginLib;

public class ETLLogLine : ISearchResult
{
    public string eventtxt = string.Empty;
    public string tasktxt = string.Empty;
    public DateTime metadatetime = DateTime.MinValue;
    public string metaprovider = string.Empty; //comes from json, not header
    public string provider = string.Empty;
    public string tempBuffer = string.Empty;
    public string firstSomething = string.Empty; //dont know what this is
    public string hexPid = string.Empty;
    public string hexTid = string.Empty;
    public string activityId = string.Empty;
    public string eventId = string.Empty;
    public string keywords = string.Empty;
    public string relatedActivityId = string.Empty;
    public string providerGuid = string.Empty;
    public string opcodeName = string.Empty;
    public int eventLevel = -1; // ETW TraceEventLevel (1=Critical..5=Verbose); -1 = unknown/not captured
    public string processName = string.Empty;
    public string structuredData = string.Empty;
    public string datetime = string.Empty;
    public DateTime parsedTime = DateTime.MinValue;
    public string json = string.Empty;
    public Dictionary<string, dynamic> keyjson = new();
    public string filename = string.Empty;

    public string originalLine = string.Empty;


    public string GetResultSource()
    {
        return "ETW: " + filename;
    }
    public static bool DoesHeaderLookRight(string textline)
    {
        var step = 0;
        var seenColon = false;
        foreach (var c in textline)
        {
            //we do it this way cause it's more performant than split
            switch (step)
            {
                //first part is [x]
                case 0:
                    if (c == ']')
                    {
                        step++;
                    }
                    break;
                case 1:
                    if (c == '.')
                    {
                        step++;
                    }
                    break;
                case 2:
                    if (c != ':')
                    {
                        break;
                    }
                    if (c == ':')
                    {
                        //We do this because there are two :: and next sequence expects :
                        if (seenColon)
                        {
                            step++;
                        }
                        else
                        {
                            seenColon = true;
                        }
                    }
                    break;
                case 3:
                    if (c == ' ')
                    {
                        step++;
                    }
                    break;
                case 4:
                    if (c == ']')
                    {
                        return true;
                    }
                    break;
                default:
                    break;
            }
        }
        return false;
    }

    private ETLLogLine() { }

    // Shared empty payload for unformatted rows (PreLoad/GetLevel only read it). Avoids allocating a
    // Dictionary per row when "Decode anyway" emits millions of them.
    private static readonly Dictionary<string, dynamic> SharedEmptyKeyJson = new();

    /// <summary>
    /// Build a single representative row for a distinct WPP message GUID that tracefmt could not format
    /// (missing TMF). Used by the viewer's "Decode anyway", which de-dupes by GUID — so instead of
    /// millions of identical unformatted events, the user sees one row per distinct missing symbol,
    /// annotated with how many events collapsed into it.
    /// </summary>
    public static ETLLogLine Unformatted(string messageGuid, long eventCount, string filename)
    {
        var msg = $"Unknown WPP event ×{eventCount:N0} — GUID={messageGuid} (no format info; TMF/symbol missing)";
        return new ETLLogLine
        {
            filename = filename,
            eventtxt = msg,
            json = msg,
            originalLine = msg,
            tasktxt = "Unformatted (missing WPP symbol)",
            provider = "(unformatted WPP)",
            metaprovider = string.Empty,
            metadatetime = DateTime.MinValue,
            parsedTime = DateTime.MinValue,
            keyjson = SharedEmptyKeyJson,
        };
    }

    public ETLLogLine(Microsoft.Diagnostics.Tracing.TraceEvent obj)
    {
        this.filename = "LIVE";
        this.provider = obj.ProviderName;
        this.tasktxt = obj.TaskName;
        
        // Collect the payload fields once (kept as JSON in StructuredData; the message is just a
        // rendering of them, so the viewer can re-render in a different format without a re-scan).
        var structured = new Dictionary<string, string>();
        foreach (var prop in obj.GetDynamicMemberNames())
        {
            if (string.IsNullOrEmpty(prop) || structured.ContainsKey(prop)) continue;
            structured[prop] = obj.PayloadStringByName(prop) ?? "";
        }
        try
        {
            this.processName = obj.ProcessName ?? "";
            if (structured.Count > 0) this.structuredData = System.Text.Json.JsonSerializer.Serialize(structured);
        }
        catch { }

        // Prefer the provider's rendered message (manifest events with a template). For TraceLogging
        // there's no template, so render the payload fields (PerfView-style key="value" default). The
        // event name is NOT prepended — it's already the TaskName column, so repeating it just
        // duplicates that column.
        var formatted = obj.FormattedMessage;
        // Render the message straight from the fields we already have — NOT from this.structuredData,
        // which would re-parse the JSON we just serialized (a wasted JsonDocument.Parse per event over
        // millions of ETL rows). Dictionary + JSON both preserve insertion order, so output is identical.
        string payload = structured.Count > 0
            ? FindNeedlePluginUtils.StructuredLog.StructuredPayloadFormatter.Render(
                System.Linq.Enumerable.ToList(structured), FindNeedlePluginUtils.StructuredLog.PayloadFormat.KeyValueQuoted)
            : string.Empty;
        if (!string.IsNullOrEmpty(formatted))
            this.eventtxt = payload.Length > 0 ? formatted + " == " + payload : formatted!;
        else
            this.eventtxt = payload.Length > 0 ? payload : (obj.EventName ?? string.Empty);

        // Carry the precise event time directly (round-trip string keeps sub-second precision; the
        // metadatetime field is what GetLogTime returns, avoiding the lossy ToString()/re-parse).
        this.datetime = obj.TimeStamp.ToString("o");
        this.metadatetime = obj.TimeStamp;
        this.hexPid = obj.ProcessID.ToString("X");
        this.hexTid = obj.ThreadID.ToString("X");
        try { this.activityId = obj.ActivityID == Guid.Empty ? "" : obj.ActivityID.ToString(); } catch { }
        try { this.eventId = ((int)obj.ID).ToString(System.Globalization.CultureInfo.InvariantCulture); } catch { }
        try { this.keywords = obj.Keywords == 0 ? "" : obj.Keywords.ToString(); } catch { }
        try { this.relatedActivityId = obj.RelatedActivityID == Guid.Empty ? "" : obj.RelatedActivityID.ToString(); } catch { }
        try { this.providerGuid = obj.ProviderGuid == Guid.Empty ? "" : obj.ProviderGuid.ToString(); } catch { }
        try { this.opcodeName = obj.OpcodeName ?? ""; } catch { }
        // Capture the event's severity. TraceLogging (and manifest) events carry it on the TraceEvent
        // itself, not in the WPP "meta" JSON that GetLevel otherwise reads — without this every
        // TraceEvent-sourced row (the whole TraceLogging path) fell through to Level.Info.
        try { this.eventLevel = (int)obj.Level; } catch { }
    }

    public ETLLogLine(string textline, string filename)
    {
        originalLine = textline;
        this.filename = filename;

        var step = 0;

        foreach (var c in textline)
        {
            //we do it this way cause it's more performant than split
            switch (step)
            {
                //first part is [x]
                case 0:
                    if (c == '[')
                    {
                        continue;
                    }
                    if (c != ']')
                    {
                        tempBuffer += c;
                    }
                    if (c == ']')
                    {
                        firstSomething = tempBuffer;
                        tempBuffer = String.Empty;
                        step++;
                    }
                    break;
                case 1:
                    if (c != '.')
                    {
                        tempBuffer += c;
                        break;
                    }
                    if (c == '.')
                    {
                        hexPid = tempBuffer;
                        tempBuffer = String.Empty;
                        step++;
                    }
                    break;
                case 2:
                    if (c != ':')
                    {
                        tempBuffer += c;
                        break;
                    }
                    if (c == ':')
                    {
                        //We do this because there are two :: and next sequence expects :
                        if (tempBuffer.Length == 0)
                        {
                            step++;
                        }
                        else
                        {
                            hexTid = tempBuffer;
                            tempBuffer = String.Empty;
                        }
                    }
                    break;
                case 3:
                    if (c != ' ')
                    {
                        tempBuffer += c;
                        break;
                    }
                    if (c == ' ')
                    {
                        datetime = tempBuffer;
                        tempBuffer = String.Empty;
                        step++;
                    }
                    break;
                case 4:
                    if (c == '[')
                    {
                        continue;
                    }
                    if (c != ']')
                    {
                        tempBuffer += c;
                        break;
                    }
                    provider = tempBuffer;
                    tempBuffer = String.Empty;
                    step++;
                    break;
                case 5:
                    //this is a json blob, just grab it for now, it's expensive to process
                    tempBuffer += c;



                    break;
                default:
                    //nothing yet, finish it out
                    break;
            }
        }
        //finish it out
        json = tempBuffer;
        tempBuffer = String.Empty;
        //step++;
    }
    public Level GetLevel()
    {
        // TraceEvent-sourced rows (live capture + TraceLogging/manifest events from .etl) carry the
        // severity directly on the event. Use it first — the keyjson "meta.level" below only exists on
        // the WPP/tracefmt text path. ETW level scheme: 1=Critical,2=Error,3=Warning,4=Info,5=Verbose.
        if (eventLevel >= 0)
        {
            return eventLevel switch
            {
                1 => Level.Catastrophic,
                2 => Level.Error,
                3 => Level.Warning,
                4 => Level.Info,
                5 => Level.Verbose,
                _ => Level.Info, // 0 = LogAlways / unknown
            };
        }
        // Try to extract from JSON if available
        if (keyjson != null && keyjson.ContainsKey("meta"))
        {
            try
            {
                var meta = keyjson["meta"];
                if (meta is Newtonsoft.Json.Linq.JObject metaObj && metaObj["level"] != null)
                {
                    var levelStr = metaObj["level"]?.ToString(); // Use null conditional operator to avoid null dereference
                    if (!string.IsNullOrEmpty(levelStr) && int.TryParse(levelStr, out var levelInt))
                    {
                        return levelInt switch
                        {
                            1 => Level.Catastrophic,
                            2 => Level.Error,
                            3 => Level.Warning,
                            4 => Level.Info,
                            5 => Level.Verbose,
                            _ => Level.Info
                        };
                    }
                    // Try string mapping
                    return levelStr?.ToLower() switch
                    {
                        "catastrophic" => Level.Catastrophic,
                        "error" => Level.Error,
                        "warning" => Level.Warning,
                        "info" => Level.Info,
                        "verbose" => Level.Verbose,
                        _ => Level.Info
                    };
                }
            }
            catch
            {
                // Ignore and fall through
            }
        }
        // Fallback: try to parse from eventtxt or json if possible
        return Level.Info;
    }
    public DateTime GetLogTime()
    {
        // Precise time set by the TraceEvent ctor (or json meta) — return it directly and skip the
        // lossy tracefmt-format string parse below.
        if (metadatetime != DateTime.MinValue)
        {
            return metadatetime;
        }

        if (parsedTime == DateTime.MinValue)
        {
            // Be defensive: a line with no/blank or unparseable time (e.g. an unformatted "Decode
            // anyway" row, or a badly-formatted event) must not throw here — that exception would be
            // unhandled on the streaming thread and take the whole app down. Fall back to MinValue.
            if (!DateTime.TryParse(datetime?.Replace("-", " "), out parsedTime))
                return DateTime.MinValue;
        }
        return parsedTime;
    }

    public void PreLoad()
    {
        // TraceEvent-decoded lines populate eventtxt/tasktxt/provider/time in the constructor and
        // leave json empty — there's nothing to parse, and the (json-assuming) logic below would
        // wipe the message. Only the tracefmt/json path carries a json payload to expand here.
        if (string.IsNullOrEmpty(json))
        {
            return;
        }
        if (!json.StartsWith("{"))
        {
            //This is likely not json and just plaintext
            eventtxt = json;
            tasktxt = string.Empty;
            metadatetime = DateTime.MinValue;
            metaprovider = string.Empty;
            return;
        }
        //Parse the json early
        try
        {
            var deserialized = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);
            if (deserialized != null)
            {
                keyjson = deserialized;
            }
            else
            {
                throw new Exception("Deserialization resulted in null");
            }

            if (keyjson.TryGetValue("meta", out var metaObj) && metaObj is Newtonsoft.Json.Linq.JObject meta)
            {
                metaprovider = meta["provider"]?.ToString() ?? string.Empty;
                var metaTime = meta["time"]?.ToString();
                if (!string.IsNullOrEmpty(metaTime))
                {
                    DateTime.TryParse(metaTime, out metadatetime);
                }
                var eventVal = meta["event"]?.ToString();
                var taskVal = meta["task"]?.ToString();
                if (!string.IsNullOrEmpty(taskVal))
                {
                    eventtxt = eventVal ?? string.Empty;
                    tasktxt = taskVal;
                }
                else
                {
                    tasktxt = eventVal ?? string.Empty;
                    try
                    {
                        eventtxt = "{";
                        foreach (var key in keyjson.Keys)
                        {
                            if (key.Equals("meta"))
                            {
                                continue; //We dont need it
                            }
                            eventtxt += $"\"{key}\": \"{keyjson[key]}\", ";
                        }
                        if (eventtxt.Length > 1)
                        {
                            eventtxt = eventtxt.Substring(0, eventtxt.Length - 2); //remove last part
                        }
                        eventtxt += "}";
                    }
                    catch (Exception)
                    {
                        //Not all of them have this, throw the rest in there;
                        eventtxt = "Error?" + json;
                    }
                }
            }
            else
            {
                eventtxt = json;
                tasktxt = "Badly formatted event";
                metadatetime = DateTime.MinValue;
                metaprovider = string.Empty;
            }
        }
        catch (Exception)
        {
            eventtxt = json;
            tasktxt = "Badly formatted event";
            metadatetime = DateTime.MinValue;
            metaprovider = string.Empty;
            return;
        }
    }

    public string GetMachineName()
    {
        return filename;
    }
    public string GetMessage()
    {
        // The TraceEvent (live) path fills eventtxt; the tracefmt (file) path leaves it empty and puts
        // the decoded line in 'json' — fall back to that so file-based ETL rows aren't blank.
        return string.IsNullOrEmpty(eventtxt) ? json : eventtxt;
    }
    public string GetOpCode() => opcodeName;
    // ETW stores PID/TID as hex (both the tracefmt text and the TraceEvent path); surface them as
    // decimal to match what users expect. The rest come from the TraceEvent path (empty for tracefmt
    // text, which doesn't carry them). ETW has no per-record id, so RecordId stays "".
    public string GetProcessId() => HexToDecimal(hexPid);
    public string GetThreadId() => HexToDecimal(hexTid);
    public string GetActivityId() => activityId;
    public string GetEventId() => eventId;
    public string GetKeywords() => keywords;
    public string GetRelatedActivityId() => relatedActivityId;
    public string GetProviderGuid() => providerGuid;
    public string GetProcessName() => processName;
    public string GetStructuredData() => structuredData;

    private static string HexToDecimal(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "";
        try { return Convert.ToInt64(hex, 16).ToString(System.Globalization.CultureInfo.InvariantCulture); }
        catch { return hex; }
    }
    public string GetSearchableData()
    {
        return json;
    }
    public string GetSource()
    {
        if (metaprovider.Equals(string.Empty))
        {
            return provider;
        }
        else
        {
            return metaprovider;
        }
    }
    public string GetTaskName()
    {
        return tasktxt;
    }
    public string GetUsername()
    {
        return "empty";
    }
    public void WriteToConsole()
    {
        // Not implemented: Console output not supported for ETLLogLine.
        // Optionally, implement if needed.
    }
}