using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using findneedle.Implementations;

namespace findneedle;


public class JsonClassMetadata
{

    public string Json
    {
        get; set;
    }

    public string FullClassName
    {
        get; set; 
    }

    public string AssemblyName
    {
        get; set; 
    }

    [JsonConstructorAttribute]
    public JsonClassMetadata(string Json, string FullClassName, string AssemblyName)
    {
        this.Json = Json;
        this.FullClassName = FullClassName;
        this.AssemblyName = AssemblyName;
    }

    public JsonClassMetadata(Type type, string json)
    {
        this.Json = json;
        this.FullClassName = type.FullName;
        this.AssemblyName = type.AssemblyQualifiedName;
    }

    public Type GetJsonType()
    {
        return Type.GetType(this.AssemblyName);
    }

}

public class SearchQueryJsonReader
{


    public static SerializableSearchQuery LoadSearchQuery(string json)
    {
        var options = new JsonSerializerOptions
        {
            IncludeFields = true,

        };
        SerializableSearchQuery? q = JsonSerializer.Deserialize<SerializableSearchQuery>(json, options);
        if (q != null)
        {
            return q;
        }
        throw new Exception("Failed to deserialize");

    }

    private static string SerializeImplementedType(object filter)
    {
        Type x = filter.GetType();
        string oJson = JsonSerializer.Serialize(filter, x, new JsonSerializerOptions
        {
            IncludeFields = true,

        });

        JsonClassMetadata z = new JsonClassMetadata(x, oJson);
        string output = JsonSerializer.Serialize(z);
        return output;
    }

    public static object DeserializeJson(string json)
    {

        JsonClassMetadata z = JsonSerializer.Deserialize<JsonClassMetadata>(json);
        Type t = Type.GetType(z.AssemblyName);

        //var ouffft = JsonSerializer.Deserialize<SimpleKeywordFilter>(z.Json);

        var js = typeof(JsonSerializer);
        var m = js.GetMethod("Deserialize", new[] { typeof(string), typeof(JsonSerializerOptions) });
        var g = m.MakeGenericMethod(t);
        var output = g.Invoke(null, new object[] { z.Json, null });

        return output;
    }

    public static SerializableSearchQuery GetSerializableSearchQuery(SearchQuery source)
    {
        SerializableSearchQuery destination = new();

        destination.Name = source.Name;
        destination.FilterJson = new List<string>();
        destination.LocationJson = new List<string>();

        //Serialize all the filters
        foreach (SearchFilter filter in source.filters)
        {
          
            string outher = SerializeImplementedType(filter);
            destination.FilterJson.Add(outher);
        }

        //Serialize all the filters
        foreach (SearchLocation loc in source.locations)
        {

            string outher = SerializeImplementedType(loc);
            destination.LocationJson.Add(outher);
        }



        return destination;
    }

    public static SearchQuery GetSearchQueryObject(SerializableSearchQuery source)
    {
        SearchQuery destination = new();
        destination.Name = source.Name;
        destination.filters = new();
        destination.locations = new();
        foreach (string filter in source.FilterJson)
        {

            SearchFilter outher = (SearchFilter)DeserializeJson(filter);
            destination.filters.Add(outher);
        }

        foreach (string loc in source.LocationJson)
        {

            SearchLocation outher = (SearchLocation)DeserializeJson(loc);
            destination.locations.Add(outher);
        }
        return destination;
    }



}


public class SerializableSearchQuery
{


    public string GetQueryJson()
    {
        return JsonSerializer.Serialize(this);
    }

   


    public string? Name
    {
        get; set;
    }

    [DefaultValue(SearchLocationDepth.Intermediate)]
    public SearchLocationDepth Depth
    {
        get; set;
    }

    public List<string> FilterJson
    {
        get; set;
    }

    public List<string> LocationJson
    {
        get; set;
    }
}
