using configlib.source.Typed.API;
using configlib.source.Typed.Config;
using configlib.source.Typed.Extensions;
using Newtonsoft.Json;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace configlib.source.Typed;

public class TypedConfigLibModSystem : ModSystem, IConfigProvider
{
    public bool CanRegisterNewConfig { get; private set; } = true;

    private static readonly Dictionary<string, IConfig> _loadedConfigs = [];

    private Dictionary<string, UnloadedConfig>? _unloadedConfigs;

    public IConfig? this[string path]
    {
        get
        {
            path = path.EnsureEndsWith(Globals.FileExtension_Json);

            _loadedConfigs.TryGetValue(path, out IConfig? config);
            return config;
        }
    }

    private bool ShouldSynchronize<T>(string relativePath, EnumAppSide relevantSide, bool? synchronize)
    {
        if (synchronize is null)
        {
            return relevantSide == EnumAppSide.Universal;
        }
        
        if(relevantSide != EnumAppSide.Universal && synchronize.Value)
        {
            Mod.Logger.Warning("Config with path '{0}' of type '{1}' is set to synchronize but is only relevant on {2} side. Synchronization is not necessary in this case, so the synchronize flag will be ignored.", relativePath, typeof(T).AssemblyQualifiedName, relevantSide);
            return false;
        }
        
        return synchronize.Value;
    }

    private bool FindExistingConfig<T>(string relativePath, out IConfig<T>? config) where T : notnull, new()
    {
        IConfig? existingConfig = this[relativePath];
        config = null;
        
        if (existingConfig is null) return false;

        if(existingConfig is IConfig<T> typedConfig)
        {
            config = typedConfig;
            return true;
        }

        Mod.Logger.Error("A config already exists with path '{0}' but of type '{1}' instead of the requested type '{2}'", relativePath, existingConfig.ConfigType?.AssemblyQualifiedName ?? Globals.Unknown, typeof(T).AssemblyQualifiedName);
        return true;
    }

    /// <summary>
    /// The method for registering/fetching your config.<br/>
    /// Note: new configs cannot be registered after <see cref="AssetsFinalize(ICoreAPI)"/>
    /// </summary>
    /// <typeparam name="T"/>
    /// <param name="api"/>
    /// <param name="relativePath">
    /// The path of the config file relative to the ModConfig folder.
    /// </param>
    /// <param name="relevantSide">
    /// The side on which the config is relevant.<br/>
    /// Note: This is used to determine wether a config should be loadable on the current side, attempting to load a config (excluding for editor) on a side that is not relevant will result in an error
    /// </param>
    /// <param name="synchronize">
    /// Whether the config should be synchronized from the server to the client
    /// </param>
    /// <param name="allowDirectEdit">
    /// Whether the player is allowed to enter DirectEdit mode which directly changes the values on the <see cref="IConfig{T}.Instance"/> rather than waiting for save/apply.<br/>
    /// Note: This is ignored in a multiplayer environment (to ensure changes are properly synchronized)
    /// </param>
    /// <param name="onInitialLoad">
    /// Action to be invoked when the config is initially loaded.
    /// Note: this is mostly for storing the config in a static field/property.
    /// </param>
    /// <returns>The registered/fetched config, or null if an error occurred</returns>
    public IConfig<T>? GetOrRegisterConfig<T>(ICoreAPI api, string relativePath, EnumAppSide relevantSide = EnumAppSide.Universal, bool? synchronize = null, bool allowDirectEdit = false, Action<IConfig<T>>? onInitialLoad = null) where T : notnull, new()
    {
        if(api.Side == EnumAppSide.Client) EnsureConfigsReadFromWorldConfig(api);
        
        relativePath = relativePath.EnsureEndsWith(Globals.FileExtension_Json);

        if((relevantSide & api.Side) == 0)
        {
            Mod.Logger.Error("Attempt to get/register a config with path '{0}' of type '{1}' on a side not relevant for the config. Relevant on: '{2}', current: '{3}'.", relativePath, typeof(T).AssemblyQualifiedName, relevantSide, api.Side);
            return null;
        }

        synchronize = ShouldSynchronize<T>(relativePath, relevantSide, synchronize);


        if(FindExistingConfig(relativePath, out IConfig<T>? existingTypedConfig))
        {
            if(existingTypedConfig is null || existingTypedConfig.ValidateExpectedSettings<T>(relativePath, synchronize.Value, relevantSide, Mod.Logger))
            {
                return existingTypedConfig;
            }
            return null;
        }

        if (!CanRegisterNewConfig)
        {
            Mod.Logger.Error("Attempt to register a new config with path '{0}' of type '{1}' after the config registration phase has ended. This is not allowed.", relativePath, typeof(T).AssemblyQualifiedName);
            return null;
        }

        var unloadedConfig = _unloadedConfigs?.GetValueOrDefault(relativePath);
        if(unloadedConfig is not null)
        {
            if(!unloadedConfig.ValidateExpectedSettings<T>(relativePath, synchronize.Value, relevantSide, Mod.Logger))
            {
                return null;
            }

            var loadedConfig = unloadedConfig.TryLoadConfig<T>(api, Mod.Logger);
            if(loadedConfig is not null && onInitialLoad is not null)
            {
                onInitialLoad.InvokeSafe(Mod.Logger, Events.InitialConfigLoad, loadedConfig);
                //TODO eventbus maybe
            }
            return loadedConfig;
        }

        if(synchronize.Value && relevantSide == EnumAppSide.Universal && api.Side == EnumAppSide.Client)
        {
            Mod.Logger.Error("Attempt to load synchronized config with path '{0}' of type '{1}' on the client side, but no such config was received from the server.", relativePath, typeof(T).AssemblyQualifiedName);
            return null;
        }

        T instance;
        try
        {
            instance = api.LoadModConfig<T>(relativePath);

            if(instance is null)
            {
                instance = new T();
                api.StoreModConfig(instance, relativePath); //TODO allow for comments/annotation
            }
        }
        catch(Exception ex)
        {
            Mod.Logger.Error("Failed to load config at {0} of type {1}, using default values. Exception: {2}", relativePath, typeof(T).AssemblyQualifiedName, ex);
            instance = new T();
        }

        var config = new Config<T>()
        {
            Instance = instance,
            RelativePath = relativePath,
            RelevantSide = relevantSide,
            IsSynchronized = synchronize.Value,
            AllowDirectEdit = allowDirectEdit
        };
        _loadedConfigs[relativePath] = config;
        onInitialLoad?.InvokeSafe(Mod.Logger, Events.InitialConfigLoad, config);

        return config;
    }

    internal static void WriteConfigsToWorldConfig(ITreeAttribute tree)
    {
        tree = tree.GetOrAddTreeAttribute(Globals.ConfigTreeKey);

        foreach (var (key, config) in _loadedConfigs)
        {
            
            var configAttr = new TreeAttribute();
            config.WriteToTreeAttribute(configAttr);
            tree[key] = configAttr;
        }
    }

    internal void EnsureConfigsReadFromWorldConfig(ICoreAPI api)
    {
        var configTree = api.World.Config.GetTreeAttribute(Globals.ConfigTreeKey);
        if(configTree is null) return;
        _unloadedConfigs ??= [];
        
        foreach (var configAttr in configTree.Values.OfType<ITreeAttribute>())
        {
            var config = new UnloadedConfig(configAttr);
            _unloadedConfigs[config.RelativePath] = config;
        }

        api.World.Config.RemoveAttribute(Globals.ConfigTreeKey);
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        CanRegisterNewConfig = false;
    }

    public override void Dispose()
    {
        base.Dispose();
        _loadedConfigs.Clear();
    }
}
