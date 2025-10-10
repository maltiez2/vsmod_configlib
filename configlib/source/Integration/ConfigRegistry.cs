using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace ConfigLib;

internal sealed class ConfigRegistry
{
    public const string ConfigLibTreeKey = "configlib";
    public const string ConfigQuanityKey = "quantity";
    public const string SerializedConfigsKey = "configs";

    /// <summary>
    /// Wether it's still posible to register new configs<br/>
    /// Will be false on server after configs have been sent to client<br/>
    /// Will be false on client once... //TODO
    /// </summary>
    public bool CanRegisterNewConfig { get; internal set; } = true;

    public readonly Dictionary<string, IContentConfig> ContentConfigs = [];
    public readonly static Dictionary<string, ITypedConfig> TypedConfigs = []; //TODO cleanup

    public void Register(ICoreAPI api, IConfig config)
    {
        switch (config)
        {
            case IContentConfig contentConfig:
                if (!CanRegisterNewConfig)
                {
                    api.Logger.Error("[Config lib] [domain: {0}] Config registered too late for '{1}'", contentConfig.Domain, contentConfig.RelativeConfigFilePath);
                    return;
                }
                ContentConfigs.Add(contentConfig.Domain, contentConfig);
                
                break;

            case ITypedConfig typedConfig:
                if (!CanRegisterNewConfig)
                {
                    api.Logger.Error("[Config lib] [mod: {0}] Config registered too late for '{1}'", typedConfig.Source?.Info.Name ?? "unknown", typedConfig.RelativeConfigFilePath);
                    return;
                }
                TypedConfigs.Add(typedConfig.RelativeConfigFilePath, typedConfig);

                break;

            default: throw new NotSupportedException("only ITypedConfigs and IContentConfigs are currently supported");
        }
    }

    internal void ExtractFromWorldConfig(IWorldAccessor resolver)
    {
        var configLibTree = resolver.Config.GetTreeAttribute(ConfigLibTreeKey);
        if(configLibTree is null)
        {
            //TODO see if we can get configlib to work even if not on server
            resolver.Logger.Warning("[Config lib] [Registry] no config lib data was received through WorldConfig");
            return;
        }

        var quantity = configLibTree.GetInt(ConfigQuanityKey);
        var data = configLibTree.GetBytes(SerializedConfigsKey);
        FromBytes(resolver, quantity, data);
    }

    public void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
    {
        if(quantity == 0 || data is null) return;

        using MemoryStream serializedRecipesList = new(data);
        using BinaryReader reader = new(serializedRecipesList);

        for (int count = 0; count < quantity; count++)
        {
            EnumConfigType configType = (EnumConfigType)reader.ReadInt32();
            string relativeConfigFilePath;
            int configDataLength;
            byte[] configData;
            

            switch (configType)
            {
                case EnumConfigType.YAML:
                case EnumConfigType.JSON:

                    string domain = reader.ReadString();
                    string modName = reader.ReadString();
                    relativeConfigFilePath = reader.ReadString();
                    
                    configDataLength = reader.ReadInt32();
                    configData = reader.ReadBytes(configDataLength);

                    SettingsPacket packet = SerializerUtil.Deserialize<SettingsPacket>(configData);
                    
                    if (configType == EnumConfigType.JSON)
                    {
                        ContentConfigs[domain] = new Config(resolver.Api, packet.Domain, modName, new(JObject.Parse(Asset.BytesToString(packet.Definition))), relativeConfigFilePath, packet.GetSettings());
                    }
                    else
                    {
                        ContentConfigs[domain] = new Config(resolver.Api, packet.Domain, modName, new(JObject.Parse(Asset.BytesToString(packet.Definition))), packet.GetSettings());
                    }

                    break;

                case EnumConfigType.CODE:

                    var configTypeStr = reader.ReadString();

                    var modid = reader.ReadString();
                    relativeConfigFilePath = reader.ReadString();
                    
                    configDataLength = reader.ReadInt32();
                    configData = reader.ReadBytes(configDataLength);

                    var source = resolver.Api.ModLoader.GetMod(modid);

                    if(TypedConfigs.ContainsKey(relativeConfigFilePath)) continue;
                    try
                    {
                        var configClassType = Type.GetType(configTypeStr) ?? throw new ConfigLibException($"Could not resolve config type '{configTypeStr}', is this type not known to client?");
                        
                        TypedConfigs[relativeConfigFilePath] = CreateTypedConfig(configClassType, resolver.Api, source, relativeConfigFilePath, configData);
                    }
                    catch(Exception exception)
                    {
                        resolver.Logger.Error("[Config lib] [mod: {0}] Failed to load received config for '{1}', exception: {2}", source?.Info.Name ?? "unknown", relativeConfigFilePath, exception);
                        continue;
                    }
                    break;
                
                default: throw new InvalidConfigException($"Invalid ConfigType '{configType}'");
            }
        }

        resolver.Logger.Debug($"[Config lib] [Registry] Received config from server: {quantity}");

        //ConfigsLoaded?.Invoke(ContentConfigs); //TODO
    }

    public void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity)
    {
        if(resolver.Side == EnumAppSide.Server) CanRegisterNewConfig = false;
        quantity = 0;
        byte[] configData;

        using MemoryStream serializedConfigs = new();
        using BinaryWriter writer = new(serializedConfigs);

        //TODO come up with better serialization/deserialization way (we don't want to have to specify fixed type here)
        foreach (Config contentConfig in ContentConfigs.Values.OfType<Config>())
        {
            writer.Write((int)contentConfig.ConfigType);
            writer.Write(contentConfig.Domain);
            writer.Write(contentConfig.ModName);
            writer.Write(contentConfig.RelativeConfigFilePath);
            configData = SerializerUtil.Serialize(new SettingsPacket(contentConfig.Domain, contentConfig.Settings, contentConfig.Definition));
            writer.Write(configData.Length);
            writer.Write(configData);

            quantity++;
        }

        foreach (ITypedConfig typedConfig in TypedConfigs.Values)
        {
            if(typedConfig.Side != EnumAppSide.Universal || !typedConfig.ShouldSynchronize) continue;
            
            var configTypeStr = typedConfig.Type.AssemblyQualifiedName;
            if(string.IsNullOrEmpty(configTypeStr))
            {
                resolver.Logger.Error("[Config lib] [mod: {0}] config type of '{1}' could not be understood", typedConfig.Source?.Info.Name ?? "unknown", typedConfig.ConfigFilePath);
                continue;
            }

            writer.Write((int)typedConfig.ConfigType);
            writer.Write(typedConfig.Source?.Info.ModID ?? "unknown");
            writer.Write(typedConfig.RelativeConfigFilePath);
            
            configData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(typedConfig.Instance, Newtonsoft.Json.Formatting.None));
            writer.Write(configData.Length);
            writer.Write(configData);

            quantity++;
        }

        resolver.Logger.Debug($"[Config lib] [Registry] Configs prepared to send to client: {quantity}");

        data = serializedConfigs.ToArray();
    }

    private static ITypedConfig CreateTypedConfig(Type type, ICoreAPI api, Mod? source, string relativeConfigFilePath, byte[] data)
    {
        return (ITypedConfig)typeof(ConfigRegistry)
            .GetMethod(nameof(CreateTypedConfigGeneric))!
            .MakeGenericMethod(type)
            .Invoke(null, [api, source, relativeConfigFilePath, data])!;
    }

    private static TypedConfig<TConfigClass> CreateTypedConfigGeneric<TConfigClass>(ICoreAPI api, Mod? source, string relativeConfigFilePath, byte[] data) where TConfigClass : class, new()
    {

        using var stream = new MemoryStream(data);
        using var streamReader = new StreamReader(stream, Encoding.UTF8);
        using var jsonTextReader = new JsonTextReader(streamReader);

        var serializer = JsonSerializer.CreateDefault();
        var instance = serializer.Deserialize<TConfigClass>(jsonTextReader) ?? throw new InvalidConfigException($"[Config lib] [mod: {source?.Info.Name ?? "Unknown"}] config received from server could not be deserialized for '{relativeConfigFilePath}'");
        return new(api, source, relativeConfigFilePath, instance);
    }
}