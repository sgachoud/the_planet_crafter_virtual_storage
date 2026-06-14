using HarmonyLib;
using SpaceCraft;

namespace VirtualStorage
{
    /// <summary>
    /// When the game refreshes the right-hand inventory grid for a Virtual Storage unit,
    /// we skip the default slot-per-slot rendering (which would try to create int.MaxValue
    /// slots) and replace it with our custom icon-grid showing all item types + counts.
    ///
    /// The left panel (player backpack + logistics button) is left entirely untouched,
    /// so it works exactly like any other container.
    /// </summary>
    [HarmonyPatch(typeof(InventoryDisplayer), "TrueRefreshContent")]
    internal static class Patch_VirtualStorageDisplay
    {
        [HarmonyPrefix]
        static bool Prefix(InventoryDisplayer __instance)
        {
            // Identify the inventory via reflection (field is protected)
            var inventory = InventoryDisplayerReflection.GetInventory(__instance);
            if (inventory == null) return true;

            if (!Patch_UnlimitedInventory.VirtualStorageInventoryIds.Contains(inventory.GetId()))
                return true; // not ours – let the original method run

            Patch_UnlimitedInventory.InventoryToWoId.TryGetValue(inventory.GetId(), out int woId);
            VirtualStorageDisplayer.Refresh(__instance, inventory, woId);
            return false; // skip the original TrueRefreshContent
        }
    }
}
