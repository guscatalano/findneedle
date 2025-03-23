// See https://aka.ms/new-console-template for more information
using findneedle;
using findneedle.Implementations;
using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindPluginCore.Searching;

var cancel = false;
Console.CancelKeyPress += delegate {
    cancel = true;
    Console.WriteLine("Cancel received, exiting");
    Environment.Exit(0);
};


var x = SearchQueryCmdLine.ParseFromCommandLine(Environment.GetCommandLineArgs());
SearchQueryCmdLine.PrintToConsole(x);
PluginManager.GetSingleton().PrintToConsole();
Console.WriteLine("If correct, please enter to search, otherwise ctrl-c to exit");
var input = Console.ReadLine();
if (!cancel || input != null) //input will be null when its control+c
{
    
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
}

