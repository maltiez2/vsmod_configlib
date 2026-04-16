using HarmonyLib;
using Vintagestory.Server;

namespace configlib.source.Typed.HarmonyPatches;

[HarmonyPatch(typeof(ServerMain), "WorldMetaDataPacket")]
[HarmonyPatchCategory("server")]
internal static class SendConfigsThroughWorldConfigPatch
{
    [HarmonyPrefix]
    public static void Prefix(SaveGame ___SaveGameData) => TypedConfigLibModSystem.WriteConfigsToWorldConfig(___SaveGameData.WorldConfiguration);

    [HarmonyPostfix]
    public static void Postfix(SaveGame ___SaveGameData) => ___SaveGameData.WorldConfiguration.RemoveAttribute(Globals.ConfigTreeKey);
}
