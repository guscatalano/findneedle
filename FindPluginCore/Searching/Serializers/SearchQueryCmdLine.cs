using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Implementations;
using findneedle;
using FindNeedleCoreUtils;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.GlobalConfiguration;
using FindPluginCore.Searching.Serializers;

namespace FindPluginCore.Searching;

public class SearchQueryCmdLine
{

    public static ISearchQuery ParseFromCommandLine(string[] cmdline, PluginManager pluginManager)
    {
        var arguments = TextManipulation.ParseCommandLineIntoDictionary(cmdline);
        return ParseFromCommandLine(arguments, pluginManager);
    }

    public static Dictionary<CommandLineRegistration, ICommandLineParser> GetCommandLineParsers(PluginManager pluginManager)
    {
        pluginManager.LoadAllPlugins(true);
        var cmdparsers = pluginManager.GetAllPluginsInstancesOfAType<ICommandLineParser>();
        Dictionary<CommandLineRegistration, ICommandLineParser> parsers = [];


        var extensions = pluginManager.GetAllPluginsInstancesOfAType<IFileExtensionProcessor>();
        var folderloc = new FolderLocation();
        folderloc.SetExtensionProcessorList(extensions);
        cmdparsers.Add(folderloc); //Hardcoded

        foreach (var pluginInstance in cmdparsers)
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

    public static void PrintToConsole(ISearchQuery q)
    {
        Console.WriteLine("Search query:");
        Console.WriteLine("Name: " + q.Name);
        Console.WriteLine("Locations:");
        foreach (var loc in q.Locations)
        {
            Console.WriteLine("\t" + loc.GetDescription());
        }
        Console.WriteLine("Filters:");
        foreach (var filter in q.Filters)
        {
            Console.WriteLine("\t" + filter.GetDescription());
        }
        Console.WriteLine("Processors:");
        foreach (var processor in q.Processors)
        {
            Console.WriteLine("\t" + processor.GetDescription());
        }
        Console.WriteLine("Outputs:");
        foreach (var output in q.Outputs)
        {
            Console.WriteLine("\t" + output.GetPluginTextDescription());
        }
        Console.WriteLine("Depth: " + q.Depth);
        Console.WriteLine("End of search query");
    }

    public static ISearchQuery ParseFromCommandLine(List<CommandLineArgument> arguments, PluginManager pluginManager, Dictionary<CommandLineRegistration, ICommandLineParser>? parsers = null)
    {
        
        parsers ??= GetCommandLineParsers(pluginManager);

        ISearchQuery q = SearchQueryFactory.CreateSearchQuery(pluginManager);

        q.Processors = pluginManager.GetAllPluginsInstancesOfAType<IResultProcessor>();

        foreach (var argument in arguments)
        {
            // Allow a plain positional path (file or folder) to be treated as a location
            // Skip the exe path (first arg) which may be present in Environment.GetCommandLineArgs()
            try
            {
                var potentialPath = argument.key;
                var entryAsmPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(potentialPath) && !string.IsNullOrEmpty(entryAsmPath) && string.Equals(potentialPath, entryAsmPath, StringComparison.OrdinalIgnoreCase))
                {
                    // it's the exe path - ignore
                }
                else if (string.IsNullOrEmpty(argument.value))
                {
                    // If it looks like an existing file or directory, or contains a path separator, treat as location
                    if (System.IO.File.Exists(potentialPath) || System.IO.Directory.Exists(potentialPath) || potentialPath.Contains(System.IO.Path.DirectorySeparatorChar) || potentialPath.Contains("\\"))
                    {
                        var loc = new FolderLocation();
                        loc.path = potentialPath;
                        // Setup extension processors so files are actually parsed
                        try
                        {
                            var extensions = pluginManager.GetAllPluginsInstancesOfAType<FindNeedlePluginLib.IFileExtensionProcessor>();
                            loc.SetExtensionProcessorList(extensions.Cast<FindNeedlePluginLib.IFileExtensionProcessor>().ToList());
                        }
                        catch
                        {
                            // ignore if plugins not available
                        }
                        q.Locations.Add(loc);
                        // Skip further parser processing for this argument
                        continue;
                    }
                }
            }
            catch
            {
                // ignore path-detection errors and fall through to normal parsing
            }
            // Support --rules or rules=path/to/file.json
            if (argument.key.Equals("--rules", StringComparison.OrdinalIgnoreCase) || argument.key.Equals("rules", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(argument.value))
                {
                    var raw = argument.value.Trim();
                    // Remove surrounding quotes if present
                    if (raw.StartsWith("\"") && raw.EndsWith("\""))
                        raw = raw.Substring(1, raw.Length - 2);

                    // Try to resolve to full path
                    try
                    {
                        raw = FileIO.FindFullPathToFile(raw);
                    }
                    catch
                    {
                        // leave raw as-is if resolution fails
                    }

                    if (q.RulesConfigPaths == null)
                        q.RulesConfigPaths = new List<string>();
                    q.RulesConfigPaths.Add(raw);
                }
                continue;
            }

            //This is a test hook
            //Make a new instance per argument, otherwise you can't have multiple
            parsers ??= GetCommandLineParsers(pluginManager);
            foreach (var parser in parsers)
            {
                
                Type t = parser.Value.GetType();
                var instance = Activator.CreateInstance(t);
                if(instance == null)
                {
                    throw new Exception("Failed to clone parser instance");
                }
                

                //Only use parserObj going forward
                var parserObj = (ICommandLineParser)instance;
                parserObj.Clone(parser.Value); //needed to copy settings

                var cmdKeyword = argument.key;
                var cmdParam = argument.value.Trim(); //Dont to lower just incase, remove begin and end spaces

                if (cmdKeyword.StartsWith(parser.Key.GetCmdLineKey(), StringComparison.OrdinalIgnoreCase))
                {
                    //Remove begin and end ( ) if provided
                    if(cmdParam.StartsWith("(") && cmdParam.EndsWith(")"))
                    {
                        cmdParam = cmdParam.Substring(1, cmdParam.Length - 2);
                    }
                    parserObj.ParseCommandParameterIntoQuery(cmdParam);

                    //If we didn't throw the object is valid
                    switch (parser.Key.handlerType)
                    {
                        case CommandLineHandlerType.Location:
                            q.Locations.Add((ISearchLocation)parserObj);
                            break;
                        case CommandLineHandlerType.Filter:
                            q.Filters.Add((ISearchFilter)parserObj);
                            break;
                         case CommandLineHandlerType.Processor:
                            q.Processors.Add((IResultProcessor)parserObj);
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
                q.Depth = depth;
            }

           
            if (argument.key.StartsWith("debug", StringComparison.OrdinalIgnoreCase))
            {
                var debug = false;
                var ret = Boolean.TryParse(argument.value, out debug);
                if (!ret)
                {
                    throw new Exception("Failed to parse depth");
                }
                GlobalSettings.Debug = debug;
            }
        }

        return q;
    }
}
