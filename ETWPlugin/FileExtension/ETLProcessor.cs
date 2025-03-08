using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using findneedle.Interfaces;
using FindNeedleCoreUtils;
using findneedle.WDK;
using Newtonsoft.Json;

namespace findneedle.Implementations.FileExtensions;
public class ETLProcessor : IFileExtensionProcessor
{
    public TraceFmtResult currentResult
    {
        get; private set; 
    }

    public Dictionary<string, int> providers = new();

    public bool LoadEarly = true; 

    public string inputfile = "";
    public ETLProcessor(string file)
    {
        inputfile = file;
        currentResult = new TraceFmtResult(); //empty

    }

    public Dictionary<string, int> GetProviderCount()
    {
        return providers;
    }

    public string GetFileName()
    {
        return inputfile; 
    }

   
    public void DoPreProcessing()
    {
        var getLock = 50;
        currentResult = TraceFmt.ParseSimpleETL(inputfile, TempStorage.GetNewTempPath("etl"));
        while (getLock > 0)
        {
            try
            {

#pragma warning disable CS8604 // Possible null reference argument.
                using var fileStream = File.OpenRead(currentResult.outputfile);
#pragma warning restore CS8604 // Possible null reference argument.
                using var streamReader = new StreamReader(fileStream, Encoding.UTF8, false); //change buffer if there's perf reasons

                string? line;

                while ((line = streamReader.ReadLine()) != null)
                {
                    var failsafe = 10;
                    while (!ETLLogLine.DoesHeaderLookRight(line) && failsafe > 0)
                    {
                        if (line.StartsWith("Unknown"))
                        {
                            failsafe = 0; //This is corrupted, let's just bail;
                            continue;
                        }
                        //line is not complete!
                        failsafe--;
                        line += streamReader.ReadLine();
                    }
                    if (failsafe == 0)
                    {
                        continue; // Don't throw or we skip too much!
                    }
                    ETLLogLine etlline = new ETLLogLine(line, inputfile, (char)streamReader.Peek());
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
                break;
            }
            catch (Exception)
            {
                Thread.Sleep(100);
                getLock--; // Sometimes tracefmt can hold the lock, wait until file is ready
            }
        }

    }

    readonly List<SearchResult> results = new();
    public void LoadInMemory() 
    {
        if (LoadEarly)
        {
            foreach(ETLLogLine result in results)
            {
                result.PreLoad();
            }
        }
        
    }

    public List<SearchResult> GetResults()
    {
        return results;
    }

    public List<string> RegisterForExtensions()
    {
        return new List<string>() { ".etl" };
    }
}

public class ETLLogLine : SearchResult
{
    public string eventtxt = String.Empty;
    public string tasktxt = String.Empty;
    public DateTime metadatetime = DateTime.MinValue;
    public string metaprovider = string.Empty; //comes from json, not header
    public string provider = String.Empty;
    public string tempBuffer = String.Empty;
    public string firstSomething = String.Empty; //dont know what this is
    public string hexPid = String.Empty;
    public string hexTid = String.Empty;
    public string datetime = String.Empty;
    public DateTime parsedTime = DateTime.MinValue;
    public string json = String.Empty;
    public Dictionary<string, dynamic> keyjson = new();
    public string filename = String.Empty;

    public string originalLine = string.Empty;


    public string GetResultSource()
    {
        return "ETW: " + filename;
    }
    public static bool DoesHeaderLookRight(string textline)
    {
        var step = 0;
        var seenColon = false;
        foreach (var c in textline)
        {
            //we do it this way cause it's more performant than split
            switch (step)
            {
                //first part is [x]
                case 0:
                    if (c == ']')
                    {
                        step++;
                    }
                    break;
                case 1:
                    if (c == '.')
                    {
                        step++;
                    }
                    break;
                case 2:
                    if (c != ':')
                    {
                        break;
                    }
                    if (c == ':')
                    {
                        //We do this because there are two :: and next sequence expects :
                        if (seenColon)
                        {
                            step++;
                        }
                        else
                        {
                            seenColon = true;
                        }
                    }
                    break;
                case 3:
                    if (c == ' ')
                    {
                        step++;
                    }
                    break;
                case 4:
                    if (c == ']')
                    {
                        return true;
                    }
                    break;
                default:
                    break;
            }
        }
        return false;           
    }

    private readonly string nextline = string.Empty;
    public ETLLogLine(string textline, string filename, char nextline)
    {
        originalLine = textline;
        this.nextline = nextline + ""; 
        this.filename = filename;

        var step = 0;

        foreach (var c in textline)
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
                    
                      
                    
                    break;
                default:
                    //nothing yet, finish it out
                    break;
            } 
        }
        //finish it out
        json = tempBuffer;
        tempBuffer = String.Empty;
        //step++;
    }
    public Level GetLevel() => throw new NotImplementedException();
    public DateTime GetLogTime() {
        if (parsedTime == DateTime.MinValue)
        {
            datetime = datetime.Replace("-", " ");
            parsedTime = DateTime.Parse(datetime);
        }

        //If we have the metadata one, return that one.
        if(metadatetime != DateTime.MinValue)
        {
            return metadatetime;
        }
        return parsedTime;
    }

    public void PreLoad()
    {
        if (!json.StartsWith("{"))
        {
            //This is likely not json and just plaintext
            eventtxt = json;
            tasktxt = string.Empty;
            metadatetime = DateTime.MinValue;
            metaprovider = string.Empty;
            return;
        }
        //Parse the json early
        try
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8601 // Possible null reference assignment.
            keyjson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(json);
#pragma warning restore CS8601 // Possible null reference assignment.


            metaprovider = keyjson["meta"]["provider"];
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            metadatetime = DateTime.Parse((string)keyjson["meta"]["time"]);
            
            if (!String.IsNullOrEmpty(keyjson["meta"]["task"]))
            {
                eventtxt = keyjson["meta"]["event"];
                tasktxt = keyjson["meta"]["task"];
            } else
            {
                tasktxt = keyjson["meta"]["event"];
                try
                {
                    eventtxt = "{";
                    foreach (var key in keyjson.Keys)
                    {
                        if (key.Equals("meta"))
                        {
                            continue; //We dont need it
                        }
                        
                        eventtxt += "\"" + key + "\": \"" + keyjson[key] + "\", ";
                    }
                    if (eventtxt.Length > 1)
                    {
                        eventtxt = eventtxt.Substring(0, eventtxt.Length - 2); //remove last part
                    }
                    eventtxt += "}";
                    
                } catch(Exception)
                {
                    //Not all of them have this, throw the rest in there;
                    eventtxt = "Error?" + json;
                }
            }

        }
        catch (Exception)
        {
            eventtxt = json;
            tasktxt = "Badly formatted event";
            metadatetime = DateTime.MinValue;
            metaprovider = string.Empty;
            return;
        }
    }

    public string GetMachineName()
    {
        return filename;
    }
    public string GetMessage()  {
        return eventtxt.ToString();
    }
    public string GetOpCode() => throw new NotImplementedException();
    public string GetSearchableData()
    {
        return json;
    }
    public string GetSource() {
        if (metaprovider.Equals(string.Empty))
        {
            return provider;
        } else
        {
            return metaprovider;
        }
    }
    public string GetTaskName() {
        return tasktxt;
    }
    public string GetUsername() {
        return "empty";
    }
    public void WriteToConsole() => throw new NotImplementedException();
}