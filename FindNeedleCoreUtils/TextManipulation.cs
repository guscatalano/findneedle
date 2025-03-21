using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedleCoreUtils;


public class CommandLineArgument
{
    public string key = "";
    public string value = "";
}

public class TextManipulation
{

    public static List<CommandLineArgument> ParseCommandLineIntoDictionary(string[] args)
    {
        var arguments = new List<CommandLineArgument>();
        foreach (var argument in args)
        {
            var splitted = argument.Split('=');
            if (splitted.Length == 2)
            {
                arguments.Add(new CommandLineArgument() { key = splitted[0], value = splitted[1] });
            } else
            {
                arguments.Add(new CommandLineArgument() { key = argument, value = string.Empty });
            }
        }
        return arguments;
    }

    public static List<string> SplitApart(string text)
    {
        List<string> ret = new List<string>();
        var results = text.Split(",");
        foreach (var i in results)
        {
            var ix = ReplaceInvalidChars(i);
            if (string.IsNullOrEmpty(ix))
            {
                continue;
            }
            ret.Add(ix);
        }
        return ret;
    }
    public static string ReplaceInvalidChars(string text)
    {
        text = text.Replace(",", "");
        text = text.Replace("(", "");
        text = text.Replace(")", "");
        text = text.Trim();
        return text;
    }
}
