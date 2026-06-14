using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;

namespace VirtualStorage
{
    /// <summary>
    /// Injects a "Demand All" (Import All) button into the logistic tab of every
    /// Virtual Storage unit, mirroring the vanilla "Supply All" (Export All) button.
    ///
    /// Also patches SetListsDisplay so that when VS demand count exceeds the same
    /// threshold used for supply (140 groups), a cloned "everything" label positioned
    /// in the demand column is shown instead of listing every item individually.
    /// </summary>
    [HarmonyPatch(typeof(LogisticSelector), "InitLogisticSelector")]
    internal static class Patch_LogisticDemandAll
    {
        // LogisticSelector instance → the cloned demand-everything text GO
        internal static readonly Dictionary<LogisticSelector, GameObject> DemandEvTexts = new();

        [HarmonyPostfix]
        static void Postfix(LogisticSelector __instance, Inventory inventory)
        {
            if (!Patch_UnlimitedInventory.VirtualStorageInventoryIds.Contains(inventory.GetId()))
                return;

            // Idempotent: only add once per selector instance
            if (DemandEvTexts.ContainsKey(__instance)) return;

            AddDemandAllButton(__instance, inventory);
        }

        private static void AddDemandAllButton(LogisticSelector selector, Inventory inventory)
        {
            var supplyAllGO = selector.supplyAllIcon;
            if (supplyAllGO == null) return;

            // World-space offset from supply side to demand side
            var supplySelRT = selector.groupSelectorSupply?.GetComponent<RectTransform>();
            var demandSelRT = selector.groupSelectorDemand?.GetComponent<RectTransform>();
            var sideOffset  = (supplySelRT != null && demandSelRT != null)
                ? demandSelRT.position - supplySelRT.position
                : Vector3.zero;

            // ── "Import All" button: clone of supplyAllIcon ───────────────────
            var demandAllGO = Object.Instantiate(supplyAllGO, supplyAllGO.transform.parent);
            demandAllGO.name = "VSdemandAllIcon";

            var supplyAllRT = supplyAllGO.GetComponent<RectTransform>();
            demandAllGO.GetComponent<RectTransform>().position = supplyAllRT.position + sideOffset;

            // Destroy the cloned persistent Button (calls OnSupplyAll) and add our own
            var oldBtn = demandAllGO.GetComponent<Button>();
            if (oldBtn != null) Object.DestroyImmediate(oldBtn);

            var btn = demandAllGO.AddComponent<Button>();
            btn.targetGraphic = demandAllGO.GetComponent<Image>()
                             ?? demandAllGO.GetComponentInChildren<Image>();
            btn.onClick.AddListener(() => OnDemandAll(selector, inventory));

            // Blue tint distinguishes "Import All" from "Export All"
            foreach (var img in demandAllGO.GetComponentsInChildren<Image>())
                img.color = new Color(0.45f, 0.80f, 1f, img.color.a);

            foreach (var t in demandAllGO.GetComponentsInChildren<TMPro.TMP_Text>())
                t.text = "Import All";
            foreach (var t in demandAllGO.GetComponentsInChildren<Text>())
                t.text = "Import All";

            demandAllGO.AddComponent<EventHoverIncrease>().SetHoverGroupEvent();
            demandAllGO.SetActive(true);

            // ── "everything" label clone: positioned in the demand column ─────
            GameObject demandEvGO = null;
            if (selector.everythingTextGameObject != null)
            {
                var evSrc = selector.everythingTextGameObject;
                demandEvGO = Object.Instantiate(evSrc, evSrc.transform.parent);
                demandEvGO.name = "VSdemandEverythingText";
                demandEvGO.GetComponent<RectTransform>().position =
                    evSrc.GetComponent<RectTransform>().position + sideOffset;
                demandEvGO.SetActive(false);
            }

            // Store reference for the SetListsDisplay patch (avoids deep Find each frame)
            DemandEvTexts[selector] = demandEvGO;
        }

        private static void OnDemandAll(LogisticSelector selector, Inventory inventory)
        {
            var lm = Managers.GetManager<LogisticManager>();
            var le = inventory.GetLogisticEntity();

            selector.groupSelectorDemand.PopulateWithItems(
                lm.GetItemsToDisplayForLogistics(ignoreLockingConditions: true));
            foreach (var group in selector.groupSelectorDemand.GetAddedGroups())
                le.AddDemandGroup(group);
            selector.groupSelectorDemand.PopulateWithItems(
                lm.GetItemsToDisplayForLogistics());
            InventoriesHandler.Instance.UpdateLogisticEntity(inventory);
        }
    }

    /// <summary>
    /// When VS demand exceeds 140 groups, clears the demand grid and shows the
    /// demand-column "everything" label (a clone positioned on the demand side).
    /// </summary>
    [HarmonyPatch(typeof(LogisticSelector), "SetListsDisplay")]
    internal static class Patch_LogisticDemandEverythingDisplay
    {
        static readonly FieldInfo FInventory =
            typeof(LogisticSelector).GetField("_inventory",
                BindingFlags.Instance | BindingFlags.NonPublic);

        [HarmonyPostfix]
        static void Postfix(LogisticSelector __instance)
        {
            var inventory = FInventory?.GetValue(__instance) as Inventory;
            if (inventory == null) return;
            if (!Patch_UnlimitedInventory.VirtualStorageInventoryIds.Contains(inventory.GetId())) return;

            bool demandIsEverything = inventory.GetLogisticEntity().GetDemandGroups().Count > 140;

            // Toggle the demand-side label clone
            if (Patch_LogisticDemandAll.DemandEvTexts.TryGetValue(__instance, out var evGO)
                && evGO != null)
            {
                evGO.SetActive(demandIsEverything);
            }

            if (demandIsEverything)
                __instance.groupListDemand.ClearGrid();
        }
    }
}
