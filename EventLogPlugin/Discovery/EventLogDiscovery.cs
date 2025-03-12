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
            ret.Add(GetDisplayName(session, name));
        }
        ret.Sort();
        return ret;
    }
    public static string GetDisplayName(EventLogSession session, string logName)
    {
        var sb = new StringBuilder(512);
        var sessionHandle = GetSessionHandle(session);
        if (sessionHandle != null && EvtIntGetClassicLogDisplayName(sessionHandle.DangerousGetHandle(), logName, 0, 0, sb.Capacity, sb, out _))
            return sb.ToString();

        return logName;
    }

    private static SafeHandle? GetSessionHandle(EventLogSession session)
    {
        var handleProperty = session.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
        if (handleProperty == null)
        {
            return null;
        }
        return handleProperty.GetValue(session) as SafeHandle;
    }

    [DllImport("wevtapi.dll", CharSet = CharSet.Unicode)]
    private static extern bool EvtIntGetClassicLogDisplayName(IntPtr session, [MarshalAs(UnmanagedType.LPWStr)] string logName, int locale, int flags, int bufferSize, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder displayName, out int bufferUsed);
}
