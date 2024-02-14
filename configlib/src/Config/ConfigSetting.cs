using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace ConfigLib;

public class ConfigSetting : ISetting
{
    public JsonObject Value { get; set; }
    public JsonObject DefaultValue { get; private set; }
    public ConfigSettingType SettingType { get; private set; }
    public string YamlCode { get; private set; }
    public string? MappingKey { get; set; }
    public string? Comment { get; set; }
    public Validation? Validation { get; private set; }
    public float SortingWeight { get; private set; }

    public ConfigSetting(string yamlCode, JsonObject defaultValue, ConfigSettingType settingType, string? comment = null, Validation? validation = null, string? mappingKey = null, float sortingWeight = 0)
    {
        Value = defaultValue;
        DefaultValue = defaultValue;
        SettingType = settingType;
        Validation = validation;
        MappingKey = mappingKey;
        Comment = comment;
        YamlCode = yamlCode;
        SortingWeight = sortingWeight;
    }
    public ConfigSetting(ConfigSettingPacket settings)
    {
        Value = new(Unwrap(JObject.Parse(settings.Value)));
        DefaultValue = new(Unwrap(JObject.Parse(settings.DefaultValue)));
        SettingType = settings.SettingType;
        YamlCode = settings.YamlCode;
        MappingKey = settings.MappingKey;
        Comment = settings.Comment;
        SortingWeight= settings.SortingWeight;
        if (settings.Validation != null) Validation = settings.Validation;
    }

    public static implicit operator ConfigSettingPacket(ConfigSetting setting) => new (setting);

    static private JToken Unwrap(JObject token)
    {
        if (token["value"] is not JToken value) return new JValue("<invalid>");
        return value;
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

    public ConfigSettingPacket() { }
    public ConfigSettingPacket(ConfigSetting settings)
    {
        Value = Wrap(settings.Value.Token).ToString();
        DefaultValue = Wrap(settings.DefaultValue.Token).ToString();
        SettingType = settings.SettingType;
        YamlCode = settings.YamlCode;
        MappingKey = settings.MappingKey;
        Comment = settings.Comment;
        if (settings.Validation != null) Validation = settings.Validation;
    }

    public static implicit operator ConfigSetting(ConfigSettingPacket setting) => new(setting);

    static private JObject Wrap(JToken token)
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
    public Validation(ValidationPacket validation)
    {
        Minimum = validation.Minimum != null ? new(Unwrap(JObject.Parse(validation.Minimum))) : null;
        Maximum = validation.Maximum != null ? new(Unwrap(JObject.Parse(validation.Maximum))) : null;
        Step = validation.Step != null ? new(Unwrap(JObject.Parse(validation.Step))) : null;
        Mapping = ToMapping(validation.Mapping);
        Values = ToValues(validation.Values);
    }

    public static implicit operator Validation(ValidationPacket setting) => new(setting);

    static private Dictionary<string, JsonObject>? ToMapping(Dictionary<string, string>? mapping)
    {
        if (mapping == null) return null;

        Dictionary<string, JsonObject> output = new();
        foreach ((var key, var value) in mapping)
        {
            output.Add(key, new(Unwrap(JObject.Parse(value))));
        }
        return output;
    }
    static private List<JsonObject>? ToValues(List<string>? values)
    {
        if (values == null) return null;

        List<JsonObject> output = new();
        foreach (var value in values)
        {
            output.Add(new(Unwrap(JObject.Parse(value))));
        }
        return output;
    }
    static private JToken Unwrap(JObject token)
    {
        if (token["value"] is not JToken value) return new JValue("<invalid>");
        return value;
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

    static private Dictionary<string, string>? ToMapping(Dictionary<string, JsonObject>? mapping)
    {
        if (mapping == null) return null;

        Dictionary<string, string> output = new();
        foreach ((var key, var value) in mapping)
        {
            output.Add(key, Wrap(value.Token).ToString());
        }
        return output;
    }
    static private List<string>? ToValues(List<JsonObject>? values)
    {
        if (values == null) return null;

        List<string> output = new();
        foreach (var value in values)
        {
            output.Add(Wrap(value.Token).ToString());
        }
        return output;
    }
    static private JObject Wrap(JToken token)
    {
        JObject result = new()
        {
            { "value", token }
        };
        return result;
    }
}
