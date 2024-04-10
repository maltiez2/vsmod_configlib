using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ConfigLib
{
    internal class SettingsOrigin : IAssetOrigin
    {
        public string OriginPath { get; protected set; }

        private readonly byte[] _data;
        private readonly AssetLocation _location;

        public SettingsOrigin(byte[] data, AssetLocation location)
        {
            _data = data;
            _location = location;
            OriginPath = _location.Path;
        }

        public void LoadAsset(IAsset asset)
        {

        }

        public bool TryLoadAsset(IAsset asset)
        {
            return true;
        }

        public List<IAsset> GetAssets(AssetCategory category, bool shouldLoad = true)
        {
            List<IAsset> list = new()
            {
                new Asset(_data, _location, this)
            };

            return list;
        }

        public List<IAsset> GetAssets(AssetLocation baseLocation, bool shouldLoad = true)
        {
            List<IAsset> list = new()
            {
                new Asset(_data, _location, this)
            };

            return list;
        }

        public virtual bool IsAllowedToAffectGameplay()
        {
            return true;
        }
    }
}