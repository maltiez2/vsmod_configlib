using ImGuiNET;
using ModdingTools;
using System;
using Vintagestory.API.Client;

namespace ConfigLib
{
    internal class GuiManager : IDisposable
    {
        private readonly GuiDialog dialog;
        private readonly ConfigWindow mConfigWindow;
        private readonly DevWindow mDevWindow;
        private readonly ModWindow mModWindow;

        private bool mDisposed;
        private bool mShowConfig = false;
        private bool mShowDev = false;
        private bool mShowMod = false;

        public GuiManager(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("configlibconfigs", "(Config lib) Open configs window", GlKeys.P, HotkeyType.DevTool, false, false, false);
            api.Input.SetHotKeyHandler("configlibconfigs", ShowConfigWindow);

            api.Input.RegisterHotKey("configlibdev", "(Config lib) Open developer configs window", GlKeys.P, HotkeyType.DevTool, false, false, true);
            api.Input.SetHotKeyHandler("configlibdev", ShowDevConfigWindow);

            api.Input.RegisterHotKey("configlibmod", "(Config lib) Open mod window", GlKeys.P, HotkeyType.DevTool, true, false, false);
            api.Input.SetHotKeyHandler("configlibmod", ShowModConfigWindow);

            dialog = new VanillaGuiDialog(api);

            mConfigWindow = new(api);
            mDevWindow = new(api);
            mModWindow = new(api);
        }

        public void Draw()
        {
            if (mShowDev) mDevWindow.Draw();
            if (mShowConfig && !mConfigWindow.Draw())
            {
                dialog.TryClose();
                mShowConfig = false;
            }
            if (mShowMod) mModWindow.Draw();
        }

        private bool ShowDevConfigWindow(KeyCombination keyCombination)
        {
            if (dialog?.IsOpened() == true)
            {
                dialog.TryClose();
                mShowDev = false;
            }
            else
            {
                dialog?.TryOpen();
                mShowDev = true;
            }

            return true;
        }
        private bool ShowConfigWindow(KeyCombination keyCombination)
        {
            if (dialog?.IsOpened() == true)
            {
                dialog.TryClose();
                mShowConfig = false;
            }
            else
            {
                dialog?.TryOpen();
                mShowConfig = true;
            }

            return true;
        }
        private bool ShowModConfigWindow(KeyCombination keyCombination)
        {
            if (dialog?.IsOpened() == true)
            {
                dialog.TryClose();
                mShowMod = false;
            }
            else
            {
                dialog?.TryOpen();
                mShowMod = true;
            }

            return true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!mDisposed)
            {
                if (disposing)
                {
                    dialog.Dispose();
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
