using ConfigLib.Patches;
using Newtonsoft.Json.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace ConfigLib;

public class ConfigLibModSystem : ModSystem, IConfigProvider
{
    public IEnumerable<string> Domains => mDomains;
    public IConfig? GetConfig(string domain) => GetConfigImpl(domain);
    public ISetting? GetSetting(string domain, string code) => GetConfigImpl(domain)?.GetSetting(code);
    public void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate) => mCustomConfigs.TryAdd(domain, drawDelegate);
    public IConfig? GetServerConfig(string domain) => GetServerConfigImpl(domain);
    public ISetting? GetServerSetting(string domain, string code) => GetServerConfigImpl(domain)?.GetSetting(code);

    /// <summary>
    /// On Server: right after configs are applied<br/>
    /// On Client: right after configs are received from server and applied (between AssetsLoaded and AssetsFinalize stages)
    /// </summary>
    public event Action? ConfigsLoaded;

    internal HashSet<string> GetDomains() => mDomains;
    internal Config? GetConfigImpl(string domain)
    {
        if (mApi is ICoreClientAPI clientApi && !clientApi.IsSinglePlayer)
        {
            return mServerConfigs?.ContainsKey(domain) == true ? mConfigs[domain] : null;
        }
        else
        {
            return mConfigs?.ContainsKey(domain) == true ? mConfigs[domain] : null;
        }
    }
    internal Config? GetServerConfigImpl(string domain) => mServerConfigs?.ContainsKey(domain) == true ? mConfigs[domain] : null;
    internal Dictionary<string, Action<string, ControlButtons>>? GetCustomConfigs() => mCustomConfigs;

    private readonly Dictionary<string, Config> mConfigs = new();
    private readonly Dictionary<string, Config> mServerConfigs = new();
    private readonly HashSet<string> mDomains = new();
    private readonly Dictionary<string, Action<string, ControlButtons>> mCustomConfigs = new();
    private GuiManager? mGuiManager;
    private ICoreAPI? mApi;
    private const string cRegistryCode = "configlib:configs";
    private ConfigRegistry? mRegistry;

    public override void Start(ICoreAPI api)
    {
        mApi = api;
        mRegistry = api.RegisterRecipeRegistry<ConfigRegistry>(cRegistryCode);

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
            mServerConfigs[domain] = config;

            config.Apply();
        }

        ConfigsLoaded?.Invoke();

        if (mApi is ICoreClientAPI clientApi)
        {
            try
            {
                mGuiManager = new(clientApi);
            }
            catch (Exception exception)
            {
                clientApi.Logger.Error($"[Config lib] Error on creating GUI manager. Probably missing ImGui or it has incorrect version.\nException:\n{exception}");
            }
        }
    }
    private void LoadConfigs()
    {
        if (mApi == null) return;

        ConfigRegistry? registry = mRegistry ?? GetRegistry(mApi);

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
        if (mApi.Side == EnumAppSide.Server) mServerConfigs.Add(domain, config);

        registry?.Register(domain, config);
    }
    private static ConfigRegistry? GetRegistry(ICoreAPI api)
    {
        return (api.World as GameMain)?.GetRecipeRegistry(cRegistryCode) as ConfigRegistry;
    }
}

internal class ConfigRegistry : RecipeRegistryBase
{
    public static event Action<Dictionary<string, Config>>? ConfigsLoaded;

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
            Config config = new(resolver.Api, packet.Domain, new(JObject.Parse(Asset.BytesToString(packet.Definition))), packet.GetSettings());

            _configs[domain] = config;
        }

        resolver.Logger.Debug($"[Config lib] [Registry] Received config from server: {quantity}");

        ConfigsLoaded?.Invoke(_configs);
    }
    public override void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity)
    {
        quantity = 0;

        using MemoryStream serializedConfigs = new();
        using BinaryWriter writer = new(serializedConfigs);

        foreach ((string domain, Config config) in _configs)
        {
            writer.Write(domain);
            byte[] configData = SerializerUtil.Serialize(new SettingsPacket(domain, config.Settings, config.Definition));
            writer.Write(configData.Length);
            writer.Write(configData);
            quantity++;
        }

        resolver.Logger.Debug($"[Config lib] [Registry] Configs prepared to send to client: {quantity}");

        data = serializedConfigs.ToArray();
    }
    public void Register(string domain, Config config)
    {
        _configs.Add(domain, config);
    }

    private readonly Dictionary<string, Config> _configs = new();
}