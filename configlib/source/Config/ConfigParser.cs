using Newtonsoft.Json.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

internal class ConfigParser
{
    public ConfigParser(ICoreAPI api, Dictionary<string, ConfigSetting> settings, JsonObject definition, SortedDictionary<float, string> yaml)
    {
        _settings = settings;
        _api = api;
        _yaml = yaml;
        int version = definition["version"]?.AsInt(0) ?? 0;

        _yaml.Add(-1, $"version: {version}");

        if (definition["settings"].KeyExists("boolean"))
        {
            _settingType = ConfigSettingType.Boolean;
            ParseCategory(definition["settings"]["boolean"]);
        }

        if (definition["settings"].KeyExists("integer"))
        {
            _settingType = ConfigSettingType.Integer;
            ParseCategory(definition["settings"]["integer"]);
        }

        if (definition["settings"].KeyExists("float"))
        {
            _settingType = ConfigSettingType.Float;
            ParseCategory(definition["settings"]["float"]);
        }

        if (definition["settings"].KeyExists("number")) // @TODO remove
        {
            _settingType = ConfigSettingType.Float;
            ParseCategory(definition["settings"]["number"]);
        }

        if (definition["settings"].KeyExists("other"))
        {
            _settingType = ConfigSettingType.Other;
            ParseCategory(definition["settings"]["other"]);
        }
    }


    private readonly Dictionary<string, ConfigSetting> _settings;
    private readonly SortedDictionary<float, string> _yaml;
    private readonly ConfigSettingType _settingType;
    private readonly ICoreAPI _api;
    private const float _delta = 1E-10f;
    private float _increment = _delta;

    private void ParseCategory(JsonObject category)
    {
        foreach (JToken item in category.Token)
        {
            ParseSetting(item);
        }
    }
    private void ParseSetting(JToken item)
    {
        if (item is not JProperty property)
        {
            _api.Logger.Error($"[Config lib] Error on parsing patches. Token '{item}' is not a property.");
            return;
        }

        (JProperty token, ConfigSetting setting) = ParseToken(property);

        string yamlToken = SerializeToken(token);
        float weight = setting.SortingWeight < 0 ? 0 : setting.SortingWeight;
        if (_yaml.ContainsKey(weight))
        {
            weight += _increment;
            _increment += _delta;
        }
        _yaml.Add(weight, AddComments(yamlToken, property));
    }

    #region Comments parsing
    private static string AddComments(string yamlToken, JProperty property)
    {
        (string first, string other) = SplitToken(yamlToken);
        string comment = GetPreComment(property.Value).Replace("\n", "");
        if (comment != "") comment += "\n";
        string inline = GetInlineComment(property.Value);
        if (inline != "") inline = $" # {inline}";
        return $"{comment}{first}{inline}{other}";
    }
    private static (string firstLine, string remainder) SplitToken(string token)
    {
        string[] split = token.Split("\r\n", 2, StringSplitOptions.RemoveEmptyEntries);

        return (split[0], split.Length > 1 ? $"\r\n{split[1]}" : "");
    }
    private static string GetPreComment(JToken item)
    {
        if (
            item is JObject itemObject &&
            itemObject.ContainsKey("comment") &&
            itemObject["comment"] is JValue commentValue &&
            commentValue.Type == JTokenType.String &&
            commentValue.Value is string comment
            )
        {
            return $"# {comment}\n";
        }

        return "";
    }
    private static string GetInlineComment(JToken item)
    {
        if (item is JObject itemObject)
        {
            if (itemObject.ContainsKey("mapping"))
            {
                return ParseMappingComment(item);
            }

            if (itemObject.ContainsKey("range"))
            {
                return ParseRangeComment(item);
            }

            if (itemObject.ContainsKey("values"))
            {
                return ParseValuesComment(item);
            }
        }

        return "";
    }
    private static string ParseMappingComment(JToken item)
    {
        if (
            item is JObject itemObject &&
            itemObject.ContainsKey("mapping") &&
            itemObject["mapping"] is JObject mapping
        )
        {
            StringBuilder result = new();
            result.Append("value from: ");
            bool first = true;
            foreach ((string name, _) in mapping)
            {
                if (!first) result.Append(", ");
                if (first) first = false;
                result.Append(name);
            }
            return result.ToString();
        }

        return "";
    }
    private static string ParseRangeComment(JToken item)
    {
        if (item is not JObject itemObject || !itemObject.ContainsKey("range") || itemObject["range"] is not JObject) return "";

        JsonObject? rangeObject = new JsonObject(item)["range"];

        float? min = rangeObject?.KeyExists("min") == true ? rangeObject["min"]?.AsFloat() : null;
        float? max = rangeObject?.KeyExists("max") == true ? rangeObject["max"]?.AsFloat() : null;
        float? step = rangeObject?.KeyExists("step") == true ? rangeObject["step"]?.AsFloat() : null;

        string minMax = (min != null, max != null) switch
        {
            (true, true) => $"from {min} to {max}",
            (true, false) => $"greater than {min}",
            (false, true) => $"less than {max}",
            _ => ""
        };

        if (step != null && minMax != "")
        {
            return $"{minMax} with step of {step}";
        }
        else
        {
            return minMax;
        }
    }
    private static string ParseValuesComment(JToken item)
    {
        if (item is not JObject itemObject || !itemObject.ContainsKey("values") || itemObject["values"] is not JArray values) return "";

        StringBuilder result = new();
        result.Append("value from: ");
        bool first = true;
        foreach (JToken element in values)
        {
            if (!first) result.Append(", ");
            if (first) first = false;
            result.Append((element as JValue)?.Value);
        }
        return result.ToString();
    }
    #endregion

    #region Token serialization
    private static string SerializeToken(JProperty token)
    {
        JObject tokenObject = new()
        {
            token
        };

        object simplifiedToken = ConvertJTokenToObject(tokenObject);

        YamlDotNet.Serialization.Serializer serializer = new();

        using StringWriter writer = new();
        serializer.Serialize(writer, simplifiedToken);
        string yaml = writer.ToString();
        return yaml;
    }
    static object ConvertJTokenToObject(JToken token)
    {
        if (token is JValue value && value.Value != null)
            return value.Value;
        if (token is JArray)
            return token.AsEnumerable().Select(ConvertJTokenToObject).ToList();
        if (token is JObject)
            return token.AsEnumerable().Cast<JProperty>().ToDictionary(x => x.Name, x => ConvertJTokenToObject(x.Value));
        throw new InvalidOperationException("Unexpected token: " + token);
    }
    private (JProperty, ConfigSetting) ParseToken(JProperty property)
    {
        string code = property.Name;
        string name = (string)((property.Value["name"] as JValue)?.Value ?? "");
        string? comment = (string?)(property.Value["comment"] as JValue)?.Value;
        string inGuiName = (string)((property.Value["ingui"] as JValue)?.Value ?? "");
        bool clientSide = (bool)((property.Value["clientSide"] as JValue)?.Value ?? false);

        if (property.Value is not JObject propertyValue) throw new InvalidConfigException("Invalid config formatting");

        (JToken value, ConfigSetting setting) = GetValue(code, name, comment, propertyValue, inGuiName);
        
        setting.ClientSide = clientSide;
        setting.YamlCode = name;
        setting.Comment = comment;
        setting.InGui = inGuiName;

        JProperty result = new(name, value);
        return (result, setting);
    }

    private (JToken, ConfigSetting) GetValue(string code, string name, string? comment, JObject property, string inGuiName)
    {
        if (!property.ContainsKey("default") || property["default"] is not JToken defaultValue)
        {
            throw new InvalidConfigException($"Invalid or absent default value for '{code}' setting");
        }

        if (property.ContainsKey("mapping") && property["mapping"] is JObject mapping)
        {
            return GetMappingValue(code, name, comment, defaultValue, mapping, property, inGuiName);
        }

        if (property.ContainsKey("range") && property["range"] is JObject range)
        {
            return GetRangeValue(code, name, comment, defaultValue, range, property, inGuiName);
        }

        if (property.ContainsKey("values") && property["values"] is JArray values)
        {
            return GetValuesValue(code, name, comment, defaultValue, values, property, inGuiName);
        }

        ConfigSetting setting = new(name, new(defaultValue), _settingType, comment, null, null, GetWeight(property), inGuiName);
        _settings.Add(code, setting);
        return (defaultValue, setting);
    }
    private (JToken, ConfigSetting) GetMappingValue(string code, string name, string? comment, JToken defaultValue, JObject mapping, JObject property, string inGuiName)
    {
        if ((defaultValue as JValue)?.Value is not string value)
        {
            throw new InvalidConfigException($"Default value for '{code}' setting should have 'string' type, because this setting has mapping in validation");
        }

        if (!mapping.ContainsKey(value) || mapping[value] is not JToken validatedValue)
        {
            if (mapping.ContainsKey(value))
            {
                throw new InvalidConfigException($"Default value '{value}' for '{code}' setting is not valid value");
            }
            else
            {
                throw new InvalidConfigException($"Default value '{value}' for '{code}' setting is not found in this setting mapping");
            }
        }

        Dictionary<string, JsonObject> settingMapping = new();

        foreach ((string key, JToken? mappingValue) in mapping)
        {
            if (mappingValue == null)
            {
                throw new InvalidConfigException($"Mapping value for entry '{key}' for setting '{code}' is not valid value");
            }

            settingMapping.Add(key, new(mappingValue));
        }

        ConfigSetting setting = new(name, new(validatedValue), _settingType, comment, new(settingMapping), value, GetWeight(property), inGuiName);
        _settings.Add(code, setting);
        return (new JValue(value), setting);
    }
    private (JToken, ConfigSetting) GetRangeValue(string code, string name, string? comment, JToken defaultValue, JObject range, JObject property, string inGuiName)
    {

        JsonObject? min = range.ContainsKey("min") ? new(range["min"]) : null;
        JsonObject? max = range.ContainsKey("max") ? new(range["max"]) : null;
        JsonObject? step = range.ContainsKey("step") ? new(range["step"]) : null;
        bool logarithmic = property.ContainsKey("logarithmic") && new JsonObject(property["logarithmic"]).AsBool(false);
        Validation parsedValidation = new(min, max, step);

        ConfigSetting setting = new(name, new(defaultValue), _settingType, comment, parsedValidation, null, GetWeight(property), inGuiName, logarithmic);
        _settings.Add(code, setting);
        return (defaultValue, setting);
    }
    private (JToken, ConfigSetting) GetValuesValue(string code, string name, string? comment, JToken defaultValue, JArray values, JObject property, string inGuiName)
    {
        List<JsonObject> parsedValues = new();
        foreach (JToken value in values)
        {
            parsedValues.Add(new(value));
        }

        ConfigSetting setting = new(name, new(defaultValue), _settingType, comment, new(parsedValues), null, GetWeight(property), inGuiName);
        _settings.Add(code, setting);
        return (defaultValue, setting);
    }
    private static float GetWeight(JObject property)
    {
        if (property.ContainsKey("weight") && property["weight"] is JValue weight && (weight.Type == JTokenType.Float || weight.Type == JTokenType.Integer))
        {
            JsonObject value = new(weight);
            return value.AsFloat(0);
        }

        return 0;
    }
    #endregion
}