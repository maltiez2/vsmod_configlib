using configlib.source.Typed.API;
using configlib.source.Typed.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace configlib.source.Typed.Config;

public class Config<T> : ConfigInfo, IConfig<T> where T : notnull, new()
{
    public Config() : base() { }

    [SetsRequiredMembers]
    public Config(IConfigInfo info, T instance) : base(info)
    {
        Instance = instance;
    }

    public required T Instance { get; init; }

    public override Type? ConfigType => typeof(T);
    
    public event Action<IConfig>? OnConfigSaved;
    
    public event Action<IConfig>? OnConfigChanged;

    public override void WriteToTreeAttribute(ITreeAttribute tree)
    {
        base.WriteToTreeAttribute(tree);

        if(!IsSynchronized) return;
        
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, Encoding.UTF8);
        using var jsonWriter = new JsonTextWriter(writer);
        
        var serializer = JsonSerializer.CreateDefault();
        serializer.Serialize(jsonWriter, Instance);
        
        tree.SetBytes(nameof(IConfig.Instance), ms.ToArray());
    }
}

public class UnloadedConfig : ConfigInfo
{
    [SetsRequiredMembers]
    public UnloadedConfig(ITreeAttribute tree) : base(tree)
    {
        ConfigTypeString = tree.GetString(nameof(IConfigInfo.ConfigType));
        ConfigType = Type.GetTypeSafe(ConfigTypeString);
        Instance = tree.GetBytes(nameof(IConfig.Instance));
    }

    public string ConfigTypeString { get; }

    public override Type? ConfigType { get; }

    public byte[]? Instance { get; }

    public IConfig<T>? TryLoadConfig<T>(ICoreAPI api, ILogger? logger) where T : notnull, new()
    {
        if((api.Side & RelevantSide) == 0)
        {
            logger?.Error("Attempt to load config at path '{0}' on side it is not relevant for. Relevant side: '{1}', current side: '{2}'", RelativePath, RelevantSide, api.Side);
            return null;
        }

        if(Instance is null)
        {
            logger?.Error("Attempt to load config at path '{0}' but no config content was received.", RelativePath);
            return null;
        }

        T instance;
        try
        {
            using var stream = new MemoryStream(Instance);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var jsonReader = new JsonTextReader(reader);

            var token = JToken.ReadFrom(jsonReader);

            instance = token.ToObject<T>() ?? throw new InvalidDataException("Received data was null");
            return new Config<T>(this, instance);
        }
        catch(Exception ex)
        {
            logger?.Error("Failed to deserialize config at path '{0}' as type '{1}', exception: {2}", RelativePath, typeof(T), ex);
            return null;
        }
    }
}

public abstract class ConfigInfo : IConfigInfo
{
    protected ConfigInfo() { }
    
    [SetsRequiredMembers]
    protected ConfigInfo(IConfigInfo info)
    {
        RelativePath = info.RelativePath;
        IsSynchronized = info.IsSynchronized;
        RelevantSide = info.RelevantSide;
        AllowDirectEdit = info.AllowDirectEdit;
    }

    [SetsRequiredMembers]
    protected ConfigInfo(ITreeAttribute tree)
    {
        RelativePath = tree.GetString(nameof(IConfigInfo.RelativePath));
        IsSynchronized = tree.GetBool(nameof(IConfigInfo.IsSynchronized));
        RelevantSide = (EnumAppSide)tree.GetInt(nameof(IConfigInfo.RelevantSide));
        AllowDirectEdit = tree.GetBool(nameof(IConfigInfo.AllowDirectEdit));
    }

    public required string RelativePath { get; init; }

    public abstract Type? ConfigType { get; }

    public required bool IsSynchronized { get; init; }
    
    public required EnumAppSide RelevantSide { get; init; }

    public bool AllowDirectEdit { get; init; }

    public virtual void WriteToTreeAttribute(ITreeAttribute tree)
    {
        tree.SetString(nameof(IConfigInfo.RelativePath), RelativePath);
        tree.SetString(nameof(IConfigInfo.ConfigType), ConfigType?.AssemblyQualifiedName);
        tree.SetBool(nameof(IConfigInfo.IsSynchronized), IsSynchronized);
        tree.SetInt(nameof(IConfigInfo.RelevantSide), (int)RelevantSide);
        tree.SetBool(nameof(IConfigInfo.AllowDirectEdit), AllowDirectEdit);
    }

    public bool ValidateExpectedSettings<T>(string relativePath, bool isSynchronized, EnumAppSide relevantSide, ILogger? logger = null)
    {
        if(typeof(T) != ConfigType)
        {
            logger?.Error("Config typ mismatch. Expected '{0}' but got '{1}'", ConfigType, typeof(T));
            return false;
        }

        if(relativePath != RelativePath)
        {
            logger?.Error("Relative path mismatch. Expected '{0}' but got '{1}'", RelativePath, relativePath);
            return false;
        }

        if(isSynchronized != IsSynchronized)
        {
            logger?.Error("IsSynchronized mismatch. Expected '{0}' but got '{1}'", IsSynchronized, isSynchronized);
            return false;
        }

        if(relevantSide != RelevantSide)
        {
            logger?.Error("RelevantSide mismatch. Expected '{0}' but got '{1}'", RelevantSide, relevantSide);
            return false;
        }

        return true;
    }
}