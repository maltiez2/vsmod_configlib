using ConfigLib.Patches;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;
using VSImGui;

namespace ConfigLib;

public class ConfigLibModSystem : ModSystem, IConfigProvider
{
    public IEnumerable<string> Domains => mDomains;
    public IConfig? GetConfig(string domain) => GetConfigImpl(domain);
    public ISetting? GetSetting(string domain, string code) => GetConfigImpl(domain)?.GetSetting(code);
    public void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate) => mCustomConfigs.TryAdd(domain, drawDelegate);

    /// <summary>
    /// On Server: right after configs are applied<br/>
    /// On Client: right after configs are received from server and applied (between AssetsLoaded and AssetsFinalize stages)
    /// </summary>
    public event Action? ConfigsLoaded;

    internal HashSet<string> GetDomains() => mDomains;
    internal Config? GetConfigImpl(string domain) => mConfigs?.ContainsKey(domain) == true ? mConfigs[domain] : null;
    internal Dictionary<string, Action<string, ControlButtons>>? GetCustomConfigs() => mCustomConfigs;

    private readonly Dictionary<string, Config> mConfigs = new();
    private readonly HashSet<string> mDomains = new();
    private readonly Dictionary<string, Action<string, ControlButtons>> mCustomConfigs = new();
    private GuiManager? mGuiManager;
    private ICoreAPI? mApi;
    private const string cRegistryCode = "configlib:configs";

    public override void Start(ICoreAPI api)
    {
        mApi = api;
        api.RegisterRecipeRegistry<ConfigRegistry>(cRegistryCode);

        if (api.Side == EnumAppSide.Client)
        {
            PauseMenuPatch.Patch();
            ConfigRegistry.ConfigsLoaded += ReloadConfigs;
        }
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        LoadConfigs();
        mApi?.Logger.Notification($"[Config lib] Configs loaded: {mConfigs.Count}");
        foreach ((_, Config config) in mConfigs)
        {
            config.Apply();
        }

        ConfigsLoaded?.Invoke();
    }
    public override double ExecuteOrder() => 0.01;
    public override void Dispose()
    {
        if (mApi?.Side == EnumAppSide.Client)
        {
            PauseMenuPatch.Patch();
        }

        mConfigs.Clear();
        mDomains.Clear();
        mCustomConfigs.Clear();
        if (mApi?.Side == EnumAppSide.Client && mGuiManager != null)
        {
            mApi.ModLoader.GetModSystem<VSImGuiModSystem>().SetUpImGuiWindows -= mGuiManager.Draw;
            mGuiManager.Dispose();
        }

        base.Dispose();
    }

    private void ReloadConfigs(Dictionary<string, Config> configs)
    {
        mApi?.Logger.Notification($"[Config lib] Configs received from server: {configs.Count}");

        foreach ((string domain, Config config) in configs)
        {
            mDomains.Add(domain);
            mConfigs[domain] = config;
            config.Apply();
        }

        ConfigsLoaded?.Invoke();

        if (mApi is ICoreClientAPI clientApi)
        {
            mGuiManager = new(clientApi);
            clientApi.ModLoader.GetModSystem<VSImGuiModSystem>().SetUpImGuiWindows += mGuiManager.Draw;
        }
    }
    private void LoadConfigs()
    {
        if (mApi == null) return;
        
        ConfigRegistry? registry = GetRegistry(mApi);

        foreach (IAsset asset in mApi.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "configlib-patches.json"))
        {
            try
            {
                LoadConfig(asset, registry);
            }
            catch (Exception exception)
            {
                mApi.Logger.Error($"[Config lib] Error on loading config for {mApi.ModLoader.GetMod(asset.Location.Domain)}.");
                mApi.Logger.VerboseDebug($"[Config lib] Error on loading config for {mApi.ModLoader.GetMod(asset.Location.Domain)}.\n{exception}\n");
            }
        }
    }
    private void LoadConfig(IAsset asset, ConfigRegistry? registry)
    {
        if (mApi == null) return;

        string domain = asset.Location.Domain;
        byte[] data = asset.Data;
        string json = System.Text.Encoding.UTF8.GetString(data);
        JObject token = JObject.Parse(json);
        JsonObject parsedConfig = new(token);
        Config config = new(mApi, domain, parsedConfig);
        mConfigs.Add(domain, config);
        mDomains.Add(domain);

        registry?.Register(domain, config);
    }
    private static ConfigRegistry? GetRegistry(ICoreAPI api)
    {
        MethodInfo? getter = typeof(GameMain).GetMethod("GetRecipeRegistry", BindingFlags.Instance | BindingFlags.NonPublic);
        return (ConfigRegistry?)getter?.Invoke(api.World, new object[] { cRegistryCode });
    }
}

internal class ConfigRegistry : RecipeRegistryBase
{
    public static event Action<Dictionary<string, Config>>? ConfigsLoaded;

    private readonly Dictionary<string, Config> mConfigs = new();

    public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
    {
        using MemoryStream serializedRecipesList = new(data);
        using BinaryReader reader = new(serializedRecipesList);

        for (int count = 0; count < quantity; count++)
        {
            string domain = reader.ReadString();
            int length = reader.ReadInt32();
            byte[] configData = reader.ReadBytes(length);

            SettingsPacket packet = SerializerUtil.Deserialize<SettingsPacket>(configData);
            Config config = new(resolver.Api, packet.Domain, packet.GetSettings(), new(JObject.Parse(Asset.BytesToString(packet.Definition))));

            mConfigs[domain] = config;
        }

        resolver.Logger.Debug($"[Config lib] [Registry] Received config from server: {quantity}");

        ConfigsLoaded?.Invoke(mConfigs);
    }
    public override void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity)
    {
        quantity = mConfigs.Count;

        using MemoryStream serializedConfigs = new();
        using BinaryWriter writer = new(serializedConfigs);

        foreach ((string domain, Config config) in mConfigs)
        {
            writer.Write(domain);
            byte[] configData = SerializerUtil.Serialize(new SettingsPacket(domain, config.Settings, config.Definition));
            writer.Write(configData.Length);
            writer.Write(configData);
        }

        resolver.Logger.Debug($"[Config lib] [Registry] Configs prepared to send to client: {quantity}");

        data = serializedConfigs.ToArray();
    }
    public void Register(string domain, Config config)
    {
        mConfigs[domain] = config;
    }
}