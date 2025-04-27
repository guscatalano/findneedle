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
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Implementations.FileExtensions;
public class ETLProcessor : IFileExtensionProcessor, IPluginDescription
{
    public TraceFmtResult currentResult
    {
        get; private set; 
    }

    public Dictionary<string, int> providers = new();

    public bool LoadEarly = true;
    private readonly string tempPath = "";

    public string inputfile = "";
    public ETLProcessor()
    {
        currentResult = new TraceFmtResult(); //empty
        tempPath = TempStorage.GetNewTempPath("etl");
    }

    public void Dispose()
    {
        TempStorage.DeleteSomeTempPath(tempPath);
    }

    public void OpenFile(string fileName)
    {
        inputfile = fileName;
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

        currentResult = TraceFmt.ParseSimpleETL(inputfile, tempPath);
        while (getLock > 0)
        {
            try
            {
                if (currentResult.outputfile == null)
                {
                    throw new InvalidOperationException("Output file is not set.");
                }
                using var fileStream = File.OpenRead(currentResult.outputfile);
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
                    ETLLogLine etlline = new ETLLogLine(line, inputfile);
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

    readonly List<ISearchResult> results = new();
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

    public List<ISearchResult> GetResults()
    {
        return results;
    }

    public List<string> RegisterForExtensions()
    {
        return new List<string>() { ".etl", ".txt" };
    }

    public bool CheckFileFormat()
    {
        if (inputfile.EndsWith(".txt")) {
            var firstline = File.ReadLines(inputfile).Take(1).ToList().First();
            if (ETLLogLine.DoesHeaderLookRight(firstline))
            {
                return true;
            } else {
                return false;
            }
        }
        return true;
    }

    public string GetPluginTextDescription() {
        return "Parses ETL and formatted ETL files";
    }
    public string GetPluginFriendlyName()
    {
        return "ETLProcessor";
    }
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }
}
