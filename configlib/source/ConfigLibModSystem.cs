using ConfigLib.Patches;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace ConfigLib;

public sealed class ConfigLibModSystem : ModSystem, IConfigProvider
{
    public IEnumerable<string> Domains => _domains;
    public IConfig? GetConfig(string domain) => GetConfigImpl(domain);
    public ISetting? GetSetting(string domain, string code) => GetConfigImpl(domain)?.GetSetting(code);
    public void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate) => _customConfigs.TryAdd(domain, DrawDelegateWrapper(drawDelegate));
    public void RegisterCustomConfig(string domain, System.Func<string, ControlButtons, ControlButtons> drawDelegate) => _customConfigs.TryAdd(domain, drawDelegate);
    public void RegisterCustomManagedConfig(string domain, object configObject, string? path = null, Action? onSyncedFromServer = null, Action<string>? onSettingChanged = null, Action? onConfigSaved = null)
    {
        if (_api == null) return;

        if (!_canRegisterNewConfig)
        {
            LoggerUtil.Error(_api, this, $"Cant register custom managed config '{domain}': too late, configs have been already sent to clients");
            return;
        }

        Config config = new(_api, domain, _api.ModLoader.GetMod(domain)?.Info.Name ?? Lang.Get(domain), configObject, path ?? domain + ".json");

        _configs.Add(domain, config);
        _domains.Add(domain);
        _configsToRegister.Add(domain, config);

        if (_api.Side == EnumAppSide.Server)
        {
            config.ConfigSaved += OnConfigSaved;
            foreach ((string code, ConfigSetting setting) in config.Settings)
            {
                setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                OnSettingLoaded(domain, code, setting);
            }
        }

        config.SettingChanged += setting => SettingChanged?.Invoke(domain, config, setting);
        config.SettingChanged += setting =>
        {
            setting.AssignSettingValue(configObject);
            onSettingChanged?.Invoke(setting.YamlCode);
        };

        config.ConfigSaved += config =>
        {
            onConfigSaved?.Invoke();
        };

        if (_api.Side == EnumAppSide.Client)
        {
            ConfigsLoaded += () =>
            {
                onSyncedFromServer?.Invoke();
            };
        }

        _customManagedConfigs.Add(domain);
    }

    public event Action? ConfigWindowClosed;
    public event Action? ConfigWindowOpened;
    public event Action<string, IConfig, ISetting>? SettingChanged;

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
    internal Dictionary<string, System.Func<string, ControlButtons, ControlButtons>>? GetCustomConfigs() => _customConfigs;

    private readonly Dictionary<string, Config> _configs = new();
    private readonly HashSet<string> _domains = new();
    private readonly Dictionary<string, System.Func<string, ControlButtons, ControlButtons>> _customConfigs = new();
    private readonly Dictionary<string, Config> _configsToRegister = [];
    private readonly HashSet<string> _customManagedConfigs = [];

    private GuiManager? _guiManager;
    private ICoreAPI? _api;
    private const string _registryCode = "configlib:configs";
    private ConfigRegistry? _registry;
    private IClientNetworkChannel? _eventsChannel;
    private IServerNetworkChannel? _eventsServerChannel;
    private const string _channelName = "configlib:events";
    private bool _canRegisterNewConfig = true;

    public override void StartPre(ICoreAPI api)
    {
        _api = api;
    }
    public override void Start(ICoreAPI api)
    {
        _registry = api.RegisterRecipeRegistry<ConfigRegistry>(_registryCode);
        api.Event.RegisterEventBusListener(ReloadJsonConfigs, filterByEventName: ConfigReloadEvent);

        if (api.Side == EnumAppSide.Client)
        {
            PauseMenuPatch.Patch();
            ConfigRegistry.ConfigsLoaded += ReloadConfigs;
            _eventsChannel = (api as ICoreClientAPI)?.Network.RegisterChannel(_channelName)
                .RegisterMessageType<ConfigEventPacket>()
                .RegisterMessageType<ServerSideSettingChanged>()
                .SetMessageHandler<ServerSideSettingChanged>(OnServerSettingChanged);

        }
        else
        {
            _eventsServerChannel = (api as ICoreServerAPI)?.Network.RegisterChannel(_channelName)
                .RegisterMessageType<ConfigEventPacket>()
                .SetMessageHandler<ConfigEventPacket>(SendEvent)
                .RegisterMessageType<ServerSideSettingChanged>()
                .SetMessageHandler<ServerSideSettingChanged>(OnServerSettingChanged);
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

        foreach ((_, Config config) in _configs)
        {
            config.Dispose();
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
        _registry = null;
        _guiManager = null;

        base.Dispose();
    }

    private static System.Func<string, ControlButtons, ControlButtons> DrawDelegateWrapper(Action<string, ControlButtons> callback)
    {
        return (domain, buttons) =>
        {
            callback(domain, buttons);
            return new() { Defaults = true, Reload = true, Restore = true, Save = true };
        };
    }

    private void ReloadConfigs(Dictionary<string, Config> configs)
    {
        _api?.Logger.Notification($"[Config lib] Configs received from server: {configs.Count}");

        foreach ((string domain, Config config) in configs)
        {
            if (_customManagedConfigs.Contains(domain))
            {
                _configs[domain].SyncFromServer(config, (_api as ICoreClientAPI)?.IsSinglePlayer == true);
                _configs[domain].ConfigSaved += OnConfigSaved;
                foreach ((string code, ConfigSetting setting) in _configs[domain].Settings)
                {
                    setting.SettingChanged += (value) => OnSettingChanged(domain, code, value);
                    OnSettingLoaded(domain, code, setting);
                }
                continue;
            }

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

        if (_api is ICoreClientAPI clientApi)
        {
            try
            {
                _guiManager = new(clientApi);
                _guiManager.ConfigWindowClosed += () => ConfigWindowClosed?.Invoke();
                _guiManager.ConfigWindowOpened += () => ConfigWindowOpened?.Invoke();
            }
            catch (Exception exception)
            {
                clientApi.Logger.Error($"[Config lib] Error on creating GUI manager. Probably missing ImGui or it has incorrect version.\nException:\n{exception}");
            }
        }

        ConfigsLoaded?.Invoke();
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
                _api.Logger.Error($"[Config lib] Error on loading config for {asset.Location.Domain}.");
                _api.Logger.VerboseDebug($"[Config lib] Error on loading config for {asset.Location.Domain}.\n{exception}\n");
            }
        }

        if (_api.Side == EnumAppSide.Server)
        {
            foreach ((string domain, Config config) in _configsToRegister)
            {
                registry?.Register(domain, config);
            }
            _configsToRegister.Clear();
        }

        ConfigRegistry.OnToBytes += () => _canRegisterNewConfig = false;
    }
    private void LoadConfig(IAsset asset, ConfigRegistry? registry)
    {
        if (_api == null) return;

        string domain = asset.Location.Domain;
        byte[] data = asset.Data;
        data = System.Text.Encoding.Convert(System.Text.Encoding.UTF8, System.Text.Encoding.Unicode, data);
        string json = System.Text.Encoding.Unicode.GetString(data);
        int startIndex = 0;
        if (json.Contains('{'))
        {
            startIndex = json.IndexOf('{');
        }
        json = json.Substring(startIndex, json.Length - startIndex);
        JObject token = JObject.Parse(json);
        JsonObject parsedConfig = new(token);

        Config config;
        if (parsedConfig.KeyExists("file"))
        {
            config = new(_api, domain, _api.ModLoader.GetMod(domain)?.Info.Name ?? Lang.Get(domain), parsedConfig, parsedConfig["file"].AsString());
        }
        else
        {
            config = new(_api, domain, _api.ModLoader.GetMod(domain)?.Info.Name ?? Lang.Get(domain), parsedConfig);
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

        config.SettingChanged += setting => SettingChanged?.Invoke(domain, config, setting);
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

        if (_api is ICoreClientAPI clientApi && clientApi.World.Player.HasPrivilege(Privilege.controlserver))
        {
            ServerSideSettingChanged packet = new(config.Domain, config.Settings.Where(setting => setting.Value.ChangedSinceLastSave && !setting.Value.ClientSide).ToDictionary(), clientApi.IsSinglePlayer);

            if (packet.Settings.Any())
            {
                _eventsChannel?.SendPacket(packet);
            }
        }

        foreach ((_, ConfigSetting? setting) in config.Settings)
        {
            setting.ChangedSinceLastSave = false;
        }
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
    private void OnServerSettingChanged(IServerPlayer player, ServerSideSettingChanged packet)
    {
        if (!packet.Settings.Any()) return;

        if (!player.HasPrivilege(Privilege.controlserver))
        {
            _api?.Logger.Warning($"[Config lib] Player '{player.PlayerName}' without privilege '{Privilege.controlserver}' tried to change config for mod  '{packet.ConfigDomain}'.");
            _api?.Logger.Audit($"[Config lib] missing privilege to change config: '{player.PlayerName}' - '{packet.ConfigDomain}'.");
            return;
        }

        Config? config = GetConfigImpl(packet.ConfigDomain);

        if (config == null)
        {
            _api?.Logger.Error($"[Config lib] Player '{player.PlayerName}' tried to change config '{packet.ConfigDomain}', but such config does not exist.");
            return;
        }

        string settingsChanged = "";

        foreach ((string settingCode, ConfigSettingPacket settingPacket) in packet.Settings)
        {
            ConfigSetting settingFromClient = new(settingPacket);

            ConfigSetting? serverSetting = (ConfigSetting?)config.GetSetting(settingCode);

            if (serverSetting == null)
            {
                _api?.Logger.Error($"[Config lib] Player '{player.PlayerName}' tried to change setting '{settingCode}' in config for mod '{packet.ConfigDomain}', but such setting does not exist in this config.");
                continue;
            }

            if (serverSetting.ClientSide) continue;

            serverSetting.Value = settingFromClient.Value;
            serverSetting.MappingKey = settingFromClient.MappingKey;

            if (settingsChanged != "") settingsChanged += ", ";
            settingsChanged += serverSetting.YamlCode;
        }

        _api?.Logger.Audit($"[Config lib] config changed: '{player.PlayerName}' - {packet.ConfigDomain} - {settingsChanged}");
        _api?.Logger.Notification($"[Config lib] Player '{player.PlayerName}' changed settings: {settingsChanged}, and saved config file for mod '{_api.ModLoader.GetMod(packet.ConfigDomain)?.Info.Name} ({packet.ConfigDomain})'.");

        if (!packet.IsSinglePlayer) config.WriteToFile();

        _eventsServerChannel?.BroadcastPacket(packet, player);
    }
    private void OnServerSettingChanged(ServerSideSettingChanged packet)
    {
        if (!packet.Settings.Any()) return;

        Config? config = GetConfigImpl(packet.ConfigDomain);

        if (config == null)
        {
            return;
        }

        string settingsChanged = "";

        foreach ((string settingCode, ConfigSettingPacket settingPacket) in packet.Settings)
        {
            ConfigSetting settingFromClient = new(settingPacket);

            ConfigSetting? serverSetting = (ConfigSetting?)config.GetSetting(settingCode);

            if (serverSetting == null)
            {
                continue;
            }

            if (serverSetting.ClientSide) continue;

            serverSetting.Value = settingFromClient.Value;
            serverSetting.MappingKey = settingFromClient.MappingKey;

            if (settingsChanged != "") settingsChanged += ", ";
            settingsChanged += serverSetting.YamlCode;
        }
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
        Dictionary<string, ConfigSettingPacket> serialized = [];
        foreach ((string key, ConfigSetting? value) in settings)
        {
            serialized.Add(key, new(value));
        }

        Definition = System.Text.Encoding.UTF8.GetBytes(definition.ToString());
        Settings = serialized;
        Domain = domain;
    }

    public Dictionary<string, ConfigSetting> GetSettings()
    {
        Dictionary<string, ConfigSetting> deserialized = [];
        foreach ((string key, ConfigSettingPacket? value) in Settings)
        {
            deserialized.Add(key, new(value));
        }
        return deserialized;
    }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ServerSideSettingChanged
{
    public Dictionary<string, ConfigSettingPacket> Settings { get; set; } = [];
    public string ConfigDomain { get; set; } = "";
    public bool IsSinglePlayer { get; set; } = false;

    public ServerSideSettingChanged() { }

    public ServerSideSettingChanged(string domain, Dictionary<string, ConfigSetting> settings, bool isSinglePlayer)
    {
        Dictionary<string, ConfigSettingPacket> serialized = [];
        foreach ((string key, ConfigSetting? value) in settings)
        {
            serialized.Add(key, new(value));
        }

        Settings = serialized;
        ConfigDomain = domain;
        IsSinglePlayer = isSinglePlayer;
    }
}