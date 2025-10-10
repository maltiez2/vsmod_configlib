using configlib.source.Util;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConfigLib;

public sealed class TypedConfig<TConfigClass> : ITypedConfig<TConfigClass>, IDisposable where TConfigClass : class, new()
{
    internal TypedConfig(ICoreAPI api, Mod? source, string relativeConfigFilePath, TConfigClass? instance = null)
    {
        _api = api;
        Source = source;

        string? extension = Path.GetExtension(relativeConfigFilePath);
        if(extension is null || !extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"incorrect file extension in path '{relativeConfigFilePath}', expected '.json' but found '{extension}'", nameof(relativeConfigFilePath));
        }

        RelativeConfigFilePath = relativeConfigFilePath;

        if(instance is not null)
        {
            Instance = instance;
        }
        else if (IsOwnConfigFile())
        {
            ReadFromFile();
            WriteToFile();
            if (!IsAutoConfig)
            {
                try
                {
                    ConfigFileWatcher = new(api, this);
                }
                catch(Exception exception)
                {
                    _api.Logger.Warning("[Config lib] [mod: {0}] Failed to create file watcher. Automatic updates when file is changed on disc will not work! path: '{1}', error: {2}", Source?.Info.Name ?? "unknown", ConfigFilePath, exception);
                }
            }
        }

        if(_instance is null)
        {
            throw new ConfigLibException($"[Config lib] [mod: {Source?.Info.Name ?? "unknown"}] no config data was loaded for '{ConfigFilePath}'");
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

            _instance = value;
            ConfigChanged();
        }
    }

    public void ConfigChanged()
    {
        if(IsAutoConfig) return;

        try
        {
            OnConfigChanged?.Invoke(Instance);
        }
        catch(Exception exception)
        {
            _api.Logger.Error("[Config lib] [mod: {0}] An error occured during the OnConfigChanged event of '{1}', error: {2}", Source?.Info.Name ?? "unknown", ConfigFilePath, exception);
        }
    }

    public event Action<TConfigClass>? OnConfigChanged;
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
            _api.Logger.Error("[Config lib] [mod: {0}] An error occured while reading config file at '{1}' will use default settings, error: {2}", Source?.Info.Name ?? "unknown", ConfigFilePath, exception);
            newConfig = new();
        }

        if(newConfig is null)
        {
            _api.Logger.Notification("[Config lib] [mod: {0}] Creating default settings file at '{1}'", Source?.Info.Name ?? "unknown", ConfigFilePath);
            newConfig = new();
        }

        Instance = newConfig;
        
        return true;
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