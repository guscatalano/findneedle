using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations.Discovery;
public class EventLogDiscovery
{
    public static List<string> GetAllEventLogs()
    {
        List<string> ret = new();
        var session = new EventLogSession();
        foreach (string name in session.GetLogNames())
        {
            //ret.Add(name, GetDisplayName(session, name));
            ret.Add(GetDisplayName(session,name));
        }
        ret.Sort();
        return ret;

    }
    public static string GetDisplayName(EventLogSession session, string logName)
    {
        var sb = new StringBuilder(512);
        int bufferUsed = 0;
        if (EvtIntGetClassicLogDisplayName(GetSessionHandle(session).DangerousGetHandle(), logName, 0, 0, sb.Capacity, sb, out bufferUsed))
            return sb.ToString();

        return logName;
    }

    private static SafeHandle GetSessionHandle(EventLogSession session)
    {
        return (SafeHandle)session.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
    }

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool EvtIntGetClassicLogDisplayName(IntPtr session, [MarshalAs(UnmanagedType.LPWStr)] string logName, int locale, int flags, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder displayName, out int bufferUsed);
}
