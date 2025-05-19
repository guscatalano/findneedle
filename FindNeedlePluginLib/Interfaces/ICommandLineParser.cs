using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib.Interfaces;


public enum CommandLineHandlerType
{
    Location = 0,
    Filter = 1,
    Processor = 2,
}

public class CommandLineRegistration
{

    public static string HandlerTypeToString(CommandLineHandlerType type)
    {
        switch (type)
        {
            case CommandLineHandlerType.Location:
                return "location";
            case CommandLineHandlerType.Filter:
                return "filter";
            case CommandLineHandlerType.Processor:
                return "processor";
            default:
                throw new Exception("Unknown handler type");
        }
    }

    public CommandLineHandlerType handlerType;
    public string key = "";

    public string GetCmdLineKey()
    {
        return HandlerTypeToString(handlerType) + "_" + key;
    }
}

/*
 * If a plugin supports this, it means that it can parse from commandline arguments
 */
public interface ICommandLineParser
{
    /* This is the key that will be used to identify the plugin in the parameter
     * Ex: filter_keyword would have been registered as "keyword" with HandlerType of filter
     */
    CommandLineRegistration RegisterCommandHandler();

    /* Initialize the plugin from commandline parameter, throw if you can't parse it with the error of why
     * The 
     */
    void ParseCommandParameterIntoQuery(string parameter);


    /* By default we clone, if there is something that needs to be retained, use this */
    void Clone(ICommandLineParser original);
}
