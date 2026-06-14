using HarmonyLib;
using SpaceCraft;

namespace VirtualStorage
{
    /// <summary>
    /// Prevents drones from delivering items to a Virtual Storage unit when the
    /// per-item-type limit for that group has been reached.
    ///
    /// Player deposits are unaffected: they go through the inventory UI click path,
    /// not through LogisticManager task creation.
    ///
    /// Both overloads are patched:
    ///  • CreateNewTaskForWorldObject       — supply-inventory → VS delivery
    ///  • CreateNewTaskForWorldObjectForSpawnedObject — ground-spawned item → VS delivery
    /// </summary>
    [HarmonyPatch(typeof(LogisticManager), "CreateNewTaskForWorldObject")]
    internal static class Patch_LogisticLimit
    {
        [HarmonyPrefix]
        static bool Prefix(Inventory demandInventory, WorldObject worldObject,
                           ref LogisticTask __result)
        {
            if (!Patch_UnlimitedInventory.VirtualStorageInventoryIds.Contains(demandInventory.GetId()))
                return true;

            if (!Patch_UnlimitedInventory.InventoryToWoId.TryGetValue(demandInventory.GetId(), out int woId))
                return true;

            if (VirtualStorageLimits.DroneCanDeposit(demandInventory, woId, worldObject.GetGroup()))
                return true;

            __result = null!;
            return false;
        }
    }

    [HarmonyPatch(typeof(LogisticManager), "CreateNewTaskForWorldObjectForSpawnedObject")]
    internal static class Patch_LogisticLimitSpawned
    {
        [HarmonyPrefix]
        static bool Prefix(Inventory demandInventory, WorldObject worldObject,
                           ref LogisticTask __result)
        {
            if (!Patch_UnlimitedInventory.VirtualStorageInventoryIds.Contains(demandInventory.GetId()))
                return true;

            if (!Patch_UnlimitedInventory.InventoryToWoId.TryGetValue(demandInventory.GetId(), out int woId))
                return true;

            if (VirtualStorageLimits.DroneCanDeposit(demandInventory, woId, worldObject.GetGroup()))
                return true;

            __result = null!;
            return false;
        }
    }
}
