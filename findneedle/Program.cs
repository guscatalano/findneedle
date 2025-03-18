// See https://aka.ms/new-console-template for more information
using findneedle;
using findneedle.Implementations;
using findneedle.Interfaces;
using FindNeedleCoreUtils;
using FindPluginCore.Searching;



var x = SearchQueryCmdLine.ParseFromCommandLine(Environment.GetCommandLineArgs());
Console.WriteLine("Searching...");
x.LoadAllLocationsInMemory();
//var y = x.GetFilteredResults();
//OutputToPlainFile output = new OutputToPlainFile();
//NullOutput output2 = new NullOutput(); ;
//output.WriteAllOutput(y);
//Console.WriteLine("Done output written to: " + output2.GetOutputFileName());
x.GetSearchStatsOutput();

//IResultProcessor p = new WatsonCrashProcessor();
//p.ProcessResults(y);
//p.GetOutputFile();


