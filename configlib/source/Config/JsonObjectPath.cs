using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace ConfigLib;

internal sealed class JsonObjectPath
{
    private delegate IEnumerable<JsonObject> PathElementDelegate(IEnumerable<JsonObject> attribute);
    private readonly IEnumerable<PathElementDelegate> _path;

    public JsonObjectPath(string path)
    {
        _path = path.Split("/").Where(element => element != "").Select(Convert);
    }

    private PathElementDelegate Convert(string element)
    {
        if (int.TryParse(element, out int index))
        {
            return tree => PathElementByIndex(tree, index);
        }
        else
        {
            if (element == "-") return tree => PathElementByIndex(tree, -1);

            PathElementDelegate? rangeResult = TryParseRange(element);
            if (rangeResult != null) return rangeResult;

            PathElementDelegate? wildcardResult = TryParseWildcard(element);
            if (wildcardResult != null) return wildcardResult;

            return tree => PathElementByKey(tree, element);
        }
    }

    public IEnumerable<JsonObject> Get(JsonObject? tree)
    {
        IEnumerable<JsonObject> result = new JsonObject[] { tree };
        foreach (PathElementDelegate element in _path)
        {
            result = element.Invoke(result);
            if (result == null) return null;
        }
        return result;
    }
    public bool Set(JsonObject? tree, JsonObject value)
    {
        IEnumerable<JsonObject> result = Get(tree);

        bool wasSet = false;
        foreach (JsonObject element in result)
        {
            element.Token?.Replace(value.Token);
            wasSet = true;
        }

        return wasSet;
    }

    private static IEnumerable<JsonObject> PathElementByIndexes(IEnumerable<JsonObject> attributes, int start, int end)
    {
        List<JsonObject> result = new();
        foreach (JsonObject[] attributesArray in attributes.Where(element => element.IsArray()).Select(element => element.AsArray()))
        {
            int size = attributesArray.Length;
            for (int i = start; i < Math.Min(end, size); i++)
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
            if (index == -1 || attribute.AsArray().Length <= index)
            {
                (attribute.Token as JArray)?.Add(new JValue(0));
                index = ((attribute.Token as JArray)?.Count ?? 1) - 1;
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

            if (attribute?.Token is not JObject token) continue;

            token.Add(key, new JValue(0));

            result.Add(attribute);
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

    private static PathElementDelegate? TryParseRange(string element)
    {
        if (!element.Contains("-")) return null;

        string[] indexes = element.Split("-");
        if (indexes.Length != 2) return null;

        bool parsedStart = int.TryParse(indexes[0], out int start);
        bool parsedEnd = int.TryParse(indexes[0], out int end);

        if (!parsedStart || !parsedEnd) return null;

        return tree => PathElementByIndexes(tree, start, end);
    }
    private static PathElementDelegate? TryParseWildcard(string element)
    {
        if (!element.StartsWith("@@")) return null;
        string wildcard = element.Substring(2, element.Length - 2);

        return tree => PathElementByWildcard(tree, wildcard);
    }
}