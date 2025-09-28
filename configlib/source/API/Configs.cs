using Vintagestory.API.Common;

namespace ConfigLib;

/// <summary>
/// Provides methods for accessing configs and settings
/// </summary>
public interface IConfigProvider
{
    /// <summary>
    /// Fired when config window is closed
    /// </summary>
    event Action? ConfigWindowClosed;
    /// <summary>
    /// Fired when config window is opened
    /// </summary>
    event Action? ConfigWindowOpened;
    /// <summary>
    /// Fired right after all configs are loaded
    /// </summary>
    event Action? ConfigsLoaded;

    /// <summary>
    /// Stores domains of all mods that with configs
    /// </summary>
    IEnumerable<string> Domains { get; }
    /// <summary>
    /// Provides access to configs by domains. Each domain can have only one config registered. Domain also is used to get mod name in GUI, because of this you should use domain that matches modid of some mod.
    /// </summary>
    /// <param name="domain">Mod id of a mod which config you want to access</param>
    /// <returns>Config or null if not found</returns>
    IConfig? GetConfig(string domain);
    /// <summary>
    /// Same as <see cref="GetConfig"/> but retrieves specific setting by its code
    /// </summary>
    /// <param name="domain">Mod id of a mod which setting you want to access</param>
    /// <param name="code">Setting code, specified either in 'code' field of the setting or by key in 'settings'</param>
    /// <returns>Setting or null if not found</returns>
    ISetting? GetSetting(string domain, string code);
    /// <summary>
    /// Registers callback for a mod with given mod id. It is called each frame on drawing GUI window, you can call ImGui widgets functions in it.<br/>
    /// This callback is provided with control buttons struct which tells what control buttons was pressed in the frame this delegate is called.
    /// </summary>
    /// <param name="domain">Mod id of your mod</param>
    /// <param name="drawDelegate">First argument is id you can pass to your ImGui widgets titles/ids to prevent interference with other widgets inside config window.<br/>
    /// Second argument provides for each control button in gui if this button is pressed this frame.
    /// </param>
    void RegisterCustomConfig(string domain, Action<string, ControlButtons> drawDelegate);
    /// <summary>
    /// Registers callback for a mod with given mod id. It is called each frame on drawing GUI window, you can call ImGui widgets functions in it.<br/>
    /// This callback is provided with control buttons struct which tells what control buttons was pressed in the frame this delegate is called.
    /// </summary>
    /// <param name="domain">Mod id of your mod</param>
    /// <param name="drawDelegate">First argument is id you can pass to your ImGui widgets titles/ids to prevent interference with other widgets inside config window.<br/>
    /// Second argument provides for each control button in gui if this button is pressed this frame.<br/>
    /// Returns what buttons should be displayed in the window.
    /// </param>
    void RegisterCustomConfig(string domain, System.Func<string, ControlButtons, ControlButtons> drawDelegate);
    /// <summary>
    /// Fired when a setting in a config changed. Action arguments: domain, config, setting.
    /// </summary>
    event Action<string, IConfig, ISetting>? SettingChanged;
}

public interface IConfig
{
    /// <summary>
    /// Full path to loaded config file.<br/>In multiplayer on client is equal to path of client side config.
    /// </summary>
    string ConfigFilePath { get; }
    /// <summary>
    /// Version of loaded config file.<br/>In multiplayer on client is equal to version of client side config.
    /// </summary>
    public int Version { get; }
    /// <summary>
    /// Writes current config into file.<br/>In multiplayer on client updates only client side settings and write to file on client side.
    /// </summary>
    void WriteToFile();
    /// <summary>
    /// Reads config from file.<br/>In multiplayer on client reads values only of client side settings.
    /// </summary>
    /// <returns></returns>
    bool ReadFromFile();
    /// <summary>
    /// Sets all settings to default values<br/>In multiplayer on client sets values only of client side settings.
    /// </summary>
    void RestoreToDefaults();
}

/// <summary>
/// Represents a config made the content modding way
/// </summary>
public interface IContentConfig : IConfig
{
    /// <summary>
    /// Returns settings by given code.<br/>Setting code, specified either in 'code' field of the setting or by key in 'settings'.
    /// </summary>
    /// <param name="code">Setting code, specified either in 'code' field of the setting or by key in 'settings'.</param>
    /// <returns></returns>
    ISetting? GetSetting(string code);
    /// <summary>
    /// Get called when a setting value changes.
    /// </summary>
    public event Action<ISetting>? SettingChanged;
    /// <summary>
    /// Will try to assign values from config to object fields or properties with matching YAML codes.
    /// </summary>
    /// <param name="target"></param>
    void AssignSettingsValues(object target);
}

/// <summary>
/// Represents a config with an actual associated type
/// </summary>
public interface ITypedConfig : IConfig
{
    /// <summary>
    /// Which mod the config originates from (can potentially be null for auto configs)
    /// </summary>
    Mod? Source { get; }

    /// <summary>
    /// Which side this config should be loaded on, by default this is <see cref="EnumAppSide.Universal"/>.
    /// </summary>
    EnumAppSide Side { get; init; }

    /// <summary>
    /// Whether the config should be synchronized from server to client, by default this is true though it is only relevant if <see cref="Side"/> is <see cref="EnumAppSide.Universal"/>
    /// </summary>
    bool ShouldSynchronize { get; init; }

    /// <summary>
    /// The loaded instance of the config type
    /// </summary>
    object Instance { get; }

    /// <summary>
    /// The type of the config instance
    /// </summary>
    Type Type { get; }

    /// <summary>
    /// Path to the config file relative to the config directory
    /// </summary>
    string RelativeConfigFilePath { get; }

    /// <summary>
    /// True if this is our own config file, false if this config file was sent over from a server
    /// </summary>
    bool IsOwnConfigFile();
}

/// <inheritdoc/>
/// <typeparam name="TConfigClass">The class of the underlying config</typeparam>
public interface ITypedConfig<TConfigClass> : ITypedConfig where TConfigClass : class
{
    /// <summary>
    /// The loaded instance of the config type
    /// </summary>
    new TConfigClass Instance { get; }

    object ITypedConfig.Instance => Instance;

    Type ITypedConfig.Type => typeof(TConfigClass);

    /// <summary>
    /// Called when the config changes instance <br/>
    /// WARNING: this is not called for Auto Configs
    /// </summary>
    event ConfigChangedDelegate? OnConfigChanged;
    public delegate void ConfigChangedDelegate(TConfigClass oldConfig, TConfigClass newConfig);
}
