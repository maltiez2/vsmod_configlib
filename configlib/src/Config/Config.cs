﻿using ConfigLib.Formatting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using YamlDotNet.Serialization;

namespace ConfigLib;

public class Config : IConfig
{
    public Dictionary<string, ConfigSetting> Settings => mSettings;
    public string ConfigFilePath { get; private set; }
    public string ConfigFileContent => mYamlConfig;
    public JsonObject Definition => mDefinition;
    public int Version { get; private set; }
    public SortedDictionary<float, IConfigBlock> ConfigBlocks => mConfigBlocks;

    private readonly ICoreAPI mApi;
    private readonly string mDomain;
    private readonly Dictionary<string, ConfigSetting> mSettings = new();
    private readonly SortedDictionary<float, IConfigBlock> mConfigBlocks = new();
    private readonly JsonObject mDefinition;
    private readonly ConfigPatches mPatches;
    private const float cDelta = 1E-9f;
    private float mIncrement = cDelta;

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
            SortedDictionary<float, string> yaml = new();
            _ = new ConfigParser(mApi, mSettings, mDefinition, yaml);
            FillConfigBlocks();
            string constructedYaml = ConstructYaml(yaml);
            mYamlConfig = (string)constructedYaml.Clone();
            ReadFromFile();
            bool validVersion = UpdateValues(DeserializeYaml(mYamlConfig));
            if (!validVersion)
            {
                mApi.Logger.Notification($"[Config lib] ({domain}) Not valid config file version, restoring to default values.");
                mYamlConfig = constructedYaml;
                UpdateValues(DeserializeYaml(mYamlConfig));
                WriteToFile();
            }
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(mYamlConfig);
            mApi.Logger.Notification($"[Config lib] ({domain}) Settings loaded: {mSettings.Count}");
            mPatches = new(api, this);
        }
        catch (ConfigLibException exception)
        {
            mApi.Logger.Error($"[Config lib] ({domain}) Error on parsing config: {exception.Message}.");
            mSettings = new();
            mYamlConfig = "<failed to load>";
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
        mDefinition = definition;
        mPatches = new(api, this);
        FillConfigBlocks();
    }
    internal void Apply() => mPatches.Apply();

    public ISetting? GetSetting(string code)
    {
        if (!mSettings.ContainsKey(code)) return null;
        return mSettings[code];
    }
    public void WriteToFile()
    {
        if (mApi is ICoreClientAPI { IsSinglePlayer: false }) return;
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
        JObject deserialized = DeserializeYaml(mYamlConfig);
        UpdateValues(deserialized);
    }
    public void RestoreToDefault()
    {
        if (mApi is ICoreClientAPI { IsSinglePlayer: true })
        {
            mSettings.Clear();
            WriteYaml(mSettings, mDefinition);
        }
    }

    private void WriteYaml(Dictionary<string, ConfigSetting> settings, JsonObject definition)
    {
        SortedDictionary<float, string> yaml = new();
        _ = new ConfigParser(mApi, settings, definition, yaml);
        FillConfigBlocks();
        mYamlConfig = ConstructYaml(yaml);
    }
    private string ConstructYaml(SortedDictionary<float, string> yaml)
    {
        foreach ((float weight, IConfigBlock block) in mConfigBlocks.Where(entry => entry.Value is IFormattingBlock))
        {
            yaml.Add(weight, (block as IFormattingBlock)?.Yaml ?? "");
        }

        return yaml.Select(entry => entry.Value).Aggregate((first, second) => $"{first}\n{second}");
    }
    private void FillConfigBlocks()
    {
        mConfigBlocks.Clear();

        foreach ((_, ConfigSetting setting) in mSettings)
        {
            float weight = setting.SortingWeight;
            if (mConfigBlocks.ContainsKey(weight))
            {
                weight += mIncrement;
                mIncrement += cDelta;
            }
            mConfigBlocks.Add(weight, setting);
        }

        if (mDefinition.KeyExists("formatting") && mDefinition["formatting"].IsArray())
        {
            foreach (JsonObject block in mDefinition["formatting"].AsArray())
            {
                IFormattingBlock formattingBlock = ParseBlock(block);
                mConfigBlocks.Add(formattingBlock.SortingWeight, formattingBlock);
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
    private void WriteValues()
    {
        JsonObject definition = ReplaceValues();

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
