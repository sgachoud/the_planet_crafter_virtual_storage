using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;

namespace VirtualStorage
{
    /// <summary>
    /// Injects localized name and description for the Virtual Storage group
    /// into the game's localization dictionary after it has been loaded.
    /// </summary>
    [HarmonyPatch(typeof(Localization), "GetLocalizedString")]
    internal static class Patch_Localization
    {
        // Keys used by Readable.GetGroupName / GetGroupDescription
        private const string NameKey = "GROUP_NAME_" + PluginInfo.GroupId;
        private const string DescKey = "GROUP_DESC_" + PluginInfo.GroupId;

        private static readonly Dictionary<string, string> _strings = new()
        {
            [NameKey] = "Virtual Storage",
            [DescKey] = $"Unlimited storage capacity. Consumes {PluginInfo.EnergyConsumptionKw:0} kW of power."
        };

        [HarmonyPrefix]
        static bool Prefix(string stringCode, ref string __result)
        {
            if (_strings.TryGetValue(stringCode, out var value))
            {
                __result = value;
                return false; // skip original
            }
            return true; // let original handle all other keys
        }
    }
}
