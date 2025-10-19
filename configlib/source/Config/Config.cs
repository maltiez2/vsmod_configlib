using ConfigLib.Formatting;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using YamlDotNet.Serialization;

namespace ConfigLib;

public sealed class Config : IConfig, IDisposable
{
    public string ConfigFilePath { get; private set; }
    public int Version { get; private set; }

    public event Action<Config>? ConfigSaved;

    public event Action<ISetting>? SettingChanged;

    public Config(ICoreAPI api, string domain, string modName, JsonObject json)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;

        RelativeFilePath = $"{_domain}.yaml";
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", RelativeFilePath);

        try
        {
            Parse(json, out _settings, out _configBlocks, out _defaultYaml, domain);
            _clientSideSettings = _settings;
            WriteToFile();
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, JsonObject json, Dictionary<string, ConfigSetting> serverSideSettings)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;

        RelativeFilePath = $"{_domain}.yaml";
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", RelativeFilePath);

        try
        {
            Parse(json, out _settings, out _configBlocks, out _defaultYaml, domain, checkVersion: false);
            DistributeSettingsBySides(serverSideSettings);
            WriteToFile();
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, JsonObject json, string file)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;
        RelativeFilePath = file;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;

        try
        {
            ParseJson(json, out _settings, out _configBlocks, out _defaultJson, domain);
            _clientSideSettings = _settings;
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, JsonObject json, string file, Dictionary<string, ConfigSetting> serverSideSettings)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = json;
        RelativeFilePath = file;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;
        JsonFilePath = file;

        try
        {
            ParseJson(json, out _settings, out _configBlocks, out _defaultJson, domain);
            DistributeSettingsBySides(serverSideSettings);
            _patches = new(api, this);
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception}.");
            _patches = new(api, this);
            _settings = new();
            _configBlocks = new();
            _defaultYaml = "";
        }
    }
    public Config(ICoreAPI api, string domain, string modName, object configObject, string file)
    {
        _api = api;
        _domain = domain;
        _modName = modName;
        _json = DefinitionFromObject(configObject, domain);
        RelativeFilePath = file;
        ConfigFilePath = Path.Combine(_api.DataBasePath, "ModConfig", file);
        JsonFilePath = ConfigFilePath;
        _configType = ConfigType.JSON;

        try
        {
            ParseJson(_json, out _settings, out _configBlocks, out _defaultJson, domain);
            _clientSideSettings = _settings;
            _patches = new(api, this);
            WriteToFile();
            CreateFileWatcher();
            SubscribeToSettingsChanges();
        }
        catch (Exception exception)
        {
            _api.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception}.");
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
    internal string RelativeFilePath { get; } = "";
    internal string Domain => _domain;
    internal string ModName => _modName;

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
                        content = ToJson(_settings.Values, ReadConfigFile(_defaultJson, false), onlyClientSide: true);
                    }
                    else
                    {
                        content = ToJson(_settings.Values, ReadConfigFile(_defaultJson, false));
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
    public bool ReadFromFile() => ReadFromFile(true);
    public bool ReadFromFile(bool overrideOnFail)
    {
        try
        {
            string content = ReadConfigFile(_defaultYaml, overrideOnFail);

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
    public bool TryReadFromFile()
    {
        try
        {
            if (!ReadConfigFile(out string content)) return false;

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
            _api.Logger.Error($"Exception when trying read '{_configType}' file and parse it for'{_domain}' config.\nException: {exception}\n");
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
    public void AssignSettingsValues(object target)
    {
        Type targetType = target.GetType();

        IEnumerable<(string code, FieldInfo field)> fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(field => (ConfigSetting.NormalizeName(field.Name), field));
        IEnumerable<(string code, PropertyInfo field)> properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(property => property.CanWrite).Select(property => (ConfigSetting.NormalizeName(property.Name), property));

        foreach ((_, ConfigSetting? setting) in _settings)
        {
            try
            {
                setting.AssignSettingValue(target, fields, properties);
            }
            catch (Exception exception)
            {
                LoggerUtil.Error(_api, this, $"Exception on assigning value for setting '{setting.YamlCode}' for config '{_domain}'.\nException: {exception}");
            }
        }
    }
    public void SyncFromServer(Config config, bool isSinglePlayer)
    {
        if (isSinglePlayer)
        {
            foreach ((string code, ConfigSetting setting) in _settings)
            {
                bool serverSide = config.Settings.ContainsKey(code) && !config.Settings[code].ClientSide;

                if (serverSide)
                {
                    _settings[code].SetValueFrom(config.Settings[code]);
                }
            }
        }
        else
        {
            _clientSideSettings.Clear();

            foreach ((string code, ConfigSetting setting) in _settings)
            {
                bool serverSide = config.Settings.ContainsKey(code) && !config.Settings[code].ClientSide;

                if (serverSide)
                {
                    _clientSideSettings.Add(code, setting.Clone());
                    _settings[code].SetValueFrom(config.Settings[code]);
                }
                else
                {
                    _clientSideSettings.Add(code, setting);
                }
            }
        }
    }


    internal enum ConfigType
    {
        YAML,
        JSON
    }

    private readonly ICoreAPI _api;
    private readonly string _domain;
    private readonly string _modName;
    private readonly Dictionary<string, ConfigSetting> _settings;
    private readonly Dictionary<string, ConfigSetting> _clientSideSettings = new();
    private readonly SortedDictionary<float, IConfigBlock> _configBlocks;
    private readonly JsonObject _json;
    private readonly ConfigPatches _patches;
    private readonly string _defaultYaml = "";
    private readonly string _defaultJson = "{}";
    private readonly ConfigType _configType = ConfigType.YAML;
    private FileSystemWatcher? _configFileWatcher;
    private bool _disposedValue;
    private readonly object _fileChangedLockObject = new();
    private bool _fileChanged = false; // protected by _fileOperationLockObject
    private long _fileChangedListener = 0;
    private const int _fileChangeCheckIntervalMs = 2000;
    private static Dictionary<string, FileSystemWatcher?> _fileWatchers = [];
    private static Dictionary<string, List<Config>> _configsByPath = [];

    private void SubscribeToSettingsChanges()
    {
        foreach ((_, ConfigSetting? setting) in _settings)
        {
            setting.SettingChanged += setting => SettingChanged?.Invoke(setting);
        }
    }
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
        string yamlConfig = ReadConfigFile(defaultConfig, true);
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
        string jsonConfig = ReadConfigFile("{}", false);

        JsonObject jsonConfigObject;

        try
        {
            jsonConfigObject = new(JObject.Parse(jsonConfig));
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(_api, this, $"[ParseJson] Error on parsing config file:\n{exception}\nFile content:\n{jsonConfig}");
            throw;
        }

        JsonObject jsonCopy = jsonConfigObject.Clone();
        foreach (ConfigSetting setting in settings.Values)
        {
            JsonObjectPath jsonPath = new(setting.YamlCode);
            IEnumerable<JsonObject> value = jsonPath.Get(jsonConfigObject);
            jsonPath.Set(jsonCopy, setting.Value);
            setting.Value = value.FirstOrDefault() ?? setting.DefaultValue;
        }

        defaultConfig = jsonCopy.Token.ToString(Newtonsoft.Json.Formatting.Indented);
    }
    private JsonObject DefinitionFromObject(object configObject, string domain)
    {
        Type configObjectType = configObject.GetType();
        MemberInfo[] members = configObjectType.GetMembers(BindingFlags.Public | BindingFlags.Instance);
        MemberInfo[] staticMembers = configObjectType.GetMembers(BindingFlags.Public | BindingFlags.Static);

        JObject root = [];
        JArray settings = [];
        root.Add("settings", settings);

        foreach (MemberInfo member in staticMembers)
        {
            if (member.Name.ToLowerInvariant() != "version")
            {
                continue;
            }

            int? value = (int?)((member as PropertyInfo)?.GetValue(configObject) ?? (member as FieldInfo)?.GetValue(configObject));
            if (value != null)
            {
                root.Add("version", value.Value);
            }
        }

        foreach (MemberInfo member in members)
        {
            if (SettingDefinitionFromObject(member, configObject, domain, out JObject definition))
            {
                settings.Add(definition);
            }
        }

        return new JsonObject(root);
    }
    private static bool SettingDefinitionFromObject(MemberInfo info, object configObject, string domain, out JObject definition)
    {
        definition = [];

        ConfigSettingType settingType = GetSettingType(info);

        if (settingType == ConfigSettingType.None) return false;

        string code = info.Name;

        definition.Add("code", code);
        definition.Add("ingui", $"{domain}:setting-{code}");
        definition.Add("type", settingType.ToString().ToLowerInvariant());
        definition.Add("default", GetDefaultValue(info, configObject, settingType));

        DescriptionAttribute? description = info.GetCustomAttribute<DescriptionAttribute>();
        if (description != null)
        {
            definition.Add("comment", description.Description);
        }

        CategoryAttribute? category = info.GetCustomAttribute<CategoryAttribute>();
        if (category != null)
        {
            IEnumerable<string> tags = category.Category.Replace(" ", "").Split(',').Select(tag => tag.Replace("_", "").ToLowerInvariant());

            foreach (string tag in tags)
            {
                switch (tag)
                {
                    case "clientside":
                        definition.Add("clientSide", true);
                        break;
                    case "logarithmic":
                        definition.Add("logarithmic", true);
                        break;
                }
            }
        }

        switch (settingType)
        {
            case ConfigSettingType.Float:
                SetFloatSettingDefinition(info, definition);
                break;
            case ConfigSettingType.Integer:
                SetIntegerSettingDefinition(info, definition);
                break;
            case ConfigSettingType.String:
                SetStringSettingDefinition(info, definition);
                break;
            default:
                break;
        }

        return true;
    }
    private static ConfigSettingType GetSettingType(MemberInfo info)
    {
        Type? valueType = (info as PropertyInfo)?.PropertyType ?? (info as FieldInfo)?.FieldType;

        if (valueType == null) return ConfigSettingType.None;

        if (valueType.IsEnum)
        {
            return ConfigSettingType.Integer;
        }
        else if (valueType == typeof(float) || valueType == typeof(double))
        {
            return ConfigSettingType.Float;
        }
        else if (valueType == typeof(string))
        {
            return ConfigSettingType.String;
        }
        else if (valueType == typeof(bool))
        {
            return ConfigSettingType.Boolean;
        }
        else if (
            valueType == typeof(int) ||
            valueType == typeof(long) ||
            valueType == typeof(short) ||
            valueType == typeof(uint) ||
            valueType == typeof(ulong) ||
            valueType == typeof(ushort))
        {
            return ConfigSettingType.Integer;
        }

        return ConfigSettingType.None;
    }
    private static JValue GetDefaultValue(MemberInfo info, object configObject, ConfigSettingType settingType)
    {
        DefaultValueAttribute? attribute = info.GetCustomAttribute<DefaultValueAttribute>();
        object? value = attribute?.Value ?? (info as PropertyInfo)?.GetValue(configObject) ?? (info as FieldInfo)?.GetValue(configObject);

        if (value == null) return new(value);

        return settingType switch
        {
            ConfigSettingType.Boolean => new JValue((bool)value),
            ConfigSettingType.Float => new JValue((float)value),
            ConfigSettingType.Integer => new JValue((int)value),
            ConfigSettingType.String => new JValue((string)value),
            _ => new(value)
        };
    }
    private static void SetFloatSettingDefinition(MemberInfo info, JObject definition)
    {
        RangeAttribute? rangeAttribute = info.GetCustomAttribute<RangeAttribute>();
        if (rangeAttribute != null)
        {
            JObject range = new()
            {
                { "min", Convert.ToSingle(rangeAttribute.Minimum) },
                { "max", Convert.ToSingle(rangeAttribute.Maximum) }
            };
            definition.Add("range", range);
        }

        AllowedValuesAttribute? allowedValuesAttribute = info.GetCustomAttribute<AllowedValuesAttribute>();
        if (allowedValuesAttribute != null)
        {
            IEnumerable<float> allowedValues = allowedValuesAttribute.Values.Select(Convert.ToSingle);
            JArray values = new(allowedValues);
            definition.Add("values", values);
        }
    }
    private static void SetIntegerSettingDefinition(MemberInfo info, JObject definition)
    {
        RangeAttribute? rangeAttribute = info.GetCustomAttribute<RangeAttribute>();
        if (rangeAttribute != null)
        {
            JObject range = new()
            {
                { "min", Convert.ToInt32(rangeAttribute.Minimum) },
                { "max", Convert.ToInt32(rangeAttribute.Maximum) }
            };
            definition.Add("range", range);
        }

        Type? valueType = (info as PropertyInfo)?.PropertyType ?? (info as FieldInfo)?.FieldType;
        if (valueType?.IsEnum == true)
        {
            string[] enumNames = valueType.GetEnumNames();
            int[] enumValues = (int[])valueType.GetEnumValues();

            if (enumNames.Length == enumValues.Length)
            {
                JObject mapping = [];
                for (int index = 0; index < enumNames.Length; index++)
                {
                    mapping.Add(enumNames[index], enumValues[index]);
                }
                definition.Add("mapping", mapping);
                int indexClamped = Math.Max(enumValues.IndexOf(definition["default"]?.Value<int>() ?? 0), 0);
                definition.Remove("default");
                definition.Add("default", enumNames[indexClamped]);
            }
        }

        AllowedValuesAttribute? allowedValuesAttribute = info.GetCustomAttribute<AllowedValuesAttribute>();
        if (allowedValuesAttribute != null)
        {
            IEnumerable<int> allowedValues = allowedValuesAttribute.Values.Select(Convert.ToInt32);
            JArray values = new(allowedValues);
            definition.Add("values", values);
        }
    }
    private static void SetStringSettingDefinition(MemberInfo info, JObject definition)
    {
        AllowedValuesAttribute? allowedValuesAttribute = info.GetCustomAttribute<AllowedValuesAttribute>();
        if (allowedValuesAttribute != null)
        {
            IEnumerable<string?> allowedValues = allowedValuesAttribute.Values.Select(Convert.ToString);
            JArray values = new(allowedValues);
            definition.Add("values", values);
        }
    }


    private bool ReadConfigFile(out string config)
    {
        config = "";

        try
        {
            if (Path.Exists(ConfigFilePath))
            {
                try
                {
                    using StreamReader outputFile = new(ConfigFilePath);
                    if (_configFileWatcher != null) _configFileWatcher.EnableRaisingEvents = true;
                    config = outputFile.ReadToEnd();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        catch
        {
            return false;
        }
    }
    private string ReadConfigFile(string defaultConfig, bool overrideOnFail)
    {
        try
        {
            if (Path.Exists(ConfigFilePath))
            {
                try
                {
                    using StreamReader outputFile = new(ConfigFilePath);
                    if (_configFileWatcher != null) _configFileWatcher.EnableRaisingEvents = true;
                    return outputFile.ReadToEnd();
                }
                catch
                {
                    if (overrideOnFail)
                    {
                        _api.Logger.Notification($"[Config lib] [config domain: {_domain}] Was not able to read settings, will create default settings file: {ConfigFilePath}");
                        using StreamWriter outputFile = new(ConfigFilePath);
                        outputFile.Write(defaultConfig);
                    }
                }
            }
            else
            {
                _api.Logger.Notification($"[Config lib] [config domain: {_domain}] Creating default settings file: {ConfigFilePath}");
                using StreamWriter outputFile = new(ConfigFilePath);
                outputFile.Write(defaultConfig);
            }
        }
        catch
        {
            _api.Logger.Debug($"[Config lib] [config domain: {_domain}] Was not able to read/write settings file: {ConfigFilePath}");
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
            _api.Logger.Error($"[Config lib] [config domain: {_domain}] Exception when trying to deserialize yaml and write it to file.\nException: {exception}\n");
        }
    }
    private void CreateFileWatcher()
    {
        string? directory = Path.GetDirectoryName(ConfigFilePath);

        if (directory == null)
        {
            LoggerUtil.Warn(_api, this, $"[config domain: {_domain}] Unable to extract directory from: {ConfigFilePath}");
            return;
        }

        if (!_fileWatchers.TryGetValue(directory, out _configFileWatcher))
        {
            try
            {
                _configFileWatcher = new(directory);
                _configFileWatcher.Changed += FileEventHandler;
                _configFileWatcher.Created += FileEventHandler;
                _configFileWatcher.NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.LastWrite;
                _configFileWatcher.Error += (_, e) => Debug.WriteLine(e.GetException());
                _configFileWatcher.EnableRaisingEvents = true;
                _fileWatchers.Add(directory, _configFileWatcher);
            }
            catch (Exception exception)
            {
                string combined = Path.Combine(_api.DataBasePath, "ModConfig", $"{_domain}.yaml");
                LoggerUtil.Error(_api, this, $"Failed to create file watcher. Automatic updates when files are changed on disc will not work.");
                LoggerUtil.Verbose(_api, this, $"[config domain: {_domain}] Failed to create file watcher. Automatic updates when file is changed on disc will not work.\nPaths:\n  data: {_api.DataBasePath}\n  combined: {combined}\nException:\n{exception}\n");
                _fileWatchers.Add(directory, null);
                return;
            }
        }

        int initialDelay = Math.Abs(Path.GetFileName(ConfigFilePath).GetHashCode()) % _fileChangeCheckIntervalMs;

        _fileChangedListener = _api.World.RegisterGameTickListener(_ => OnFileChanged(), _fileChangeCheckIntervalMs, initialDelay);

        if (_configsByPath.TryGetValue(ConfigFilePath, out List<Config>? configs))
        {
            configs.Add(this);
        }
        else
        {
            _configsByPath.TryAdd(ConfigFilePath, [this]);
        }
    }
    private bool CheckIfFileChanged()
    {
        lock (_fileChangedLockObject)
        {
            if (!_fileChanged) return false;

            _fileChanged = false;
            return true;
        }
    }
    private static void FileEventHandler(object sender, FileSystemEventArgs eventArgs)
    {
        if (eventArgs.ChangeType != WatcherChangeTypes.Changed && eventArgs.ChangeType != WatcherChangeTypes.Created)
        {
            return;
        }

        Debug.WriteLine($"File changed: {eventArgs.FullPath}");

        if (_configsByPath.TryGetValue(eventArgs.FullPath, out List<Config>? configs))
        {
            Debug.WriteLine($"Config changed ({configs.Count}): {eventArgs.FullPath}");

            foreach (Config config in configs)
            {
                lock (config._fileChangedLockObject)
                {
                    config._fileChanged = true;
                }
            }
        }
    }
    private void OnFileChanged()
    {
        if (CheckIfFileChanged())
        {
            TryReadFromFile();
        }
    }


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
    private int FromJsonDefinition(JsonObject json, out Dictionary<string, ConfigSetting> settings, out SortedDictionary<float, IConfigBlock> configBlocks, string domain)
    {
        settings = new();

        bool arrayFormat = json["settings"].IsArray();
        int version = json["version"]?.AsInt(0) ?? 0;

        if (arrayFormat)
        {
            SettingsAndFormattingFromJsonArray(settings, json["settings"].AsArray(), out configBlocks, domain);
        }
        else
        {
            SettingsFromJson(settings, json, ref version, domain);

            FormattingFromJson(json, out SortedDictionary<float, IConfigBlock> formatting, domain);
            configBlocks = CombineConfigBlocks(formatting, settings.Values);
        }

        if (json.KeyExists("constants"))
        {
            ParseConstants(settings, json["constants"]);
        }

        return version;
    }
    private string ToJson(IEnumerable<ConfigSetting> settings, string defaultJson, bool onlyClientSide = false)
    {
        JsonObject config = new(JObject.Parse(defaultJson));
        foreach (ConfigSetting setting in settings.Where(item => !onlyClientSide || item.ClientSide))
        {
            JsonObjectPath jsonPath = new(setting.YamlCode);
            if (setting.Validation?.Mapping == null)
            {
                int count = jsonPath.Set(config, setting.Value);
                if (count == 0 && !setting.YamlCode.Contains('\\'))
                {
                    (config.Token as JObject)?.Add(setting.YamlCode, setting.Value.Token);
                }
            }
            else
            {
                int count = jsonPath.Set(config, new(new JValue(setting.MappingKey)));
                if (count == 0 && !setting.YamlCode.Contains('\\'))
                {
                    (config.Token as JObject)?.Add(setting.YamlCode, setting.MappingKey);
                }
            }
        }
        return config.Token.ToString(Newtonsoft.Json.Formatting.Indented);
    }
    private bool FromJson(IEnumerable<ConfigSetting> settings, string json, bool onlyClientSide = false)
    {
        JsonObject jsonConfigObject = new(JObject.Parse(json));
        foreach (ConfigSetting setting in settings.Where(item => !onlyClientSide || item.ClientSide))
        {
            JsonObjectPath jsonPath = new(setting.YamlCode);
            IEnumerable<JsonObject> values = jsonPath.Get(jsonConfigObject);

            JsonObject value = values.FirstOrDefault((JsonObject?)null) ?? setting.DefaultValue;

            if (setting.Validation?.Mapping == null)
            {
                setting.Value = value;
                continue;
            }

            string key = value.AsString("");
            if (setting.Validation?.Mapping?.ContainsKey(key) == true)
            {
                setting.Value = setting.Validation.Mapping[key];
                setting.MappingKey = key;
            }
        }
        return true;
    }

    private void ParseConstants(Dictionary<string, ConfigSetting> settings, JsonObject constants)
    {
        foreach (JToken item in constants.Token)
        {
            if (item is not JProperty property)
            {
                continue;
            }

            string code = property.Name;
            ConfigSetting setting = new(code, new JsonObject(property.Value), ConfigSettingType.Constant)
            {
                Hide = true
            };
            settings.Add(code, setting);
        }
    }
    private SortedDictionary<float, IConfigBlock> CombineConfigBlocks(SortedDictionary<float, IConfigBlock> formatting, IEnumerable<ConfigSetting> settings)
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
    private void FormattingFromJson(JsonObject json, out SortedDictionary<float, IConfigBlock> formatting, string domain)
    {
        formatting = new();
        float delta = 1E-8f;
        float increment = delta;

        if (!json.KeyExists("formatting") || !json["formatting"].IsArray()) return;

        foreach (JsonObject block in json["formatting"].AsArray())
        {
            IFormattingBlock formattingBlock = ParseBlock(block, domain);

            float weight = formattingBlock.SortingWeight;
            if (formatting.ContainsKey(weight))
            {
                weight += increment;
                increment += delta;
            }
            formatting.Add(weight, formattingBlock);
        }
    }
    private IFormattingBlock ParseBlock(JsonObject block, string domain)
    {
        switch (block["type"]?.AsString())
        {
            case "separator":
                return new Separator(block, domain, _api);
        }

        return new Blank();
    }
    private int ConvertVersion(JToken value)
    {
        return new JsonObject(ConvertValue(value, ConfigSettingType.Integer)).AsInt(0);
    }
    private string ConstructYaml(IEnumerable<ConfigSetting> settings, SortedDictionary<float, IConfigBlock> formatting, int version)
    {
        SettingsToYaml(settings, out SortedDictionary<float, string> yaml);

        yaml.Add(-1, $"version: {version}");

        foreach ((float weight, IConfigBlock block) in formatting.Where(entry => entry.Value is IFormattingBlock))
        {
            yaml.Add(weight, (block as IFormattingBlock)?.Yaml ?? "");
        }

        return yaml.Select(entry => entry.Value).Aggregate((first, second) => $"{first}\n{second}");
    }
    private void ValuesFromYaml(out Dictionary<string, JsonObject> values, string yaml)
    {
        JObject json;

        try
        {
            IDeserializer deserializer = new DeserializerBuilder().Build();
            object? yamlObject = deserializer.Deserialize(yaml);

            ISerializer serializer = new SerializerBuilder()
                .JsonCompatible()
                .Build();

            json = JObject.Parse(serializer.Serialize(yamlObject));
        }
        catch (Exception exception)
        {
            LoggerUtil.Verbose(_api, this, $"[ValuesFromYaml] Error on parsing config file:\n{exception}\nFile content:\n{yaml}");
            throw;
        }

        values = [];
        foreach ((string code, JToken? value) in json)
        {
            if (value == null) continue;
            values.Add(code, new(value));
        }
    }
    private JToken ConvertValue(JToken? value, ConfigSettingType type)
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
            case ConfigSettingType.Color:
                return new JValue(strValue);
            default:
                return value ?? new JValue(strValue);
        }
    }
    private void SettingsToYaml(IEnumerable<ConfigSetting> settings, out SortedDictionary<float, string> yaml)
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
    private void SettingsFromJson(Dictionary<string, ConfigSetting> settings, JsonObject definition, ref int version, string domain)
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

        if (definition["settings"].KeyExists("color"))
        {
            ParseSettingsCategory(definition["settings"]["color"], settings, ConfigSettingType.Color, domain);
        }
    }
    private void SettingsAndFormattingFromJsonArray(Dictionary<string, ConfigSetting> settings, JsonObject[] definition, out SortedDictionary<float, IConfigBlock> configBlocks, string domain)
    {
        configBlocks = new();
        float weight = 0;
        foreach (JsonObject block in definition)
        {
            string type = block["type"].AsString();
            weight += 1;

            switch (type)
            {
                case "separator":
                    IConfigBlock formattingBlock = ParseFormattingBlock(type, block, domain);
                    configBlocks.Add(weight, formattingBlock);
                    continue;
                default:
                    break;
            }

            ConfigSettingType settingType = type switch
            {
                "boolean" => ConfigSettingType.Boolean,
                "integer" => ConfigSettingType.Integer,
                "number" => ConfigSettingType.Float,
                "float" => ConfigSettingType.Float,
                "string" => ConfigSettingType.String,
                "other" => ConfigSettingType.Other,
                "color" => ConfigSettingType.Color,
                _ => ConfigSettingType.None
            };

            (string code, ConfigSetting setting) = ParseSettingBlock(block, settingType, domain);
            configBlocks.Add(weight, setting);
            settings.Add(code, setting);
            setting.SortingWeight = weight;
        }
    }
    private IConfigBlock ParseFormattingBlock(string type, JsonObject block, string domain)
    {
        IFormattingBlock formattingBlock = ParseBlock(block, domain);
        return formattingBlock;
    }
    private (string code, ConfigSetting setting) ParseSettingBlock(JsonObject block, ConfigSettingType settingType, string domain)
    {
        if (!block.KeyExists("code"))
        {
            throw new ArgumentException($"[Config lib] ({domain}) Setting has no code: {block}");
        }

        string code = block["code"].AsString();
        ConfigSetting setting = ConfigSetting.FromJson(block, settingType, domain, code, _api);
        return (code, setting);
    }
    private void ParseSettingsCategory(JsonObject category, Dictionary<string, ConfigSetting> settings, ConfigSettingType settingType, string domain)
    {
        foreach (JToken item in category.Token)
        {
            if (item is not JProperty property)
            {
                continue;
            }

            string code = property.Name;
            ConfigSetting setting = ConfigSetting.FromJson(new(property.Value), settingType, domain, code, _api);
            settings.Add(code, setting);
        }
    }


    private void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _configFileWatcher?.Dispose();
                _configFileWatcher = null;
                _configsByPath.Clear();
                if (_fileChangedListener != 0)
                {
                    _api.World.UnregisterGameTickListener(_fileChangedListener);
                    _fileChangedListener = 0;
                }
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
