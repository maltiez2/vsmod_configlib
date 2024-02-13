using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using YamlDotNet.Serialization;

namespace ConfigLib;

public class Config : IConfig
{
    public Dictionary<string, ConfigSetting> Settings => mSettings;
    public string ConfigFilePath { get; private set; }
    public string ConfigFileContent => mYamlConfig;
    public bool LoadedFromFile { get; private set; }
    public JsonObject Definition => mDefinition;
    public int Version { get; private set; }

    private readonly ICoreAPI mApi;
    private readonly string mDomain;
    private readonly Dictionary<string, ConfigSetting> mSettings = new();
    private readonly JsonObject mDefinition;
    private readonly ConfigPatches mPatches;

    private string mYamlConfig;


    public Config(ICoreAPI api, string domain, JsonObject definition)
    {
        mApi = api;
        mDomain = domain;
        mDefinition = definition;
        ConfigFilePath = Path.Combine(mApi.DataBasePath, "ModConfig", $"{mDomain}.yaml");
        Version = definition["version"].AsInt(0);

        try
        {
            System.Text.StringBuilder yaml = new();
            _ = new ConfigParser(mApi, mSettings, mDefinition, yaml);
            mYamlConfig = yaml.ToString();
            ReadFromFile();
            bool validVersion = UpdateValues(DeserializeYaml(mYamlConfig));
            if (!validVersion)
            {
                mApi.Logger.Notification($"[Config lib] ({domain}) Not valid config file version, restoring to default values.");
                mYamlConfig = yaml.ToString();
                UpdateValues(DeserializeYaml(mYamlConfig));
                WriteToFile();
            }
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(mYamlConfig);
            mApi.Logger.Notification($"[Config lib] ({domain}) Settings loaded: {mSettings.Count}");
            LoadedFromFile = true;
            mPatches = new(api, this);
        }
        catch (ConfigLibException exception)
        {
            mApi.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception.Message}.");
            mSettings = new();
            mYamlConfig = "<failed to load>";
            LoadedFromFile = false;
            mPatches = new(api, this);
            return;
        }
    }
    public Config(ICoreAPI api, string domain, Dictionary<string, ConfigSetting> settings, JsonObject definition)
    {
        mApi = api;
        mDomain = domain;
        mSettings = settings;
        Version = -1;
        ConfigFilePath = Path.Combine(mApi.DataBasePath, "ModConfig", $"{mDomain}.yaml");
        mYamlConfig = "<not available on client in multiplayer>";
        LoadedFromFile = false;
        mDefinition = definition;
        mPatches = new(api, this);
    }
    internal void Apply() => mPatches.Apply();

    public ISetting? GetSetting(string code)
    {
        if (!mSettings.ContainsKey(code)) return null;
        return mSettings[code];
    }
    public void WriteToFile()
    {
        if (!LoadedFromFile) return;
        try
        {
            WriteValues();
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(mYamlConfig);
        }
        catch (Exception exception)
        {
            mApi.Logger.Error($"Exception when trying to deserialize yaml and write it to file for '{mDomain}' config.\nException: {exception}\n");
        }
        
    }
    public bool ReadFromFile()
    {
        if (Path.Exists(ConfigFilePath))
        {
            try
            {
                using StreamReader outputFile = new(ConfigFilePath);
                mYamlConfig = outputFile.ReadToEnd();
                return true;
            }
            catch
            {
                mApi.Logger.Notification($"[Config lib] [config domain: {mDomain}] Was not able to read settings, will create default settings file: {ConfigFilePath}");
                using StreamWriter outputFile = new(ConfigFilePath);
                outputFile.Write(mYamlConfig);
            }
        }
        else
        {
            mApi.Logger.Notification($"[Config lib] [config domain: {mDomain}] Creating default settings file: {ConfigFilePath}");
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(mYamlConfig);
        }

        return false;
    }
    public void UpdateFromFile()
    {
        ReadFromFile();
        UpdateValues(DeserializeYaml(mYamlConfig));
    }
    public void RestoreToDefault()
    {
        if (LoadedFromFile)
        {
            mSettings.Clear();
            System.Text.StringBuilder yaml = new();
            _ = new ConfigParser(mApi, mSettings, mDefinition, yaml);
            mYamlConfig = yaml.ToString();
        }
    }

    private JsonObject ReplaceValues()
    {
        JsonObject result = mDefinition.Clone();
        if (result["settings"]?.Token is not JObject settings) return result;
        foreach ((_, JToken? category) in settings)
        {
            if (category is not JObject categoryValue) continue;
            
            foreach ((string key, JToken? value) in categoryValue)
            {
                if (mSettings.ContainsKey(key) && value is JObject valueObject)
                {
                    if (mSettings[key].MappingKey != null)
                    {
                        valueObject["default"]?.Replace(new JValue(mSettings[key].MappingKey));
                    }
                    else
                    {
                        valueObject["default"]?.Replace(mSettings[key].Value.Token);
                    }

                }
            }
        }
        return result;
    }
    private bool UpdateValues(JObject values)
    {
        if (Version != -1)
        {
            if (!values.ContainsKey("version")) return false;
            if (GetVersion(values) != Version) return false;
        }

        foreach ((_, ConfigSetting? setting) in mSettings)
        {
            if (!values.ContainsKey(setting.YamlCode)) continue;

            if (setting.Validation?.Mapping == null)
            {
                setting.Value = new(ConvertValue(values[setting.YamlCode], setting.SettingType));
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
        switch (type)
        {
            case ConfigSettingType.Boolean:
                bool boolValue = bool.Parse(strValue);
                return new JValue(boolValue);
            case ConfigSettingType.Float:
                float floatValue = float.Parse(strValue);
                return new JValue(floatValue);
            case ConfigSettingType.Integer:
                int intValue = int.Parse(strValue);
                return new JValue(intValue);
            default:
                return value ?? new JValue(strValue);
        }
    }
    private void WriteValues()
    {
        JsonObject definition = ReplaceValues();

        System.Text.StringBuilder yaml = new();
        _ = new ConfigParser(mApi, new(), definition, yaml);
        mYamlConfig = yaml.ToString();
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
