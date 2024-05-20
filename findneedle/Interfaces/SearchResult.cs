using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle
{

    public enum Level
    {
        Catastrophic,
        Error,
        Warning,
        Info,
        Verbose
    }

    public interface SearchResult
    {

        public const string NOT_SUPPORTED = "!NOT_SUPPORTED!"; //Use this in a search location where the request makes no sense, or throw.
        public DateTime GetLogTime();
        public string GetMachineName();
        public void WriteToConsole();
        public Level GetLevel();

        public string GetUsername();

        public string GetTaskName();
        public string GetOpCode();
        public string GetSource();

        public string GetSearchableData();
        public string GetMessage();

        public string GetResultSource();
    }

}
