using ImGuiNET;
using VSImGui;
using VSImGui.ImGuiUtils;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Newtonsoft.Json.Linq;

namespace ConfigLib
{
    internal class ConfigWindow
    {
        private readonly ICoreClientAPI mApi;
        private readonly IEnumerable<string> mDomains;
        private int mCurrentIndex = 0;
        private long mNextId = 0;

        private readonly string mPath = "gui/backgrounds/soil.png";
        private readonly Vector2 mTextureSize = new Vector2(256, 256);

        private readonly Style mStyle;

        public ConfigWindow(ICoreClientAPI api)
        {
            mApi = api;
            mDomains = ConfigLibModSystem.GetDomains();
            mStyle = new Style();
            mStyle.Font = ("Montserrat-Regular", 18);
            mStyle.PaddingWindow = (0, 4);
        }

        public bool Draw()
        {
            ImGui.ShowDemoWindow();

            StyleEditor.Draw(mStyle);

            mNextId = 0;
            bool opened = true;

            using (new StyleApplier(mStyle))
            {
                ImGui.Begin("Configs##configlib", ref opened, ImGuiWindowFlags.MenuBar);
                Utils.TileWindowWithTexture(mApi, new(mPath), mTextureSize, Utils.TextureRenderLevel.Window);

                ImGui.BeginMenuBar();
                if (ImGui.MenuItem("Save")) SaveSettings();
                ImGui.EndMenuBar();

                DrawConfigList();

                ImGui.End();
            }

            return opened;
        }

        private void DrawConfigList()
        {
            string filter = "";
            ImGui.InputTextWithHint("Mods configs##configlib", "filter (supports wildcards)", ref filter, 100);
            FilterMods(filter, out string[] domains, out string[] names);
            ImGui.ListBox($"##modslist.configlib", ref mCurrentIndex, names, domains.Length);
            ImGui.NewLine();
            if (domains.Length > mCurrentIndex) DrawDomainTab(domains[mCurrentIndex]);
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
            domains = mDomains.ToArray();
            names = GetNames();

            if (filter == "") return;

            List<string> newDomains = new();
            List<string> newNames = new();

            for (int index = 0; index < domains.Length; index++)
            {
                if (WildcardUtil.Match(filter, names[index]))
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
            Config? config = ConfigLibModSystem.GetConfigStatic(domain);
            if (config != null)
            {
                DrawModConfig(config);
            }
            else
            {
                ImGui.Text("\nConfig is unavailable\n");
            }
        }

        private void DrawModConfig(Config config)
        {
            ImGui.PushItemWidth(200);
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
