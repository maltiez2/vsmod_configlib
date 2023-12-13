using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace ConfigLib
{
    public class ConfigLibModSystem : ModSystem
    {
        static public HashSet<string> Domains { get; private set; } = new();
        static public ConfigLibConfig? GetConfig(string domain) => mConfigs?.ContainsKey(domain) == true ? mConfigs[domain] : null;
        
        static private readonly Dictionary<string, ConfigLibConfig> mConfigs = new();

        private ICoreAPI? mApi;

        public override void Start(ICoreAPI api)
        {
            mApi = api;
            HarmonyPatches.Patch("ConfigLib");
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            SettingsTokenReplacer.Logger = api.Logger;
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            if (api is not ICoreServerAPI serverApi)
            {
                foreach (IAsset item in api.Assets.GetMany(AssetCategory.config.Code))
                {
                    if (!item.Location.BeginsWith("configlib", "config/configlib")) continue;
                    byte[] data = item.Data;
                    SettingsPacket packet = SerializerUtil.Deserialize<SettingsPacket>(data);
                    ConfigLibConfig config = new ConfigLibConfig(api, packet.Domain, packet.GetSettings());
                    mConfigs.TryAdd(packet.Domain, config);
                    if (!Domains.Contains(packet.Domain)) Domains.Add(packet.Domain);
                    api.Logger.Notification($"[Config lib] Loaded config from server assets for '{packet.Domain}'");
                }
                return;
            }

            foreach (IAsset asset in api.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "settings-config.json"))
            {
                string domain = asset.Location.Domain;
                byte[] data = asset.Data;
                string json = System.Text.Encoding.UTF8.GetString(data);
                JObject token = JObject.Parse(json);
                JsonObject parsedConfig = new(token);
                ConfigLibConfig config = new ConfigLibConfig(serverApi, domain, parsedConfig);
                mConfigs.Add(domain, config);
                Domains.Add(domain);
                api.Logger.Notification($"[Config lib] Loaded config for '{domain}'");

                byte[] newData = SerializerUtil.Serialize(new SettingsPacket(domain, config.Settings));
                AssetLocation location = new AssetLocation("configlib", $"config/configlib/{domain}");
                Asset configAsset = new(newData, location, new SettingsOrigin(newData, location));
                api.Assets.Add(location, configAsset);
            }
        }

        public override double ExecuteOrder()
        {
            return 0.15; // Before 'ModRegistryObjectTypeLoader'
        }

        public override void Dispose()
        {
            SettingsTokenReplacer.Logger = null;
            mConfigs.Clear();
            Domains.Clear();
            HarmonyPatches.Unpatch("ConfigLib");
            base.Dispose();
        }
    }
}
