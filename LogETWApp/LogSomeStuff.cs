using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LogETWApp
{
    public class LogSomeStuff
    {
        private static readonly EventSource log = new("LogETWApp");
        public static void LogFor10Seconds()
        {
            Console.WriteLine("logging for 10 seconds...");
            log.Write("Starting logging for 10 seconds", new { time = DateTime.Now });
            for (var i = 0; i < 10; i++)
            {
                ExampleStructuredData EventData = new ExampleStructuredData() { TransactionID = i, TransactionDate = DateTime.Now };
                log.Write("Sending some data", EventData);
                log.Write("Sleeping for a second");
                Task.Delay(1000).Wait();
            }
            log.Write("Done", new { time = DateTime.Now });
            Console.WriteLine("Done logging");
        }

        [EventData] // [EventData] makes it possible to pass an instance of the class as an argument to EventSource.Write()
        public sealed class ExampleStructuredData
        {
            public int TransactionID
            {
                get; set;
            }
            public DateTime TransactionDate
            {
                get; set;
            }
        }
    }
}
