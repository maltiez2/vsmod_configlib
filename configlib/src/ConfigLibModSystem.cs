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

namespace ConfigLib;

public class ConfigLibModSystem : ModSystem, IConfigProvider
{
    public IEnumerable<string> Domains => sDomains;
    public IConfig? GetConfig(string domain) => GetConfigStatic(domain);
    public ISetting? GetSetting(string domain, string code) => GetConfigStatic(domain)?.GetSetting(code);
    public void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate) => sCustomConfigs.TryAdd(domain, drawDelegate);

    static internal HashSet<string> GetDomains() => sDomains;
    static internal Config? GetConfigStatic(string domain) => sConfigs?.ContainsKey(domain) == true ? sConfigs[domain] : null;
    static internal Dictionary<string, Action<string, ControlButtons>>? GetCustomConfigs() => sCustomConfigs;

    static private readonly Dictionary<string, Config> sConfigs = new();
    static private readonly HashSet<string> sDomains = new();
    static private readonly Dictionary<string, Action<string, ControlButtons>> sCustomConfigs = new();
    private GuiManager? mGuiManager;
    private ICoreAPI? mApi;

    public override void Start(ICoreAPI api)
    {
        mApi = api;
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        LoadConfigs();
        foreach ((_, Config config) in sConfigs)
        {
            config.Apply();
        }
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
            case EnumAppSide.Universal:
            case EnumAppSide.Server:
                foreach (IAsset asset in mApi.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "configlib-patches.json"))
                {
                    try
                    {
                        LoadConfig(asset);
                    }
                    catch (Exception exception)
                    {
                        mApi.Logger.Error($"[Config lib] Error on loading config for {mApi.ModLoader.GetMod(asset.Location.Domain)}.");
                        mApi.Logger.VerboseDebug($"[Config lib] Error on loading config for {mApi.ModLoader.GetMod(asset.Location.Domain)}.\n{exception}\n");
                    }
                    
                }
                break;
            case EnumAppSide.Client:
                foreach (IAsset asset in mApi.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Location.BeginsWith("configlib", "config/configlib")))
                {
                    try
                    {
                        RetrieveConfig(asset);
                    }
                    catch (Exception exception)
                    {
                        mApi.Logger.Error($"[Config lib] Error on loading config for {mApi.ModLoader.GetMod(asset.Location.GetName())}.");
                        mApi.Logger.VerboseDebug($"[Config lib] Error on loading config for {mApi.ModLoader.GetMod(asset.Location.GetName())}.\n{exception}\n");
                    }
                }
                break;
        }
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
        Config config = new(mApi, packet.Domain, packet.GetSettings(), new(JObject.Parse(Asset.BytesToString(packet.Definition))));
        sConfigs.TryAdd(packet.Domain, config);
        if (!sDomains.Contains(packet.Domain)) sDomains.Add(packet.Domain);
        mApi.Logger.Debug($"[Config lib] Loaded config from server assets for '{packet.Domain}'");
    }
    private void StoreConfig(string domain, Config config)
    {
        byte[] newData = SerializerUtil.Serialize(new SettingsPacket(domain, config.Settings, config.Definition));
        AssetLocation location = new("configlib", $"config/configlib/{domain}");
        Asset configAsset = new(newData, location, new SettingsOrigin(newData, location));
        mApi?.Assets.Add(location, configAsset);
    }
}
