using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VirtualStorage
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log = null!;

        private void Awake()
        {
            Log = Logger;
            new Harmony(PluginInfo.GUID).PatchAll();
            Logger.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded.");
        }
    }

    internal static class PluginInfo
    {
        public const string GUID    = "com.sgachoud.VirtualStorage";
        public const string Name    = "VirtualStorage";
        public const string Version = "1.0.0";

        /// <summary>Group id for the buildable Virtual Storage unit.</summary>
        public const string GroupId = "VirtualStorageT1";

        /// <summary>Energy consumed by the Virtual Storage (positive kW; stored as negative in unitGenerationEnergy).</summary>
        public const float EnergyConsumptionKw = 10_000f;
    }
}
