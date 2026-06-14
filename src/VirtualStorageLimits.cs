using System.Collections.Generic;
using System.Text;
using SpaceCraft;

namespace VirtualStorage
{
    /// <summary>
    /// Per-item-type deposit limits for each Virtual Storage world-object.
    ///
    /// A limit of -1 means unlimited (the default).  Limits only block
    /// drone/logistic delivery; the player can always deposit manually.
    ///
    /// Limits are persisted in WorldObject.SetText() using the prefix "vs_lim:"
    /// followed by semicolon-separated "groupId=count" pairs.
    /// </summary>
    internal static class VirtualStorageLimits
    {
        // Values to cycle through on right-click.  -1 (∞) must be first.
        internal static readonly int[] Presets = { -1, 1, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000 };

        private const string Prefix = "vs_lim:";

        // woId → (groupId → limit)
        private static readonly Dictionary<int, Dictionary<string, int>> _limits = new();

        // woId → WorldObject (cached on Load so CycleLimit can persist without a handler lookup)
        private static readonly Dictionary<int, WorldObject> _worldObjects = new();

        // ── Public API ─────────────────────────────────────────────────────────

        public static int GetLimit(int woId, string groupId)
        {
            if (!_limits.TryGetValue(woId, out var g)) return -1;
            return g.TryGetValue(groupId, out int v) ? v : -1;
        }

        /// <summary>Step the limit for <paramref name="groupId"/> forward (or backward) through presets.</summary>
        public static void CycleLimit(int woId, string groupId, bool reverse = false)
        {
            if (!_worldObjects.TryGetValue(woId, out var wo)) return;

            int current = GetLimit(woId, groupId);
            int idx = 0;
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Presets[i] == current) { idx = i; break; }
            }
            int next = reverse
                ? Presets[(idx - 1 + Presets.Length) % Presets.Length]
                : Presets[(idx + 1)                  % Presets.Length];
            SetAndSave(woId, wo, groupId, next);
        }

        /// <summary>
        /// Returns false when the item must not be delivered by a drone:
        /// the current count of <paramref name="group"/> in <paramref name="inv"/>
        /// is already at or above the limit.
        /// </summary>
        public static bool DroneCanDeposit(Inventory inv, int woId, Group group)
        {
            int limit = GetLimit(woId, group.GetId());
            if (limit == -1) return true;

            int count = 0;
            foreach (var item in inv.GetInsideWorldObjects())
                if (item.GetGroup().GetId() == group.GetId()) count++;
            return count < limit;
        }

        public static string FormatLimit(int limit) => limit < 0 ? "∞" : limit.ToString();

        // ── Persistence ────────────────────────────────────────────────────────

        /// <summary>Called from the InventoryAssociated patch on placement and load.</summary>
        public static void Load(int woId, WorldObject wo)
        {
            _worldObjects[woId] = wo;
            _limits.Remove(woId);

            var text = wo.GetText();
            if (string.IsNullOrEmpty(text) || !text.StartsWith(Prefix)) return;

            var g = new Dictionary<string, int>();
            foreach (var part in text.Substring(Prefix.Length).Split(';'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                if (int.TryParse(part.Substring(eq + 1), out int v))
                    g[part.Substring(0, eq)] = v;
            }
            if (g.Count > 0)
                _limits[woId] = g;
        }

        // ── Internal helpers ──────────────────────────────────────────────────

        private static void SetAndSave(int woId, WorldObject wo, string groupId, int limit)
        {
            if (!_limits.TryGetValue(woId, out var g))
            {
                g = new Dictionary<string, int>();
                _limits[woId] = g;
            }
            if (limit == -1)
                g.Remove(groupId);
            else
                g[groupId] = limit;

            Persist(wo, g);
        }

        private static void Persist(WorldObject wo, Dictionary<string, int> g)
        {
            if (g.Count == 0) { wo.SetText(""); return; }
            var sb = new StringBuilder(Prefix);
            foreach (var kv in g)
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            wo.SetText(sb.ToString());
        }
    }
}
