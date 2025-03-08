using System.Diagnostics.Eventing.Reader;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace findneedle.Implementations.Discovery;
public class EventLogDiscovery
{
    public static List<string> GetAllEventLogs()
    {
        List<string> ret = new();
        var session = new EventLogSession();
        foreach (var name in session.GetLogNames())
        {
            //ret.Add(name, GetDisplayName(session, name));
            ret.Add(GetDisplayName(session, name));
        }
        ret.Sort();
        return ret;

    }
    public static string GetDisplayName(EventLogSession session, string logName)
    {
        var sb = new StringBuilder(512);
        if (EvtIntGetClassicLogDisplayName(GetSessionHandle(session).DangerousGetHandle(), logName, 0, 0, sb.Capacity, sb, out _))
            return sb.ToString();

        return logName;
    }

    private static SafeHandle GetSessionHandle(EventLogSession session)
    {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning disable CS8603 // Possible null reference return.
        return (SafeHandle)session.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
#pragma warning restore CS8603 // Possible null reference return.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
#pragma warning restore CS8602 // Dereference of a possibly null reference.
    }

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool EvtIntGetClassicLogDisplayName(IntPtr session, [MarshalAs(UnmanagedType.LPWStr)] string logName, int locale, int flags, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder displayName, out int bufferUsed);
}
