using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SpaceCraft;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace VirtualStorage
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Reflection helpers for InventoryDisplayer's protected members
    // ──────────────────────────────────────────────────────────────────────────
    internal static class InventoryDisplayerReflection
    {
        private static readonly FieldInfo FInventory =
            typeof(InventoryDisplayer).GetField("_inventory",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo FGrid =
            typeof(InventoryDisplayer).GetField("_grid",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo FOriginalSizeDelta =
            typeof(InventoryDisplayer).GetField("_originalSizeDelta",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo MSetIcons =
            typeof(InventoryDisplayer).GetMethod("SetIconsPositionRelativeToGrid",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo MConsume =
            typeof(InventoryDisplayer).GetMethod("Consume",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static Inventory?        GetInventory(InventoryDisplayer d)         => (Inventory?)FInventory.GetValue(d);
        public static GridLayoutGroup?  GetGrid(InventoryDisplayer d)              => (GridLayoutGroup?)FGrid.GetValue(d);
        public static Vector2           GetOriginalSizeDelta(InventoryDisplayer d) => (Vector2)FOriginalSizeDelta.GetValue(d);
        public static void              SetOriginalSizeDelta(InventoryDisplayer d, Vector2 v) => FOriginalSizeDelta.SetValue(d, v);
        public static void              CallSetIconsPosition(InventoryDisplayer d)  => MSetIcons?.Invoke(d, null);
        public static void              CallConsume(InventoryDisplayer d, WorldObject wo)     => MConsume?.Invoke(d, new object[] { wo });
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Custom inventory display for Virtual Storage
    //  – One InventoryBlock icon tile per item type that has count > 0
    //  – Count badge (dark bg + white text) at bottom-right of each tile
    //  – Hover tooltip identical to the logistic-tab icons
    //  – Vertical scrollbar when items overflow the panel height
    // ──────────────────────────────────────────────────────────────────────────
    internal static class VirtualStorageDisplayer
    {
        private static readonly GroupInfosDisplayerBlocksSwitches TooltipFlags = new()
        {
            showName         = true,
            showDescription  = true,
            showMultipliers  = true,
            showActions      = false,
            showRecipe       = false,
            showUnits        = false,
            showInfos        = true,
        };

        private const float CellSize    = 70f;
        private const float CellGap     = 2f;
        private const float ScrollbarW  = 12f;

        // ── Entry point called by the Harmony patch ──────────────────────────
        public static void Refresh(InventoryDisplayer displayer, Inventory inventory, int woId)
        {
            var grid = InventoryDisplayerReflection.GetGrid(displayer);
            if (grid == null) return;

            // Mirror what TrueRefreshContent does for _originalSizeDelta
            var originalDelta = InventoryDisplayerReflection.GetOriginalSizeDelta(displayer);
            if (originalDelta == Vector2.zero)
            {
                originalDelta = displayer.GetComponent<RectTransform>().sizeDelta;
                InventoryDisplayerReflection.SetOriginalSizeDelta(displayer, originalDelta);
            }

            // Keep the panel at its original size — scrolling replaces expansion
            displayer.GetComponent<RectTransform>().sizeDelta = originalDelta;

            // Wrap the grid in a ScrollRect once (idempotent)
            EnsureScrollView(grid);

            // Configure the grid
            grid.cellSize       = new Vector2(CellSize, CellSize);
            grid.spacing        = new Vector2(CellGap,  CellGap);
            grid.childAlignment = TextAnchor.UpperLeft;

            // Destroy old tiles
            for (int i = grid.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(grid.transform.GetChild(i).gameObject);

            // Count items per group
            var counts = CountByGroup(inventory);

            var vrh = Managers.GetManager<VisualsResourcesHandler>();

            foreach (var group in GroupsHandler.GetGroupsItem())
            {
                if (!counts.TryGetValue(group.GetId(), out var entry)) continue;

                var blockGO = UnityEngine.Object.Instantiate(vrh.GetInventoryBlock(), grid.transform);
                var block   = blockGO.GetComponent<InventoryBlock>();

                // Set icon
                block.groupImage.sprite = group.GetImage();
                block.groupImage.color  = Color.white;

                // Hide slot chrome that doesn't apply here
                if (block.lockedImage    != null) block.lockedImage.SetActive(false);
                if (block.dropIcon       != null) block.dropIcon.SetActive(false);
                if (block.logisticIcon   != null) block.logisticIcon.SetActive(false);
                if (block.authorizedIcon != null) block.authorizedIcon.gameObject.SetActive(false);
                // NOTE: leave button.interactable = true so pointer events reach our EventTrigger

                // Hover: pass the actual WorldObject so showMultipliers shows production values
                blockGO.AddComponent<EventHoverShowGroup>()
                        .SetHoverGroupEvent(group, TooltipFlags, default(Vector2), entry.first);
                blockGO.AddComponent<EventHoverIncrease>().SetHoverGroupEvent();

                var capturedGroup     = group;
                var capturedInventory = inventory;
                var capturedWoId      = woId;
                var capturedDisp      = displayer;
                var clickData         = new EventTriggerCallbackData(group);

                // Left-click: transfer to backpack.  Right-click: consume/use (mirrors
                // InventoryDisplayer.OnImageClicked which calls Action vs Consume).
                EventsHelpers.AddTriggerEvent(blockGO, EventTriggerType.PointerClick,
                    (data) =>
                    {
                        var btn = data.pointerEventData?.button;
                        if (btn != PointerEventData.InputButton.Left &&
                            btn != PointerEventData.InputButton.Right) return;

                        WorldObject? freshWo = null;
                        foreach (var wo in capturedInventory.GetInsideWorldObjects())
                        {
                            if (wo.GetGroup().GetId() == capturedGroup.GetId())
                            { freshWo = wo; break; }
                        }
                        if (freshWo == null) return;

                        if (btn == PointerEventData.InputButton.Left)
                            InventoriesHandler.Instance.AnInventoryHasBeenClicked(capturedInventory, freshWo);
                        else
                            InventoryDisplayerReflection.CallConsume(capturedDisp, freshWo);
                    },
                    clickData);

                // Scroll: Ctrl+scroll cycles the limit; plain scroll is forwarded to the
                // ScrollRect.  Without forwarding, ExecuteEvents stops at this EventTrigger
                // (the first IScrollHandler in the hierarchy) and the ScrollRect never sees it.
                EventsHelpers.AddTriggerEvent(blockGO, EventTriggerType.Scroll,
                    (data) =>
                    {
                        if (data.pointerEventData == null) return;
                        var kb = UnityEngine.InputSystem.Keyboard.current;
                        if (kb != null && kb.ctrlKey.isPressed)
                        {
                            bool reverse = data.pointerEventData.scrollDelta.y < 0;
                            VirtualStorageLimits.CycleLimit(capturedWoId, capturedGroup.GetId(), reverse);
                            Refresh(capturedDisp, capturedInventory, capturedWoId);
                        }
                        else
                        {
                            var sr = capturedDisp.GetComponentInChildren<ScrollRect>();
                            if (sr != null)
                                ExecuteEvents.Execute(sr.gameObject, data.pointerEventData,
                                    ExecuteEvents.scrollHandler);
                        }
                    },
                    clickData);

                // Count badge (bottom-right) + limit badge (top-left)
                AttachCountBadge(blockGO, entry.count);
                AttachLimitBadge(blockGO, VirtualStorageLimits.GetLimit(woId, group.GetId()));

                blockGO.SetActive(true);
            }

            // Force layout so ContentSizeFitter computes grid height before scrollbar evaluates it
            LayoutRebuilder.ForceRebuildLayoutImmediate(grid.GetComponent<RectTransform>());

            // Position the controls overlay at the LEFT edge of the viewport
            displayer.StartCoroutine(SnapIconsToLeft(displayer));
        }

        // ── Wrap grid in a ScrollRect (one-time, idempotent) ─────────────────
        private static void EnsureScrollView(GridLayoutGroup grid)
        {
            // Already set up if any ancestor has a ScrollRect
            if (grid.GetComponentInParent<ScrollRect>() != null) return;

            var gridParent = grid.transform.parent;

            // ── Viewport (masked clipping area, leaves room for scrollbar) ──
            var vpGO = new GameObject("VSViewport");
            vpGO.transform.SetParent(gridParent, worldPositionStays: false);

            var vpRT          = vpGO.AddComponent<RectTransform>();
            vpRT.anchorMin    = Vector2.zero;
            vpRT.anchorMax    = Vector2.one;
            vpRT.offsetMin    = Vector2.zero;
            vpRT.offsetMax    = new Vector2(-ScrollbarW - 2f, 0f); // right gap for scrollbar

            // Color.white + showMaskGraphic=false: stencil is written (children visible),
            // graphic itself is culled so it doesn't appear.  Color.clear (alpha=0) skips
            // the stencil write entirely and makes all children invisible.
            vpGO.AddComponent<Image>().color = Color.white;
            vpGO.AddComponent<Mask>().showMaskGraphic = false;

            // ── Move grid into viewport; stretch horizontally, grow downward ──
            grid.transform.SetParent(vpGO.transform, worldPositionStays: false);

            var gridRT            = grid.GetComponent<RectTransform>();
            gridRT.anchorMin      = new Vector2(0f, 1f);
            gridRT.anchorMax      = new Vector2(1f, 1f);
            gridRT.pivot          = new Vector2(0.5f, 1f);
            gridRT.anchoredPosition = Vector2.zero;
            gridRT.sizeDelta      = Vector2.zero;

            if (grid.GetComponent<ContentSizeFitter>() == null)
            {
                var csf = grid.gameObject.AddComponent<ContentSizeFitter>();
                csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            }

            // ── Scrollbar ─────────────────────────────────────────────────────
            var sbGO = new GameObject("VSScrollbar");
            sbGO.transform.SetParent(gridParent, worldPositionStays: false);

            var sbRT          = sbGO.AddComponent<RectTransform>();
            sbRT.anchorMin    = new Vector2(1f, 0f);
            sbRT.anchorMax    = new Vector2(1f, 1f);
            sbRT.pivot        = new Vector2(1f, 0.5f);
            sbRT.anchoredPosition = Vector2.zero;
            sbRT.sizeDelta    = new Vector2(ScrollbarW, 0f);

            sbGO.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
            var sb            = sbGO.AddComponent<Scrollbar>();
            sb.direction      = Scrollbar.Direction.BottomToTop;

            // Slide area (gives a small inset so the handle doesn't touch edges)
            var saGO = new GameObject("SlideArea");
            saGO.transform.SetParent(sbGO.transform, worldPositionStays: false);
            var saRT      = saGO.AddComponent<RectTransform>();
            saRT.anchorMin = Vector2.zero;
            saRT.anchorMax = Vector2.one;
            saRT.offsetMin = new Vector2(0f,  4f);
            saRT.offsetMax = new Vector2(0f, -4f);

            var hGO = new GameObject("Handle");
            hGO.transform.SetParent(saGO.transform, worldPositionStays: false);
            var hRT       = hGO.AddComponent<RectTransform>();
            hRT.anchorMin = Vector2.zero;
            hRT.anchorMax = Vector2.one;
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;
            var hImg      = hGO.AddComponent<Image>();
            hImg.color    = new Color(0.55f, 0.55f, 0.55f, 0.9f);

            sb.handleRect     = hRT;
            sb.targetGraphic  = hImg;

            // ── ScrollRect on the grid's original parent ───────────────────────
            var sr = gridParent.gameObject.AddComponent<ScrollRect>();
            sr.viewport    = vpRT;
            sr.content     = gridRT;
            sr.horizontal  = false;
            sr.vertical    = true;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            sr.scrollSensitivity = 30f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia      = false;
        }

        // ── Position the controls overlay flush-left on the viewport ─────────
        private static IEnumerator SnapIconsToLeft(InventoryDisplayer displayer)
        {
            yield return new WaitForEndOfFrame();

            var grid = InventoryDisplayerReflection.GetGrid(displayer);
            if (grid == null || grid.transform.childCount == 0) yield break;
            if (displayer.iconsContainer == null) yield break;

            // Use the viewport (fixed, masked area) as position reference so the
            // controls stay put while the user scrolls the grid content.
            var viewport = grid.GetComponentInParent<Mask>()?.GetComponent<RectTransform>();
            var referenceRT = viewport ?? grid.GetComponent<RectTransform>();

            // World-space corners: [0]=BL [1]=TL [2]=TR [3]=BR
            var corners = new Vector3[4];
            referenceRT.GetWorldCorners(corners);

            // Y from the first tile (top of the visible area when scroll is at top)
            var firstChildPos = grid.transform.GetChild(0)
                                    .GetComponent<RectTransform>().position;

            displayer.iconsContainer.GetComponent<RectTransform>().position =
                new Vector3(corners[1].x, firstChildPos.y, firstChildPos.z);

            // Ensure controls render on top of the scroll view (VSViewport's Image+Mask
            // was added after iconsContainer so it has a higher sibling index by default,
            // making it render on top and block the controls).
            displayer.iconsContainer.transform.SetAsLastSibling();
        }

        // ── Count items and keep one WorldObject per group (for the tooltip) ──
        private static Dictionary<string, (int count, WorldObject first)> CountByGroup(Inventory inventory)
        {
            var d = new Dictionary<string, (int count, WorldObject first)>();
            foreach (var wo in inventory.GetInsideWorldObjects())
            {
                var id = wo.GetGroup().GetId();
                if (d.TryGetValue(id, out var existing))
                    d[id] = (existing.count + 1, existing.first);
                else
                    d[id] = (1, wo);
            }
            return d;
        }

        // ── Limit badge: top-left, teal, shows ∞ or the numeric limit ───────
        private static void AttachLimitBadge(GameObject parent, int limit)
        {
            var bgGO = new GameObject("LimitBadge");
            bgGO.transform.SetParent(parent.transform, false);

            var bgRT              = bgGO.AddComponent<RectTransform>();
            bgRT.anchorMin        = new Vector2(0f, 1f);   // top-left
            bgRT.anchorMax        = new Vector2(0f, 1f);
            bgRT.pivot            = new Vector2(0f, 1f);
            bgRT.anchoredPosition = new Vector2(1f, -1f);
            bgRT.sizeDelta        = new Vector2(36f, 18f);

            var bg   = bgGO.AddComponent<Image>();
            bg.color = new Color(0f, 0.55f, 0.55f, 0.80f);  // teal

            var txtGO = new GameObject("LimitText");
            txtGO.transform.SetParent(bgGO.transform, false);

            var txtRT       = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            var txt         = txtGO.AddComponent<Text>();
            txt.text        = VirtualStorageLimits.FormatLimit(limit);
            txt.fontSize    = 12;
            txt.fontStyle   = FontStyle.Bold;
            txt.color       = Color.white;
            txt.alignment   = TextAnchor.MiddleCenter;
            txt.font        = Resources.GetBuiltinResource<Font>("Arial.ttf");

            bgGO.transform.SetAsLastSibling();
        }

        // ── Add a small count badge to the bottom-right of a tile ────────────
        private static void AttachCountBadge(GameObject parent, int count)
        {
            // Dark semi-transparent backing rectangle
            var bgGO = new GameObject("CountBadge");
            bgGO.transform.SetParent(parent.transform, false);

            var bgRT              = bgGO.AddComponent<RectTransform>();
            bgRT.anchorMin        = new Vector2(1f, 0f);   // anchored to bottom-right
            bgRT.anchorMax        = new Vector2(1f, 0f);
            bgRT.pivot            = new Vector2(1f, 0f);
            bgRT.anchoredPosition = new Vector2(-1f, 1f);
            bgRT.sizeDelta        = new Vector2(36f, 18f);

            var bg   = bgGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.65f);

            // Text on top of the backing
            var txtGO = new GameObject("CountText");
            txtGO.transform.SetParent(bgGO.transform, false);

            var txtRT       = txtGO.AddComponent<RectTransform>();
            txtRT.anchorMin = Vector2.zero;
            txtRT.anchorMax = Vector2.one;
            txtRT.offsetMin = Vector2.zero;
            txtRT.offsetMax = Vector2.zero;

            var txt         = txtGO.AddComponent<Text>();
            txt.text        = FormatCount(count);
            txt.fontSize    = 12;
            txt.fontStyle   = FontStyle.Bold;
            txt.color       = Color.white;
            txt.alignment   = TextAnchor.MiddleCenter;
            txt.font        = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // Ensure the badge renders above the icon
            bgGO.transform.SetAsLastSibling();
        }

        // ── Number formatting ─────────────────────────────────────────────────
        private static string FormatCount(int n) =>
            n >= 1_000_000 ? $"{n / 1_000_000}M"  :
            n >= 10_000    ? $"{n / 1_000}k"       :
            n.ToString();
    }
}
