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

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public JsonClassMetadata(Type type, string json)
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
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
#pragma warning disable CS8603 // Possible null reference return.
        return Type.GetType(this.AssemblyName);
#pragma warning restore CS8603 // Possible null reference return.
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

#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
        JsonClassMetadata z = JsonSerializer.Deserialize<JsonClassMetadata>(json);

#pragma warning disable CS8602 // Dereference of a possibly null reference.
        Type t = Type.GetType(z.AssemblyName);
#pragma warning restore CS8602 // Dereference of a possibly null reference.
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.

        //var ouffft = JsonSerializer.Deserialize<SimpleKeywordFilter>(z.Json);

        var js = typeof(JsonSerializer);
        var m = js.GetMethod("Deserialize", new[] { typeof(string), typeof(JsonSerializerOptions) });
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
#pragma warning disable CS8602 // Dereference of a possibly null reference.
#pragma warning disable CS8604 // Possible null reference argument.
        var g = m.MakeGenericMethod(t);
#pragma warning restore CS8604 // Possible null reference argument.
#pragma warning restore CS8602 // Dereference of a possibly null reference.

        var output = g.Invoke(null, new object[] { z.Json, null });
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.

#pragma warning disable CS8603 // Possible null reference return.
        return output;
#pragma warning restore CS8603 // Possible null reference return.
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
          
            var outher = SerializeImplementedType(filter);
            destination.FilterJson.Add(outher);
        }

        //Serialize all the filters
        foreach (SearchLocation loc in source.locations)
        {

            var outher = SerializeImplementedType(loc);
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
#pragma warning disable CS8602 // Dereference of a possibly null reference.
        foreach (var filter in source.FilterJson)
        {

            SearchFilter outher = (SearchFilter)DeserializeJson(filter);
            destination.filters.Add(outher);
        }


        foreach (var loc in source.LocationJson)
        {

            SearchLocation outher = (SearchLocation)DeserializeJson(loc);
            destination.locations.Add(outher);
        }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
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
