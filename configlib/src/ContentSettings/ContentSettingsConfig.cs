using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using YamlDotNet.Serialization;

namespace ConfigLib
{
    public class ConfigLibConfig
    {
        public Dictionary<string, ConfigSetting> Settings => mSettings;

        private readonly ICoreAPI mApi;
        private readonly string mDomain;
        private readonly Dictionary<string, ConfigSetting> mSettings;
        private readonly TokenReplacer? mReplacer;

        public ConfigLibConfig(ICoreAPI api, string domain, JsonObject definition)
        {
            mApi = api;
            mDomain = domain;

            try
            {
                string defaultConfig = ConfigParser.ParseDefinition(definition, out mSettings);
                LoadSettings(defaultConfig);
                mApi.Logger.Notification($"[Config lib] [config domain: {domain}] Settings loaded: {mSettings.Count}");
                mReplacer = new(mSettings);
            }
            catch (ConfigLibException exception)
            {
                mApi.Logger.Error($"[Config lib] [config domain: {domain}] Error on parsing config: {exception.Message}.");
                mSettings = new();
                return;
            }
        }

        public ConfigLibConfig(ICoreAPI api, string domain, Dictionary<string, ConfigSetting> settings)
        {
            mApi = api;
            mDomain = domain;
            mSettings = settings;
            mReplacer = new(mSettings);
        }

        public void ReplaceToken(JArray token)
        {
            mReplacer?.ReplaceToken(token);
        }

        private void LoadSettings(string defaultConfig)
        {
            string path = Path.Combine(mApi.DataBasePath, "ModConfig", $"{mDomain}.yaml");

            try
            {
                using (StreamReader outputFile = new StreamReader(path))
                {
                    defaultConfig = outputFile.ReadToEnd();
                }
            }
            catch
            {
                mApi.Logger.Notification($"[Config lib] [config domain: {mDomain}] Was not able to read settings, will create default settings file: {path}");
            }

            JObject config = DeserializeYaml(defaultConfig);

            UpdateValues(config);

            using (StreamWriter outputFile = new StreamWriter(path))
            {
                outputFile.Write(defaultConfig);
            }
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
