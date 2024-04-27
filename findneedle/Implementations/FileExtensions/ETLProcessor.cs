using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using findneedle.Interfaces;
using findneedle.Utils;
using findneedle.WDK;

namespace findneedle.Implementations.FileExtensions;
public class ETLProcessor : FileExtensionProcessor
{
    public TraceFmtResult currentResult
    {
        get; private set; 
    }

    public Dictionary<string, int> providers = new();

    public string inputfile = "";
    public ETLProcessor(string file)
    {
        inputfile = file;

    }

   
    public void DoPreProcessing()
    {
        int getLock = 10;
        currentResult = TraceFmt.ParseSimpleETL(inputfile, TempStorage.GetNewTempPath("etl"));
        while (getLock > 0)
        {
            try
            {

                using (var fileStream = File.OpenRead(currentResult.outputfile))
                {
                    using (var streamReader = new StreamReader(fileStream, Encoding.UTF8, false)) //change buffer if there's perf reasons
                    {

                        String line;
                        while ((line = streamReader.ReadLine()) != null)
                        {
                            ETLLogLine etlline = new ETLLogLine(line);
                            if (providers.ContainsKey(etlline.GetSource()))
                            {
                                providers[etlline.GetSource()]++;
                            }
                            else
                            {
                                providers[etlline.GetSource()] = 1;
                            }
                            results.Add(etlline);
                        }
                    }
                }
                break;
            }
            catch (Exception ex)
            {
                Thread.Sleep(100);
                getLock--; // Sometimes tracefmt can hold the lock
            }
        }

    }

    List<SearchResult> results = new List<SearchResult>();
    public void LoadInMemory() 
    {

        
    }

    public List<SearchResult> GetResults()
    {
        return results;
    }
}

public class ETLLogLine : SearchResult
{
    public string provider = String.Empty;
    public string tempBuffer = String.Empty;
    public string firstSomething = String.Empty; //dont know what this is
    public string hexPid = String.Empty;
    public string hexTid = String.Empty;
    public string datetime = String.Empty;
    public DateTime parsedTime = DateTime.MinValue;
    public string json = String.Empty;
    public ETLLogLine(string textline)
    {


        int step = 0;

        foreach (char c in textline)
        {
            //we do it this way cause it's more performant than split
            switch (step)
            {
                //first part is [x]
                case 0:
                    if (c == '[')
                    {
                        continue;
                    }
                    if (c != ']')
                    {
                        tempBuffer += c;
                    }
                    if (c == ']')
                    {
                        firstSomething = tempBuffer;
                        tempBuffer = String.Empty;
                        step++;
                    }
                    break;
                case 1:
                    if (c != '.')
                    {
                        tempBuffer += c;
                        break;
                    }
                    if (c == '.')
                    {
                        hexPid = tempBuffer;
                        tempBuffer = String.Empty;
                        step++;
                    }
                    break;
                case 2:
                    if (c != ':')
                    {
                        tempBuffer += c;
                        break;
                    }
                    if (c == ':')
                    {
                        //We do this because there are two :: and next sequence expects :
                        if (tempBuffer.Length == 0)
                        {
                            step++;
                        } else
                        {
                            hexTid = tempBuffer;
                            tempBuffer = String.Empty;
                        }
                    }
                    break;
                case 3:
                    if (c != ' ')
                    {
                        tempBuffer += c;
                        break;
                    }
                    if (c == ' ')
                    {
                        datetime = tempBuffer;
                        tempBuffer = String.Empty;
                        step++;
                    }
                    break;
                case 4:
                    if (c == '[')
                    {
                        continue;
                    }
                    if (c != ']')
                    {
                        tempBuffer += c;
                        break;
                    }
                    provider = tempBuffer;
                    tempBuffer = String.Empty;
                    step++;
                    break;
                case 5:
                    //this is a json blob, just grab it for now, it's expensive to process
                    tempBuffer += c;
                    if (c == '}')
                    {
                        step++;
                    }
                    break;
                default:
                    //nothing yet, finish it out
                    break;
            }

        }
    }
    public Level GetLevel() => throw new NotImplementedException();
    public DateTime GetLogTime() {
        if (parsedTime == DateTime.MinValue)
        {
            parsedTime = DateTime.Parse(datetime);
        }
        return parsedTime;
    }
    public string GetMachineName() => throw new NotImplementedException();
    public string GetMessage()  { return ":(";}
    public string GetOpCode() => throw new NotImplementedException();
    public string GetSearchableData() => throw new NotImplementedException();
    public string GetSource() {
        return provider;
    }
    public string GetTaskName() => throw new NotImplementedException();
    public string GetUsername() => throw new NotImplementedException();
    public void WriteToConsole() => throw new NotImplementedException();
}