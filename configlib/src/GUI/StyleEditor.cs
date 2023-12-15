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
using System.IO;
using System.Text.RegularExpressions;
using Vintagestory.API.MathTools;

namespace ConfigLib
{
    static public class StyleEditor
    {
        static private uint sIdCounter = 0;
        static public bool Draw(Style style)
        {
            sIdCounter = 0;
            bool oppend = true;
            ImGui.Begin("Style editor##vsimgui", ref oppend);
            if (ImGui.BeginTabBar("##tabs", ImGuiTabBarFlags.None))
            {
                if (ImGui.BeginTabItem("Font"))
                {
                    FontEditor(style);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Colors"))
                {
                    ColorsEditor(style);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Layout"))
                {
                    LayoutSettingsEditor(style);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Other"))
                {
                    OtherSettingsEditor(style);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }

            ImGui.End();
            return oppend;
        }

        static public void FontEditor(Style style)
        {
            string[] fonts = ImGuiController.Fonts.ToArray();
            string[] fontsNames = ImGuiController.Fonts.Select(font => Path.GetFileNameWithoutExtension(font)).ToArray();
            int[] sizes = ImGuiController.FontSizes.ToArray();
            string[] sizesNames = ImGuiController.FontSizes.Select(size => $"{size}px").ToArray();
            int currentSizeIndex, currentFontIndex;
            for (currentFontIndex = 0; currentFontIndex < fonts.Length; currentFontIndex++)
            {
                if (fontsNames[currentFontIndex] == style.FontName) break;
            }
            for (currentSizeIndex = 0; currentSizeIndex < fonts.Length; currentSizeIndex++)
            {
                if (sizes[currentSizeIndex] == style.FontSize) break;
            }

            ImGui.Combo("Font", ref currentFontIndex, fontsNames, fonts.Length);
            ImGui.Combo("Size", ref currentSizeIndex, sizesNames, sizes.Length);

            style.Font = (fontsNames[currentFontIndex], sizes[currentSizeIndex]);
        }
        static public void ColorsEditor(Style style)
        {
            string filter = "";
            ImGui.InputTextWithHint("Filter colors", "filter, supports wildcards", ref filter, 100);
            filter = WildCardToRegular(filter);
            ImGui.BeginChild("Filter colors", new Vector2(), true);
            BackgroundColorsEditor(filter, style);
            TextEditor(filter, style);
            BordersEditor(filter, style);
            ScrollEditor(filter, style);
            ButtonsEditor(filter, style);
            HeadersEditor(filter, style);
            SeparatorEditor(filter, style);
            ResizeEditor(filter, style);
            TabsEditor(filter, style);
            PlotsEditor(filter, style);
            OtherColorsEditor(filter, style);
            ImGui.EndChild();
        }

        static public void LayoutSettingsEditor(Style style)
        {

        }

        static public void OtherSettingsEditor(Style style)
        {

        }

        static private void BackgroundColorsEditor(string filter, Style style)
        {
            if (!Match(filter,
                "BackgroundWindow",
                "BackgroundChild",
                "BackgroundPopup",
                "BackgroundFrame",
                "BackgroundFrameHovered",
                "BackgroundFrameActive",
                "BackgroundTitle",
                "BackgroundTitleActive",
                "BackgroundTitleCollapsed",
                "BackgroundMenuBar",
                "BackgroundScrollbar",
                "BackgroundDockingEmpty",
                "BackgroundTableHeader",
                "BackgroundTableRow",
                "BackgroundTableRowAlt",
                "BackgroundTextSelected",
                "BackgroundNavWindowingDim",
                "BackgroundModalWindowDim"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Backgrounds");
            style.ColorBackgroundWindow = Color("BackgroundWindow", "Window", filter, style.ColorBackgroundWindow);
            style.ColorBackgroundChild = Color("BackgroundChild", "Child", filter, style.ColorBackgroundChild);
            style.ColorBackgroundPopup = Color("BackgroundPopup", "Popup", filter, style.ColorBackgroundPopup);
            style.ColorBackgroundFrame = Color("BackgroundFrame", "Frame", filter, style.ColorBackgroundFrame);
            style.ColorBackgroundFrameHovered = Color("BackgroundFrameHovered", "FrameHovered", filter, style.ColorBackgroundFrameHovered);
            style.ColorBackgroundFrameActive = Color("BackgroundFrameActive", "FrameActive", filter, style.ColorBackgroundFrameActive);
            style.ColorBackgroundTitle = Color("BackgroundTitle", "Title", filter, style.ColorBackgroundTitle);
            style.ColorBackgroundTitleActive = Color("BackgroundTitleActive", "TitleActive", filter, style.ColorBackgroundTitleActive);
            style.ColorBackgroundTitleCollapsed = Color("BackgroundTitleCollapsed", "TitleCollapsed", filter, style.ColorBackgroundTitleCollapsed);
            style.ColorBackgroundMenuBar = Color("BackgroundMenuBar", "MenuBar", filter, style.ColorBackgroundMenuBar);
            style.ColorBackgroundScrollbar = Color("BackgroundScrollbar", "Scrollbar", filter, style.ColorBackgroundScrollbar);
            style.ColorBackgroundDockingEmpty = Color("BackgroundDockingEmpty", "DockingEmpty", filter, style.ColorBackgroundDockingEmpty);
            style.ColorBackgroundTableHeader = Color("BackgroundTableHeader", "TableHeader", filter, style.ColorBackgroundTableHeader);
            style.ColorBackgroundTableRow = Color("BackgroundTableRow", "TableRow", filter, style.ColorBackgroundTableRow);
            style.ColorBackgroundTableRowAlt = Color("BackgroundTableRowAlt", "TableRowAlt", filter, style.ColorBackgroundTableRowAlt);
            style.ColorBackgroundTextSelected = Color("BackgroundTextSelected", "TextSelected", filter, style.ColorBackgroundTextSelected);
            style.ColorBackgroundNavWindowingDim = Color("BackgroundNavWindowingDim", "NavWindowingDim", filter, style.ColorBackgroundNavWindowingDim);
            style.ColorBackgroundModalWindowDim = Color("BackgroundModalWindowDim", "ModalWindowDim", filter, style.ColorBackgroundModalWindowDim);
        }
        static private void TextEditor(string filter, Style style)
        {
            if (!Match(filter,
                "Text",
                "TextDisabled"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Text");
            style.ColorText = Color("Text", "Text", filter, style.ColorText);
            style.ColorTextDisabled = Color("TextDisabled", "TextDisabled", filter, style.ColorTextDisabled);
            style.ColorBackgroundTextSelected = Color("BackgroundTextSelected", "TextSelected", filter, style.ColorBackgroundTextSelected);
        }
        static private void BordersEditor(string filter, Style style)
        {
            if (!Match(filter,
                "Border",
                "BorderShadow",
                "TableBorderStrong",
                "TableBorderLight"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Borders");
            style.ColorBorder = Color("Border", "Border", filter, style.ColorBorder);
            style.ColorBorderShadow = Color("BorderShadow", "BorderShadow", filter, style.ColorBorderShadow);
            style.ColorTableBorderStrong = Color("TableBorderStrong", "TableBorderStrong", filter, style.ColorTableBorderStrong);
            style.ColorTableBorderLight = Color("TableBorderLight", "TableBorderLight", filter, style.ColorTableBorderLight);
        }
        static private void ScrollEditor(string filter, Style style)
        {
            if (!Match(filter,
                "ScrollbarGrab",
                "ScrollbarGrabHovered",
                "ScrollbarGrabActive",
                "SliderGrab",
                "SliderGrabActive"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Scrollbars & sliders");
            style.ColorBackgroundScrollbar = Color("BackgroundScrollbar", "ScrollbarBackground", filter, style.ColorBackgroundScrollbar);
            style.ColorScrollbarGrab = Color("ScrollbarGrab", "ScrollbarGrab", filter, style.ColorScrollbarGrab);
            style.ColorScrollbarGrabHovered = Color("ScrollbarGrabHovered", "ScrollbarGrabHovered", filter, style.ColorScrollbarGrabHovered);
            style.ColorScrollbarGrabActive = Color("ScrollbarGrabActive", "ScrollbarGrabActive", filter, style.ColorScrollbarGrabActive);
            style.ColorSliderGrab = Color("SliderGrab", "SliderGrab", filter, style.ColorSliderGrab);
            style.ColorSliderGrabActive = Color("SliderGrabActive", "SliderGrabActive", filter, style.ColorSliderGrabActive);
        }
        static private void ButtonsEditor(string filter, Style style)
        {
            if (!Match(filter,
                "Button",
                "ButtonHovered",
                "ButtonActive"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Buttons");
            style.ColorButton = Color("Button", "Button", filter, style.ColorButton);
            style.ColorButtonHovered = Color("ButtonHovered", "ButtonHovered", filter, style.ColorButtonHovered);
            style.ColorButtonActive = Color("ButtonActive", "ButtonActive", filter, style.ColorButtonActive);
        }
        static private void HeadersEditor(string filter, Style style)
        {
            if (!Match(filter,
                "Header",
                "HeaderHovered",
                "HeaderActive"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Headers");
            style.ColorHeader = Color("Header", "Header", filter, style.ColorHeader);
            style.ColorHeaderHovered = Color("HeaderHovered", "HeaderHovered", filter, style.ColorHeaderHovered);
            style.ColorHeaderActive = Color("HeaderActive", "HeaderActive", filter, style.ColorHeaderActive);
        }
        static private void SeparatorEditor(string filter, Style style)
        {
            if (!Match(filter,
                "Separator",
                "SeparatorHovered",
                "SeparatorActive"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Separators");
            style.ColorSeparator = Color("Separator", "Separator", filter, style.ColorSeparator);
            style.ColorSeparatorHovered = Color("SeparatorHovered", "SeparatorHovered", filter, style.ColorSeparatorHovered);
            style.ColorSeparatorActive = Color("SeparatorActive", "SeparatorActive", filter, style.ColorSeparatorActive);
        }
        static private void ResizeEditor(string filter, Style style)
        {
            if (!Match(filter,
                "ResizeGrip",
                "ResizeGripHovered",
                "ResizeGripActive"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Resize");
            style.ColorResizeGrip = Color("ResizeGrip", "ResizeGrip", filter, style.ColorResizeGrip);
            style.ColorResizeGripHovered = Color("ResizeGripHovered", "ResizeGripHovered", filter, style.ColorResizeGripHovered);
            style.ColorResizeGripActive = Color("ResizeGripActive", "ResizeGripActive", filter, style.ColorResizeGripActive);
        }
        static private void TabsEditor(string filter, Style style)
        {
            if (!Match(filter,
                "Tab",
                "TabHovered",
                "TabActive",
                "TabUnfocused",
                "TabUnfocusedActive"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Tabs");
            style.ColorTab = Color("Tab", "Tab", filter, style.ColorTab);
            style.ColorTabHovered = Color("TabHovered", "TabHovered", filter, style.ColorTabHovered);
            style.ColorTabActive = Color("TabActive", "TabActive", filter, style.ColorTabActive);
            style.ColorTabUnfocused = Color("TabUnfocused", "TabUnfocused", filter, style.ColorTabUnfocused);
            style.ColorTabUnfocusedActive = Color("TabUnfocusedActive", "TabUnfocusedActive", filter, style.ColorTabUnfocusedActive);
        }
        static private void PlotsEditor(string filter, Style style)
        {
            if (!Match(filter,
                "PlotLines",
                "PlotLinesHovered",
                "PlotHistogram",
                "PlotHistogramHovered"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Plots");
            style.ColorPlotLines = Color("PlotLines", "PlotLines", filter, style.ColorPlotLines);
            style.ColorPlotLinesHovered = Color("PlotLinesHovered", "PlotLinesHovered", filter, style.ColorPlotLinesHovered);
            style.ColorPlotHistogram = Color("PlotHistogram", "PlotHistogram", filter, style.ColorPlotHistogram);
            style.ColorPlotHistogramHovered = Color("PlotHistogramHovered", "PlotHistogramHovered", filter, style.ColorPlotHistogramHovered);
        }
        static private void OtherColorsEditor(string filter, Style style)
        {
            if (!Match(filter,
                "CheckMark",
                "DockingPreview",
                "DragDropTarget",
                "NavHighlight",
                "NavWindowingHighlight"
                ))
            {
                return;
            }

            ImGui.SeparatorText("Other");
            style.ColorCheckMark = Color("CheckMark", "CheckMark", filter, style.ColorCheckMark);
            style.ColorDockingPreview = Color("DockingPreview", "DockingPreview", filter, style.ColorDockingPreview);
            style.ColorDragDropTarget = Color("DragDropTarget", "DragDropTarget", filter, style.ColorDragDropTarget);
            style.ColorNavHighlight = Color("NavHighlight", "NavHighlight", filter, style.ColorNavHighlight);
            style.ColorNavWindowingHighlight = Color("NavWindowingHighlight", "NavWindowingHighlight", filter, style.ColorNavWindowingHighlight);
        }

        static private string Title(string title) => $"{title}##{sIdCounter++}";

        static public string WildCardToRegular(string value) => "^.*" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + ".*$";
        static public bool Match(string filter, string value) => Regex.IsMatch(value, filter, RegexOptions.IgnoreCase);
        static public bool Match(string filter, params string[] values)
        {
            foreach (var value in values)
            {
                if (Match(filter, value)) return true;
            }
            return false;
        }

        static public readonly Dictionary<string, string> Hints = new()
        {
            {  "BackgroundWindow", "TEST" }
        };
        static public Value4 Color(string id, string name, string filter, Value4 value)
        {
            if (filter == "" || Match(filter, id)) return Editors.Color(Title(name), value, Hints.ContainsKey(id) ? Hints[id] : null);
            return value;
        }
    }

    static public class Editors
    {
        static public Value4 Color(string name, Value4 value, string? hint = null)
        {
            Vector4 color = value;
            if (hint == null)
            {
                ImGui.ColorEdit4(name, ref color, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar);
            }
            else
            {
                Vector2 spacing = ImGui.GetStyle().ItemSpacing;
                spacing.X = 1;
                ImGui.ColorEdit4($"##{name}", ref color, ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, spacing);
                ImGui.SameLine();
                DrawHint(hint);
                ImGui.SameLine();
                ImGui.Text(name.Split("##", 2)[0]);
                ImGui.PopStyleVar();
            }
            
            return color;
        }

        static public void DrawHint(string hint)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.BeginItemTooltip())
            {
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35f);
                ImGui.TextUnformatted(hint);
                ImGui.PopTextWrapPos();

                ImGui.EndTooltip();
            }
        }
    }
}
