using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.ServerMods.NoObf;

namespace ConfigLib
{
    public class HarmonyPatches
    {
        private readonly ICoreAPI? mApi;
        private static ICoreAPI? mClientApi;
        private static ICoreAPI? mServerApi;

        public HarmonyPatches(ICoreAPI? api)
        {
            mApi = api;
            if (mApi?.Side == EnumAppSide.Client)
            {
                mClientApi = api;
            }
            else
            {
                mServerApi = api;
            }
        }

        public HarmonyPatches Patch(string harmonyId)
        {
            if (mApi?.Side == EnumAppSide.Client)
            {
                new Harmony(harmonyId).Patch(
                    typeof(ModRegistryObjectTypeLoader).GetMethod("GatherVariantsAndPopulate", AccessTools.all),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(ReplaceInBaseTypeRegistryClient)))
                    );
            }
            else
            {
                new Harmony(harmonyId).Patch(
                    typeof(ModRegistryObjectTypeLoader).GetMethod("GatherVariantsAndPopulate", AccessTools.all),
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(ReplaceInBaseTypeRegistryServer)))
                    );
            }
            return this;
        }

        public HarmonyPatches Unpatch(string harmonyId)
        {
            new Harmony(harmonyId).Unpatch(typeof(ModRegistryObjectTypeLoader).GetMethod("GatherVariantsAndPopulate", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
            return this;
        }

        public static void ReplaceInBaseTypeRegistryServer(RegistryObjectType baseType)
        {
            RegistryObjectTokensReplacer.ReplaceInBaseType(baseType.Code.Domain, baseType.jsonObject, baseType.Code.ToString(), "registry objects", mServerApi?.Logger);
        }

        public static void ReplaceInBaseTypeRegistryClient(RegistryObjectType baseType)
        {
            RegistryObjectTokensReplacer.ReplaceInBaseType(baseType.Code.Domain, baseType.jsonObject, baseType.Code.ToString(), "registry objects", mClientApi?.Logger);
        }
    }
}
 