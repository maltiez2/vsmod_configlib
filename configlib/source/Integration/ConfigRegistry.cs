using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.Common;

namespace ConfigLib;

internal sealed class ConfigRegistry : RecipeRegistryBase
{
    public static event Action<Dictionary<string, Config>>? ConfigsLoaded;
    public static event Action? OnToBytes;

    public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data)
    {
        using MemoryStream serializedRecipesList = new(data);
        using BinaryReader reader = new(serializedRecipesList);

        for (int count = 0; count < quantity; count++)
        {
            string domain = reader.ReadString();
            string modName = reader.ReadString();
            Config.ConfigType fileType = (Config.ConfigType)reader.ReadInt32();
            string jsonFile = reader.ReadString();
            int length = reader.ReadInt32();
            byte[] configData = reader.ReadBytes(length);

            SettingsPacket packet = SerializerUtil.Deserialize<SettingsPacket>(configData);
            Config config;

            if (fileType == Config.ConfigType.JSON)
            {
                config = new(resolver.Api, packet.Domain, modName, new(JObject.Parse(Asset.BytesToString(packet.Definition))), jsonFile, packet.GetSettings());
            }
            else
            {
                config = new(resolver.Api, packet.Domain, modName, new(JObject.Parse(Asset.BytesToString(packet.Definition))), packet.GetSettings());
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
            writer.Write(config.ModName);
            writer.Write((int)config.FileType);
            writer.Write(config.RelativeFilePath);
            byte[] configData = SerializerUtil.Serialize(new SettingsPacket(domain, config.Settings, config.Definition));
            writer.Write(configData.Length);
            writer.Write(configData);
            quantity++;
        }

        resolver.Logger.Debug($"[Config lib] [Registry] Configs prepared to send to client: {quantity}");

        data = serializedConfigs.ToArray();

        OnToBytes?.Invoke();
    }
    public void Register(string domain, Config config)
    {
        _configs.Add(domain, config);
    }

    private readonly Dictionary<string, Config> _configs = [];
}