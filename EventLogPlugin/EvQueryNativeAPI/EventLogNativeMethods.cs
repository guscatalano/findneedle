using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace EventLogPlugin.EvQueryNativeAPI;

public static class EventLogNativeMethods
{
    // Define the EvtQueryFlags enumeration
    [Flags]
    public enum EvtQueryFlags : uint
    {
        EvtQueryChannelPath = 0x1,
        EvtQueryFilePath = 0x2,
        EvtQueryForwardDirection = 0x100,
        EvtQueryReverseDirection = 0x200,
        EvtQueryTolerateQueryErrors = 0x1000
    }

    // Import the EvtQuery function
    [DllImport("wevtapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr EvtQuery(
        IntPtr session,
        string path,
        string query,
        EvtQueryFlags flags
    );

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtNext(
       IntPtr queryHandle,
       int eventArraySize,
       [Out] IntPtr[] eventHandles,
       int timeout,
       int flags,
       ref int returned);

    // Import the EvtClose function to close handles
    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EvtClose(IntPtr handle);

    public enum EvtRenderFlags : uint
    {
        EvtRenderEventValues = 0, // Render the event as an array of property values
        EvtRenderEventXml = 1,    // Render the event as an XML string
        EvtRenderBookmark = 2     // Render the bookmark as an XML string
    }

    [DllImport("wevtapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EvtRender(
        IntPtr context,
        IntPtr eventHandle,
        EvtRenderFlags flags,
        int bufferSize,
        IntPtr buffer,
        out int bufferUsed,
        out int propertyCount
    );
}