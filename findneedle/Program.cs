// See https://aka.ms/new-console-template for more information
using findneedle;
using findneedle.Implementations;
using findneedle.Implementations.Outputs;
using findneedle.Implementations.ResultProcessors;
using findneedle.Interfaces;
using findneedle.WDK;
using System.Diagnostics;

WDKFinder.GetPathOfWDK();

var arguments = new Dictionary<string, string>();

foreach (string argument in Environment.GetCommandLineArgs())
{
    int count = 0;
    string[] splitted = argument.Split('=');

    if (splitted.Length == 2)
    {
        arguments[splitted[0]+count] = splitted[1];
    }
}

var x = new SearchQuery(arguments);
Console.WriteLine("Searching...");
x.LoadAllLocationsInMemory();
var y = x.GetFilteredResults();
OutputToPlainFile output = new OutputToPlainFile();
NullOutput output2 = new NullOutput(); ;
output.WriteAllOutput(y);
Console.WriteLine("Done output written to: " + output2.GetOutputFileName());
x.GetSearchStatsOutput();

ResultProcessor p = new WatsonCrashProcessor();
p.ProcessResults(y);
p.GetOutputFile();


