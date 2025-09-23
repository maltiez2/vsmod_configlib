using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ConfigLib;


internal sealed class JsonObjectPath
{
    public JsonObjectPath(string path)
    {
        _path = path.Split("/").Where(element => element != "").Select(Convert);
    }

    public IEnumerable<JsonObject> Get(JsonObject tree)
    {
        IEnumerable<JsonObject> result = [tree];
        foreach (PathElementDelegate element in _path)
        {
            result = element.Invoke(result);
            if (result == null) return Array.Empty<JsonObject>();
        }
        return result;
    }
    public int Set(JsonObject tree, JsonObject value)
    {
        IEnumerable<JsonObject> result = Get(tree);

        foreach (JsonObject element in result)
        {
            element.Token?.Replace(value.Token);
        }

        return result.Count();
    }

    private delegate IEnumerable<JsonObject> PathElementDelegate(IEnumerable<JsonObject> attribute);
    private readonly IEnumerable<PathElementDelegate> _path;

    private PathElementDelegate Convert(string element)
    {
        if (int.TryParse(element, out int index))
        {
            return tree => PathElementByIndex(tree, index);
        }
        else
        {
            if (element == "-") return tree => PathElementByAllIndexes(tree);

            PathElementDelegate? rangeResult = TryParseRange(element);
            if (rangeResult != null) return rangeResult;

            PathElementDelegate? wildcardResult = TryParseWildcard(element);
            if (wildcardResult != null) return wildcardResult;

            PathElementDelegate? conditionResult = TryParseCondition(element);
            if (conditionResult != null) return conditionResult;

            return tree => PathElementByKey(tree, element);
        }
    }

    private static IEnumerable<JsonObject> PathElementByAllIndexes(IEnumerable<JsonObject> attributes)
    {
        List<JsonObject> result = new();
        foreach (JsonObject[] attributesArray in attributes.Where(element => element.IsArray()).Select(element => element.AsArray()))
        {
            int size = attributesArray.Length;
            for (int i = 0; i < size; i++)
            {
                result.Add(attributesArray[i]);
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByIndexes(IEnumerable<JsonObject> attributes, int start, int end)
    {
        List<JsonObject> result = new();
        foreach (JsonObject[] attributesArray in attributes.Where(element => element.IsArray()).Select(element => element.AsArray()))
        {
            int size = attributesArray.Length;
            for (int i = Math.Max(0, start); i < Math.Min(end, size); i++)
            {
                result.Add(attributesArray[i]);
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByIndex(IEnumerable<JsonObject> attributes, int index)
    {
        List<JsonObject> result = new();

        foreach (JsonObject attribute in attributes.Where(element => element.IsArray()))
        {
            if (index < 0 || attribute.AsArray().Length <= index)
            {
                continue;
            }

            JsonObject[] jsonArray = attribute.AsArray();

            result.Add(jsonArray[index]);
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByKey(IEnumerable<JsonObject> attributes, string key)
    {
        List<JsonObject> result = new();

        foreach (JsonObject attribute in attributes)
        {
            if (attribute?.KeyExists(key) == true)
            {
                result.Add(attribute[key]);
                continue;
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByWildcard(IEnumerable<JsonObject> attributes, string wildcard)
    {
        List<JsonObject> result = new();

        foreach (JObject token in attributes.Select(attribute => attribute.Token).OfType<JObject>())
        {
            foreach ((string key, JToken? value) in token)
            {
                if (WildcardUtil.Match(wildcard, key) && value != null)
                {
                    result.Add(new(value));
                }
            }
        }

        return result;
    }
    private static IEnumerable<JsonObject> PathElementByCondition(IEnumerable<JsonObject> attributes, string code, string condition)
    {
        IEnumerable<JArray> arrays = attributes
            .Select(element => element.Token)
            .OfType<JArray>();

        IEnumerable<JObject> objects = attributes
            .Select(element => element.Token)
            .OfType<JObject>();

        IEnumerable<JToken> tokens = [];
        if (arrays.Any())
        {
            tokens = tokens.Concat(arrays.Select(a => a as IEnumerable<JToken>).Aggregate((a, b) => a.Concat(b)));
        }
        if (objects.Any())
        {
            tokens = tokens.Concat(objects.Select(a => a as IEnumerable<JToken>).Aggregate((a, b) => a.Concat(b)));
        }

        IEnumerable<JsonObject> fromObjects = tokens
            .OfType<JObject>()
            .Select(a => new JsonObject(a))
            .Where(a => a.KeyExists(code) && a[code].AsString() == condition);

        IEnumerable<JsonObject> fromProperties = tokens
            .OfType<JProperty>()
            .Select(a => new JsonObject(a.Value))
            .Where(a => a.KeyExists(code) && a[code].AsString() == condition);

        return fromObjects.Concat(fromProperties);
    }

    private static PathElementDelegate? TryParseRange(string element)
    {
        if (!element.Contains("-")) return null;

        string[] indexes = element.Split("-");
        if (indexes.Length != 2) return null;

        bool parsedStart = int.TryParse(indexes[0], out int start);
        bool parsedEnd = int.TryParse(indexes[1], out int end);

        if (!parsedStart || !parsedEnd) return null;

        return tree => PathElementByIndexes(tree, start, end);
    }
    private static PathElementDelegate? TryParseWildcard(string element)
    {
        if (!element.StartsWith("@@")) return null;
        string wildcard = element.Substring(2, element.Length - 2);

        return tree => PathElementByWildcard(tree, wildcard);
    }
    private static PathElementDelegate? TryParseCondition(string element)
    {
        if (!element.Contains("=")) return null;

        string[] parts = element.Split("=");
        
        if (parts.Length != 2) return null;

        string code = parts[0];
        string condition = parts[1];

        return tree => PathElementByCondition(tree, code, condition);
    }
}