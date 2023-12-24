﻿using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;
using VSImGui;

namespace ConfigLib
{
    public class ConfigLibModSystem : ModSystem, IConfigProvider
    {
        public IEnumerable<string> Domains => sDomains;
        public IConfig? GetConfig(string domain) => GetConfigStatic(domain);
        public ISetting? GetSetting(string domain, string code) => GetConfigStatic(domain)?.GetSetting(code);

        static internal HashSet<string> GetDomains() => sDomains;
        static internal Config? GetConfigStatic(string domain) => sConfigs?.ContainsKey(domain) == true ? sConfigs[domain] : null;
        static internal ILogger? Logger { get; private set; }

        static private readonly Dictionary<string, Config> sConfigs = new();
        static private readonly HashSet<string> sDomains = new();
        private GuiManager? mGuiManager;
        private ICoreAPI? mApi;

        internal Dictionary<string, Action> ModWindowsOpen = new();

        public override void Start(ICoreAPI api)
        {
            mApi = api;
            HarmonyPatches.Patch("ConfigLib");
            if (api.Side == EnumAppSide.Server) Logger = api.Logger;
            if (api.Side == EnumAppSide.Client && Logger == null) Logger = api.Logger;

            // These  will be called inside each mod
            // This is just an example
            RegisterMod("Biomes", new Action(() => { } ));
            RegisterMod("Barbershop", new Action(() => { }));
            RegisterMod("Some other mod", new Action(() => { }));
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

            ReplaceTokens();
        }
        static private readonly HashSet<AssetCategory> sCategories = new()
        {
            //AssetCategory.blocktypes,
            //AssetCategory.itemtypes,
            //AssetCategory.entities,
            AssetCategory.patches,
            AssetCategory.recipes
        };
        private void ReplaceTokens()
        {
            if (mApi == null) return;
            
            var watch = System.Diagnostics.Stopwatch.StartNew();

            int failed = 0;
            int succeeded = 0;
            int skipped = 0;
            foreach ((var location, var asset) in mApi.Assets.AllAssets)
            {
                if (!sCategories.Contains(location.Category) || (!sDomains.Contains(location.Domain))) continue;
                try
                {
                    string domain = asset.Location.Domain;
                    byte[] data = asset.Data;
                    string json = System.Text.Encoding.UTF8.GetString(data);
                    JArray token = JArray.Parse(json);

                    foreach (var item in token)
                    {
                        if (item is not JObject objectItem) continue;
                        
                        if (RegistryObjectTokensReplacer.ReplaceInBaseType(domain, objectItem, location.Path))
                        {
                            asset.Data = System.Text.Encoding.UTF8.GetBytes(token.ToString());
                            succeeded++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                }
                catch
                {
                    failed++;
                }
            }

            watch.Stop();
            var elapsedMs = watch.ElapsedMilliseconds;

            mApi?.Logger?.Notification($"[Config lib] [Recipes and patches] Assets patched: {succeeded}, assets not patched: {skipped}, assets were not able to patch: {failed}. Time spent: {elapsedMs}ms");
        }
        public override void AssetsFinalize(ICoreAPI api)
        {
            if (api is ICoreClientAPI clientApi)
            {
                mGuiManager = new(clientApi);
                clientApi.ModLoader.GetModSystem<VSImGuiModSystem>().SetUpImGuiWindows += mGuiManager.Draw;
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
            if (mApi?.Side == EnumAppSide.Client && mGuiManager != null)
            {
                mApi.ModLoader.GetModSystem<VSImGuiModSystem>().SetUpImGuiWindows -= mGuiManager.Draw;
                mGuiManager.Dispose();
            }
            
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

        public void RegisterMod(string id, Action action)
        {
            ModWindowsOpen.Add(id, action);
        }
    }
}
