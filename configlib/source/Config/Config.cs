using ConfigLib.Formatting;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using YamlDotNet.Serialization;

namespace ConfigLib;

public sealed class Config : IConfig
{
    public string ConfigFilePath { get; private set; }
    public int Version { get; private set; }
    public event Action<Config>? ConfigSaved;

    public Config(ICoreAPI api, string domain, JsonObject json)
    {
        _api = api;
        _domain = domain;
        _json = json;

        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");

        try
        {
            Parse(json, out _settings, out _configBlocks, out _defaultYaml, domain);
            _clientSideSettings = _settings;
            WriteToFile();
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
            Parse(json, out _settings, out _configBlocks, out _defaultYaml, domain, checkVersion: false);
            DistributeSettingsBySides(serverSideSettings);
            WriteToFile();
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
    public Config(ICoreAPI api, string domain, JsonObject json, string file)
    {
        _api = api;
        _domain = domain;
        _json = json;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;

        try
        {
            ParseJson(json, out _settings, out _configBlocks, out _defaultJson, domain);
            _clientSideSettings = _settings;
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
    public Config(ICoreAPI api, string domain, JsonObject json, string file, Dictionary<string, ConfigSetting> serverSideSettings)
    {
        _api = api;
        _domain = domain;
        _json = json;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;
        JsonFilePath = file;

        try
        {
            ParseJson(json, out _settings, out _configBlocks, out _defaultJson, domain);
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
    internal ConfigType FileType => _configType;
    internal string JsonFilePath { get; } = "";
    internal string Domain => _domain;

    public ISetting? GetSetting(string code)
    {
        if (!_settings.ContainsKey(code)) return null;
        return _settings[code];
    }
    public void WriteToFile()
    {
        try
        {
            string content = "";

            switch (_configType)
            {
                case ConfigType.YAML:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        content = ToYaml(_clientSideSettings.Values);
                    }
                    else
                    {
                        content = ToYaml(_settings.Values);
                    }
                    break;
                case ConfigType.JSON:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        content = ToJson(_settings.Values, ReadConfigFile(_defaultJson), onlyClientSide: true);
                    }
                    else
                    {
                        content = ToJson(_settings.Values, ReadConfigFile(_defaultJson));
                    }
                    break;
            }

            WriteConfigFile(content);
            ConfigSaved?.Invoke(this);
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
            string content = ReadConfigFile(_defaultYaml);

            switch (_configType)
            {
                case ConfigType.YAML:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        return FromYaml(_clientSideSettings.Values, content);
                    }
                    else
                    {
                        return FromYaml(_settings.Values, content);
                    }
                case ConfigType.JSON:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        return FromJson(_settings.Values, content, onlyClientSide: true);
                    }
                    else
                    {
                        return FromJson(_settings.Values, content);
                    }
            }

            return false;
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
            switch (_configType)
            {
                case ConfigType.YAML:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        FromYaml(_clientSideSettings.Values, _defaultYaml);
                    }
                    else
                    {
                        FromYaml(_settings.Values, _defaultYaml);
                    }
                    break;
                case ConfigType.JSON:
                    if (_api is ICoreClientAPI { IsSinglePlayer: false })
                    {
                        FromJson(_settings.Values, _defaultJson, onlyClientSide: true);
                    }
                    else
                    {
                        FromJson(_settings.Values, _defaultJson);
                    }
                    break;
            }
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying to restore settings to defaults for '{_domain}' config.\nException: {exception}\n");
        }
    }

    internal enum ConfigType
    {
        YAML,
        JSON
    }

    private readonly ICoreAPI _api;
    private readonly string _domain;
    private readonly Dictionary<string, ConfigSetting> _settings;
    private readonly Dictionary<string, ConfigSetting> _clientSideSettings = new();
    private readonly SortedDictionary<float, IConfigBlock> _configBlocks;
    private readonly JsonObject _json;
    private readonly ConfigPatches _patches;
    private readonly string _defaultYaml = "";
    private readonly string _defaultJson = "";
    private readonly ConfigType _configType = ConfigType.YAML;

    private void DistributeSettingsBySides(Dictionary<string, ConfigSetting> serverSideSettings)
    {
        foreach ((string code, ConfigSetting setting) in _settings)
        {
            bool serverSide = serverSideSettings.ContainsKey(code) && !serverSideSettings[code].ClientSide;

            if (serverSide)
            {
                _clientSideSettings.Add(code, setting.Clone());
                _settings[code].SetValueFrom(serverSideSettings[code]);
            }
            else
            {
                _clientSideSettings.Add(code, setting);
            }
        }
    }
    private void Parse(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, out string defaultConfig, string domain, bool checkVersion = true)
    {
        Version = FromJsonDefinition(json, out settings, out configBlocks, domain);
        defaultConfig = ToYaml(settings.Values);
        string yamlConfig = ReadConfigFile(defaultConfig);
        bool valid = FromYaml(settings.Values, yamlConfig);
        if (checkVersion && !valid)
        {
            WriteConfigFile(defaultConfig);
            FromYaml(settings.Values, defaultConfig);
        }
    }
    private void ParseJson(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, out string defaultConfig, string domain)
    {
        Version = FromJsonDefinition(json, out settings, out configBlocks, domain);
        string jsonConfig = ReadConfigFile(ConfigFilePath);

        JsonObject jsonConfigObject = new(JObject.Parse(jsonConfig));
        JsonObject jsonCopy = jsonConfigObject.Clone();
        foreach (ConfigSetting setting in settings.Values)
        {
            JsonObjectPath jsonPath = new(setting.YamlCode);
            JsonObject? value = jsonPath.Get(jsonConfigObject);
            jsonPath.Set(jsonCopy, setting.Value);
            setting.Value = value ?? setting.DefaultValue;
        }

        defaultConfig = jsonCopy.Token.ToString(Newtonsoft.Json.Formatting.Indented);
    }

    #region Files
    private string ReadConfigFile(string defaultConfig)
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
    private void WriteConfigFile(string content)
    {
        try
        {
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(content);
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
        return ConstructYaml(settings, _configBlocks, Version);
    }
    private static int FromJsonDefinition(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, string domain)
    {
        int version = 0;
        settings = new();

        SettingsFromJson(settings, json, ref version, domain);

        FormattingFromJson(json, out SortedDictionary<float, IConfigBlock> formatting);
        configBlocks = CombineConfigBlocks(formatting, settings.Values);

        return version;
    }
    private static string ToJson(IEnumerable<ConfigSetting> settings, string defaultJson, bool onlyClientSide = false)
    {
        JsonObject config = new(JObject.Parse(defaultJson));
        foreach (ConfigSetting setting in settings.Where(item => !onlyClientSide || item.ClientSide))
        {
            JsonObjectPath jsonPath = new(setting.YamlCode);
            jsonPath.Set(config, setting.Value);
        }
        return config.Token.ToString(Newtonsoft.Json.Formatting.Indented);
    }
    private static bool FromJson(IEnumerable<ConfigSetting> settings, string json, bool onlyClientSide = false)
    {
        JsonObject jsonConfigObject = new(JObject.Parse(json));
        foreach (ConfigSetting setting in settings.Where(item => !onlyClientSide || item.ClientSide))
        {
            JsonObjectPath jsonPath = new(setting.YamlCode);
            JsonObject? value = jsonPath.Get(jsonConfigObject);
            setting.Value = value ?? setting.DefaultValue;
        }
        return true;
    }

    private static SortedDictionary<float, IConfigBlock> CombineConfigBlocks(SortedDictionary<float, IConfigBlock> formatting, IEnumerable<ConfigSetting> settings)
    {
        float delta = 1E-10f;
        float increment = delta;

        SortedDictionary<float, IConfigBlock> configBlocks = new();
        foreach ((float sortingWeight, IConfigBlock block) in formatting)
        {
            float weight = sortingWeight;
            if (configBlocks.ContainsKey(weight))
            {
                weight += increment;
                increment += delta;
            }

            configBlocks.Add(weight, block);
        }

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

        return configBlocks;
    }
    private static void FormattingFromJson(JsonObject json, out SortedDictionary<float, IConfigBlock> formatting)
    {
        formatting = new();
        float delta = 1E-8f;
        float increment = delta;

        if (!json.KeyExists("formatting") || !json["formatting"].IsArray()) return;

        foreach (JsonObject block in json["formatting"].AsArray())
        {
            IFormattingBlock formattingBlock = ParseBlock(block);

            float weight = formattingBlock.SortingWeight;
            if (formatting.ContainsKey(weight))
            {
                weight += increment;
                increment += delta;
            }
            formatting.Add(weight, formattingBlock);
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
    private static string ConstructYaml(IEnumerable<ConfigSetting> settings, SortedDictionary<float, IConfigBlock> formatting, int version)
    {
        SettingsToYaml(settings, out SortedDictionary<float, string> yaml);

        yaml.Add(-1, $"version: {version}");

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
        foreach (ConfigSetting setting in settings.Where(setting => !setting.Hide))
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
    private static void SettingsFromJson(Dictionary<string, ConfigSetting> settings, JsonObject definition, ref int version, string domain)
    {
        version = definition["version"]?.AsInt(0) ?? 0;

        if (definition["settings"].KeyExists("boolean"))
        {
            ParseSettingsCategory(definition["settings"]["boolean"], settings, ConfigSettingType.Boolean, domain);
        }

        if (definition["settings"].KeyExists("integer"))
        {
            ParseSettingsCategory(definition["settings"]["integer"], settings, ConfigSettingType.Integer, domain);
        }

        if (definition["settings"].KeyExists("float"))
        {
            ParseSettingsCategory(definition["settings"]["float"], settings, ConfigSettingType.Float, domain);
        }

        if (definition["settings"].KeyExists("number"))
        {
            ParseSettingsCategory(definition["settings"]["number"], settings, ConfigSettingType.Float, domain);
        }

        if (definition["settings"].KeyExists("string"))
        {
            ParseSettingsCategory(definition["settings"]["string"], settings, ConfigSettingType.String, domain);
        }

        if (definition["settings"].KeyExists("other"))
        {
            ParseSettingsCategory(definition["settings"]["other"], settings, ConfigSettingType.Other, domain);
        }
    }
    private static void ParseSettingsCategory(JsonObject category, Dictionary<string, ConfigSetting> settings, ConfigSettingType settingType, string domain)
    {
        foreach (JToken item in category.Token)
        {
            if (item is not JProperty property)
            {
                continue;
            }

            string code = property.Name;
            ConfigSetting setting = ConfigSetting.FromJson(new(property.Value), settingType, domain);
            settings.Add(code, setting);
        }
    }
    #endregion
}
