using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace VirtualStorage
{
    /// <summary>
    /// Fires every time an Inventory is assigned to a scene object — on placement and
    /// on every save/load.  This is the reliable hook that fires after all Awake()s.
    ///
    /// For Virtual Storage world-objects this patch:
    ///  • removes the inventory size cap (unlimited storage)
    ///  • records the inventory ID so Patches_UI can identify it
    ///  • strips crafter-specific components from the instance
    ///  • adds ActionOpenable so E-press opens the container UI
    /// </summary>
    [HarmonyPatch(typeof(InventoryAssociated), "SetInventory")]
    internal static class Patch_UnlimitedInventory
    {
        /// <summary>Set of inventory IDs that belong to a Virtual Storage unit.</summary>
        public static readonly HashSet<int> VirtualStorageInventoryIds = new();

        /// <summary>Maps inventory ID → world-object ID for every VS unit.</summary>
        public static readonly Dictionary<int, int> InventoryToWoId = new();

        [HarmonyPostfix]
        static void Postfix(InventoryAssociated __instance, Inventory inventory)
        {
            var woa = __instance.GetComponent<WorldObjectAssociated>();
            if (woa == null) return;

            var wo = woa.GetWorldObject();
            if (wo == null) return;

            if (wo.GetGroup()?.GetId() != PluginInfo.GroupId) return;

            inventory.SetSize(int.MaxValue);
            VirtualStorageInventoryIds.Add(inventory.GetId());
            InventoryToWoId[inventory.GetId()] = wo.GetId();
            VirtualStorageLimits.Load(wo.GetId(), wo);
            Plugin.Log.LogInfo($"[VirtualStorage] Inventory {inventory.GetId()} → unlimited (WO {wo.GetId()}).");

            SetupContainerBehavior(__instance.gameObject, wo.GetId());
        }

        private static void SetupContainerBehavior(GameObject go, int woId)
        {
            // ActionCrafter.OnDestroy() dereferences managers that may not be ready
            // during the initial world load.  Deferred Destroy avoids the NullRef.
            foreach (var c in go.GetComponentsInChildren<ActionCrafter>())
                Object.Destroy(c);
            foreach (var c in go.GetComponentsInChildren<ActionnableInteractive>())
                Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<MachineConvertRecipe>())
                Object.DestroyImmediate(c);

            // The crafter Canvas's GraphicRaycaster would swallow E-press clicks.
            foreach (var canvas in go.GetComponentsInChildren<Canvas>())
            {
                if (canvas.gameObject != go)
                    Object.DestroyImmediate(canvas.gameObject);
                else
                    Object.DestroyImmediate(canvas);
            }

            // InventoryAssociatedProxy is what MachineAutoCrafter iterates to read storage
            // contents.  CraftStation2's prefab has InventoryAssociated (not the proxy), so
            // the autocrafter's proxy loop found nothing.  Adding the proxy fresh lets
            // GetRequestedInventoryData() auto-discover this instance's WorldObjectAssociated.
            if (go.GetComponent<InventoryAssociatedProxy>() == null)
                go.AddComponent<InventoryAssociatedProxy>();

            if (go.GetComponent<ActionOpenable>() != null) return;

            var openable = go.AddComponent<ActionOpenable>();
            openable.uiType                   = DataConfig.UiType.Container;
            openable.hideLogisticsButton      = false;
            // string field defaults to null in C#; null != "" passes the guard in
            // OpenInventories → SetHoversInfosText(null) → null.Trim() NullRef.
            openable.infosHoverLocalizationId = "";

            Plugin.Log.LogInfo($"[VirtualStorage] Instance configured as container (WO {woId}).");
        }
    }
}
