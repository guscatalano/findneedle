using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using findneedle;
using FindNeedleCoreUtils;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;
using findneedle.Interfaces;
using FindPluginCore.PluginSubsystem;

namespace FindPluginCore.Searching;

public class SearchQueryCmdLine
{

    public static SearchQuery ParseFromCommandLine(string[] cmdline)
    {
        var arguments = TextManipulation.ParseCommandLineIntoDictionary(cmdline);
        return ParseFromCommandLine(arguments);
    }

    public static Dictionary<CommandLineRegistration, ICommandLineParser> GetCommandLineParsers()
    {
        var manager = PluginManager.GetSingleton();
        manager.LoadAllPlugins(true);
        var list = manager.GetAllPluginsInstancesOfAType<ICommandLineParser>();
        Dictionary<CommandLineRegistration, ICommandLineParser> parsers = [];
        foreach (var pluginInstance in list)
        {
            if (pluginInstance == null)
            {
                throw new Exception("We got a null instance?");
            }
            var reg = pluginInstance.RegisterCommandHandler();
            parsers.Add(reg, pluginInstance);
            //Should end up with something like filter_keyword
        }
        return parsers;
    }

    public static void PrintToConsole(SearchQuery q)
    {
        Console.WriteLine("Search query:");
        Console.WriteLine("Name: " + q.Name);
        Console.WriteLine("Locations:");
        foreach (var loc in q.locations)
        {
            Console.WriteLine("\t" + loc.GetDescription());
        }
        Console.WriteLine("Filters:");
        foreach (var filter in q.filters)
        {
            Console.WriteLine("\t" + filter.GetDescription());
        }
        Console.WriteLine("Processors:");
        foreach (var processor in q.processors)
        {
            Console.WriteLine("\t" + processor.GetDescription());
        }
        Console.WriteLine("Depth: " + q.Depth);
        Console.WriteLine("End of search query");
    }

    public static SearchQuery ParseFromCommandLine(List<CommandLineArgument> arguments, Dictionary<CommandLineRegistration, ICommandLineParser>? parsers = null)
    {
        //This is a test hook
        parsers ??= GetCommandLineParsers();

        SearchQuery q = new();
        foreach (var argument in arguments)
        {
            foreach (var parser in parsers)
            {
                var cmdKeyword = argument.key;
                var cmdParam = argument.value.Trim(); //Dont to lower just incase, remove begin and end spaces

                if (cmdKeyword.StartsWith(parser.Key.GetCmdLineKey(), StringComparison.OrdinalIgnoreCase))
                {
                    //Remove begin and end ( ) if provided
                    if(cmdParam.StartsWith("(") && cmdParam.EndsWith(")"))
                    {
                        cmdParam = cmdParam.Substring(1, cmdParam.Length - 2);
                    }
                    parser.Value.ParseCommandParameterIntoQuery(cmdParam);

                    //If we didn't throw the object is valid
                    switch (parser.Key.handlerType)
                    {
                        case CommandLineHandlerType.Location:
                            q.locations.Add((ISearchLocation)parser.Value);
                            break;
                        case CommandLineHandlerType.Filter:
                            q.filters.Add((ISearchFilter)parser.Value);
                            break;
                         case CommandLineHandlerType.Processor:
                            q.processors.Add((IResultProcessor)parser.Value);
                             break;
                        default:
                            throw new Exception("Unknown handler type");
                    }
                }
            }


            //Non-plugin parameters
            //TODO: Check they are not duplicated twice!
            if (argument.key.StartsWith("depth", StringComparison.OrdinalIgnoreCase))
            {
                SearchLocationDepth depth = SearchLocationDepth.Intermediate;
                var ret = Enum.TryParse<SearchLocationDepth>(argument.value, out depth);
                if (!ret)
                {
                    throw new Exception("Failed to parse depth");
                }
                q.SetDepth(depth);
            }
        }

        return q;
    }
}
