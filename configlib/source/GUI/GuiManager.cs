using ModdingTools;
using System;
using Vintagestory.API.Client;
using VSImGui.src.ImGui;

namespace ConfigLib
{
    internal class GuiManager : IDisposable
    {
        private readonly ConfigWindow mConfigWindow;

        private bool mDisposed;
        private bool mShowConfig = false;
        private static GuiManager? mInstance;

        public GuiManager(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("configlibconfigs", "(Config lib) Open configs window", GlKeys.P, HotkeyType.DevTool, false, false, false);
            api.Input.SetHotKeyHandler("configlibconfigs", ShowConfigWindow);

            mConfigWindow = new(api);
            mInstance = this;
        }

        public VSDialogStatus Draw(float deltaSeconds)
        {
            if (!mShowConfig) return VSDialogStatus.Closed;
            
            if (!mConfigWindow.Draw())
            {
                mShowConfig = false;
                return VSDialogStatus.Closed;
            }

            return VSDialogStatus.GrabMouse;
        }

        public void ShowConfigWindow()
        {
            mShowConfig = !mShowConfig;
        }

        public static bool ShowConfigWindowStatic()
        {
            mInstance?.ShowConfigWindow();

            return true;
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
                    // Nothing to dispose
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
