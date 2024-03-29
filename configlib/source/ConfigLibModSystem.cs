﻿using ConfigLib.Patches;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace ConfigLib;

public class ConfigLibModSystem : ModSystem, IConfigProvider
{
    public IEnumerable<string> Domains => _domains;
    public IConfig? GetConfig(string domain) => GetConfigImpl(domain);
    public ISetting? GetSetting(string domain, string code) => GetConfigImpl(domain)?.GetSetting(code);
    public void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate) => _customConfigs.TryAdd(domain, drawDelegate);

    public const string ConfigSavedEvent = "configlib:{0}:config-saved";
    public const string ConfigChangedEvent = "configlib:{0}:setting-changed";
    public const string ConfigLoadedEvent = "configlib:{0}:setting-loaded";
    public const string ConfigReloadEvent = "configlib:config-reload";

    /// <summary>
    /// On Server: right after configs are applied<br/>
    /// On Client: right after configs are received from server and applied (between AssetsLoaded and AssetsFinalize stages)
    /// </summary>
    public event Action? ConfigsLoaded;

    internal HashSet<string> GetDomains() => _domains;
    internal Config? GetConfigImpl(string domain) => _configs?.ContainsKey(domain) == true ? _configs[domain] : null;
    internal Dictionary<string, Action<string, ControlButtons>>? GetCustomConfigs() => _customConfigs;

    private readonly Dictionary<string, Config> _configs = new();
    private readonly HashSet<string> _domains = new();
    private readonly Dictionary<string, Action<string, ControlButtons>> _customConfigs = new();
    private GuiManager? _guiManager;
    private ICoreAPI? _api;
    private const string _registryCode = "configlib:configs";
    private ConfigRegistry? _registry;
    private IClientNetworkChannel? _eventsChannel;
    private const string _channelName = "configlib:events";

    public override void Start(ICoreAPI api)
    {
        _api = api;
        _registry = api.RegisterRecipeRegistry<ConfigRegistry>(_registryCode);
        api.Event.RegisterEventBusListener(ReloadJsonConfigs, filterByEventName: ConfigReloadEvent);

        if (api.Side == EnumAppSide.Client)
        {
            PauseMenuPatch.Patch();
            ConfigRegistry.ConfigsLoaded += ReloadConfigs;
            _eventsChannel = (api as ICoreClientAPI)?.Network.RegisterChannel(_channelName)
                .RegisterMessageType<ConfigEventPacket>();
        }
        else
        {
            (api as ICoreServerAPI)?.Network.RegisterChannel(_channelName)
                .RegisterMessageType<ConfigEventPacket>()
                .SetMessageHandler<ConfigEventPacket>(SendEvent);
        }
    }
    public override void AssetsLoaded(ICoreAPI api)
    {
        LoadConfigs();
        _api?.Logger.Notification($"[Config lib] Configs loaded: {_configs.Count}");
        foreach ((_, Config config) in _configs)
        {
            config.Apply();
        }

        ConfigsLoaded?.Invoke();
    }
    public override double ExecuteOrder() => 0.01;
    public override void Dispose()
    {
        if (_api?.Side == EnumAppSide.Client)
        {
            PauseMenuPatch.Patch();
        }

        _configs.Clear();
        _domains.Clear();
        _customConfigs.Clear();
        if (_api?.Side == EnumAppSide.Client && _guiManager != null)
        {
            _guiManager.Dispose();
        }
        if (_api?.Side == EnumAppSide.Client)
        {
            ConfigRegistry.ConfigsLoaded -= ReloadConfigs;
        }

        base.Dispose();
    }

    private void ReloadConfigs(Dictionary<string, Config> configs)
    {
        _api?.Logger.Notification($"[Config lib] Configs received from server: {configs.Count}");

        foreach ((string domain, Config config) in configs)
        {
            _domains.Add(domain);
            _configs[domain] = config;
            config.Apply();

            config.ConfigSaved += OnConfigSaved;
            foreach ((string code, ConfigSetting setting) in config.Settings)
            {
                setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                OnSettingLoaded(domain, code, setting);
            }
        }

        ConfigsLoaded?.Invoke();

        if (_api is ICoreClientAPI clientApi)
        {
            try
            {
                _guiManager = new(clientApi);
            }
            catch (Exception exception)
            {
                clientApi.Logger.Error($"[Config lib] Error on creating GUI manager. Probably missing ImGui or it has incorrect version.\nException:\n{exception}");
            }
        }
    }
    private void LoadConfigs()
    {
        if (_api == null) return;

        ConfigRegistry? registry = _registry ?? GetRegistry(_api);

        foreach (IAsset asset in _api.Assets.GetMany(AssetCategory.config.Code).Where((asset) => asset.Name == "configlib-patches.json"))
        {
            try
            {
                LoadConfig(asset, registry);
            }
            catch (Exception exception)
            {
                _api.Logger.Error($"[Config lib] Error on loading config for {_api.ModLoader.GetMod(asset.Location.Domain)}.");
                _api.Logger.VerboseDebug($"[Config lib] Error on loading config for {_api.ModLoader.GetMod(asset.Location.Domain)}.\n{exception}\n");
            }
        }
    }
    private void LoadConfig(IAsset asset, ConfigRegistry? registry)
    {
        if (_api == null) return;

        string domain = asset.Location.Domain;
        byte[] data = asset.Data;
        string json = System.Text.Encoding.UTF8.GetString(data);
        JObject token = JObject.Parse(json);
        JsonObject parsedConfig = new(token);

        Config config;
        if (parsedConfig.KeyExists("file"))
        {
            config = new(_api, domain, parsedConfig, parsedConfig["file"].AsString());
        }
        else
        {
            config = new(_api, domain, parsedConfig);
        }

        _configs.Add(domain, config);
        _domains.Add(domain);

        registry?.Register(domain, config);

        if (_api.Side == EnumAppSide.Server)
        {
            config.ConfigSaved += OnConfigSaved;
            foreach ((string code, ConfigSetting setting) in config.Settings)
            {
                setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                OnSettingLoaded(domain, code, setting);
            }
        }
    }
    private static ConfigRegistry? GetRegistry(ICoreAPI api)
    {
        return (api.World as GameMain)?.GetRecipeRegistry(_registryCode) as ConfigRegistry;
    }

    private void OnConfigSaved(Config config)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", config.Domain);
        string eventName = string.Format(ConfigSavedEvent, config.Domain);
        _api?.Event.PushEvent(eventName, eventDataTree);
        SendEventToServer(eventName, eventDataTree);
    }
    private void OnSettingChanged(string domain, string code, ConfigSetting setting)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", domain);
        eventDataTree.SetString("setting", code);

        switch (setting.SettingType)
        {
            case ConfigSettingType.Boolean:
                eventDataTree.SetBool("value", setting.Value.AsBool());
                break;
            case ConfigSettingType.Float:
                eventDataTree.SetFloat("value", setting.Value.AsFloat());
                break;
            case ConfigSettingType.Integer:
                eventDataTree.SetInt("value", setting.Value.AsInt());
                break;
            case ConfigSettingType.String:
                eventDataTree.SetString("value", setting.Value.AsString());
                break;
            case ConfigSettingType.Other:
                eventDataTree.SetAttribute("value", setting.Value.ToAttribute());
                break;
        }
        string eventName = string.Format(ConfigChangedEvent, domain);
        _api?.Event.PushEvent(eventName, eventDataTree);
        SendEventToServer(eventName, eventDataTree);
    }
    private void OnSettingLoaded(string domain, string code, ConfigSetting setting)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.SetString("domain", domain);
        eventDataTree.SetString("setting", code);

        switch (setting.SettingType)
        {
            case ConfigSettingType.Boolean:
                eventDataTree.SetBool("value", setting.Value.AsBool());
                break;
            case ConfigSettingType.Float:
                eventDataTree.SetFloat("value", setting.Value.AsFloat());
                break;
            case ConfigSettingType.Integer:
                eventDataTree.SetInt("value", setting.Value.AsInt());
                break;
            case ConfigSettingType.String:
                eventDataTree.SetString("value", setting.Value.AsString());
                break;
            case ConfigSettingType.Other:
                eventDataTree.SetAttribute("value", setting.Value.ToAttribute());
                break;
        }
        string eventName = string.Format(ConfigLoadedEvent, domain);
        _api?.Event.PushEvent(eventName, eventDataTree);
    }
    private void ReloadJsonConfigs(string eventName, ref EnumHandling handling, IAttribute data)
    {
        string domain = (data as ITreeAttribute)?.GetAsString("domain") ?? "";
        _configs[domain].ReadFromFile();
    }

    private void SendEventToServer(string eventName, TreeAttribute eventData)
    {
        ConfigEventPacket eventPacket = new()
        {
            EventName = eventName,
            Data = eventData.ToBytes()
        };
        _eventsChannel?.SendPacket(eventPacket);
    }
    private void SendEvent(IServerPlayer fromPlayer, ConfigEventPacket eventPacket)
    {
        TreeAttribute eventDataTree = new();
        eventDataTree.FromBytes(eventPacket.Data);
        eventDataTree.SetString("player", fromPlayer.PlayerUID);
        _api?.Event.PushEvent(eventPacket.EventName, eventDataTree);
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
            Config.ConfigType fileType = (Config.ConfigType)reader.ReadInt32();
            string jsonFile = reader.ReadString();
            int length = reader.ReadInt32();
            byte[] configData = reader.ReadBytes(length);

            SettingsPacket packet = SerializerUtil.Deserialize<SettingsPacket>(configData);
            Config config;

            if (fileType == Config.ConfigType.JSON)
            {
                config = new(resolver.Api, packet.Domain, new(JObject.Parse(Asset.BytesToString(packet.Definition))), jsonFile, packet.GetSettings());
            }
            else
            {
                config = new(resolver.Api, packet.Domain, new(JObject.Parse(Asset.BytesToString(packet.Definition))), packet.GetSettings());
            }

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
            writer.Write((int)config.FileType);
            writer.Write(config.JsonFilePath);
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

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ConfigEventPacket
{
    public string EventName { get; set; } = "";
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class SettingsPacket
{
    public string Domain { get; set; } = "";
    public Dictionary<string, ConfigSettingPacket> Settings { get; set; } = new();
    public byte[] Definition { get; set; } = System.Array.Empty<byte>();

    public SettingsPacket() { }

    public SettingsPacket(string domain, Dictionary<string, ConfigSetting> settings, JsonObject definition)
    {
        Dictionary<string, ConfigSettingPacket> serialized = new();
        foreach ((string key, var value) in settings)
        {
            serialized.Add(key, new(value));
        }

        Definition = System.Text.Encoding.UTF8.GetBytes(definition.ToString());
        Settings = serialized;
        Domain = domain;
    }

    public Dictionary<string, ConfigSetting> GetSettings()
    {
        Dictionary<string, ConfigSetting> deserialized = new();
        foreach ((string key, var value) in Settings)
        {
            deserialized.Add(key, new(value));
        }
        return deserialized;
    }
}