using ImGuiNET;
using System.Collections.Generic;
using Vintagestory.API.Client;
using VSImGui;

namespace ConfigLib
{
    internal class DevWindow
    {
        private readonly ICoreClientAPI mApi;
        private readonly IEnumerable<string> mDomains;
        private Config? mCurrentConfig; 
        private int mCurrentIndex = 0;
        private long mNextId = 0;
        private Style mStyle;
        private bool mStyleLoaded = false;

        public DevWindow(ICoreClientAPI api)
        {
            mApi = api;
            mDomains = ConfigLibModSystem.GetDomains();
            mStyle = new Style();
            LoadStyle();
        }

        public bool Draw()
        {
            mNextId = 0;
            bool opened = true;

            using (new StyleApplier(mStyle))
            {
                ImGui.SetNextWindowSizeConstraints(new(500, 600), new(1000, 2000));
                ImGui.Begin("Configs##configlib", ref opened, ImGuiWindowFlags.MenuBar);
                if (ImGui.BeginMenuBar())
                {
                    if (ImGui.MenuItem("Code")) Code();
                    ImGui.EndMenuBar();
                }
                

                ImGui.End();
            }

            return opened;
        }

        private void LoadStyle()
        {
            if (mStyleLoaded) return;

            mStyle = new Style();
            mStyle.ColorBackgroundMenuBar = (0, 0, 0, 0);
            mStyle.BorderFrame = 0;
            mStyleLoaded = true;
        }

        private void Code()
        {

        }
    }
}
