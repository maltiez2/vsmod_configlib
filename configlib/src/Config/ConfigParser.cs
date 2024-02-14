using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

internal class ConfigParser
{
    private readonly Dictionary<string, ConfigSetting> mSettings;
    private readonly SortedDictionary<float, string> mYaml;
    private readonly ConfigSettingType mSettingType;
    private readonly ICoreAPI mApi;
    private const float cDelta = 1E-10f;
    private float mIncrement = cDelta;

    public ConfigParser(ICoreAPI api, Dictionary<string, ConfigSetting> settings, JsonObject definition, SortedDictionary<float, string> yaml)
    {
        mSettings = settings;
        mApi = api;
        mYaml = yaml;
        int version = definition["version"]?.AsInt(0) ?? 0;

        mYaml.Add(-1, $"version: {version}");

        if (definition["settings"].KeyExists("boolean"))
        {
            mSettingType = ConfigSettingType.Boolean;
            ParseCategory(definition["settings"]["boolean"]);
        }

        if (definition["settings"].KeyExists("integer"))
        {
            mSettingType = ConfigSettingType.Integer;
            ParseCategory(definition["settings"]["integer"]);
        }

        if (definition["settings"].KeyExists("float"))
        {
            mSettingType = ConfigSettingType.Float;
            ParseCategory(definition["settings"]["float"]);
        }

        if (definition["settings"].KeyExists("number")) // @TODO remove
        {
            mSettingType = ConfigSettingType.Float;
            ParseCategory(definition["settings"]["number"]);
        }

        if (definition["settings"].KeyExists("other"))
        {
            mSettingType = ConfigSettingType.Other;
            ParseCategory(definition["settings"]["other"]);
        }
    }
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
            mApi.Logger.Error($"[Config lib] Error on parsing patches. Token '{item}' is not a property.");
            return;
        }

        (JProperty token, ConfigSetting setting) = ParseToken(property);

        string yamlToken = SerializeToken(token);
        float weight = setting.SortingWeight < 0 ? 0 : setting.SortingWeight;
        if (mYaml.ContainsKey(weight))
        {
            weight += mIncrement;
            mIncrement += cDelta;
        }
        mYaml.Add(weight, AddComments(yamlToken, property));
    }

    #region Comments parsing
    private static string AddComments(string yamlToken, JProperty property)
    {
        (string first, string other) = SplitToken(yamlToken);
        string comment = GetPreComment(property.Value).Replace("\n","");
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
        if (
            item is JObject itemObject &&
            itemObject.ContainsKey("range") &&
            itemObject["range"] is JObject range
        )
        {
            string? min = range.ContainsKey("min") ? (item["min"] as JValue)?.Value?.ToString() : null;
            string? max = range.ContainsKey("max") ? (item["max"] as JValue)?.Value?.ToString() : null;
            string? step = range.ContainsKey("max") ? (item["max"] as JValue)?.Value?.ToString() : null;

            string minMax = (min != null, max != null) switch
            {
                (true, true) => $"from {min} to {max}",
                (true, false) => $"greater than {min}",
                (false, true) => $"lesser than {max}",
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

        return "";
    }
    private static string ParseValuesComment(JToken item)
    {
        if (
            item is JObject itemObject &&
            itemObject.ContainsKey("values") &&
            itemObject["values"] is JArray values
        )
        {
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

        return "";
    }
    #endregion

    #region Token serialization
    private string SerializeToken(JProperty token)
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

        if (property.Value is not JObject propertyValue) throw new InvalidConfigException("Invalid config formatting");

        (JToken value, ConfigSetting setting) = GetValue(code, name, comment, propertyValue);
        JProperty result = new(name, value);
        return (result, setting);
    }

    private (JToken, ConfigSetting) GetValue(string code, string name, string? comment, JObject property)
    {
        if (!property.ContainsKey("default") || property["default"] is not JToken defaultValue)
        {
            throw new InvalidConfigException($"Invalid or absent default value for '{code}' setting");
        }

        if (property.ContainsKey("mapping") && property["mapping"] is JObject mapping)
        {
            return GetMappingValue(code, name, comment, defaultValue, mapping, property);
        }

        if (property.ContainsKey("range") && property["range"] is JObject range)
        {
            return GetRangeValue(code, name, comment, defaultValue, range, property);
        }

        if (property.ContainsKey("values") && property["values"] is JArray values)
        {
            return GetValuesValue(code, name, comment, defaultValue, values, property);
        }

        ConfigSetting setting = new(name, new(defaultValue), mSettingType, comment, null, null, GetWeight(property));
        mSettings.Add(code, setting);
        return (defaultValue, setting);
    }
    private (JToken, ConfigSetting) GetMappingValue(string code, string name, string? comment, JToken defaultValue, JObject mapping, JObject property)
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

        ConfigSetting setting = new(name, new(validatedValue), mSettingType, comment, new(settingMapping), value, GetWeight(property));
        mSettings.Add(code, setting);
        return (new JValue(value), setting);
    }
    private (JToken, ConfigSetting) GetRangeValue(string code, string name, string? comment, JToken defaultValue, JObject range, JObject property)
    {

        JsonObject? min = range.ContainsKey("min") ? new(range["min"]) : null;
        JsonObject? max = range.ContainsKey("max") ? new(range["max"]) : null;
        JsonObject? step = range.ContainsKey("step") ? new(range["step"]) : null;
        Validation parsedValidation = new(min, max, step);

        ConfigSetting setting = new(name, new(defaultValue), mSettingType, comment, parsedValidation, null, GetWeight(property));
        mSettings.Add(code, setting);
        return (defaultValue, setting);
    }
    private (JToken, ConfigSetting) GetValuesValue(string code, string name, string? comment, JToken defaultValue, JArray values, JObject property)
    {
        List<JsonObject> parsedValues = new();
        foreach (JToken value in values)
        {
            parsedValues.Add(new(value));
        }

        ConfigSetting setting = new(name, new(defaultValue), mSettingType, comment, new(parsedValues), null, GetWeight(property));
        mSettings.Add(code, setting);
        return (defaultValue, setting);
    }
    private float GetWeight(JObject property)
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