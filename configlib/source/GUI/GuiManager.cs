using Vintagestory.API.Client;
using VSImGui;
using VSImGui.API;

namespace ConfigLib;

internal class GuiManager : IDisposable
{
    public event Action? ConfigWindowClosed;
    public event Action? ConfigWindowOpened;

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
            CloseConfigWindow();
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
        if (!_showConfig) ConfigWindowOpened?.Invoke();
        _showConfig = true;
        _modSystem.Show();
    }
    public void CloseConfigWindow()
    {
        if (_showConfig) ConfigWindowClosed?.Invoke();
        _showConfig = false;
    }

    private bool ShowConfigWindow(KeyCombination keyCombination)
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
