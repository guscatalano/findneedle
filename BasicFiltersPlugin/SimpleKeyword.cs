using System.Text.Json.Serialization;
using FindNeedlePluginLib.Interfaces;
using Windows.ApplicationModel.Activation;

namespace findneedle.Implementations;

public class SimpleKeywordFilter : ISearchFilter, ICommandLineParser, IPluginDescription
{

    public string term 
    {
        get; set;
    }

    public SimpleKeywordFilter()
    {
        term = "invalidserach";
    }

    [JsonConstructorAttribute]
    public SimpleKeywordFilter(string term)
    {
        this.term = term.Trim();
        if (string.IsNullOrEmpty(term))
        {
            throw new Exception("Can't search for empty terms");
        }
    }



    public bool Filter(ISearchResult entry)
    {
        if (entry.GetSearchableData().ToLower().Contains(term))
        {
            return true;
        }
        return false;
    }

    public string GetDescription()
    {
        return "SimpleKeyword";
    }
    public string GetName()
    {
        return term;
    }

    public CommandLineRegistration RegisterCommandHandler()
    {
        var reg = new CommandLineRegistration()
        {
            handlerType = CommandLineHandlerType.Filter,
            key = "keyword"
        };
        return reg;
    }
    public void ParseCommandParameterIntoQuery(string parameter)
    {
        term = parameter;
    }

    public string GetPluginTextDescription()
    {
        return "Filters the search results by a simple keyword";
    }
    public string GetPluginFriendlyName() {
        return "Keyword Filter";
    }
    public string GetPluginClassName()
    {
        return GetType().FullName ?? string.Empty;
    }
}
