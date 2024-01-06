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
        public void RegisterCustomConfig(string domain, Action<string> drawDelegate) => sCustomConfigs.TryAdd(domain, drawDelegate);

        static internal HashSet<string> GetDomains() => sDomains;
        static internal Config? GetConfigStatic(string domain) => sConfigs?.ContainsKey(domain) == true ? sConfigs[domain] : null;
        static internal Dictionary<string, Action<string>>? GetCustomConfigs() => sCustomConfigs;

        static private readonly Dictionary<string, Config> sConfigs = new();
        static private readonly HashSet<string> sDomains = new();
        static private readonly Dictionary<string, Action<string>> sCustomConfigs = new();
        private GuiManager? mGuiManager;
        private HarmonyPatches? mPatches;
        private ICoreAPI? mApi;

        public override void Start(ICoreAPI api)
        {
            mApi = api;
            mPatches = new HarmonyPatches(api).Patch("ConfigLib");
        }
        public override void AssetsLoaded(ICoreAPI api)
        {
            LoadConfigs();
            PatchAssetsAndLog("patches");
            PatchAssetsAndLog("recipes");
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
            return 0.01;
        }
        public override void Dispose()
        {
            sConfigs.Clear();
            sDomains.Clear();
            sCustomConfigs.Clear();
            mPatches?.Unpatch("ConfigLib");
            if (mApi?.Side == EnumAppSide.Client && mGuiManager != null)
            {
                mApi.ModLoader.GetModSystem<VSImGuiModSystem>().SetUpImGuiWindows -= mGuiManager.Draw;
                mGuiManager.Dispose();
            }

            base.Dispose();
        }

        private void LoadConfigs()
        {
            switch (mApi?.Side)
            {
                case EnumAppSide.Server:
                    foreach (IAsset asset in mApi.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "settings-config.json"))
                    {
                        LoadConfig(asset);
                    }
                    break;
                case EnumAppSide.Client:
                    foreach (IAsset asset in mApi.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Location.BeginsWith("configlib", "config/configlib")))
                    {
                        RetrieveConfig(asset);
                    }
                    break;
                case EnumAppSide.Universal:
                    foreach (IAsset asset in mApi.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "settings-config.json"))
                    {
                        LoadConfig(asset);
                    }
                    break;
            }
        }
        private void PatchAssetsAndLog(string target)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            (int patched, int skipped, int failed) = PatchAssets(target);
            watch.Stop();
            long elapsedMs = watch.ElapsedMilliseconds;
            mApi?.Logger?.Debug($"[Config lib] ({target}) Finish. Assets patched: {patched}, assets not patched: {skipped}, assets were not able to patch: {failed}. Time spent: {elapsedMs}ms");
        }
        private (int patched, int skipped, int failed) PatchAssets(string target)
        {
            if (mApi == null) return (0, 0, 0);

            (int patched, int skipped, int failed) result = (0, 0, 0);

            List<IAsset> many = mApi.Assets.GetMany(target);

            foreach (IAsset asset in many)
            {
                string json = Asset.BytesToString(asset.Data);

                try
                {
                    if (PatchArray(ref json, asset.Location, target))
                    {
                        asset.Data = System.Text.Encoding.UTF8.GetBytes(json);
                        result.patched++;
                    }
                    else
                    {
                        result.skipped++;
                    }
                    continue;
                }
                catch
                {
                    // Will be handled in next block
                }

                try
                {
                    if (PatchObject(ref json, asset.Location, target))
                    {
                        asset.Data = System.Text.Encoding.UTF8.GetBytes(json);
                        result.patched++;
                    }
                    else
                    {
                        result.skipped++;
                    }
                    continue;
                }
                catch
                {
                    result.failed++;
                }
            }

            return result;
        }
        private bool PatchArray(ref string json, AssetLocation location, string target)
        {
            JArray token = JArray.Parse(json);
            JObject dummy = new()
            {
                {"value", token}
            };

            if (RegistryObjectTokensReplacer.ReplaceInBaseType(location.Domain, dummy, location.Path, target, mApi?.Logger))
            {
                json = dummy["value"]?.ToString() ?? json;
                return true;
            }

            return false;
        }
        private bool PatchObject(ref string json, AssetLocation location, string target)
        {
            JObject token = JObject.Parse(json);

            if (RegistryObjectTokensReplacer.ReplaceInBaseType(location.Domain, token, location.Path, target, mApi?.Logger))
            {
                json = token.ToString();
                return true;
            }

            return false;
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
            mApi.Logger.Debug($"[Config lib] Loaded config from server assets for '{packet.Domain}'");
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
