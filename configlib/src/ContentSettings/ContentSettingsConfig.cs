using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using YamlDotNet.Serialization;

namespace ConfigLib
{
    public class ConfigLibConfig
    {
        public Dictionary<string, ConfigSetting> Settings => mSettings;
        public string ConfigFilePath { get; private set; }
        public string ConfigFileContent => mYamlConfig;
        public bool LoadedFromFile { get; private set; }

        private readonly ICoreAPI mApi;
        private readonly string mDomain;
        private readonly Dictionary<string, ConfigSetting> mSettings;
        private readonly TokenReplacer? mReplacer;
        
        private string mYamlConfig;

        public ConfigLibConfig(ICoreAPI api, string domain, JsonObject definition)
        {
            mApi = api;
            mDomain = domain;
            ConfigFilePath = Path.Combine(mApi.DataBasePath, "ModConfig", $"{mDomain}.yaml");

            try
            {
                mYamlConfig = ConfigParser.ParseDefinition(definition, out mSettings);
                ReadConfigFromFile();
                UpdateAndWriteConfig();
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

        public ConfigLibConfig(ICoreAPI api, string domain, Dictionary<string, ConfigSetting> settings)
        {
            mApi = api;
            mDomain = domain;
            mSettings = settings;
            mReplacer = new(mSettings);
            ConfigFilePath = Path.Combine(mApi.DataBasePath, "ModConfig", $"{mDomain}.yaml");
            mYamlConfig = "<not available on client in multiplayer>";
            LoadedFromFile = false;
        }

        public void ReplaceToken(JArray token)
        {
            mReplacer?.ReplaceToken(token);
        }

        public void UpdateAndWriteConfig()
        {
            JObject config = DeserializeYaml(mYamlConfig);

            UpdateValues(config);

            using StreamWriter outputFile = new(ConfigFilePath);
            outputFile.Write(mYamlConfig);
        }

        public bool ReadConfigFromFile()
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
                }
            }
            else
            {
                mApi.Logger.Notification($"[Config lib] [config domain: {mDomain}] Creating default settings file: {ConfigFilePath}");
            }

            return false;
        } 

        private void UpdateValues(JObject values)
        {
            foreach ((_, var setting) in mSettings)
            {
                if (!values.ContainsKey(setting.YamlCode)) continue;

                if (setting.Mapping == null)
                {
                    setting.Value = new(values[setting.YamlCode]);
                    continue;
                }

                string key = (string?)(values[setting.YamlCode] as JValue)?.Value ?? "";

                if (setting.Mapping.ContainsKey(key))
                {
                    setting.Value = setting.Mapping[key];
                }
            }
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
