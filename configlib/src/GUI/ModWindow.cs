using ImGuiNET;
using System.Linq;
using System.Runtime.InteropServices.JavaScript;
using Vintagestory.API.Client;
using VSImGui;

namespace ConfigLib
{
    internal class ModWindow
    {
        private readonly ICoreClientAPI mApi;
        private readonly ConfigLibModSystem configLibModSystem;

        private Style mStyle;
        private bool mStyleLoaded = false;

        public ModWindow(ICoreClientAPI api)
        {
            mApi = api;
            configLibModSystem = mApi.ModLoader.GetModSystem<ConfigLibModSystem>();
        }

        public void Draw()
        {
            LoadStyle();

            using (new StyleApplier(mStyle))
            {
                ImGui.SetNextWindowSizeConstraints(new(500, 600), new(1000, 2000));
                ImGui.Begin("Mods##configlib");

                foreach (var item in configLibModSystem.ModWindowsOpen.OrderBy(x => x.Key))
                {
                    if (ImGui.Button(item.Key)) item.Value.Invoke();
                    ImGui.NewLine();
                }

                ImGui.End();
            }
        }

        private void LoadStyle()
        {
            if (mStyleLoaded) return;

            mStyle = new Style();
            mStyle.ColorBackgroundMenuBar = (0, 0, 0, 0);
            mStyle.BorderFrame = 0;
            mStyleLoaded = true;
        }
    }
}
