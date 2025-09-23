using Newtonsoft.Json.Linq;
using ProtoBuf;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
            if (changed)
            {
                ChangedSinceLastSave = true;
                SettingChanged?.Invoke(this);
            }
        }
    }
    public JsonObject DefaultValue { get; internal set; }
    public ConfigSettingType SettingType { get; internal set; }
    public string YamlCode { get; internal set; }
    public string? MappingKey
    {
        get => _mappingKey;
        set
        {
            _mappingKey = value;
            if (_mappingKey != null && Validation != null && Validation.Mapping != null)
            {
                Value = Validation.Mapping[_mappingKey];
            }
        }
    }
    public string? Comment { get; internal set; }
    public Validation? Validation { get; internal set; }
    public float SortingWeight { get; internal set; }
    public string? InGui { get; internal set; }
    public bool Logarithmic { get; internal set; }
    public bool ClientSide { get; internal set; }
    public bool Hide { get; internal set; }
    public string Link { get; internal set; } = "";
    public bool ChangedSinceLastSave {
        get;
        internal set;
    }

    public event Action<ConfigSetting>? SettingChanged;

    public ConfigSetting(string yamlCode, JsonObject defaultValue, ConfigSettingType settingType)
    {
        _value = defaultValue;
        DefaultValue = defaultValue;
        SettingType = settingType;
        YamlCode = yamlCode;
        InGui = yamlCode;
        Hide = false;
    }
    public ConfigSetting(ConfigSettingPacket settings)
    {
        _value = new(Unwrap(JObject.Parse(settings.Value)));
        DefaultValue = _value;
        SettingType = ConfigSettingType.None;
        MappingKey = settings.MappingKey;
        ClientSide = settings.ClientSide;
        YamlCode = string.Empty;
        Hide = false;
    }
    private ConfigSetting(ConfigSetting previous)
    {
        _value = previous.Value.Clone();
        DefaultValue = previous.DefaultValue.Clone();
        SettingType = previous.SettingType;
        YamlCode = previous.YamlCode;
        MappingKey = previous.MappingKey;
        Comment = previous.Comment;
        Validation = previous.Validation;
        SortingWeight = previous.SortingWeight;
        InGui = previous.InGui;
        Logarithmic = previous.Logarithmic;
        ClientSide = previous.ClientSide;
        SettingChanged = previous.SettingChanged;
        Hide = previous.Hide;
        Link = previous.Link;
    }

    internal void Changed() => Debug.Write("");// SettingChanged?.Invoke(this);
    internal void SetValueFrom(ConfigSetting value)
    {
        Value = value._value.Clone();
        MappingKey = value.MappingKey;
    }

    public static implicit operator ConfigSettingPacket(ConfigSetting setting) => new(setting);

    public ConfigSetting Clone() => new(this);

    public bool AssignSettingValue(object target)
    {
        Type targetType = target.GetType();

        IEnumerable<(string code, FieldInfo field)> fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(field => (NormalizeName(field.Name), field));
        IEnumerable<(string code, PropertyInfo field)> properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(property => property.CanWrite).Select(property => (NormalizeName(property.Name), property));

        return AssignSettingValue(target, fields, properties);
    }
    internal bool AssignSettingValue(object target, IEnumerable<(string code, FieldInfo field)> fields, IEnumerable<(string code, PropertyInfo field)> properties)
    {
        string code = NormalizeName(YamlCode);

        bool anyAssigned = false;

        foreach ((_, FieldInfo? field) in fields.Where(entry => entry.code == code))
        {
            if (AssignSettingValue(target, field))
            {
                anyAssigned = true;
            }
        }

        foreach ((_, PropertyInfo? field) in properties.Where(entry => entry.code == code))
        {
            if (AssignSettingValue(target, field))
            {
                anyAssigned = true;
            }
        }

        return anyAssigned;
    }

    internal static string NormalizeName(string name) => name.ToLowerInvariant().Replace("_", "");
    private bool AssignSettingValue(object target, PropertyInfo? property)
    {
        if (property != null && property.CanWrite)
        {
            switch (SettingType)
            {
                case ConfigSettingType.Boolean:
                    property.SetValue(target, Value.AsBool());
                    break;
                case ConfigSettingType.Float:
                    property.SetValue(target, Value.AsFloat());
                    break;
                case ConfigSettingType.Integer:
                    property.SetValue(target, Value.AsInt());
                    break;
                case ConfigSettingType.String:
                    property.SetValue(target, Value.AsString());
                    break;
                case ConfigSettingType.Other:
                    property.SetValue(target, Value.ToAttribute());
                    break;
                case ConfigSettingType.Color:
                    property.SetValue(target, Value.AsString());
                    break;
            }
            return true;
        }

        return false;
    }
    private bool AssignSettingValue(object target, FieldInfo? field)
    {
        if (field != null)
        {
            switch (SettingType)
            {
                case ConfigSettingType.Boolean:
                    field.SetValue(target, Value.AsBool());
                    break;
                case ConfigSettingType.Float:
                    field.SetValue(target, Value.AsFloat());
                    break;
                case ConfigSettingType.Integer:
                    field.SetValue(target, Value.AsInt());
                    break;
                case ConfigSettingType.String:
                    field.SetValue(target, Value.AsString());
                    break;
                case ConfigSettingType.Other:
                    field.SetValue(target, Value.ToAttribute());
                    break;
                case ConfigSettingType.Color:
                    field.SetValue(target, Value.AsString());
                    break;
            }
            return true;
        }

        return false;
    }

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
        string comment = GetPreComment();
        if (comment != "") comment += "\n";
        if (Link != "") comment += $"# {Link}\n";
        string inline = GetInlineComment();
        if (inline != "") inline = $" # {inline}";
        string defaultValue = GetDefaultValueComment();
        if (defaultValue != "" && inline == "") defaultValue = $" # {defaultValue}";
        return $"{comment}{first}{inline}{defaultValue}{other}";
    }
    private static (string firstLine, string remainder) SplitToken(string token)
    {
        string[] split = token.Split("\r\n", 2, StringSplitOptions.RemoveEmptyEntries);

        return (split[0], split.Length > 1 ? $"\r\n{split[1]}" : "");
    }
    private string GetPreComment()
    {
        return Comment == null ? "" : $"\n# {Comment.Replace("\n", "\n# ")}";
    }
    private string GetDefaultValueComment()
    {
        return $" (default: {DefaultValue}) ";
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
    private string? _mappingKey;

    private static JToken Unwrap(JObject token)
    {
        if (token["value"] is not JToken value) return new JValue("<invalid>");
        return value;
    }
    private static string Localize(string value, string domain)
    {
        bool hasDomain = value.Contains(':');
        string langCode = hasDomain ? value : $"{domain}:{value}";
        return Lang.HasTranslation(langCode) ? Lang.Get(langCode) : Lang.Get(value);
    }
    internal static ConfigSetting FromJson(JsonObject json, ConfigSettingType settingType, string domain, string code, ICoreAPI api)
    {
        if (!json.KeyExists("default")) LoggerUtil.Error(api, typeof(ConfigSetting), $"Setting '{domain}' of type '{settingType}' does not have default value");

        ConfigSetting setting = new(
            yamlCode: json["name"].AsString(code),
            defaultValue: json["default"],
            settingType
            )
        {
            Value = json["default"],
            Comment = json["comment"].AsString(),
            InGui = json["ingui"].AsString(json["nameInGui"].AsString(json["name"].AsString(code))),
            ClientSide = json["clientSide"].AsBool(false),
            SortingWeight = json["weight"].AsFloat(0),
            Logarithmic = json["logarithmic"].AsBool(false),
            Hide = json["hide"].AsBool(false),
            Link = json["link"].AsString(""),
        };

        if (setting.InGui != null) setting.InGui = Localize(setting.InGui, domain);
        if (setting.Comment != null) setting.Comment = Localize(setting.Comment, domain);

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
    public string Value { get; set; } = string.Empty;
    public string? MappingKey { get; set; }
    public bool ClientSide { get; private set; }

    public ConfigSettingPacket() { }
    public ConfigSettingPacket(ConfigSetting settings)
    {
        Value = Wrap(settings.Value.Token).ToString();
        MappingKey = settings.MappingKey;
        ClientSide = settings.ClientSide;
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