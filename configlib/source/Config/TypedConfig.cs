using configlib.source.Util;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConfigLib;

public sealed class TypedConfig<TConfigClass> : ITypedConfig<TConfigClass>, IDisposable where TConfigClass : class, new()
{
    internal TypedConfig(ICoreAPI api, Mod source, string relativeConfigFilePath, byte[]? dataFromServer = null)
    {
        _api = api;
        Source = source;

        string? extension = Path.GetExtension(relativeConfigFilePath);
        if(extension is null || !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"incorrect file extension in path '{relativeConfigFilePath}', expected '.json' but found '{extension}'", nameof(relativeConfigFilePath));
        }

        RelativeConfigFilePath = relativeConfigFilePath;

        if (IsOwnConfigFile())
        {
            ReadFromFile();
            WriteToFile();
            try
            {
                ConfigFileWatcher = new(api, this);
            }
            catch(Exception exception)
            {
                _api.Logger.Warning("[Config lib] [mod: {0}] Failed to create file watcher. Automatic updates when file is changed on disc will not work! path: '{1}', error: {2}", Source?.Info.Name ?? "Unkown", ConfigFilePath, exception);
            }
        }
        else if(dataFromServer is not null)
        {
            ReadFromServerData(dataFromServer);
        }

        if(_instance is null)
        {
            throw new ConfigLibException($"[Config lib] [mod: {Source?.Info.Name ?? "Unknown"}] no config data was loaded for '{ConfigFilePath}'");
        }
    }

    private readonly ICoreAPI _api;

    public bool IsAutoConfig { get; internal init; } = false;

    public Mod? Source { get; }

    public string RelativeConfigFilePath { get; }

    public string ConfigFilePath => Path.Combine(GamePaths.ModConfig, RelativeConfigFilePath);

    public ConfigFileWatcher? ConfigFileWatcher { get; set; }

    public EnumAppSide Side { get; init; } = EnumAppSide.Universal;

    public bool ShouldSynchronize { get; init; } = true;

    private TConfigClass _instance;

    public TConfigClass Instance
    {
        get => _instance;
        private set
        {
            if (_instance == value) return;

            if (IsAutoConfig)
            {
                _instance.CopyFieldsFrom(value);
                return;
            }

            var oldInstance = _instance;
            _instance = value;

            try
            {
                OnConfigChanged?.Invoke(oldInstance, value);
            }
            catch(Exception exception)
            {
                _api.Logger.Error("[Config lib] [mod: {0}] An error occured during the OnConfigChanged event of '{1}', error: {2}", Source?.Info.Name ?? "Unkown", ConfigFilePath, exception);
            }

            if(oldInstance is IDisposable disposableConfig)
            {
                disposableConfig.Dispose();
            }
        }
    }

    public event ITypedConfig<TConfigClass>.ConfigChangedDelegate? OnConfigChanged;
    //TODO syncing config to joined clients!

    public System.Func<TConfigClass, int>? VersionProvider { get; init; }

    public int Version => VersionProvider is null ? 1 : VersionProvider(Instance);

    public void RestoreToDefaults() => Instance.CopyFieldsFrom(new());
    
    public bool IsOwnConfigFile()
    {
        //TODO validate that side is correct in comparison to the side it's requested from in code
        return Side switch
        {
            EnumAppSide.Client => true,
            EnumAppSide.Server => true,
            EnumAppSide.Universal => !ShouldSynchronize || _api is not ICoreClientAPI { IsSinglePlayer: false },
            _ => false
        };
    }

    public bool ReadFromFile()
    {
        if(!IsOwnConfigFile()) return false;

        TConfigClass? newConfig;
        try
        {
             newConfig = _api.LoadModConfig<TConfigClass>(RelativeConfigFilePath);
        }
        catch(Exception exception)
        {
            _api.Logger.Error("[Config lib] [mod: {0}] An error occured while reading config file at '{1}' will use default settings, error: {2}", Source?.Info.Name ?? "Unkown", ConfigFilePath, exception);
            newConfig = new();
        }

        if(newConfig is null)
        {
            _api.Logger.Notification("[Config lib] [mod: {0}] Creating default settings file at '{1}'", Source?.Info.Name ?? "Unkown", ConfigFilePath);
            newConfig = new();
        }

        Instance = newConfig;
        
        return true;
    }

    private void ReadFromServerData(byte[] dataFromServer)
    {
        using var stream = new MemoryStream(dataFromServer);
        using var streamReader = new StreamReader(stream);
        using var jsonTextReader = new JsonTextReader(streamReader);
        var serializer = JsonSerializer.CreateDefault();
        Instance = serializer.Deserialize<TConfigClass>(jsonTextReader) ?? throw new InvalidConfigException($"[Config lib] [mod: {Source?.Info.Name ?? "Unknown"}] config received from server could not be deserialized for '{ConfigFilePath}'");

    }

    public void WriteToFile()
    {
        if(!IsOwnConfigFile()) return;

        _api.StoreModConfig(Instance, RelativeConfigFilePath);
    }

    public void Dispose()
    {
        if(Instance is IDisposable disposableConfig)
        {
            disposableConfig.Dispose();
        }
        ConfigFileWatcher?.Dispose();
    }
}