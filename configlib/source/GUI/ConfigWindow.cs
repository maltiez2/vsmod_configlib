using ConfigLib.Formatting;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using VSImGui;

namespace ConfigLib;

public struct ControlButtons
{
    public bool Save { get; set; } = false;
    public bool Restore { get; set; } = false;
    public bool Reload { get; set; } = false;
    public bool Defaults { get; set; } = false;

    public ControlButtons() { }

    public ControlButtons(bool defaultValue)
    {
        Save = defaultValue;
        Restore = defaultValue;
        Reload = defaultValue;
        Defaults = defaultValue;
    }

    public void Reset()
    {
        Save = false;
        Restore = false;
        Defaults = false;
        Reload = false;
    }
}

internal class ConfigWindow
{
    public ConfigWindow(ICoreClientAPI api)
    {
        _api = api;
        _configsSystem = api.ModLoader.GetModSystem<ConfigLibModSystem>(true);
        _domains = _configsSystem.GetDomains();
        _custom = _configsSystem.GetCustomConfigs() ?? new();

        foreach (string mod in _domains)
        {
            _mods.Add(mod);
        }
        foreach (string mod in _custom.Keys)
        {
            if (!_mods.Contains(mod)) _mods.Add(mod);
        }

        _style = new Style
        {
            ColorBackgroundMenuBar = (0, 0, 0, 0),
            BorderFrame = 0
        };
        _redButton = new Style()
        {
            ColorButton = new(0.6f, 0.4f, 0.4f, 1.0f),
            ColorButtonHovered = new(1.0f, 0.5f, 0.5f, 1.0f),
        };
        _greenButton = new Style()
        {
            ColorButton = new(0.4f, 0.6f, 0.4f, 1.0f),
            ColorButtonHovered = new(0.6f, 1.0f, 0.5f, 1.0f),
        };
    }
    public bool Draw()
    {
        _nextId = 0;
        bool opened = true;
        _controlButtons.Reset();

        UpdateCustomMods();

        using (new StyleApplier(_style))
        {
            ImGui.SetNextWindowSizeConstraints(new(600, 600), new(1000, 2000));
            ImGuiWindowFlags flags = ImGuiWindowFlags.MenuBar;
            if (_unsavedChanges) flags |= ImGuiWindowFlags.UnsavedDocument;

            ImGui.Begin("Configs##configlib", ref opened, flags);

            if (ImGui.GetIO().KeysDown[(int)ImGuiKey.Escape] && ImGui.IsWindowFocused())
            {
                ImGui.End();
                SaveAll();
                return false;
            }

            DrawMenuBar();
            DrawConfigList();

            ImGui.End();
        }

        if (!opened) SaveAll();

        return opened;
    }


    private readonly ICoreClientAPI _api;
    private readonly IEnumerable<string> _domains;
    private readonly Dictionary<string, System.Func<string, ControlButtons, ControlButtons>> _custom;
    private readonly HashSet<string> _mods = new();
    private readonly HashSet<int> _unsavedDomains = new();
    private readonly ConfigLibModSystem _configsSystem;
    private readonly Style _redButton;
    private readonly Style _greenButton;
    private readonly Style _style;
    private readonly HashSet<float> _disabledHeaders = new();

    private int _currentIndex = 0;
    private long _nextId = 0;
    private ControlButtons _controlButtons = new();
    private ControlButtons _visibleControlButtons = new(true);
    private bool _unsavedChanges = false;
    private bool _customConfig = false;
    private string _filter = "";
    private string _settingsFilter = "";

    private void DrawMenuBar()
    {
        if (!ImGui.BeginMenuBar()) return;

        if (!_unsavedChanges) ImGui.BeginDisabled();
        if (ImGui.MenuItem("Save All"))
        {
            _controlButtons.Save = true;
            SaveAll();
        }
        if (!_unsavedChanges) ImGui.EndDisabled();
        DrawItemHint("Saves all configs to files.");
        if (ImGui.MenuItem("Restore All"))
        {
            _controlButtons.Restore = true;
            RestoreAll();
        }
        DrawItemHint($"Retrieves values from config files.");
        ImGui.EndMenuBar();
    }

    private void SaveAll()
    {
        foreach (string domain in _domains)
        {
            Config? config = _configsSystem.GetConfigImpl(domain);
            if (_api.IsSinglePlayer) config?.WriteToFile();
        }
        _unsavedDomains.Clear();
        _unsavedChanges = false;
    }
    private void RestoreAll()
    {
        if (!_api.IsSinglePlayer) return;

        foreach (string domain in _domains)
        {
            Config? config = _configsSystem.GetConfigImpl(domain);
            config?.ReadFromFile();
        }

        SetUnsavedChanges();
    }
    private void UpdateCustomMods()
    {
        foreach (string mod in _custom.Keys)
        {
            if (!_mods.Contains(mod)) _mods.Add(mod);
        }
    }

    private void DrawConfigList()
    {
        ImGui.InputTextWithHint("Mods configs##configlib", "filter (supports wildcards)", ref _filter, 100);
        FilterMods(StyleEditor.WildCardToRegular(_filter), out string[] domains, out string[] names);
        ImGui.ListBox($"##modslist.configlib", ref _currentIndex, names, domains.Length, 5);
        ImGui.NewLine();
        float height = ImGui.GetWindowHeight() - ImGui.GetCursorPosY() - 20;
        ImGui.BeginChild("##configlibdomainconfig", new(0, height), true, ImGuiWindowFlags.MenuBar);
        if (domains.Length > _currentIndex) DrawDomainTab(domains[_currentIndex]);
        ImGui.EndChild();
    }

    private void SaveSettings(string domain)
    {
        if (!_domains.Contains(domain)) return;

        Config? config = _configsSystem.GetConfigImpl(domain);
        config?.WriteToFile();

        if (_unsavedDomains.Contains(_currentIndex)) _unsavedDomains.Remove(_currentIndex);
        if (!_unsavedDomains.Any()) _unsavedChanges = false;
    }
    private void RestoreSettings(string domain)
    {
        if (!_domains.Contains(domain)) return;

        Config? config = _configsSystem.GetConfigImpl(domain);
        config?.ReadFromFile();

        SetUnsavedChanges();
    }
    private void DefaultSettings(string domain)
    {
        if (!_domains.Contains(domain)) return;

        Config? config = _configsSystem.GetConfigImpl(domain);
        config?.RestoreToDefaults();

        SetUnsavedChanges();
    }

    private string Title(string name) => $"{name}##configlib:{_nextId++}";

    private void FilterMods(string filter, out string[] domains, out string[] names)
    {
        domains = _mods.ToArray();
        names = _mods.Select(GetModName).ToArray();


        if (filter == "") return;

        List<string> newDomains = new();
        List<string> newNames = new();

        for (int index = 0; index < domains.Length; index++)
        {
            if (StyleEditor.Match(filter, names[index]))
            {
                newDomains.Add(domains[index]);
                newNames.Add(names[index]);
            }
        }

        domains = newDomains.ToArray();
        names = newNames.ToArray();
    }

    private string GetModName(string domain)
    {
        string? modName = _configsSystem.GetConfigImpl(domain)?.ModName;
        if (modName == null)
        {
            try
            {
                modName = _api.ModLoader.GetMod(domain)?.Info?.Name ?? Lang.Get(domain);
            }
            catch
            {
                // Dont care
            }
        }
        return modName ?? domain;
    }

    private bool _confirmDefaults = false;
    private void DrawDomainTab(string domain) // @TODO refactor this nasty nested ifs
    {
        if (ImGui.BeginMenuBar())
        {
            if (_confirmDefaults)
            {
                ImGui.Text("Defaults:");
                ImGui.SameLine();

                using (new StyleApplier(_greenButton))
                {
                    if (ImGui.Button("Confirm"))
                    {
                        _controlButtons.Defaults = true;
                        _confirmDefaults = false;
                        if (_api.IsSinglePlayer) DefaultSettings(domain);
                    }
                }

                using (new StyleApplier(_redButton))
                {
                    if (ImGui.Button("Cancel"))
                    {
                        _confirmDefaults = false;
                    }
                }
            }
            else
            {
                if (!_customConfig && !_unsavedDomains.Contains(_currentIndex)) ImGui.BeginDisabled();
                if (_visibleControlButtons.Save && ImGui.MenuItem("Save"))
                {
                    _controlButtons.Save = true;
                    SaveSettings(domain);
                    _confirmDefaults = false;
                }
                if (!_customConfig && !_unsavedDomains.Contains(_currentIndex)) ImGui.EndDisabled();
                DrawItemHint($"Saves changes to config file.");


                if (_visibleControlButtons.Restore && ImGui.MenuItem("Restore"))
                {
                    _controlButtons.Restore = true;
                    RestoreSettings(domain);
                    _confirmDefaults = false;
                }
                DrawItemHint($"Retrieves values from config file.");


                if (!_custom.ContainsKey(domain)) ImGui.BeginDisabled();
                if (_visibleControlButtons.Reload && ImGui.MenuItem("Reload"))
                {
                    _controlButtons.Reload = true;
                    _confirmDefaults = false;
                }
                DrawItemHint($"Applies settings changes (if the mod supports this feature)");


                if (!_custom.ContainsKey(domain)) ImGui.EndDisabled();
                if (_visibleControlButtons.Defaults && ImGui.MenuItem("Defaults"))
                {
                    _confirmDefaults = true;
                }
                DrawItemHint($"Sets settings to default values");
            }

            ImGui.EndMenuBar();
        }

        if (_domains.Contains(domain))
        {
            Config? config = _configsSystem.GetConfigImpl(domain);
            if (config != null)
            {
                _visibleControlButtons = new(true);
                ImGui.Text("To apply changes press 'Save' and re-enter the world.");
                if (!_api.IsSinglePlayer && !_api.World.Player.HasPrivilege(Privilege.controlserver)) ImGui.Text("Only client side settings are available for edit.");
                ImGui.Separator();
                DrawModConfig(config);
            }
            else
            {
                ImGui.Text("\nConfig is unavailable\n");
            }
        }

        _customConfig = false;
        if (_custom.ContainsKey(domain))
        {
            ImGui.Separator();
            _visibleControlButtons = _custom[domain]?.Invoke($"configlib:{_nextId++}", _controlButtons) ?? new(true);
            _customConfig = true;
        }
    }

    private void DrawModConfig(Config config)
    {
        ImGui.InputTextWithHint("Search##settings", "filter (supports wildcards)", ref _settingsFilter, 100);
        ImGui.Separator();
        string filter = _settingsFilter == "" ? ".*" : StyleEditor.WildCardToRegular(_settingsFilter);

        _disabledHeaders.Clear();
        float currentHeader = 0;
        bool hasSettings = false;
        foreach ((float weight, IConfigBlock? block) in config.ConfigBlocks)
        {
            if (
                block is ConfigSetting setting && !setting.Hide && (
                    StyleEditor.Match(filter, setting.YamlCode) ||
                    StyleEditor.Match(filter, setting.InGui ?? setting.YamlCode)
                )
            )
            {
                hasSettings = true;
            }

            if (block is IFormattingBlock formatting && (formatting.Collapsible || formatting.StopCollapsible))
            {
                if (!hasSettings && currentHeader != 0)
                {
                    _disabledHeaders.Add(currentHeader);
                }
                currentHeader = weight;
                hasSettings = false;
            }
        }
        if (!hasSettings && currentHeader != 0)
        {
            _disabledHeaders.Add(currentHeader);
        }


        bool collapsed = false;
        foreach ((float weight, IConfigBlock? block) in config.ConfigBlocks)
        {
            if (
                block is ConfigSetting setting && !collapsed && !setting.Hide && (
                    StyleEditor.Match(filter, setting.YamlCode) ||
                    StyleEditor.Match(filter, setting.InGui ?? setting.YamlCode)
                )
            )
            {
                DrawSetting(setting, config.Domain);
            }

            if (block is IFormattingBlock formatting)
            {
                if (formatting.Collapsible)
                {
                    if (_disabledHeaders.Contains(weight)) ImGui.BeginDisabled();
                    collapsed = !formatting.Draw(weight.ToString());
                    if (_disabledHeaders.Contains(weight)) ImGui.EndDisabled();
                }
                else if (formatting.StopCollapsible)
                {
                    collapsed = false;
                    formatting.Draw(weight.ToString());
                }
                else
                {
                    if (!collapsed) formatting.Draw(weight.ToString());
                }
            }
        }
    }

    private void DrawSetting(ConfigSetting setting, string domain)
    {
        string name = setting.InGui ?? setting.YamlCode;

        if (!_api.IsSinglePlayer && !setting.ClientSide && !_api.World.Player.HasPrivilege(Privilege.controlserver))
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button($"~##{name}"))
        {

            if (setting.Validation != null && setting.Validation.Mapping != null)
            {
                setting.MappingKey = setting.DefaultValue.AsString();
                setting.Value = setting.Validation.Mapping[setting.MappingKey].Clone();
            }
            else
            {
                setting.Value = setting.DefaultValue.Clone();
            }

            SetUnsavedChanges();
        }
        string hint = "";
        if (setting.ClientSide) hint += "(client side) ";
        hint += $"Reset to default value: {setting.DefaultValue}";
        DrawItemHint(hint);
        ImGui.SameLine();

        if (setting.Link != "")
        {
            DrawLink(setting, setting.YamlCode);
            ImGui.SameLine();
        }

        ImGui.PushItemWidth(300);

        if (setting.Validation != null)
        {
            DrawValidatedSetting(name, setting, domain);
        }
        else
        {
            switch (setting.SettingType)
            {
                case ConfigSettingType.Boolean:
                    DrawBooleanSetting(name, setting);
                    break;
                case ConfigSettingType.Integer:
                    DrawIntegerSetting(name, setting);
                    break;
                case ConfigSettingType.Float:
                    DrawFloatSetting(name, setting);
                    break;
                case ConfigSettingType.String:
                    DrawStringSetting(name, setting);
                    break;
                case ConfigSettingType.Color:
                    DrawColorSetting(name, setting);
                    break;
                default:
                    ImGui.TextDisabled($"{setting.YamlCode}: unavailable");
                    break;
            }
        }

        DrawHint(setting);


        ImGui.PopItemWidth();

        if (!_api.IsSinglePlayer && !setting.ClientSide && !_api.World.Player.HasPrivilege(Privilege.controlserver)) ImGui.EndDisabled();
    }

    private static void DrawHint(ConfigSetting setting)
    {
        if (setting.Comment == null) return;

        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.BeginItemTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(setting.Comment);
            ImGui.PopTextWrapPos();

            ImGui.EndTooltip();
        }
    }

    private void DrawLink(ConfigSetting setting, string name)
    {
        ImGui.SameLine();
        if (ImGui.Button($"link##{name}"))
        {
            _api.Gui.OpenLink(setting.Link);
        }
        DrawItemHint($"open in browser: {setting.Link}");
    }

    private void DrawValidatedSetting(string name, ConfigSetting setting, string domain)
    {
        bool mapping = setting.Validation?.Mapping != null;
        bool values = setting.Validation?.Values != null;
        bool minmax = setting.Validation?.Minimum != null || setting.Validation?.Maximum != null;

        if (mapping)
        {
            DrawMappingSetting(name, setting, domain);
        }
        else if (values)
        {
            DrawValuesSetting(name, setting);
        }
        else if (minmax)
        {
            DrawMinMaxSetting(name, setting);
        }
        else
        {
            ImGui.Text($"{name}: unavailable");
        }
    }

    private void DrawMappingSetting(string name, ConfigSetting setting, string domain)
    {
        if (setting.Validation?.Mapping == null || setting.MappingKey == null) return;
        string[] values = setting.Validation.Mapping.Keys.ToArray();
        string[] translatedValues = values.Select(key => Lang.GetIfExists($"{domain}:mappingkey-{key}") ?? key).ToArray();
        setting.MappingKey = values[DrawComboBox(Title(name), Lang.GetIfExists($"{domain}:mappingkey-{setting.MappingKey}") ?? setting.MappingKey, translatedValues, setting)];
    }

    private void DrawValuesSetting(string name, ConfigSetting setting)
    {
        if (setting.Validation?.Values == null || setting.Validation.Values.Count == 0) return;
        string[] values = setting.Validation.Values.Select((value) => value.Token.ToString()).ToArray();
        string value = setting.Value.ToString();
        int index = DrawComboBox(Title(name), value, values, setting);
        if (index < setting.Validation.Values.Count)
        {
            setting.Value = setting.Validation.Values[index];
        }
    }

    private void DrawMinMaxSetting(string name, ConfigSetting setting)
    {
        switch (setting.Value.Token.Type)
        {
            case JTokenType.Integer:
                DrawIntegerMinMaxSetting(name, setting);
                break;
            case JTokenType.Float:
                DrawFloatMinMaxSetting(name, setting);
                break;
            default:
                ImGui.Text($"{name}: unavailable");
                break;
        }
    }

    private void DrawIntegerMinMaxSetting(string name, ConfigSetting setting)
    {
        if (setting?.Validation == null) return;

        int value = setting.Value.AsInt();
        int? min = setting.Validation.Minimum?.AsInt();
        int? max = setting.Validation.Maximum?.AsInt();
        int? step = setting.Validation.Step?.AsInt();

        int previous = value;
        if (min != null && max != null)
        {
            ImGuiSliderFlags flags = ImGuiSliderFlags.None;
            if (setting.Logarithmic) flags |= ImGuiSliderFlags.Logarithmic;

            ImGui.PushItemWidth(70);
            ImGui.DragInt(Title($"##{name}edit"), ref value, step ?? 1, min.Value, max.Value);
            ImGui.SameLine();
            ImGui.PopItemWidth();

            if (value < min.Value) value = min.Value;
            if (value > max.Value) value = max.Value;

            ImGui.PushItemWidth(223);
            ImGui.SliderInt(Title(name), ref value, min.Value, max.Value, "", flags);
            ImGui.PopItemWidth();
            StepInt(ref value, min, max, step);
        }
        else
        {
            ImGui.DragInt(Title(name), ref value, 1, min ?? int.MinValue, max ?? int.MaxValue);
            if (min != null && value < min.Value) value = min.Value;
            if (max != null && value > max.Value) value = max.Value;
            StepInt(ref value, min, max, step);
        }
        if (previous != value)
        {
            SetUnsavedChanges();
            setting.Changed();
        }

        setting.Value = new JsonObject(new JValue(value));
    }

    private static void StepInt(ref int value, int? min, int? max, int? step)
    {
        if (step == null || step == 0) return;
        int stepValue = step.Value;
        int stepPoint = min ?? max ?? 0;

        value = (value - stepPoint) / stepValue * stepValue + stepPoint;
    }

    private void DrawFloatMinMaxSetting(string name, ConfigSetting setting)
    {
        if (setting?.Validation == null) return;

        float value = setting.Value.AsFloat();
        float? min = setting.Validation.Minimum?.AsFloat();
        float? max = setting.Validation.Maximum?.AsFloat();
        float? step = setting.Validation.Step?.AsFloat();

        if (step != null && step % 1 == 0 && (min != null && min % 1 == 0 || max != null && max % 1 == 0))
        {
            DrawIntegerSetting(name, setting);
            return;
        }

        float previous = value;
        if (min != null && max != null)
        {
            ImGuiSliderFlags flags = ImGuiSliderFlags.None;
            if (setting.Logarithmic) flags |= ImGuiSliderFlags.Logarithmic;

            ImGui.PushItemWidth(70);
            ImGui.DragFloat(Title($"##{name}edit"), ref value, 1, min.Value, max.Value);
            ImGui.SameLine();
            ImGui.PopItemWidth();

            if (value < min.Value) value = min.Value;
            if (value > max.Value) value = max.Value;

            ImGui.PushItemWidth(223);
            ImGui.SliderFloat(Title(name), ref value, min.Value, max.Value, "", flags);
            ImGui.PopItemWidth();
            StepFloat(ref value, min, max, step);
        }
        else
        {
            ImGui.DragFloat(Title(name), ref value, 1, min ?? float.MinValue, max ?? float.MaxValue);
            if (min != null && value < min.Value) value = min.Value;
            if (max != null && value > max.Value) value = max.Value;
            StepFloat(ref value, min, max, step);
        }
        if (previous != value)
        {
            SetUnsavedChanges();
            setting.Changed();
        }

        setting.Value = new JsonObject(new JValue(value));
    }

    private static void StepFloat(ref float value, float? min, float? max, float? step)
    {
        if (step == null || step == 0) return;
        float stepValue = step.Value;
        float stepPoint = min ?? max ?? 0;

        value = MathF.Round((value - stepPoint) / stepValue) * stepValue + stepPoint;
    }

    private void DrawIntegerSetting(string name, ConfigSetting setting)
    {
        int value = setting.Value.AsInt();
        int previous = value;
        ImGui.DragInt(Title(name), ref value);
        if (previous != value)
        {
            SetUnsavedChanges();
            setting.Changed();
        }
        setting.Value = new JsonObject(new JValue(value));
    }

    private void DrawFloatSetting(string name, ConfigSetting setting)
    {
        float value = setting.Value.AsFloat();
        float previous = value;
        ImGui.DragFloat(Title(name), ref value);
        if (previous != value)
        {
            SetUnsavedChanges();
            setting.Changed();
        }
        setting.Value = new JsonObject(new JValue(value));
    }

    private void DrawStringSetting(string name, ConfigSetting setting)
    {
        string value = setting.Value.AsString();
        string previous = value;
        ImGui.InputText(Title(name), ref value, 256);
        if (previous != value)
        {
            SetUnsavedChanges();
            setting.Changed();
        }
        setting.Value = new JsonObject(new JValue(value));
    }

    private void DrawColorSetting(string name, ConfigSetting setting)
    {
        string value = setting.Value.AsString();
        string previous = value;

        int red = int.Parse(value.Substring(1, 2), NumberStyles.HexNumber);
        int green = int.Parse(value.Substring(3, 2), NumberStyles.HexNumber);
        int blue = int.Parse(value.Substring(5, 2), NumberStyles.HexNumber);

        Vector3 color = new(red / 255f, green / 255f, blue / 255f);

        ImGui.ColorEdit3(Title(name), ref color, ImGuiColorEditFlags.DisplayHex);

        value = ColorUtil.Int2Hex(ColorUtil.ToRgba(255, (int)(color.X * 255), (int)(color.Y * 255), (int)(color.Z * 255)));

        if (previous != value)
        {
            SetUnsavedChanges();
            setting.Changed();
        }
        setting.Value = new JsonObject(new JValue(value));
    }

    private void DrawBooleanSetting(string name, ConfigSetting setting)
    {
        bool value = setting.Value.AsBool(false);
        bool previous = value;
        ImGui.Checkbox(Title(name), ref value);
        if (previous != value)
        {
            SetUnsavedChanges();
            setting.Changed();
        }
        setting.Value = new JsonObject(new JValue(value));
    }

    private int DrawComboBox(string name, string value, string[] values, ConfigSetting setting)
    {
        int index;
        for (index = 0; index < values.Length; index++) if (values[index] == value) break;
        int previous = index;
        ImGui.Combo(name, ref index, values, values.Length);
        if (previous != index)
        {
            SetUnsavedChanges();
            setting.Changed();
        }
        return index;
    }

    public static void DrawItemHint(string hint)
    {
        if (ImGui.BeginItemTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
            ImGui.TextUnformatted(hint);
            ImGui.PopTextWrapPos();

            ImGui.EndTooltip();
        }
    }

    private void SetUnsavedChanges()
    {
        if (!_unsavedDomains.Contains(_currentIndex)) _unsavedDomains.Add(_currentIndex);
        _unsavedChanges = true;
    }
}
