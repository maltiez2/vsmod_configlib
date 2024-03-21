using ConfigLib.Formatting;
using ImGuiNET;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using VSImGui;

namespace ConfigLib;

public struct ControlButtons
{
    public bool Save { get; set; } = false;
    public bool Restore { get; set; } = false;
    public bool Reload { get; set; } = false;
    public bool Defaults { get; set; } = false;

    public ControlButtons() { }

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
    private readonly ICoreClientAPI mApi;
    private readonly IEnumerable<string> mDomains;
    private readonly Dictionary<string, Action<string, ControlButtons>> mCustom;
    private readonly HashSet<string> mMods = new();
    private readonly HashSet<int> mUnsavedDomains = new();
    private readonly ConfigLibModSystem mConfigsSystem;
    private readonly Style mRedButton;
    private readonly Style mGreenButton;
    private readonly Style mStyle;

    private int mCurrentIndex = 0;
    private long mNextId = 0;
    private ControlButtons mControlButtons = new();
    private bool mUnsavedChanges = false;
    private bool mCustomConfig = false;
    private string mFilter = "";

    public ConfigWindow(ICoreClientAPI api)
    {
        mApi = api;
        mConfigsSystem = api.ModLoader.GetModSystem<ConfigLibModSystem>(true);
        mDomains = mConfigsSystem.GetDomains();
        mCustom = mConfigsSystem.GetCustomConfigs() ?? new();

        foreach (string mod in mDomains)
        {
            mMods.Add(mod);
        }
        foreach (string mod in mCustom.Keys)
        {
            if (!mMods.Contains(mod)) mMods.Add(mod);
        }

        mStyle = new Style
        {
            ColorBackgroundMenuBar = (0, 0, 0, 0),
            BorderFrame = 0
        };
        mRedButton = new Style()
        {
            ColorButton = new(0.6f, 0.4f, 0.4f, 1.0f),
            ColorButtonHovered = new(1.0f, 0.5f, 0.5f, 1.0f),
        };
        mGreenButton = new Style()
        {
            ColorButton = new(0.4f, 0.6f, 0.4f, 1.0f),
            ColorButtonHovered = new(0.6f, 1.0f, 0.5f, 1.0f),
        };
    }

    public bool Draw()
    {
        mNextId = 0;
        bool opened = true;
        mControlButtons.Reset();

        UpdateCustomMods();

        using (new StyleApplier(mStyle))
        {
            ImGui.SetNextWindowSizeConstraints(new(500, 600), new(1000, 2000));
            ImGuiWindowFlags flags = ImGuiWindowFlags.MenuBar;
            if (mUnsavedChanges) flags |= ImGuiWindowFlags.UnsavedDocument;
            ImGui.Begin("Configs##configlib", ref opened, flags);
            if (ImGui.BeginMenuBar())
            {
                if (!mUnsavedChanges) ImGui.BeginDisabled();
                if (ImGui.MenuItem("Save All"))
                {
                    mControlButtons.Save = true;
                    SaveAll();
                }
                if (!mUnsavedChanges) ImGui.EndDisabled();
                DrawItemHint($"Saves all configs to files.");
                if (ImGui.MenuItem("Restore All"))
                {
                    mControlButtons.Restore = true;
                    RestoreAll();
                }
                DrawItemHint($"Retrieves values from config files.");
                ImGui.EndMenuBar();
            }
            DrawConfigList();

            ImGui.End();
        }

        if (!opened) SaveAll();

        return opened;
    }

    public void SaveAll()
    {
        foreach (string domain in mDomains)
        {
            Config? config = mConfigsSystem.GetConfigImpl(domain);
            if (mApi.IsSinglePlayer) config?.WriteToFile();
        }
        mUnsavedDomains.Clear();
        mUnsavedChanges = false;
    }

    public void RestoreAll()
    {
        if (!mApi.IsSinglePlayer) return;

        foreach (string domain in mDomains)
        {
            Config? config = mConfigsSystem.GetConfigImpl(domain);
            config?.ReadFromFile();
        }

        SetUnsavedChanges();
    }

    private void UpdateCustomMods()
    {
        foreach (string mod in mCustom.Keys)
        {
            if (!mMods.Contains(mod)) mMods.Add(mod);
        }
    }

    private void DrawConfigList()
    {
        ImGui.InputTextWithHint("Mods configs##configlib", "filter (supports wildcards)", ref mFilter, 100);
        FilterMods(StyleEditor.WildCardToRegular(mFilter), out string[] domains, out string[] names);
        ImGui.ListBox($"##modslist.configlib", ref mCurrentIndex, names, domains.Length, 5);
        ImGui.NewLine();
        ImGui.BeginChild("##configlibdomainconfig", new(0, 0), true, ImGuiWindowFlags.MenuBar);
        if (domains.Length > mCurrentIndex) DrawDomainTab(domains[mCurrentIndex]);
        ImGui.EndChild();
    }

    private void SaveSettings(string domain)
    {
        if (!mDomains.Contains(domain)) return;

        Config? config = mConfigsSystem.GetConfigImpl(domain);
        config?.WriteToFile();

        if (mUnsavedDomains.Contains(mCurrentIndex)) mUnsavedDomains.Remove(mCurrentIndex);
        if (!mUnsavedDomains.Any()) mUnsavedChanges = false;
    }
    private void RestoreSettings(string domain)
    {
        if (!mDomains.Contains(domain)) return;

        Config? config = mConfigsSystem.GetConfigImpl(domain);
        config?.ReadFromFile();

        SetUnsavedChanges();
    }
    private void DefaultSettings(string domain)
    {
        if (!mDomains.Contains(domain)) return;

        Config? config = mConfigsSystem.GetConfigImpl(domain);
        config?.RestoreToDefaults();

        SetUnsavedChanges();
    }

    private string Title(string name) => $"{name}##configlib:{mNextId++}";

    private void FilterMods(string filter, out string[] domains, out string[] names)
    {
        domains = mMods.ToArray();
        names = mMods.Select((domain) => mApi.ModLoader.GetMod(domain).Info.Name).ToArray();

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

    private bool mConfirmDefaults = false;
    private void DrawDomainTab(string domain) // @TODO refactor this nasty nested ifs
    {
        if (ImGui.BeginMenuBar())
        {
            if (mConfirmDefaults)
            {
                ImGui.Text("Defaults:");
                ImGui.SameLine();

                using (new StyleApplier(mGreenButton))
                {
                    if (ImGui.Button("Confirm"))
                    {
                        mControlButtons.Defaults = true;
                        mConfirmDefaults = false;
                        if (mApi.IsSinglePlayer) DefaultSettings(domain);
                    }
                }

                using (new StyleApplier(mRedButton))
                {
                    if (ImGui.Button("Cancel"))
                    {
                        mConfirmDefaults = false;
                    }
                }
            }
            else
            {
                if (!mCustomConfig && !mUnsavedDomains.Contains(mCurrentIndex)) ImGui.BeginDisabled();
                if (ImGui.MenuItem("Save"))
                {
                    mControlButtons.Save = true;
                    SaveSettings(domain);
                    mConfirmDefaults = false;
                }
                if (!mCustomConfig && !mUnsavedDomains.Contains(mCurrentIndex)) ImGui.EndDisabled();
                DrawItemHint($"Saves changes to config file.");


                if (ImGui.MenuItem("Restore"))
                {
                    mControlButtons.Restore = true;
                    RestoreSettings(domain);
                    mConfirmDefaults = false;
                }
                DrawItemHint($"Retrieves values from config file.");


                if (!mCustom.ContainsKey(domain)) ImGui.BeginDisabled();
                if (ImGui.MenuItem("Reload"))
                {
                    mControlButtons.Reload = true;
                    mConfirmDefaults = false;
                }
                DrawItemHint($"Applies settings changes (if the mod supports this feature)");


                if (!mCustom.ContainsKey(domain)) ImGui.EndDisabled();
                if (ImGui.MenuItem("Defaults"))
                {
                    mConfirmDefaults = true;
                }
                DrawItemHint($"Sets settings to default values");
            }

            ImGui.EndMenuBar();
        }

        if (mDomains.Contains(domain))
        {
            Config? config = mConfigsSystem.GetConfigImpl(domain);
            if (config != null)
            {
                ImGui.TextDisabled("To apply changes press 'Save' and re-enter the world.");
                if (!mApi.IsSinglePlayer) ImGui.TextDisabled("Only client side settings are available for edit.");
                ImGui.Separator();
                DrawModConfig(config);
            }
            else
            {
                ImGui.Text("\nConfig is unavailable\n");
            }
        }

        mCustomConfig = false;
        if (mCustom.ContainsKey(domain))
        {
            ImGui.Separator();
            mCustom[domain]?.Invoke($"configlib:{mNextId++}", mControlButtons);
            mCustomConfig = true;
        }
    }

    private void DrawModConfig(Config config)
    {
        foreach ((float weight, IConfigBlock? block) in config.ConfigBlocks)
        {
            if (block is ConfigSetting setting) DrawSetting(setting);
            if (block is IFormattingBlock formatting) formatting.Draw(weight.ToString());
        }
    }

    private void DrawSetting(ConfigSetting setting)
    {
        if (!mApi.IsSinglePlayer && !setting.ClientSide)
        {
            ImGui.BeginDisabled();
        }

        ImGui.PushItemWidth(300);

        string name = setting.InGui ?? setting.YamlCode ?? "";

        if (Lang.HasTranslation(name)) name = Lang.Get(name);

        if (setting.Validation != null)
        {
            DrawValidatedSetting(name, setting);
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
                default:
                    ImGui.TextDisabled($"{setting.YamlCode}: unavailable");
                    break;
            }
        }

        DrawHint(setting);
        ImGui.PopItemWidth();

        if (!mApi.IsSinglePlayer && !setting.ClientSide) ImGui.EndDisabled();
    }

    private void DrawHint(ConfigSetting setting)
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

    private void DrawValidatedSetting(string name, ConfigSetting setting)
    {
        bool mapping = setting.Validation?.Mapping != null;
        bool values = setting.Validation?.Values != null;
        bool minmax = setting.Validation?.Minimum != null || setting.Validation?.Maximum != null;

        if (mapping)
        {
            DrawMappingSetting(name, setting);
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

    private void DrawMappingSetting(string name, ConfigSetting setting)
    {
        if (setting.Validation?.Mapping == null || setting.MappingKey == null) return;
        string[] values = setting.Validation.Mapping.Keys.ToArray();
        setting.MappingKey = values[DrawComboBox(Title(name), setting.MappingKey, values)];
    }

    private void DrawValuesSetting(string name, ConfigSetting setting)
    {
        if (setting.Validation?.Values == null) return;
        string[] values = setting.Validation.Values.Select((value) => value.Token.ToString()).ToArray();
        string value = setting.Value.ToString();
        int index = DrawComboBox(Title(name), value, values);
        setting.Value = setting.Validation.Values[index];
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

            ImGui.PushItemWidth(223);
            ImGui.SliderInt(Title(name), ref value, min.Value, max.Value, "", flags);
            ImGui.PopItemWidth();
            StepInt(ref value, min, max, step);
        }
        else
        {
            ImGui.DragInt(Title(name), ref value, 1, min ?? int.MinValue, max ?? int.MaxValue);
            StepInt(ref value, min, max, step);
        }
        if (previous != value) SetUnsavedChanges();

        setting.Value = new JsonObject(new JValue(value));
    }

    private void StepInt(ref int value, int? min, int? max, int? step)
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

            ImGui.PushItemWidth(223);
            ImGui.SliderFloat(Title(name), ref value, min.Value, max.Value, "", flags);
            ImGui.PopItemWidth();
            StepFloat(ref value, min, max, step);
        }
        else
        {
            ImGui.DragFloat(Title(name), ref value, 1, min ?? float.MinValue, max ?? float.MaxValue);
            StepFloat(ref value, min, max, step);
        }
        if (previous != value) SetUnsavedChanges();

        setting.Value = new JsonObject(new JValue(value));
    }

    private void StepFloat(ref float value, float? min, float? max, float? step)
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
        if (previous != value) SetUnsavedChanges();
        setting.Value = new JsonObject(new JValue(value));
    }

    private void DrawFloatSetting(string name, ConfigSetting setting)
    {
        float value = setting.Value.AsFloat();
        float previous = value;
        ImGui.DragFloat(Title(name), ref value);
        if (previous != value) SetUnsavedChanges();
        setting.Value = new JsonObject(new JValue(value));
    }

    private void DrawStringSetting(string name, ConfigSetting setting)
    {
        string value = setting.Value.AsString();
        string previous = value;
        ImGui.InputText(Title(name), ref value, 256);
        if (previous != value) SetUnsavedChanges();
        setting.Value = new JsonObject(new JValue(value));
    }

    private void DrawBooleanSetting(string name, ConfigSetting setting)
    {
        bool value = setting.Value.AsBool();
        bool previous = value;
        ImGui.Checkbox(Title(name), ref value);
        if (previous != value) SetUnsavedChanges();
        setting.Value = new JsonObject(new JValue(value));
    }

    private int DrawComboBox(string name, string value, string[] values)
    {
        int index;
        for (index = 0; index < values.Length; index++) if (values[index] == value) break;
        int previous = index;
        ImGui.Combo(name, ref index, values, values.Length);
        if (previous != index) SetUnsavedChanges();
        return index;
    }

    public void DrawItemHint(string hint)
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
        if (!mUnsavedDomains.Contains(mCurrentIndex)) mUnsavedDomains.Add(mCurrentIndex);
        mUnsavedChanges = true;
    }
}
