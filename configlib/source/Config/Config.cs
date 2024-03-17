using ConfigLib.Formatting;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using YamlDotNet.Serialization;

namespace ConfigLib;

public class Config : IConfig
{
    public string ConfigFilePath { get; private set; }
    public int Version { get; private set; }

    public Config(ICoreAPI api, string domain, JsonObject json)
    {
        _api = api;
        _domain = domain;
        _json = json;

        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");

        try
        {
            Parse(json, out _settings, out _configBlocks, out _defaultYaml);
            _clientSideSettings = _settings;
            _serverSideSettings = _settings;
            _patches = new(api, this);
        }
        catch (ConfigLibException exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception.Message}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, JsonObject json, Dictionary<string, ConfigSetting> serverSideSettings)
    {
        _api = api;
        _domain = domain;
        _json = json;

        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");

        try
        {
            Parse(json, out _settings, out _configBlocks, out _defaultYaml, checkVersion: false);
            DistributeSettingsBySides(serverSideSettings);
            _patches = new(api, this);
        }
        catch (ConfigLibException exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception.Message}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }

    internal void Apply() => _patches.Apply();
    internal JsonObject Definition => _json;
    internal SortedDictionary<float, IConfigBlock> ConfigBlocks => _configBlocks;
    internal Dictionary<string, ConfigSetting> Settings => _settings;

    public ISetting? GetSetting(string code)
    {
        if (!_settings.ContainsKey(code)) return null;
        return _settings[code];
    }
    public void WriteToFile()
    {
        try
        {
            string yaml;
            if (_api is ICoreClientAPI { IsSinglePlayer: false })
            {
                yaml = ToYaml(_clientSideSettings.Values);
            }
            else
            {
                yaml = ToYaml(_settings.Values);
            }

            WriteYaml(yaml);
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying to deserialize yaml and write it to file for '{_domain}' config.\nException: {exception}\n");
        }

    }
    public bool ReadFromFile()
    {
        try
        {
            string yaml = ReadYaml(_defaultYaml);

            if (_api is ICoreClientAPI { IsSinglePlayer: false })
            {
                return FromYaml(_clientSideSettings.Values, yaml);
            }
            else
            {
                return FromYaml(_settings.Values, yaml);
            }
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying read YAML file and parse it for'{_domain}' config.\nException: {exception}\n");
            return false;
        }
    }
    public void RestoreToDefaults()
    {
        try
        {
            if (_api is ICoreClientAPI { IsSinglePlayer: false })
            {
                FromYaml(_clientSideSettings.Values, _defaultYaml);
            }
            else
            {
                FromYaml(_settings.Values, _defaultYaml);
            }
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying to restore settings to defaults for '{_domain}' config.\nException: {exception}\n");
        }
    }


    private readonly ICoreAPI _api;
    private readonly string _domain;
    private readonly Dictionary<string, ConfigSetting> _settings;
    private readonly Dictionary<string, ConfigSetting> _clientSideSettings = new();
    private readonly Dictionary<string, ConfigSetting> _serverSideSettings = new();
    private readonly SortedDictionary<float, IConfigBlock> _configBlocks;
    private readonly JsonObject _json;
    private readonly ConfigPatches _patches;
    private readonly string _defaultYaml;

    private void DistributeSettingsBySides(Dictionary<string, ConfigSetting> serverSideSettings)
    {
        foreach ((string code, ConfigSetting setting) in _settings)
        {
            _clientSideSettings.Add(code, setting);

            if (!setting.ClientSide && serverSideSettings.ContainsKey(code))
            {
                _settings[code] = serverSideSettings[code];
                _serverSideSettings.Add(code, serverSideSettings[code]);
            }
            else
            {
                _serverSideSettings.Add(code, _settings[code]);
            }
        }
    }
    private void Parse(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, out string defaultConfig, bool checkVersion = true)
    {
        Version = FromJson(json, out settings, out configBlocks);
        defaultConfig = ToYaml(settings.Values);
        string yamlConfig = ReadYaml(defaultConfig);
        bool valid = FromYaml(settings.Values, yamlConfig);
        if (checkVersion && !valid)
        {
            WriteYaml(defaultConfig);
            FromYaml(settings.Values, defaultConfig);
        }
    }

    #region Files
    private string ReadYaml(string defaultConfig)
    {
        if (Path.Exists(ConfigFilePath))
        {
            try
            {
                using StreamReader outputFile = new(ConfigFilePath);
                return outputFile.ReadToEnd();
            }
            catch
            {
                _api.Logger.Notification($"[Config lib] [config domain: {_domain}] Was not able to read settings, will create default settings file: {ConfigFilePath}");
                using StreamWriter outputFile = new(ConfigFilePath);
                outputFile.Write(defaultConfig);
            }
        }
        else
        {
            _api.Logger.Notification($"[Config lib] [config domain: {_domain}] Creating default settings file: {ConfigFilePath}");
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(defaultConfig);
        }

        return defaultConfig;
    }
    private void WriteYaml(string yaml)
    {
        try
        {
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(yaml);
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying to deserialize yaml and write it to file for '{_domain}' config.\nException: {exception}\n");
        }

    }
    #endregion

    #region Serialization
    private bool FromYaml(IEnumerable<ConfigSetting> settings, string yaml)
    {
        ValuesFromYaml(out Dictionary<string, JsonObject> values, yaml);

        if (Version != -1 && (!values.ContainsKey("version") || ConvertVersion(values["version"].Token) != Version)) return false;

        foreach (ConfigSetting setting in settings)
        {
            if (!values.ContainsKey(setting.YamlCode)) continue;

            if (setting.Validation?.Mapping == null)
            {
                JToken converted = ConvertValue(values[setting.YamlCode].Token, setting.SettingType);
                setting.Value = new(converted);
                continue;
            }

            string key = values[setting.YamlCode].AsString("");
            if (setting.Validation?.Mapping?.ContainsKey(key) == true)
            {
                setting.Value = setting.Validation.Mapping[key];
                setting.MappingKey = key;
            }
        }

        return true;
    }
    private string ToYaml(IEnumerable<ConfigSetting> settings)
    {
        return ConstructYaml(settings, _configBlocks);
    }
    private static int FromJson(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks)
    {
        int version = 0;
        settings = new();

        SettingsFromJson(settings, json, ref version);

        FormattingFronJson(json, out SortedDictionary<float, IConfigBlock> formatting);
        configBlocks = CombineConfigBlocks(formatting, settings.Values);

        return version;
    }

    private static SortedDictionary<float, IConfigBlock> CombineConfigBlocks(SortedDictionary<float, IConfigBlock> formatting, IEnumerable<ConfigSetting> settings)
    {
        float delta = 1E-10f;
        float increment = delta;

        SortedDictionary<float, IConfigBlock> configBlocks = new();
        foreach (ConfigSetting setting in settings)
        {
            float weight = setting.SortingWeight;
            if (configBlocks.ContainsKey(weight))
            {
                weight += increment;
                increment += delta;
            }

            configBlocks.Add(weight, setting);
        }

        foreach ((float sortingWeight, IConfigBlock block) in formatting)
        {
            float weight = sortingWeight;
            if (configBlocks.ContainsKey(weight))
            {
                weight -= increment;
                increment += delta;
            }

            configBlocks.Add(weight, block);
        }

        return configBlocks;
    }
    private static void FormattingFronJson(JsonObject json, out SortedDictionary<float, IConfigBlock> formatting)
    {
        formatting = new();
        float delta = 1E-10f;
        float increment = delta;

        foreach ((_, ConfigSetting setting) in _settings)
        {
            float weight = setting.SortingWeight;
            if (formatting.ContainsKey(weight))
            {
                weight += _increment;
                _increment += _delta;
            }
            formatting.Add(weight, setting);
        }

        if (json.KeyExists("formatting") && json["formatting"].IsArray())
        {
            foreach (JsonObject block in json["formatting"].AsArray())
            {
                IFormattingBlock formattingBlock = ParseBlock(block);
                formatting.Add(formattingBlock.SortingWeight, formattingBlock);
            }
        }
    }
    private static IFormattingBlock ParseBlock(JsonObject block)
    {
        switch (block["type"]?.AsString())
        {
            case "separator":
                return new Separator(block);
        }

        return new Blank();
    }
    private static int ConvertVersion(JToken value)
    {
        return new JsonObject(ConvertValue(value, ConfigSettingType.Integer)).AsInt(0);
    }
    private static string ConstructYaml(IEnumerable<ConfigSetting> settings, SortedDictionary<float, IConfigBlock> formatting)
    {
        SettingsToYaml(settings, out SortedDictionary<float, string> yaml);

        foreach ((float weight, IConfigBlock block) in formatting.Where(entry => entry.Value is IFormattingBlock))
        {
            yaml.Add(weight, (block as IFormattingBlock)?.Yaml ?? "");
        }

        return yaml.Select(entry => entry.Value).Aggregate((first, second) => $"{first}\n{second}");
    }
    private static void ValuesFromYaml(out Dictionary<string, JsonObject> values, string yaml)
    {
        IDeserializer deserializer = new DeserializerBuilder().Build();
        object? yamlObject = deserializer.Deserialize(yaml);

        ISerializer serializer = new SerializerBuilder()
            .JsonCompatible()
            .Build();

        JObject json = JObject.Parse(serializer.Serialize(yamlObject));

        values = new();
        foreach ((string code, JToken? value) in json)
        {
            if (value == null) continue;
            values.Add(code, new(value));
        }
    }
    private static JToken ConvertValue(JToken? value, ConfigSettingType type)
    {
        string? strValue = (string?)(value as JValue)?.Value;
        if (strValue == null) return value ?? new JValue(strValue);

        CultureInfo culture = new("en-US");

        switch (type)
        {
            case ConfigSettingType.Boolean:
                bool boolValue = bool.Parse(strValue);
                return new JValue(boolValue);
            case ConfigSettingType.Float:
                float floatValue = float.Parse(strValue, NumberStyles.Float, culture);
                return new JValue(floatValue);
            case ConfigSettingType.Integer:
                int intValue = int.Parse(strValue, NumberStyles.Integer, culture);
                return new JValue(intValue);
            case ConfigSettingType.String:
                return new JValue(strValue);
            default:
                return value ?? new JValue(strValue);
        }
    }
    private static void SettingsToYaml(IEnumerable<ConfigSetting> settings, out SortedDictionary<float, string> yaml)
    {
        float delta = 1E-10f;
        float increment = delta;

        yaml = new();
        foreach (ConfigSetting setting in settings)
        {
            float weight = setting.SortingWeight < 0 ? 0 : setting.SortingWeight;
            if (yaml.ContainsKey(weight))
            {
                weight += increment;
                increment += delta;
            }
            yaml.Add(weight, setting.ToYaml());
        }
    }
    private static void SettingsFromJson(Dictionary<string, ConfigSetting> settings, JsonObject definition, ref int version)
    {
        version = definition["version"]?.AsInt(0) ?? 0;

        if (definition["settings"].KeyExists("boolean"))
        {
            ParseSettingsCategory(definition["settings"]["boolean"], settings, ConfigSettingType.Boolean);
        }

        if (definition["settings"].KeyExists("integer"))
        {
            ParseSettingsCategory(definition["settings"]["integer"], settings, ConfigSettingType.Integer);
        }

        if (definition["settings"].KeyExists("float"))
        {
            ParseSettingsCategory(definition["settings"]["float"], settings, ConfigSettingType.Float);
        }

        if (definition["settings"].KeyExists("number"))
        {
            ParseSettingsCategory(definition["settings"]["number"], settings, ConfigSettingType.Float);
        }

        if (definition["settings"].KeyExists("string"))
        {
            ParseSettingsCategory(definition["settings"]["string"], settings, ConfigSettingType.String);
        }

        if (definition["settings"].KeyExists("other"))
        {
            ParseSettingsCategory(definition["settings"]["other"], settings, ConfigSettingType.Other);
        }
    }
    private static void ParseSettingsCategory(JsonObject category, Dictionary<string, ConfigSetting> settings, ConfigSettingType settingType)
    {
        foreach (JToken item in category.Token)
        {
            if (item is not JProperty property)
            {
                continue;
            }

            string code = property.Name;
            ConfigSetting setting = ConfigSetting.FromJson(new(property.Value), settingType);
            settings.Add(code, setting);
        }
    }
    #endregion
}
