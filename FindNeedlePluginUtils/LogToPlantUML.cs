using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils;
public class LogToPlantUML
{

    public struct KeyPoint
    {
        public string textToMatch;
        public string? textToUnmatch;
        public ISearchResult? matchedLine;
        public string? umlText;
        public Func<UmlGenerationParams, string>? umlTextDelegateComplex;
    }

    public struct ProcessLifeTime
    {
        public int processID;
        public DateTime? startTime;
        public DateTime? endTime;
    }

    public class ProcessLifeTimeTracker
    {
        public string processName;
        public Dictionary<int, ProcessLifeTime> lives = new();


        public ProcessLifeTimeTracker(string processName)
        {
            this.processName = processName;
        }

        public string GetUMLAtTime(DateTime currentLogTime, DateTime lastLogTime)
        {
            var ret = "";
            foreach (var life in lives)
            {
                if (life.Value.startTime != null && life.Value.endTime != null)
                {
                    //The last time we did not get it, and now we passed it.
                    if (lastLogTime < life.Value.startTime && currentLogTime > life.Value.startTime)
                    {
                        ret += "== " + this.processName + " first seen pid: " + life.Value.processID + " == " + Environment.NewLine;
                    }

                    if (lastLogTime <= life.Value.endTime && currentLogTime > life.Value.endTime)
                    {
                        ret += "== " + this.processName + " last seen pid: " + life.Value.processID + " == " + Environment.NewLine;
                    }
                }
            }
            return ret;
        }


        public void NewEvent(DateTime time, int pid)
        {
            if (lives.ContainsKey(pid))
            {
                // Create a copy of the struct, modify it, and then assign it back to the dictionary
                var processLifeTime = lives[pid];

                if (processLifeTime.startTime > time)
                {
                    processLifeTime.startTime = time;
                }
                if (processLifeTime.endTime < time)
                {
                    processLifeTime.endTime = time;
                }

                lives[pid] = processLifeTime; // Assign the modified struct back to the dictionary
            }
            else
            {
                lives.Add(pid, new ProcessLifeTime()
                {
                    processID = pid,
                    startTime = time,
                    endTime = time
                });
            }
        }
    }

    public struct UmlGenerationParams
    {
        public string msg;
        public string matchedText;
        public bool includeTime;
    }

}
