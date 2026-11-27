using Alcax.AI;
using Alcax.Dialogue;
using UnityEngine;

namespace Alcax.Items
{
    /// <summary>
    /// Stealing: taking an owned item (or from an owned container). Unseen, the item is just marked stolen (you can
    /// keep it, use it, eat it). Seen by anyone nearby, it's a crime: station security sends a guard to fine you
    /// (pay, or refuse and fight). Selling stolen goods to a trader who minds is a crime on the spot.
    /// </summary>
    public static class Theft
    {
        const float SightRange = 14f, SightAngle = 70f;

        /// <summary>Is anyone (not the owner's goods' thief) watching the player right now? Returns who.</summary>
        public static bool Watched(out string who)
        {
            who = null;
            var p = PlayerTarget.Instance;
            if (p == null) return false;
            var head = p.HeadPosition;
            foreach (var n in Npc.Active)
            {
                if (n == null || !n.isActiveAndEnabled) continue;
                var civ = n.GetComponent<Civilian>();
                if (civ != null && civ.Dead) continue;
                var op = n.GetComponent<Operative>();
                if (op != null && op.IsDead) continue;
                var eye = n.FocusPoint;
                var to = head - eye;
                if (to.magnitude > SightRange) continue;
                var fwd = n.transform.forward; fwd.y = 0f; var flat = to; flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f && Vector3.Angle(fwd, flat) > SightAngle) continue;
                if (Physics.Linecast(eye, head, AILayers.World, QueryTriggerInteraction.Ignore)) continue;
                who = n.displayName;
                return true;
            }
            return false;
        }

        /// <summary>Took cash from an owner (a register).</summary>
        public static void TookCredits(int amount, string owner)
        {
            if (amount <= 0 || string.IsNullOrEmpty(owner) || !Watched(out var who)) return;
            StationSecurity.Instance?.ReportTheft(amount, who);
        }

        /// <summary>The player took this from its owner. Marks it stolen, and reports it when someone saw.</summary>
        public static void Took(ItemStack s, string owner)
        {
            if (s == null || string.IsNullOrEmpty(owner)) return;
            s.stolen = true;
            if (!Watched(out var who)) return;
            var sec = StationSecurity.Instance;
            if (sec != null) sec.ReportTheft(s.Value, who);
        }
    }
}
