using System.Collections.Generic;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;

namespace VirtualStorage
{
    /// <summary>
    /// Registers the new Virtual Storage constructible group into the game's group registry.
    /// Runs once after StaticDataHandler.LoadStaticData() has built all vanilla groups.
    /// </summary>
    [HarmonyPatch(typeof(StaticDataHandler), "LoadStaticData")]
    internal static class Patch_RegisterGroup
    {
        [HarmonyPostfix]
        static void Postfix()
        {
            // Bail out if already registered (e.g. hot-reload scenarios)
            if (GroupsHandler.GetGroupViaId(PluginInfo.GroupId) != null)
                return;

            // ── Visual source: CraftStation2 mesh/icon ───────────────────────
            // Group IDs (no "T" in the name): CraftStation2 = Advanced Craft Station
            Group? craftGroup =
                GroupsHandler.GetGroupViaId("CraftStation2") ??
                GroupsHandler.GetGroupViaId("CraftStation1");

            // ── Behavior source: a plain container (ActionOpenable → Container UI) ─
            Group? containerGroup =
                GroupsHandler.GetGroupViaId("Container2") ??
                GroupsHandler.GetGroupViaId("Container1");

            if (containerGroup == null)
            {
                Plugin.Log.LogError("[VirtualStorage] No base container group found – Virtual Storage will not be registered.");
                return;
            }

            // Use CraftStation2's original prefab for placement and visuals.
            // Crafter behaviour is stripped and ActionOpenable is added per-instance
            // by Patch_UnlimitedInventory.SetupContainerBehavior at placement/load time.
            var customPrefab = BuildVisualPrefab(craftGroup, containerGroup);
            Plugin.Log.LogInfo($"[VirtualStorage] Using prefab '{customPrefab.name}'.");

            // Build GroupDataConstructible at runtime
            var gdc = ScriptableObject.CreateInstance<GroupDataConstructible>();
            gdc.id                    = PluginInfo.GroupId;
            gdc.associatedGameObject  = customPrefab;
            gdc.icon                  = craftGroup?.GetImage() ?? containerGroup.GetImage();

            // Energy consumption: negative = consumer (value defined in PluginInfo)
            gdc.unitGenerationEnergy  = -PluginInfo.EnergyConsumptionKw;

            // Large initial size – will be overridden to int.MaxValue in InventoryAssociated patch
            gdc.inventorySize         = 9999;

            // Place in the Machines build-panel category (closest to storage machines)
            // Use the same category as Container2 so the autocrafter treats VS as storage.
            var containerGdc = containerGroup.GetGroupData() as GroupDataConstructible;
            gdc.groupCategory              = containerGdc?.groupCategory ?? DataConfig.GroupCategory.Machines;
            // EnabledOnAllInventories: the autocrafter's very first filter rejects anything
            // where this == Disabled.  Container2 uses EnabledOnAllInventories.
            gdc.logisticInterplanetaryType = DataConfig.LogisticInterplanetaryType.EnabledOnAllInventories;

            // Recipe: T2 Drone + T3 Dismantling chip + Construction chip + Circuit board
            //         + one of each quartz type (Solar, Cosmic, Pulsar, Quasar, Magnetar, Blazar)
            gdc.recipeIngredients = BuildRecipe(
                ("Drone2",                1),
                ("MultiToolDeconstruct3", 1),
                ("MultiBuild",            1),
                ("CircuitBoard1",         1),
                ("SolarQuartz",           1),
                //("CosmicQuartz",          1),
                ("PulsarQuartz",          1),
                ("QuasarQuartz",          1),
                ("MagnetarQuartz",        1),
                ("BalzarQuartz",          1)
            );

            // Unlocked from start: any real WorldUnitType with value 0 is always satisfied.
            // WorldUnitType.Null means "needs blueprint/story event" and never shows in normal mode.
            gdc.unlockingWorldUnit          = DataConfig.WorldUnitType.Oxygen;
            gdc.unlockingValue              = 0f;
            gdc.terraformStageUnlock        = null;

            // These list/array fields MUST be non-null — SetAllGroups iterates them directly
            gdc.unlockInPlanets              = new List<SpaceCraft.PlanetData>();
            gdc.secondaryInventoriesSize     = new List<int>();
            gdc.notAllowedPlanetsRequirement = new List<SpaceCraft.PlanetData>();
            gdc.terraStageRequirements       = System.Array.Empty<SpaceCraft.TerraformStage>();

            // Create the runtime Group object
            var newGroup = new GroupConstructible(gdc);

            // StaticDataHandler calls SetRecipe() for built-in groups; we must do it manually.
            var recipeIngredients = gdc.recipeIngredients ?? new List<GroupDataItem>();
            newGroup.SetRecipe(new Recipe(recipeIngredients));

            // Append to the global group list
            var allGroups = new List<Group>(GroupsHandler.GetAllGroups()) { newGroup };
            GroupsHandler.SetAllGroups(allGroups);

            Plugin.Log.LogInfo($"[VirtualStorage] Registered group '{PluginInfo.GroupId}' " +
                               $"(energy: {gdc.unitGenerationEnergy} kW, recipe: {gdc.recipeIngredients?.Count ?? 0} ingredients).");
        }

        // Returns CraftStation2's original prefab unchanged (falls back to Container2).
        // Placement, snapping, and visuals are identical to CraftStation2.
        // Per-instance behaviour is set up by Patch_UnlimitedInventory at runtime.
        private static GameObject BuildVisualPrefab(Group? craftGroup, Group containerGroup) =>
            craftGroup?.GetAssociatedGameObject() ?? containerGroup.GetAssociatedGameObject();

        // ── Build a recipe list from (groupId, count) pairs ──────────────────
        private static List<GroupDataItem>? BuildRecipe(params (string id, int count)[] ingredients)
        {
            var list = new List<GroupDataItem>();
            foreach (var (id, count) in ingredients)
            {
                var g = GroupsHandler.GetGroupViaId(id);
                if (g?.GetGroupData() is GroupDataItem gdi && g is GroupItem)
                {
                    for (int i = 0; i < count; i++)
                        list.Add(gdi);
                }
                else
                {
                    Plugin.Log.LogWarning($"[VirtualStorage] Recipe ingredient '{id}' not found – skipping.");
                }
            }
            return list.Count > 0 ? list : null;
        }
    }
}
