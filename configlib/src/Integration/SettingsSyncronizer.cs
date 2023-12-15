using ProtoBuf;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ConfigLib
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    internal class SettingsPacket
    {
        public string Domain { get; set; } = "";
        public Dictionary<string, ConfigSettingPacket> Settings { get; set; } = new();

        public SettingsPacket() { }

        public SettingsPacket(string domain, Dictionary<string, ConfigSetting> settings)
        {
            Dictionary<string, ConfigSettingPacket> serialized = new();
            foreach ((string key, var value) in settings)
            {
                serialized.Add(key, new(value));
            }

            Settings = serialized;
            Domain = domain;
        }

        public Dictionary<string, ConfigSetting> GetSettings()
        {
            Dictionary<string, ConfigSetting> deserialized = new();
            foreach ((string key, var value) in Settings)
            {
                deserialized.Add(key, new(value));
            }
            return deserialized;
        }
    }

    public class SettingsSynchronizer
    {
        public delegate void SettingsHandler(string domain, Dictionary<string, ConfigSetting> settings);

        private readonly SettingsHandler mHandler;

        public SettingsSynchronizer(ICoreAPI api, SettingsHandler handler, string channelName)
        {
            mHandler = handler;

            if (api is ICoreClientAPI clientApi)
            {
                StartClientSide(clientApi, channelName);
            }
            else if (api is ICoreServerAPI serverApi)
            {
                StartServerSide(serverApi, channelName);
            }
        }

        // CLIENT SIDE

        private void StartClientSide(ICoreClientAPI api, string channelName)
        {
            api.Network.RegisterChannel(channelName)
            .RegisterMessageType<SettingsPacket>()
            .SetMessageHandler<SettingsPacket>(OnClientPacket);
        }
        private void OnClientPacket(SettingsPacket packet)
        {
            Dictionary<string, ConfigSetting> deserialized = new();
            foreach((string key, var value) in packet.Settings)
            {
                deserialized.Add(key, new (value));
            }

            mHandler(packet.Domain, deserialized);
        }

        // SERVER SIDE

        IServerNetworkChannel? mServerNetworkChannel;

        private void StartServerSide(ICoreServerAPI api, string channelName)
        {
            mServerNetworkChannel = api.Network.RegisterChannel(channelName)
            .RegisterMessageType<SettingsPacket>();
        }
        public void SendPacket(string domain, Dictionary<string, ConfigSetting> settings, IServerPlayer player)
        {
            if (mServerNetworkChannel == null) return;

            Dictionary<string, ConfigSettingPacket> serialized = new();
            foreach ((string key, var value) in settings)
            {
                serialized.Add(key, new (value));
            }

            SettingsPacket packet = new()
            {
                Settings = serialized,
                Domain = domain
            };

            mServerNetworkChannel.SendPacket(packet, player);
        }
    }
}
