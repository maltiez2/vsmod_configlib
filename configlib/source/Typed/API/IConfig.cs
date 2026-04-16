using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace configlib.source.Typed.API;

public interface IConfigProvider
{
    /// <summary>
    /// Whether configs are still allowed to be registered or if the registration phase has ended. <br/>
    /// </summary>
    bool CanRegisterNewConfig { get; }

    IConfig? this[string path] { get; }
}

public interface IConfig<T> : IConfig where T : notnull, new()
{
    public new T Instance { get; init; }

    Type? IConfigInfo.ConfigType => typeof(T);

    object IConfig.Instance => Instance;
}

public interface IConfig : IConfigInfo
{
    object Instance { get; }

    /// <summary>
    /// Event that fires when the config is saved
    /// </summary>
    public event Action<IConfig>? OnConfigSaved;

    /// <summary>
    /// Event that fires when the config value has been changed
    /// </summary>
    public event Action<IConfig>? OnConfigChanged;

}

public interface IConfigInfo
{
    /// <summary>
    /// Path to the config file relative to the ModConfig directory.
    /// </summary>
    public string RelativePath { get; init; }
    
    Type? ConfigType { get; }

    /// <summary>
    /// Whether the editor is allowed to directly change the values on the <see cref="Instance"/> rather then waiting for save/apply <br/>
    /// Note: This is ignored in a multiplayer environment. (to ensure changes are properly synchronized)
    /// </summary>
    bool AllowDirectEdit { get; init; }

    /// <summary>
    /// Whether the config should be synchronized from the server.
    /// </summary>
    bool IsSynchronized { get; init; }

    /// <summary>
    /// The relevenat side of the config
    /// </summary>
    EnumAppSide RelevantSide { get; init; }

    /// <summary>
    /// Validates that the settings are as expected by the one requesting this config to be loaded.
    /// </summary>
    /// <returns>True if the settings match, otherwise false</returns>
    bool ValidateExpectedSettings<T>(string relativePath, bool isSynchronized, EnumAppSide relevantSide, ILogger? logger = null);

    void WriteToTreeAttribute(ITreeAttribute tree);
}