﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations.Locations.EventLogQueryLocation;

namespace findneedle.Implementations.Locations.EventLogQueryLocation;

public class EventLogResult : SearchResult
{
    readonly EventRecord entry;
    readonly IEventLogQueryLocation location;
    readonly string eventdata = "";
    readonly string systemdata = "";
    public EventLogResult(EventRecord entry, IEventLogQueryLocation location)
    {
        this.entry = entry;
        this.location = location;

        if (location.GetSearchDepth() != SearchLocationDepth.Shallow)
        {
            var doc = entry.ToXml();

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
        }
    }


    public Level GetLevel()
    {
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
        throw new NotImplementedException();
    }

    public string GetSource()
    {
        return entry.ProviderName;
    }

    public string GetTaskName()
    {
        return entry.TaskDisplayName;
    }

    public string GetUsername()
    {

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

    public string GetMessage()
    {
        return eventdata;
    }

    //This is usually the message, but it can be more. This is likely not readable
    public string GetSearchableData()
    {

        return string.Join(' ', eventdata, systemdata);
    }

    public void WriteToConsole()
    {
        Console.WriteLine(eventdata);
    }
}


