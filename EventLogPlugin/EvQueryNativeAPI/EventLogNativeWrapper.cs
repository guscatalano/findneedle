using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using findneedle;

namespace EventLogPlugin.EvQueryNativeAPI;
public class EventLogNativeWrapper
{

    public static List<ISearchResult> GetEventsAsResults(string logName, string query)
    {
        List<ISearchResult> ret = new();

        var xmls = GetEvent(logName, query);
        foreach(var xml in xmls)
        {
            //Console.WriteLine(xml);
            var result = new EventLogNativeResult(xml, logName, query);
            ret.Add(result);
        }
        return ret;
    }

    public static List<string> GetEvent(string logName, string query)
    {
        List<string> ret = new();
        // Define the log name and query
        //string logName = "Application";
        //string query = "*"; // Query all events

        // Call EvtQuery
        var queryHandle = EventLogNativeMethods.EvtQuery(
            IntPtr.Zero, // Local session
            logName,
            query,
            EventLogNativeMethods.EvtQueryFlags.EvtQueryChannelPath | EventLogNativeMethods.EvtQueryFlags.EvtQueryForwardDirection
        );

        if (queryHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Exception($"Failed to query event log. Error: {error}");
        }


        try
        {
            // Prepare to retrieve events
            const int eventArraySize = 10; // Number of events to retrieve at a time
            var eventHandles = new IntPtr[eventArraySize];
            var returned = 0;

            while (EventLogNativeMethods.EvtNext(queryHandle, eventArraySize, eventHandles, 10000, 0, ref returned))
            {
                for (var i = 0; i < returned; i++)
                {
                    // Process each event handle
                    var eventHandle = eventHandles[i];
                    Console.WriteLine($"Retrieved event handle: {eventHandle}");

                    // Extract data from the event handle
                    var eventXml = RenderEventAsXml(eventHandle);
                    //Console.WriteLine($"Event Data: {eventXml}");
                    if(eventXml != null)
                    {
                        ret.Add(eventXml);
                    }

                    // Close the event handle after processing
                    EventLogNativeMethods.EvtClose(eventHandle);
                }
            }

            // Check for errors after the loop
            var error = Marshal.GetLastWin32Error();
            if (error != 0 && error != 259) // 259 = ERROR_NO_MORE_ITEMS
            {
                throw new Exception($"Failed to retrieve events. Error: {error}");
            }
        }
        finally
        {
            // Close the query handle
            EventLogNativeMethods.EvtClose(queryHandle);
        }
        return ret;
    }


    private static string RenderEventAsXml(IntPtr eventHandle)
    {
        const int initialBufferSize = 4096;
        var buffer = Marshal.AllocHGlobal(initialBufferSize);

        try
        {
            int bufferUsed;
            int propertyCount;

            // Call EvtRender to render the event as XML
            if (!EventLogNativeMethods.EvtRender(
                IntPtr.Zero,
                eventHandle,
                EventLogNativeMethods.EvtRenderFlags.EvtRenderEventXml,
                initialBufferSize,
                buffer,
                out bufferUsed,
                out propertyCount))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Exception($"Failed to render event. Error: {error}");
            }

            // Convert the buffer to a string
            return Marshal.PtrToStringUni(buffer, bufferUsed / 2); // Divide by 2 because it's Unicode
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
