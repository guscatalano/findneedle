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
        PluginManager manager = new PluginManager();
        manager.LoadAllPlugins(true);
        var list = manager.GetAllPluginObjectsOfAType("ICommandLineParser");
        Dictionary<CommandLineRegistration, ICommandLineParser> parsers = new();
        foreach (var plugin in list)
        {
            var parser = (ICommandLineParser)plugin;
            var tempInstance = plugin.CreateInstance();
            var reg = parser.RegisterCommandHandler();
            if (tempInstance == null)
            {
                throw new Exception("Failed to create instance of plugin");
            }
            parsers.Add(reg, (ICommandLineParser)tempInstance);
            //Should end up with something like filter_keyword
        }
        return parsers;
    }

    public static SearchQuery ParseFromCommandLine(Dictionary<string, string> arguments, Dictionary<CommandLineRegistration, ICommandLineParser>? parsers = null)
    {
        //This is a test hook
        parsers ??= GetCommandLineParsers();

        SearchQuery q = new();
        foreach (var pair in arguments)
        {
            foreach (var parser in parsers)
            {
                var cmdKeyword = pair.Key;
                var cmdParam = pair.Value.Trim(); //Dont to lower just incase, remove begin and end spaces

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
        }

       
        foreach (var pair in arguments)
        {
          
            if (pair.Key.StartsWith("depth", StringComparison.OrdinalIgnoreCase))
            {
                SearchLocationDepth depth = SearchLocationDepth.Intermediate;
                var ret = Enum.TryParse<SearchLocationDepth>(pair.Value, out depth);
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
