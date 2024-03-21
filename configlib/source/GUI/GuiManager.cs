using Vintagestory.API.Client;
using VSImGui;
using VSImGui.API;

namespace ConfigLib;

internal class GuiManager : IDisposable
{
    private readonly ImGuiModSystem _modSystem;
    private readonly ConfigWindow _configWindow;
    private bool _disposed;
    private bool _showConfig = false;
    
    private static GuiManager? _instance;

    public GuiManager(ICoreClientAPI api)
    {
        api.Input.RegisterHotKey("configlibconfigs", "(Config lib) Open configs window", GlKeys.P, HotkeyType.DevTool, false, false, false);
        api.Input.SetHotKeyHandler("configlibconfigs", ShowConfigWindow);
        _modSystem = api.ModLoader.GetModSystem<ImGuiModSystem>();
        _modSystem.Draw += Draw;
        _modSystem.Closed += CloseConfigWindow;
        _configWindow = new(api);
        _instance = this;
    }

    public CallbackGUIStatus Draw(float deltaSeconds)
    {
        if (!_showConfig) return CallbackGUIStatus.Closed;

        if (!_configWindow.Draw())
        {
            _showConfig = false;
            return CallbackGUIStatus.Closed;
        }

        return CallbackGUIStatus.GrabMouse;
    }

    public bool ToggleConfigWindow()
    {
        if (!_showConfig)
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
        _instance?.ToggleConfigWindow();

        return true;
    }

    public void OpenConfigWindow()
    {
        _showConfig = true;
        _modSystem.Show();
    }
    public void CloseConfigWindow()
    {
        _showConfig = false;
    }

    private bool ShowConfigWindow(KeyCombination keyCombination)
    {
        _showConfig = !_showConfig;

        return true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _modSystem.Draw -= Draw;
                _modSystem.Closed -= CloseConfigWindow;
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
