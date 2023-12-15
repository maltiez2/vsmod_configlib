using Vintagestory.API.Client;

namespace ConfigLib
{
    internal class DevWindow
    {
        private readonly ICoreClientAPI mApi;

        public DevWindow(ICoreClientAPI api)
        {
            mApi = api;
        }

        public void Draw()
        {

        }
    }
}
