using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ConfigLib.Patches;

[HarmonyPatchCategory("client")]
[HarmonyPatch]
internal static class PauseMenuPatch
{
    [HarmonyPatch(typeof(GuiComposerHelpers), nameof(GuiComposerHelpers.AddButton), argumentTypes: [ typeof(GuiComposer), typeof(string), typeof(ActionConsumable), typeof(ElementBounds), typeof(EnumButtonStyle), typeof(string) ])]
    [HarmonyPrefix]
    private static bool AddButton(ref GuiComposer __result, GuiComposer composer, string text, ActionConsumable onClick, ElementBounds bounds)
    {
        if (text != Lang.Get("game:mainmenu-settings") || bounds.fixedWidth < 200) return true;

        ElementBounds left = new()
        {
            Alignment = EnumDialogArea.LeftFixed,
            BothSizing = ElementSizing.Fixed,
            fixedY = bounds.fixedY,
            fixedPaddingX = 2.0,
            fixedPaddingY = 2.0
        };

        ElementBounds right = new()
        {
            Alignment = EnumDialogArea.RightFixed,
            BothSizing = ElementSizing.Fixed,
            fixedY = bounds.fixedY,
            fixedPaddingX = 2.0,
            fixedPaddingY = 2.0
        };

        __result = composer
            .AddButton(text, onClick, left.WithFixedWidth(144))
            .AddButton("Mods settings", GuiManager.ShowConfigWindowStatic, right.WithFixedWidth(183));

        return false;
    }
}
