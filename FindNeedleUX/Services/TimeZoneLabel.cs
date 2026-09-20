using System;
using System.Collections.Generic;

namespace FindNeedleUX.Services;

/// <summary>
/// What zone the timestamps in a result set are in - and, crucially, whether that is knowable.
///
/// ETW events arrive in UTC, Event Log records in local time, and a plain text log carries whatever
/// its writer printed with no zone at all. The viewer renders every one of them the same way, so a
/// nine-o'clock row in one log and a nine-o'clock row in another may be an hour apart with nothing on
/// screen saying so. This turns the rows' <see cref="DateTimeKind"/> into a label the Time column can
/// wear, and refuses to guess: unspecified stays "as recorded", and a set carrying both kinds says
/// "mixed" rather than picking one.
/// </summary>
public static class TimeZoneLabel
{
    public const string Utc = "UTC";
    public const string Local = "local";
    public const string Mixed = "mixed zones";
    public const string AsRecorded = "as recorded";

    /// <summary>The label for a sample of rows' timestamps (the visible page is a fine sample).</summary>
    public static string For(IEnumerable<DateTime> sample)
    {
        bool utc = false, local = false, unspecified = false;
        foreach (var t in sample ?? Array.Empty<DateTime>())
        {
            if (t == default) continue; // no timestamp at all: says nothing about the zone
            switch (t.Kind)
            {
                case DateTimeKind.Utc: utc = true; break;
                case DateTimeKind.Local: local = true; break;
                default: unspecified = true; break;
            }
        }
        if (utc && !local && !unspecified) return Utc;
        if (local && !utc && !unspecified) return Local;
        if (!utc && !local) return AsRecorded;  // all unspecified, or nothing sampled
        return Mixed;                           // two sources disagreeing is worth saying out loud
    }

    /// <summary>The Time column's header for a given label - "Time (UTC)", or plain "Time" when the
    /// data says nothing (a marker that reads "as recorded" on every text log is just noise).</summary>
    public static string Header(string label)
        => string.IsNullOrEmpty(label) || label == AsRecorded ? "Time" : $"Time ({label})";

    /// <summary>One row's timestamp spelled out for the detail view, zone included.</summary>
    public static string Describe(DateTime time)
    {
        if (time == default) return "";
        return time.Kind switch
        {
            DateTimeKind.Utc => $"{time:yyyy-MM-dd HH:mm:ss.fffffff} UTC",
            DateTimeKind.Local => $"{time:yyyy-MM-dd HH:mm:ss.fffffff} local ({TimeZoneInfo.Local.StandardName})",
            _ => $"{time:yyyy-MM-dd HH:mm:ss.fffffff} (no zone recorded)",
        };
    }
}
