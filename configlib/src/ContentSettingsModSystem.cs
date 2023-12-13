using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace ConfigLib
{
    public class ConfigLibModSystem : ModSystem
    {
        static public HashSet<string> Domains { get; private set; } = new();
        static public ConfigLibConfig? GetConfig(string domain) => mConfigs?.ContainsKey(domain) == true ? mConfigs[domain] : null;
        static internal ILogger? Logger { get; private set; }
        
        static private readonly Dictionary<string, ConfigLibConfig> mConfigs = new();

        private ICoreAPI? mApi;

        public override void Start(ICoreAPI api)
        {
            mApi = api;
            HarmonyPatches.Patch("ConfigLib");
            if (api.Side == EnumAppSide.Server) Logger = api.Logger;
            if (api.Side == EnumAppSide.Client && Logger == null) Logger = api.Logger;
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            switch (api.Side)
            {
                case EnumAppSide.Server:
                    foreach (IAsset asset in api.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "settings-config.json"))
                    {
                        LoadConfig(asset);
                    }
                    break;
                case EnumAppSide.Client:
                    foreach (IAsset asset in api.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Location.BeginsWith("configlib", "config/configlib")))
                    {
                        RetrieveConfig(asset);
                    }
                    break;
                case EnumAppSide.Universal:
                    foreach (IAsset asset in api.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "settings-config.json"))
                    {
                        LoadConfig(asset);
                    }
                    break;
            }
        }

        private void RetrieveConfig(IAsset asset)
        {
            if (mApi == null) return;

            byte[] data = asset.Data;
            SettingsPacket packet = SerializerUtil.Deserialize<SettingsPacket>(data);
            ConfigLibConfig config = new(mApi, packet.Domain, packet.GetSettings());
            mConfigs.TryAdd(packet.Domain, config);
            if (!Domains.Contains(packet.Domain)) Domains.Add(packet.Domain);
            mApi.Logger.Notification($"[Config lib] Loaded config from server assets for '{packet.Domain}'");
        }

        private void LoadConfig(IAsset asset)
        {
            if (mApi == null) return;
            
            string domain = asset.Location.Domain;
            byte[] data = asset.Data;
            string json = System.Text.Encoding.UTF8.GetString(data);
            JObject token = JObject.Parse(json);
            JsonObject parsedConfig = new(token);
            ConfigLibConfig config = new(mApi, domain, parsedConfig);
            mConfigs.Add(domain, config);
            Domains.Add(domain);
            mApi.Logger.Notification($"[Config lib] Loaded config for '{domain}'");

            StoreConfig(domain, config);
        }

        private void StoreConfig(string domain, ConfigLibConfig config)
        {
            byte[] newData = SerializerUtil.Serialize(new SettingsPacket(domain, config.Settings));
            AssetLocation location = new("configlib", $"config/configlib/{domain}");
            Asset configAsset = new(newData, location, new SettingsOrigin(newData, location));
            mApi?.Assets.Add(location, configAsset);
        }

        public override double ExecuteOrder()
        {
            return 0.15; // Before 'ModRegistryObjectTypeLoader'
        }

        public override void Dispose()
        {
            Logger = null;
            mConfigs.Clear();
            Domains.Clear();
            HarmonyPatches.Unpatch("ConfigLib");
            base.Dispose();
        }
    }
}
