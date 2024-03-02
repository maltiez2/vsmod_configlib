using System;
using Vintagestory.API.Client;
using VSImGui;
using VSImGui.API;

namespace ConfigLib
{
    internal class GuiManager : IDisposable
    {
        private readonly ConfigWindow mConfigWindow;

        private bool mDisposed;
        private bool mShowConfig = false;
        private static GuiManager? mInstance;
        private ImGuiModSystem mModSystem;

        public GuiManager(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("configlibconfigs", "(Config lib) Open configs window", GlKeys.P, HotkeyType.DevTool, false, false, false);
            api.Input.SetHotKeyHandler("configlibconfigs", ShowConfigWindow);
            mModSystem = api.ModLoader.GetModSystem<ImGuiModSystem>();
            mModSystem.Draw += Draw;
            mModSystem.Closed += CloseConfigWindow;
            mConfigWindow = new(api);
            mInstance = this;
        }

        public CallbackGUIStatus Draw(float deltaSeconds)
        {
            if (!mShowConfig) return CallbackGUIStatus.Closed;

            if (!mConfigWindow.Draw())
            {
                mShowConfig = false;
                return CallbackGUIStatus.Closed;
            }

            return CallbackGUIStatus.GrabMouse;
        }

        public bool ToggleConfigWindow()
        {
            if (!mShowConfig)
            {
                OpenConfigWindow();
            }
            else
            {
                CloseConfigWindow();
            }

            return true;
        }

        public static bool ShowConfigWindowStatic()
        {
            mInstance?.ToggleConfigWindow();

            return true;
        }

        public void OpenConfigWindow()
        {
            mShowConfig = true;
            mModSystem.Show();
        }
        public void CloseConfigWindow()
        {
            mShowConfig = false;
        }

        private bool ShowConfigWindow(KeyCombination keyCombination)
        {
            mShowConfig = !mShowConfig;

            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!mDisposed)
            {
                if (disposing)
                {
                    mModSystem.Draw -= Draw;
                    mModSystem.Closed -= CloseConfigWindow;
                }

                mDisposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
