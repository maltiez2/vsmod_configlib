using Vintagestory.API.Common;

namespace configlib.source.Typed.API;

public delegate void ConfigEventDelegate(IConfig config);

public interface IConfig<T> : IConfig where T : notnull, new()
{
    public new T Instance { get; init; }

    Type IConfig.ConfigType => typeof(T);

    object IConfig.Instance => Instance;
}

public interface IConfig
{
    /// <summary>
    /// Path to the config file relative to the ModConfig directory.
    /// </summary>
    public string RelativePath { get; init; }
    
    Type ConfigType { get; }

    object Instance { get; }

    /// <summary>
    /// Wether the editor is allowed to directly change the values on the <see cref="Instance"/> rather then waiting for save/apply</br>
    /// Note: Direct edit will automatically be disabled in the case of synchronized configs in a multiplayer environment.
    /// </summary>
    bool AllowDirectEdit { get; init; }

    /// <summary>
    /// The relevenat side of the config</br>
    /// Client => client side config
    /// Server => server side config
    /// Universal => universal config that is synced from server to client
    /// </summary>
    EnumAppSide RelevantSide { get; init; }

    /// <summary>
    /// Event that fires when the config is initially loaded
    /// </summary>
    public event ConfigEventDelegate ConfigLoaded;

    /// <summary>
    /// Event that fires when the config is saved
    /// </summary>
    public event ConfigEventDelegate OnConfigSaved;

    /// <summary>
    /// Event that fires when the config value has been changed
    /// </summary>
    public event ConfigEventDelegate OnConfigChanged;
}
