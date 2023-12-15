using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ConfigLib
{
    internal class SettingsOrigin : IAssetOrigin
    {
        public string OriginPath { get; protected set; }

        private readonly byte[] mData;
        private readonly AssetLocation mLocation;

        public SettingsOrigin(byte[] data, AssetLocation location)
        {
            mData = data;
            mLocation = location;
            OriginPath = mLocation.Path;
        }

        public void LoadAsset(IAsset asset)
        {
            
        }

        public bool TryLoadAsset(IAsset asset)
        {
            return true;
        }

        public List<IAsset> GetAssets(AssetCategory Category, bool shouldLoad = true)
        {
            List<IAsset> list = new()
            {
                new Asset(mData, mLocation, this)
            };

            return list;
        }

        public List<IAsset> GetAssets(AssetLocation baseLocation, bool shouldLoad = true)
        {
            List<IAsset> list = new()
            {
                new Asset(mData, mLocation, this)
            };

            return list;
        }

        public virtual bool IsAllowedToAffectGameplay()
        {
            return true;
        }
    }
}