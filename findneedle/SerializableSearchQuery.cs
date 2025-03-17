using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using findneedle.Implementations;

namespace findneedle;


public class JsonClassMetadata
{
    public string Json { get; set; } = string.Empty;
    public string FullClassName { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;

    [JsonConstructorAttribute]
    public JsonClassMetadata(string Json, string FullClassName, string AssemblyName)
    {
        this.Json = Json;
        this.FullClassName = FullClassName;
        this.AssemblyName = AssemblyName;
    }

    public JsonClassMetadata(Type type, string json)
    {
        if (type != null && type.FullName != null && type.AssemblyQualifiedName != null)
        {
            this.Json = json;
            this.FullClassName = type.FullName;
            this.AssemblyName = type.AssemblyQualifiedName;
        }
    }

    public Type GetJsonType()
    {
        return Type.GetType(AssemblyName) ?? throw new InvalidOperationException("Type could not be resolved from AssemblyName.");
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
        var oJson = JsonSerializer.Serialize(filter, x, new JsonSerializerOptions
        {
            IncludeFields = true,

        });

        JsonClassMetadata z = new JsonClassMetadata(x, oJson);
        var output = JsonSerializer.Serialize(z);
        return output;
    }

    public static object DeserializeJson(string json)
    {
        var z = JsonSerializer.Deserialize<JsonClassMetadata>(json) ?? throw new Exception("Failed to deserialize JsonClassMetadata");
        var t = Type.GetType(z.AssemblyName) ?? throw new Exception("Failed to get type from AssemblyName");
        var js = typeof(JsonSerializer);
        var m = js.GetMethod("Deserialize", new[] { typeof(string), typeof(JsonSerializerOptions) });
        if (m == null)
        {
            throw new Exception("Failed to get Deserialize method");
        }

        var g = m.MakeGenericMethod(t);
        var options = new JsonSerializerOptions();
        var output = g.Invoke(null, new object[] { z.Json, options });

        return output ?? throw new Exception("Failed to deserialize object");
    }

    public static SerializableSearchQuery GetSerializableSearchQuery(SearchQuery source)
    {
        SerializableSearchQuery destination = new();

        destination.Name = source.Name;
        destination.FilterJson = new List<string>();
        destination.LocationJson = new List<string>();

        //Serialize all the filters
        foreach (ISearchFilter filter in source.filters)
        {
          
            var outher = SerializeImplementedType(filter);
            destination.FilterJson.Add(outher);
        }

        //Serialize all the filters
        foreach (var loc in source.locations)
        {

            var outher = SerializeImplementedType(loc);
            destination.LocationJson.Add(outher);
        }



        return destination;
    }

    public static SearchQuery GetSearchQueryObject(SerializableSearchQuery source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        SearchQuery destination = new();
        destination.Name = source.Name;
        destination.filters = new();
        destination.locations = new();

        if (source.FilterJson != null)
        {
            foreach (var filter in source.FilterJson)
            {
                ISearchFilter outher = (ISearchFilter)DeserializeJson(filter);
                destination.filters.Add(outher);
            }
        }

        if (source.LocationJson != null)
        {
            foreach (var loc in source.LocationJson)
            {
                ISearchLocation outher = (ISearchLocation)DeserializeJson(loc);
                destination.locations.Add(outher);
            }
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

    public List<string>? FilterJson
    {
        get; set;
    }

    public List<string>? LocationJson
    {
        get; set;
    }
}
