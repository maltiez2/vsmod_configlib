using ModdingTools;
using System;
using Vintagestory.API.Client;

namespace ConfigLib
{
    internal class GuiManager : IDisposable
    {
        private readonly GuiDialog dialog;
        private readonly ConfigWindow mConfigWindow;

        private bool mDisposed;
        private bool mShowConfig = false;
        private static GuiManager? mInstance;

        public GuiManager(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("configlibconfigs", "(Config lib) Open configs window", GlKeys.P, HotkeyType.DevTool, false, false, false);
            api.Input.SetHotKeyHandler("configlibconfigs", ShowConfigWindow);

            dialog = new VanillaGuiDialog(api);

            mConfigWindow = new(api);
            mInstance = this;
        }

        public void Draw()
        {
            if (mShowConfig && !mConfigWindow.Draw())
            {
                dialog.TryClose();
                mShowConfig = false;
            }
        }

        public bool ShowConfigWindow()
        {
            if (dialog?.IsOpened() == false)
            {
                dialog.TryOpen();
                mShowConfig = true;
            }

            return true;
        }

        public static bool ShowConfigWindowStatic()
        {
            mInstance?.ShowConfigWindow();

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
