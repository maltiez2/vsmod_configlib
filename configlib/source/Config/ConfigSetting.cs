﻿using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Text;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

public class ConfigSetting : ISetting
{
    public JsonObject Value
    {
        get => _value;
        set
        {
            bool changed = _value.ToString() != value.ToString();
            _value = value;
            if (changed) SettingChanged?.Invoke(this);
        }
    }
    public JsonObject DefaultValue { get; internal set; }
    public ConfigSettingType SettingType { get; internal set; }
    public string YamlCode { get; internal set; }
    public string? MappingKey { get; internal set; }
    public string? Comment { get; internal set; }
    public Validation? Validation { get; internal set; }
    public float SortingWeight { get; internal set; }
    public string? InGui { get; internal set; }
    public bool Logarithmic { get; internal set; }
    public bool ClientSide { get; internal set; }

    public event Action<ConfigSetting>? SettingChanged;

    public ConfigSetting(string yamlCode, JsonObject defaultValue, ConfigSettingType settingType)
    {
        _value = defaultValue;
        DefaultValue = defaultValue;
        SettingType = settingType;
        YamlCode = yamlCode;
        InGui = yamlCode;
    }
    public ConfigSetting(ConfigSettingPacket settings)
    {
        _value = new(Unwrap(JObject.Parse(settings.Value)));
        DefaultValue = new(Unwrap(JObject.Parse(settings.DefaultValue)));
        SettingType = settings.SettingType;
        YamlCode = settings.YamlCode;
        MappingKey = settings.MappingKey;
        Comment = settings.Comment;
        SortingWeight = settings.SortingWeight;
        InGui = settings.InGui;
        Logarithmic = settings.Logarithmic;
        ClientSide = settings.ClientSide;
        if (settings.Validation != null) Validation = settings.Validation;
    }

    public static implicit operator ConfigSettingPacket(ConfigSetting setting) => new(setting);

    internal string ToYaml()
    {
        string yamlToken = SerializeToken();
        yamlToken = AddComments(yamlToken);
        return yamlToken;
    }

    #region YAML
    private string SerializeToken()
    {
        JProperty token;
        if (MappingKey != null)
        {
            token = new(YamlCode, new JValue(MappingKey));
        }
        else
        {
            token = new(YamlCode, Value.Token);
        }


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

    private string AddComments(string yamlToken)
    {
        (string first, string other) = SplitToken(yamlToken);
        string comment = GetPreComment().Replace("\n", "");
        if (comment != "") comment += "\n";
        string inline = GetInlineComment();
        if (inline != "") inline = $" # {inline}";
        return $"{comment}{first}{inline}{other}";
    }
    private static (string firstLine, string remainder) SplitToken(string token)
    {
        string[] split = token.Split("\r\n", 2, StringSplitOptions.RemoveEmptyEntries);

        return (split[0], split.Length > 1 ? $"\r\n{split[1]}" : "");
    }
    private string GetPreComment()
    {
        return Comment == null ? "" : $"# {Comment}\n";
    }
    private string GetInlineComment()
    {
        if (Validation?.Mapping != null)
        {
            return ParseMappingComment();
        }

        if (Validation?.Values != null)
        {
            return ParseValuesComment();
        }

        return ParseRangeComment();
    }
    private string ParseMappingComment()
    {
        if (Validation == null || Validation.Mapping == null) return "";

        StringBuilder result = new();
        result.Append("value from: ");
        bool first = true;
        foreach ((string name, _) in Validation.Mapping)
        {
            if (!first) result.Append(", ");
            if (first) first = false;
            result.Append(name);
        }
        return result.ToString();
    }
    private string ParseRangeComment()
    {
        if (Validation == null || (Validation.Minimum == null && Validation.Maximum == null && Validation.Step == null)) return "";

        string minMax = (Validation.Minimum != null, Validation.Maximum != null) switch
        {
            (true, true) => $"from {Validation.Minimum} to {Validation.Maximum}",
            (true, false) => $"greater than {Validation.Minimum}",
            (false, true) => $"less than {Validation.Maximum}",
            _ => ""
        };

        if (Validation.Step != null && minMax != "")
        {
            return $"{minMax} with step of {Validation.Step}";
        }
        else
        {
            return minMax;
        }
    }
    private string ParseValuesComment()
    {
        if (Validation == null || Validation.Values == null) return "";

        StringBuilder result = new();
        result.Append("value from: ");
        bool first = true;
        foreach (string value in Validation.Values.Select(element => element.ToString()))
        {
            if (!first) result.Append(", ");
            if (first) first = false;
            result.Append(value);
        }
        return result.ToString();
    }
    #endregion

    private JsonObject _value;
    private static JToken Unwrap(JObject token)
    {
        if (token["value"] is not JToken value) return new JValue("<invalid>");
        return value;
    }

    internal static ConfigSetting FromJson(JsonObject json, ConfigSettingType settingType)
    {
        ConfigSetting setting = new(
            yamlCode: json["name"].AsString(),
            defaultValue: json["default"],
            settingType
            )
        {
            Value = json["default"],
            Comment = json["comment"].AsString(),
            InGui = json["ingui"].AsString(json["name"].AsString()),
            ClientSide = json["clientSide"].AsBool(false),
            SortingWeight = json["weight"].AsFloat(0),
            Logarithmic = json["logarithmic"].AsBool(false)
        };

        (string? mappingKey, JsonObject? value, Validation? validation) = Validation.FromJson(json, setting.Value.AsString());
        if (value != null) setting.Value = value;
        setting.MappingKey = mappingKey;
        setting.Validation = validation;

        return setting;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ConfigSettingPacket
{
    public string Value { get; set; } = "";
    public string DefaultValue { get; set; } = "";
    public ConfigSettingType SettingType { get; set; } = ConfigSettingType.Float;
    public string YamlCode { get; set; } = "";
    public string? MappingKey { get; set; }
    public string? Comment { get; set; }
    public ValidationPacket? Validation { get; private set; }
    public float SortingWeight { get; private set; } = 0;
    public string? InGui { get; private set; } = null;
    public bool Logarithmic { get; private set; }
    public bool ClientSide { get; private set; }

    public ConfigSettingPacket() { }
    public ConfigSettingPacket(ConfigSetting settings)
    {
        Value = Wrap(settings.Value.Token).ToString();
        DefaultValue = Wrap(settings.DefaultValue.Token).ToString();
        SettingType = settings.SettingType;
        YamlCode = settings.YamlCode;
        MappingKey = settings.MappingKey;
        Comment = settings.Comment;
        SortingWeight = settings.SortingWeight;
        InGui = settings.InGui;
        Logarithmic = settings.Logarithmic;
        ClientSide = settings.ClientSide;
        if (settings.Validation != null) Validation = settings.Validation;
    }

    public static implicit operator ConfigSetting(ConfigSettingPacket setting) => new(setting);

    private static JObject Wrap(JToken token)
    {
        JObject result = new()
        {
            { "value", token }
        };
        return result;
    }
}

public class Validation
{
    public Dictionary<string, JsonObject>? Mapping { get; private set; }
    public JsonObject? Minimum { get; private set; }
    public JsonObject? Maximum { get; private set; }
    public JsonObject? Step { get; private set; }
    public List<JsonObject>? Values { get; private set; }

    public Validation() { }
    public Validation(Dictionary<string, JsonObject> mapping) => Mapping = mapping;
    public Validation(List<JsonObject> values) => Values = values;
    public Validation(JsonObject? min, JsonObject? max = null, JsonObject? step = null)
    {
        Minimum = min;
        Maximum = max;
        Step = step;
    }
    internal Validation(ValidationPacket validation)
    {
        Minimum = validation.Minimum != null ? new(Unwrap(JObject.Parse(validation.Minimum))) : null;
        Maximum = validation.Maximum != null ? new(Unwrap(JObject.Parse(validation.Maximum))) : null;
        Step = validation.Step != null ? new(Unwrap(JObject.Parse(validation.Step))) : null;
        Mapping = ToMapping(validation.Mapping);
        Values = ToValues(validation.Values);
    }

    public static implicit operator Validation(ValidationPacket setting) => new(setting);

    private static Dictionary<string, JsonObject>? ToMapping(Dictionary<string, string>? mapping)
    {
        if (mapping == null) return null;

        Dictionary<string, JsonObject> output = new();
        foreach ((string? key, string? value) in mapping)
        {
            output.Add(key, new(Unwrap(JObject.Parse(value))));
        }
        return output;
    }
    private static List<JsonObject>? ToValues(List<string>? values)
    {
        if (values == null) return null;

        List<JsonObject> output = new();
        foreach (string value in values)
        {
            output.Add(new(Unwrap(JObject.Parse(value))));
        }
        return output;
    }
    private static JToken Unwrap(JObject token)
    {
        if (token["value"] is not JToken value) return new JValue("<invalid>");
        return value;
    }
    internal static (string? mappingKey, JsonObject? value, Validation? validation) FromJson(JsonObject json, string? value)
    {
        Validation validation = new();

        if (value != null && json.KeyExists("mapping"))
        {
            SetMapping(json["mapping"], validation);
            return (value, validation.Mapping?[value], validation);
        }

        if (json.KeyExists("range"))
        {
            SetRange(json["range"], validation);
            return (null, null, validation);
        }

        if (json.KeyExists("values"))
        {
            SetValues(json["values"].AsArray(), validation);
            return (null, null, validation);
        }


        return (null, null, null);
    }

    private static void SetMapping(JsonObject mapping, Validation validation)
    {
        if (mapping.Token is not JObject mappingObject)
        {
            throw new InvalidConfigException($"Mapping has wrong format");
        }

        validation.Mapping = new();

        foreach ((string key, JToken? mappingValue) in mappingObject)
        {
            if (mappingValue == null)
            {
                throw new InvalidConfigException($"Mapping value for entry '{key}' is not valid value");
            }

            validation.Mapping.Add(key, new(mappingValue));
        }
    }
    private static void SetRange(JsonObject range, Validation validation)
    {
        JsonObject? min = range.KeyExists("min") ? range["min"] : null;
        JsonObject? max = range.KeyExists("max") ? range["max"] : null;
        JsonObject? step = range.KeyExists("step") ? range["step"] : null;

        validation.Minimum = min;
        validation.Maximum = max;
        validation.Step = step;
    }
    private static void SetValues(JsonObject[] values, Validation validation)
    {
        validation.Values = values.ToList();
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ValidationPacket
{
    public string? Minimum { get; private set; }
    public string? Maximum { get; private set; }
    public string? Step { get; private set; }
    public Dictionary<string, string>? Mapping { get; private set; }
    public List<string>? Values { get; private set; }

    public ValidationPacket() { }
    public ValidationPacket(Validation validation)
    {
        Minimum = validation.Minimum != null ? Wrap(validation.Minimum.Token).ToString() : null;
        Maximum = validation.Maximum != null ? Wrap(validation.Maximum.Token).ToString() : null;
        Step = validation.Step != null ? Wrap(validation.Step.Token).ToString() : null;
        Mapping = ToMapping(validation.Mapping);
        Values = ToValues(validation.Values);
    }

    public static implicit operator ValidationPacket(Validation validation) => new(validation);

    private static Dictionary<string, string>? ToMapping(Dictionary<string, JsonObject>? mapping)
    {
        if (mapping == null) return null;

        Dictionary<string, string> output = new();
        foreach ((string? key, JsonObject? value) in mapping)
        {
            output.Add(key, Wrap(value.Token).ToString());
        }
        return output;
    }
    private static List<string>? ToValues(List<JsonObject>? values)
    {
        if (values == null) return null;

        List<string> output = new();
        foreach (JsonObject value in values)
        {
            output.Add(Wrap(value.Token).ToString());
        }
        return output;
    }
    private static JObject Wrap(JToken token)
    {
        JObject result = new()
        {
            { "value", token }
        };
        return result;
    }
}
