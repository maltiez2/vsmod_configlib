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
    public Dictionary<string, ConfigSetting> Settings => _settings;
    public string ConfigFilePath { get; private set; }
    public string ConfigFileContent => _yamlConfig;
    public JsonObject Definition => _definition;
    public int Version { get; private set; }
    public SortedDictionary<float, IConfigBlock> ConfigBlocks => _configBlocks;

    public Config(ICoreAPI api, string domain, JsonObject definition)
    {
        _api = api;
        _domain = domain;
        _definition = definition;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");
        Version = definition["version"].AsInt(0);

        try
        {
            SortedDictionary<float, string> yaml = new();
            _ = new ConfigParser(_api, _settings, _definition, yaml);
            FillConfigBlocks();
            string constructedYaml = ConstructYaml(yaml);
            _yamlConfig = (string)constructedYaml.Clone();
            ReadFromFile();
            bool validVersion = UpdateValues(DeserializeYaml(_yamlConfig), _settings);
            if (!validVersion)
            {
                _api.Logger.Notification($"[Config lib] ({domain}) Not valid config file version, restoring to default values.");
                _yamlConfig = constructedYaml;
                UpdateValues(DeserializeYaml(_yamlConfig), _settings);
                WriteToFile();
            }
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(_yamlConfig);
            _api.Logger.Notification($"[Config lib] ({domain}) Settings loaded: {_settings.Count}");
            _patches = new(api, this);
        }
        catch (ConfigLibException exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception.Message}.");
            _settings = new();
            _yamlConfig = "<failed to load>";
            _patches = new(api, this);
            return;
        }
    }
    public Config(ICoreAPI api, string domain, Dictionary<string, ConfigSetting> settings, JsonObject definition)
    {
        _api = api;
        _domain = domain;
        _settings = settings;
        Version = -1;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");
        _yamlConfig = "<not available on client in multiplayer>";
        _definition = definition;
        _patches = new(api, this);
        FillConfigBlocks();
    }
    public Config(ICoreAPI api, string domain, JsonObject definition, Dictionary<string, ConfigSetting> settings)
    {
        _api = api;
        _domain = domain;
        _definition = definition;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");
        Version = definition["version"].AsInt(0);

        try
        {
            SortedDictionary<float, string> yaml = new();
            _ = new ConfigParser(_api, _settings, _definition, yaml);
            FillConfigBlocks();
            string constructedYaml = ConstructYaml(yaml);
            _yamlConfig = (string)constructedYaml.Clone();
            ReadFromFile();
            bool validVersion = UpdateValues(DeserializeYaml(_yamlConfig), _settings);
            if (!validVersion)
            {
                _api.Logger.Notification($"[Config lib] ({domain}) Not valid config file version, restoring to default values.");
                _yamlConfig = constructedYaml;
                UpdateValues(DeserializeYaml(_yamlConfig), _settings);
                WriteToFile();
            }
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(_yamlConfig);
            _api.Logger.Notification($"[Config lib] ({domain}) Settings loaded: {_settings.Count}");
            FillServerSettingsValues(settings);
            _patches = new(api, this);
        }
        catch (ConfigLibException exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception.Message}.");
            _settings = new();
            _yamlConfig = "<failed to load>";
            _patches = new(api, this);
            return;
        }
    }
    internal void Apply() => _patches.Apply();

    public ISetting? GetSetting(string code)
    {
        if (!_settings.ContainsKey(code)) return null;
        return _settings[code];
    }
    public void WriteToFile()
    {
        try
        {
            if (_api is ICoreClientAPI { IsSinglePlayer: false })
            {
                WriteValues(_clientSideSettings);
            }
            else
            {
                WriteValues(_settings);
            }
                
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(_yamlConfig);
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"Exception when trying to deserialize yaml and write it to file for '{_domain}' config.\nException: {exception}\n");
        }

    }
    public bool ReadFromFile()
    {
        if (Path.Exists(ConfigFilePath))
        {
            try
            {
                using StreamReader outputFile = new(ConfigFilePath);
                _yamlConfig = outputFile.ReadToEnd();
                return true;
            }
            catch
            {
                _api.Logger.Notification($"[Config lib] [config domain: {_domain}] Was not able to read settings, will create default settings file: {ConfigFilePath}");
                using StreamWriter outputFile = new(ConfigFilePath);
                outputFile.Write(_yamlConfig);
            }
        }
        else
        {
            _api.Logger.Notification($"[Config lib] [config domain: {_domain}] Creating default settings file: {ConfigFilePath}");
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(_yamlConfig);
        }

        return false;
    }
    public void UpdateFromFile()
    {
        ReadFromFile();
        JObject deserialized = DeserializeYaml(_yamlConfig);
        if (_api is ICoreClientAPI { IsSinglePlayer: false })
        {
            UpdateValues(deserialized, _clientSideSettings);
        }
        else
        {
            UpdateValues(deserialized, _settings);
        }
        
    }
    public void RestoreToDefault()
    {
        if (_api is ICoreClientAPI { IsSinglePlayer: true })
        {
            _settings.Clear();
            WriteYaml(_settings, _definition);
        }
        else
        {
            _clientSideSettings.Clear();
            WriteYaml(_clientSideSettings, _definition);
        }
    }


    private readonly ICoreAPI _api;
    private readonly string _domain;
    private readonly Dictionary<string, ConfigSetting> _settings = new();
    private readonly Dictionary<string, ConfigSetting> _clientSideSettings = new();
    private readonly SortedDictionary<float, IConfigBlock> _configBlocks = new();
    private readonly JsonObject _definition;
    private readonly ConfigPatches _patches;
    private const float _delta = 1E-9f;

    private float _increment = _delta;
    private string _yamlConfig;

    private void FillServerSettingsValues(Dictionary<string, ConfigSetting> settings)
    {
        foreach ((string code, ConfigSetting setting) in _settings)
        {
            _clientSideSettings.Add(code, setting);
        }
        foreach ((string code, ConfigSetting setting) in settings.Where(entry => !entry.Value.ClientSide && _settings.ContainsKey(entry.Key)))
        {
            _settings[code] = setting;
        }
    }
    private void FillClientSettingsValues(Dictionary<string, ConfigSetting> settings)
    {
        foreach ((string code, ConfigSetting setting) in settings.Where(entry => entry.Value.ClientSide && _settings.ContainsKey(entry.Key)))
        {
            _settings[code] = setting;
        }
    }
    private void WriteYaml(Dictionary<string, ConfigSetting> settings, JsonObject definition)
    {
        SortedDictionary<float, string> yaml = new();
        _ = new ConfigParser(_api, settings, definition, yaml);
        FillConfigBlocks();
        FillClientSettingsValues(_clientSideSettings);
        _yamlConfig = ConstructYaml(yaml);
    }
    private string ConstructYaml(SortedDictionary<float, string> yaml)
    {
        foreach ((float weight, IConfigBlock block) in _configBlocks.Where(entry => entry.Value is IFormattingBlock))
        {
            yaml.Add(weight, (block as IFormattingBlock)?.Yaml ?? "");
        }

        return yaml.Select(entry => entry.Value).Aggregate((first, second) => $"{first}\n{second}");
    }
    private void FillConfigBlocks()
    {
        _configBlocks.Clear();

        foreach ((_, ConfigSetting setting) in _settings)
        {
            float weight = setting.SortingWeight;
            if (_configBlocks.ContainsKey(weight))
            {
                weight += _increment;
                _increment += _delta;
            }
            _configBlocks.Add(weight, setting);
        }

        if (_definition.KeyExists("formatting") && _definition["formatting"].IsArray())
        {
            foreach (JsonObject block in _definition["formatting"].AsArray())
            {
                IFormattingBlock formattingBlock = ParseBlock(block);
                _configBlocks.Add(formattingBlock.SortingWeight, formattingBlock);
            }
        }
    }
    private IFormattingBlock ParseBlock(JsonObject block)
    {
        switch (block["type"]?.AsString())
        {
            case "separator":
                return new Separator(block);
        }

        return new Blank();
    }
    private JsonObject ReplaceValues(Dictionary<string, ConfigSetting> currentSettings)
    {
        JsonObject result = _definition.Clone();
        if (result["settings"]?.Token is not JObject settings) return result;
        foreach ((_, JToken? category) in settings)
        {
            if (category is not JObject categoryValue) continue;

            foreach ((string key, JToken? value) in categoryValue)
            {
                if (currentSettings.ContainsKey(key) && value is JObject valueObject)
                {
                    if (currentSettings[key].MappingKey != null)
                    {
                        valueObject["default"]?.Replace(new JValue(currentSettings[key].MappingKey));
                    }
                    else
                    {
                        valueObject["default"]?.Replace(currentSettings[key].Value.Token);
                    }

                }
            }
        }
        return result;
    }
    private bool UpdateValues(JObject values, Dictionary<string, ConfigSetting> currentSettings)
    {
        if (Version != -1)
        {
            if (!values.ContainsKey("version")) return false;
            if (GetVersion(values) != Version) return false;
        }

        foreach ((_, ConfigSetting? setting) in currentSettings)
        {
            if (!values.ContainsKey(setting.YamlCode)) continue;

            if (setting.Validation?.Mapping == null)
            {
                JToken converted = ConvertValue(values[setting.YamlCode], setting.SettingType);
                setting.Value = new(converted);
                continue;
            }

            string key = (string?)(values[setting.YamlCode] as JValue)?.Value ?? "";

            if (setting.Validation?.Mapping?.ContainsKey(key) == true)
            {
                setting.Value = setting.Validation.Mapping[key];
                setting.MappingKey = key;
            }
        }

        return true;
    }
    private int GetVersion(JObject values)
    {
        if (values == null) return -1;
        if (!values.ContainsKey("version")) return -1;

        object? value = (values["version"] as JValue)?.Value;
        if (value == null) return -1;

        return int.Parse((string)value);
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
            default:
                return value ?? new JValue(strValue);
        }
    }
    private void WriteValues(Dictionary<string, ConfigSetting> currentSettings)
    {
        JsonObject definition = ReplaceValues(currentSettings);

        WriteYaml(new(), definition);
    }
    static private JObject DeserializeYaml(string config)
    {
        IDeserializer deserializer = new DeserializerBuilder().Build();
        object? yamlObject = deserializer.Deserialize(config);

        ISerializer serializer = new SerializerBuilder()
            .JsonCompatible()
            .Build();

        string json = serializer.Serialize(yamlObject);

        return JObject.Parse(json);
    }
}
