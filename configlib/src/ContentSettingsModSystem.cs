using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace ConfigLib
{
    public class ConfigLibModSystem : ModSystem, IConfigProvider
    {
        public IEnumerable<string> Domains => sDomains;
        public IConfig? GetConfig(string domain) => GetConfigStatic(domain);

        static internal HashSet<string> GetDomains() => sDomains;
        static internal Config? GetConfigStatic(string domain) => sConfigs?.ContainsKey(domain) == true ? sConfigs[domain] : null;
        static internal ILogger? Logger { get; private set; }

        static private readonly Dictionary<string, Config> sConfigs = new();
        static private readonly HashSet<string> sDomains = new();
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
        public override double ExecuteOrder()
        {
            return 0.15; // Before 'ModRegistryObjectTypeLoader'
        }
        public override void Dispose()
        {
            Logger = null;
            sConfigs.Clear();
            sDomains.Clear();
            HarmonyPatches.Unpatch("ConfigLib");
            base.Dispose();
        }

        private void LoadConfig(IAsset asset)
        {
            if (mApi == null) return;

            string domain = asset.Location.Domain;
            byte[] data = asset.Data;
            string json = System.Text.Encoding.UTF8.GetString(data);
            JObject token = JObject.Parse(json);
            JsonObject parsedConfig = new(token);
            Config config = new(mApi, domain, parsedConfig);
            sConfigs.Add(domain, config);
            sDomains.Add(domain);
            mApi.Logger.Notification($"[Config lib] Loaded config for '{domain}'");

            StoreConfig(domain, config);
        }
        private void RetrieveConfig(IAsset asset)
        {
            if (mApi == null) return;

            byte[] data = asset.Data;
            SettingsPacket packet = SerializerUtil.Deserialize<SettingsPacket>(data);
            Config config = new(mApi, packet.Domain, packet.GetSettings());
            sConfigs.TryAdd(packet.Domain, config);
            if (!sDomains.Contains(packet.Domain)) sDomains.Add(packet.Domain);
            mApi.Logger.Notification($"[Config lib] Loaded config from server assets for '{packet.Domain}'");
        }
        private void StoreConfig(string domain, Config config)
        {
            byte[] newData = SerializerUtil.Serialize(new SettingsPacket(domain, config.Settings));
            AssetLocation location = new("configlib", $"config/configlib/{domain}");
            Asset configAsset = new(newData, location, new SettingsOrigin(newData, location));
            mApi?.Assets.Add(location, configAsset);
        }
    }
}
