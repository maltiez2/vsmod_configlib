using ConfigLib;
using HarmonyLib;
using System.Reflection.Emit;
using Vintagestory.API.Datastructures;
using Vintagestory.Server;

namespace configlib.source.Integration;

[HarmonyPatch]
[HarmonyPatchCategory("server")]
internal static class SettingsThroughWorldConfigPatch
{
    [HarmonyPatch(typeof(ServerMain), "WorldMetaDataPacket")]
    internal static IEnumerable<CodeInstruction> AppendConfigsToWorldMetaDataPacket(IEnumerable<CodeInstruction> instructions)
    {
        var matcher = new CodeMatcher(instructions);
        
        matcher.MatchStartForward(
            CodeMatch.Calls(AccessTools.Method(typeof(TreeAttribute), nameof(TreeAttribute.ToBytes)))
        );

        if (!matcher.IsValid)
        {
            throw new ConfigLibException($"Failed to find binding point for '{nameof(AppendConfigsToWorldMetaDataPacket)}', was ServerMain.WorldMetaDataPacket method changed or did some other mod interfere?");
        }

        //Replace ToBytes with call that also appends our configs
        matcher.Instruction.opcode = OpCodes.Call;
        matcher.Instruction.operand = AccessTools.Method(typeof(SettingsThroughWorldConfigPatch), nameof(ToBytesWithConfigs));
        matcher.Insert(CodeInstruction.LoadArgument(0));

        return matcher.InstructionEnumeration();
    }

    internal static byte[] ToBytesWithConfigs(TreeAttribute worldConfig, ServerMain serverMain)
    {
        var configLibSystem = serverMain.Api.ModLoader.GetModSystem<ConfigLibModSystem>();
        configLibSystem._registry.ToBytes(serverMain.Api.World, out byte[] serializedConfigs, out int quantity);
        
        var configLibTree = worldConfig.GetOrAddTreeAttribute(ConfigRegistry.ConfigLibTreeKey);
        configLibTree.SetInt(ConfigRegistry.ConfigQuanityKey, quantity);
        configLibTree.SetBytes(ConfigRegistry.SerializedConfigsKey, serializedConfigs);

        var result = worldConfig.ToBytes();
        
        worldConfig.RemoveAttribute(ConfigRegistry.ConfigLibTreeKey);
        
        return result;
    }
}
