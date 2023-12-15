using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using YamlDotNet.Serialization;

namespace ConfigLib
{
    public class Config : IConfig
    {
        public Dictionary<string, ConfigSetting> Settings => mSettings;
        public string ConfigFilePath { get; private set; }
        public string ConfigFileContent => mYamlConfig;
        public bool LoadedFromFile { get; private set; }

        private readonly ICoreAPI mApi;
        private readonly string mDomain;
        private Dictionary<string, ConfigSetting> mSettings;
        private readonly TokenReplacer? mReplacer;
        private readonly JsonObject mDefinition;
        private string mYamlConfig;
        


        public Config(ICoreAPI api, string domain, JsonObject definition)
        {
            mApi = api;
            mDomain = domain;
            mDefinition = definition;
            ConfigFilePath = Path.Combine(mApi.DataBasePath, "ModConfig", $"{mDomain}.yaml");

            try
            {
                mYamlConfig = ConfigParser.ParseDefinition(mDefinition, out mSettings);
                ReadFromFile();
                UpdateValues(DeserializeYaml(mYamlConfig));
                using StreamWriter outputFile = new(ConfigFilePath);
                outputFile.Write(mYamlConfig);
                mApi.Logger.Notification($"[Config lib] [config domain: {domain}] Settings loaded: {mSettings.Count}");
                mReplacer = new(mSettings);
                LoadedFromFile = true;
            }
            catch (ConfigLibException exception)
            {
                mApi.Logger.Error($"[Config lib] [config domain: {domain}] Error on parsing config: {exception.Message}.");
                mSettings = new();
                mYamlConfig = "<failed to load>";
                LoadedFromFile = false;
                return;
            }
        }
        public Config(ICoreAPI api, string domain, Dictionary<string, ConfigSetting> settings)
        {
            mApi = api;
            mDomain = domain;
            mSettings = settings;
            mReplacer = new(mSettings);
            ConfigFilePath = Path.Combine(mApi.DataBasePath, "ModConfig", $"{mDomain}.yaml");
            mYamlConfig = "<not available on client in multiplayer>";
            LoadedFromFile = false;
            mDefinition = new JsonObject(new JValue("<not loaded>"));
        }

        public ISetting? GetSetting(string code)
        {
            if (!mSettings.ContainsKey(code)) return null;
            return mSettings[code];
        }
        public void WriteToFile()
        {
            if (!LoadedFromFile) return;
            WriteValues(DeserializeYaml(mYamlConfig));
            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(mYamlConfig);
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
        public void RestoreToDefault()
        {
            if (LoadedFromFile) mYamlConfig = ConfigParser.ParseDefinition(mDefinition, out mSettings);
        }

        internal void ReplaceToken(JArray token)
        {
            mReplacer?.ReplaceToken(token);
        }

        private JsonObject ReplaceValues()
        {
            JsonObject result = mDefinition.Clone();
            if (result.Token is not JObject settings) return result;
            foreach ((string key, JToken? value) in settings)
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
            return result;
        }
        private void UpdateValues(JObject values)
        {
            foreach ((_, var setting) in mSettings)
            {
                if (!values.ContainsKey(setting.YamlCode)) continue;

                if (setting.Validation?.Mapping == null)
                {
                    setting.Value = new(values[setting.YamlCode]);
                    continue;
                }

                string key = (string?)(values[setting.YamlCode] as JValue)?.Value ?? "";

                if (setting.Validation?.Mapping?.ContainsKey(key) == true)
                {
                    setting.Value = setting.Validation.Mapping[key];
                    setting.MappingKey = key;
                }
            }
        }
        private void WriteValues(JObject values)
        {
            foreach ((_, var setting) in mSettings)
            {
                if (!values.ContainsKey(setting.YamlCode)) continue;

                if (setting.Validation?.Mapping == null)
                {
                    values[setting.YamlCode]?.Replace(setting.Value.Token);
                    continue;
                }

                string key = (string?)(values[setting.YamlCode] as JValue)?.Value ?? "";

                if (setting.Validation?.Mapping?.ContainsKey(key) == true)
                {
                    values[setting.YamlCode]?.Replace(new JValue(setting.MappingKey));
                }
            }

            JsonObject definition = ReplaceValues();
            mYamlConfig = ConfigParser.ParseDefinition(definition, out _);
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
}
