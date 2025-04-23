using System.Diagnostics.CodeAnalysis;
using findneedle;
using findneedle.Implementations;
using findneedle.Interfaces;
using findneedle.PluginSubsystem;
using FindNeedleCoreUtils;
using FindPluginCore.Searching;

[ExcludeFromCodeCoverage]
internal class Program
{
    static void Main(string[] args)
    {

        var cancel = false;
        Console.CancelKeyPress += delegate {
            cancel = true;
            Console.WriteLine("Cancel received, exiting");
            Environment.Exit(0);
        };


        var x = SearchQueryCmdLine.ParseFromCommandLine(Environment.GetCommandLineArgs(), PluginManager.GetSingleton());
        SearchQueryCmdLine.PrintToConsole(x);
        PluginManager.GetSingleton().PrintToConsole();
        Console.WriteLine("If correct, please enter to search, otherwise ctrl-c to exit");
        var input = Console.ReadLine();
        if (!cancel || input != null) //input will be null when its control+c
        {

            Console.WriteLine("Searching...");
            x.RunThrough();
           
        }


    }
}