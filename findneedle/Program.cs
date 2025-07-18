﻿global using System.Diagnostics.CodeAnalysis;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
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
        Console.WriteLine("Done");
        Console.ReadLine();


    }
}