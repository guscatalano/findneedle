using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedleCoreUtils;

public class TextManipulation
{

    public static Dictionary<string, string> ParseCommandLineIntoDictionary(string[] args)
    {
        var arguments = new Dictionary<string, string>();
        foreach (var argument in args)
        {
            var count = 0;
            var splitted = argument.Split('=');
            if (splitted.Length == 2)
            {
                arguments[splitted[0] + count] = splitted[1];
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
