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

        private bool mDisposed;
        private bool mShowConfig = false;
        private bool mShowDev = false;

        public GuiManager(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("configlibconfigs", "(Config lib) Open configs window", GlKeys.P, HotkeyType.DevTool, false, false, false);
            api.Input.SetHotKeyHandler("configlibconfigs", ShowConfigWindow);

            //api.Input.RegisterHotKey("configlibdev", "(Config lib) Open developer configs window", GlKeys.P, HotkeyType.DevTool, false, false, true);
            //api.Input.SetHotKeyHandler("configlibdev", ShowDevConfigWindow);

            dialog = new VanillaGuiDialog(api);

            mConfigWindow = new(api);
            mDevWindow = new(api);
        }

        public void Draw()
        {
            if (mShowDev) mDevWindow.Draw();
            if (mShowConfig && !mConfigWindow.Draw())
            {
                dialog.TryClose();
                mShowConfig = false;
            }
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
