using ImGuiNET;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Newtonsoft.Json.Linq;
using VSImGui;
using System;
using HarmonyLib;

namespace ConfigLib
{
    internal class ConfigWindow
    {
        private readonly ICoreClientAPI mApi;
        private readonly IEnumerable<string> mDomains;
        private readonly Dictionary<string, Action<string>> mCustom;
        private readonly HashSet<string> mMods = new();
        private int mCurrentIndex = 0;
        private long mNextId = 0;
        private Style mStyle;
        private bool mStyleLoaded = false;

        public ConfigWindow(ICoreClientAPI api)
        {
            mApi = api;
            mDomains = ConfigLibModSystem.GetDomains();
            mCustom = ConfigLibModSystem.GetCustomConfigs() ?? new();

            foreach (string mod in mDomains)
            {
                mMods.Add(mod);
            }
            foreach (string mod in mCustom.Keys)
            {
                if (!mMods.Contains(mod)) mMods.Add(mod);
            }

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
                    if (ImGui.MenuItem("Save")) SaveSettings();
                    ImGui.BeginDisabled();
                    ImGui.MenuItem("Restore (WIP)");
                    ImGui.MenuItem("Reload (WIP)");
                    ImGui.MenuItem("Defaults (WIP)");
                    ImGui.EndDisabled();
                    ImGui.EndMenuBar();
                }
                DrawConfigList();

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

        private void DrawConfigList()
        {
            string filter = "";
            ImGui.InputTextWithHint("Mods configs##configlib", "filter (supports wildcards)", ref filter, 100);
            FilterMods(StyleEditor.WildCardToRegular(filter), out string[] domains, out string[] names);
            ImGui.ListBox($"##modslist.configlib", ref mCurrentIndex, names, domains.Length, 5);
            ImGui.NewLine();
            ImGui.BeginChild("##configlibdomainconfig", new(0, 0), true);
            if (domains.Length > mCurrentIndex) DrawDomainTab(domains[mCurrentIndex]);
            ImGui.EndChild();
        }

        private void SaveSettings()
        {
            foreach (string domain in mDomains)
            {
                Config? config = ConfigLibModSystem.GetConfigStatic(domain);
                config?.WriteToFile();
            }
        }

        private string Title(string name) => $"{name}##configlib:{mNextId++}";

        private void FilterMods(string filter, out string[] domains, out string[] names)
        {
            domains = mMods.ToArray();
            names = GetNames();

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

        private string[] GetNames()
        {
            List<string> names = new();
            foreach (string domain in mDomains)
            {
                names.Add(mApi.ModLoader.GetMod(domain).Info.Name);
            }
            return names.ToArray();
        }

        private void DrawDomainTab(string domain)
        {
            if (mDomains.Contains(domain))
            {
                Config? config = ConfigLibModSystem.GetConfigStatic(domain);
                if (config != null)
                {
                    ImGui.Text("To apply changes press 'Save' and re-enter the world");
                    DrawModConfig(config);
                }
                else
                {
                    ImGui.Text("\nConfig is unavailable\n");
                }
            }
            
            if (mCustom.ContainsKey(domain))
            {
                mCustom[domain]?.Invoke($"configlib:{mNextId++}");
            }
        }

        private void DrawModConfig(Config config)
        {
            ImGui.PushItemWidth(250);
            foreach ((string name, ConfigSetting setting) in config.Settings)
            {
                if (setting.Validation != null)
                {
                    DrawValidatedSetting(setting.YamlCode, setting);
                }
                else
                {
                    switch (setting.JsonType)
                    {
                        case JTokenType.Integer:
                            DrawIntegerSetting(setting.YamlCode, setting);
                            break;
                        case JTokenType.Float:
                            DrawFloatSetting(setting.YamlCode, setting);
                            break;
                        case JTokenType.String:
                            DrawStringSetting(setting.YamlCode, setting);
                            break;
                        case JTokenType.Boolean:
                            DrawBooleanSetting(setting.YamlCode, setting);
                            break;
                        default:
                            ImGui.Text($"{setting.YamlCode}: unavailable");
                            break;
                    }
                }

                DrawHint(setting);
            }
            ImGui.PopItemWidth();
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
            switch (setting.JsonType)
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

            if (min != null && max != null)
            {
                ImGui.SliderInt(Title(name), ref value, min.Value, max.Value);
            }
            else
            {
                ImGui.DragInt(Title(name), ref value, 1, min ?? int.MinValue, max ?? int.MaxValue);
            }

            setting.Value = new JsonObject(new JValue(value));
        }

        private void DrawFloatMinMaxSetting(string name, ConfigSetting setting)
        {
            if (setting?.Validation == null) return;

            float value = setting.Value.AsFloat();
            float? min = setting.Validation.Minimum?.AsFloat();
            float? max = setting.Validation.Maximum?.AsFloat();

            if (min != null && max != null)
            {
                ImGui.SliderFloat(Title(name), ref value, min.Value, max.Value);
            }
            else
            {
                ImGui.DragFloat(Title(name), ref value, 1, min ?? float.MinValue, max ?? float.MaxValue);
            }

            setting.Value = new JsonObject(new JValue(value));
        }

        private void DrawIntegerSetting(string name, ConfigSetting setting)
        {
            int value = setting.Value.AsInt();
            ImGui.DragInt(Title(name), ref value);
            setting.Value = new JsonObject(new JValue(value));
        }

        private void DrawFloatSetting(string name, ConfigSetting setting)
        {
            float value = setting.Value.AsFloat();
            ImGui.DragFloat(Title(name), ref value);
            setting.Value = new JsonObject(new JValue(value));
        }

        private void DrawBooleanSetting(string name, ConfigSetting setting)
        {
            bool value = setting.Value.AsBool();
            ImGui.Checkbox(Title(name), ref value);
            setting.Value = new JsonObject(new JValue(value));
        }

        private void DrawStringSetting(string name, ConfigSetting setting)
        {
            string value = setting.Value.AsString();
            ImGui.InputText(Title(name), ref value, 500, ImGuiInputTextFlags.EnterReturnsTrue);
            setting.Value = new JsonObject(new JValue(value));
        }

        private int DrawComboBox(string name, string value, string[] values)
        {
            int index;
            for (index = 0; index < values.Length; index++) if (values[index] == value) break;
            ImGui.Combo(name, ref index, values, values.Length);
            return index;
        }
    }
}
