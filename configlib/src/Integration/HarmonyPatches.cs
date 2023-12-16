using HarmonyLib;
using Vintagestory.ServerMods.NoObf;

namespace ConfigLib
{
    static internal class HarmonyPatches
    {
        public static void Patch(string harmonyId)
        {
            var OriginalMethod = typeof(ModRegistryObjectTypeLoader).GetMethod("GatherVariantsAndPopulate", AccessTools.all);
            var PrefixMethod = AccessTools.Method(typeof(RegistryObjectTokensReplacer), nameof(RegistryObjectTokensReplacer.ReplaceInBaseTypePatch));
            new Harmony(harmonyId).Patch(OriginalMethod, prefix: new HarmonyMethod(PrefixMethod));
        }

        public static void Unpatch(string harmonyId)
        {
            new Harmony(harmonyId).Unpatch(typeof(ModRegistryObjectTypeLoader).GetMethod("GatherVariantsAndPopulate", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        }
    }
}
 