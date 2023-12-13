using ProtoBuf;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ConfigLib
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class SettingsPacket
    {
        public string Domain;
        public Dictionary<string, ConfigSettingPacket> Settings;

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
            if (api.Side == EnumAppSide.Client)
            {
                mHandler = handler;
                StartClientSide(api as ICoreClientAPI, channelName);
            }
            else if (api.Side == EnumAppSide.Server)
            {
                StartServerSide(api as ICoreServerAPI, channelName);
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

        IServerNetworkChannel mServerNetworkChannel;

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

            SettingsPacket packet = new SettingsPacket()
            {
                Settings = serialized,
                Domain = domain
            };

            mServerNetworkChannel.SendPacket(packet, player);
        }
    }
}
