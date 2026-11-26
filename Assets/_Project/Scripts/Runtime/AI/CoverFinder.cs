using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.AI
{
    public enum CoverKind { None, Low, Full, Corner }

    public enum CoverIntent { Hold, Advance, Retreat, Flank }

    public struct CoverSpot
    {
        public bool Valid;
        public Vector3 Position;
        public CoverKind Kind;
        /// <summary>Where to stand to shoot: the same spot standing for low cover, a side-step for a corner.</summary>
        public Vector3 PeekPosition;
        public float Score;
    }

    /// <summary>
    /// Finds cover on the fly against a threat position, with no hand-placed cover nodes. It samples NavMesh
    /// points in rings, then raycasts from the threat's eye to each point at crouched and standing heights:
    ///   Full: hidden standing and crouched. Low: hidden crouched, visible standing (pop up to shoot).
    ///   Corner: hidden, but a short side-step opens a line of fire.
    /// Spots are scored for an intent (hold, advance, retreat, flank) plus distance, exposure on the way,
    /// height, and spacing from squadmates.
    /// </summary>
    public static class CoverFinder
    {
        public static readonly List<(Vector3 p, CoverKind k, float s)> DebugLast = new();
        static readonly float[] Radii = { 2.5f, 5f, 8f, 12f, 17f };

        public static CoverSpot Find(Operative op, Vector3 threatEye, CoverIntent intent, float searchRadius, bool debug = false, float sideSign = 0f)
        {
            var best = new CoverSpot();
            var self = op.transform.position;
            float selfToThreat = Vector3.Distance(self.Flat(), threatEye.Flat());
            if (debug) DebugLast.Clear();
            var tr = op.Traits;

            int dirs = 16;
            float rot = Random.value * 360f;
            foreach (float radius in Radii)
            {
                if (radius > searchRadius) break;
                for (int i = 0; i < dirs; i++)
                {
                    float a = (rot + i * 360f / dirs) * Mathf.Deg2Rad;
                    var probe = self + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    if (!NavMesh.SamplePosition(probe, out var nh, 1.6f, NavMesh.AllAreas)) continue;
                    var p = nh.position;
                    var kind = Classify(p, threatEye, out var peek);
                    if (kind == CoverKind.None) { if (debug) DebugLast.Add((p, kind, 0f)); continue; }
                    // A fighting position needs a firing line: full concealment is only for getting away
                    if (kind == CoverKind.Full && intent != CoverIntent.Retreat && op.Config.useFightingCover && !op.Weapon.Dry) continue;

                    float dThreat = Vector3.Distance(p.Flat(), threatEye.Flat());
                    float dSelf = Vector3.Distance(p, self);
                    // Standoff: don't pick a spot on top of the threat, or where he probably is
                    if (op.Config.useStandoff)
                    {
                        if (dThreat < op.Standoff) continue;
                        if (op.Squad != null && op.Squad.Map != null && op.Squad.Map.MassNear(p, 2.5f) > 0.12f) continue;
                    }
                    // Moves that do not accomplish their intent are not options at all (no shuffling in place)
                    if (intent == CoverIntent.Advance && dThreat > selfToThreat - 2f) continue;
                    if (intent == CoverIntent.Retreat && dThreat < selfToThreat + 3f) continue;
                    if (intent == CoverIntent.Flank && dSelf < 4f) continue;
                    float s = kind switch { CoverKind.Low => 1f, CoverKind.Corner => 0.95f, _ => 0.75f };

                    // Keep within the preferred fighting band unless retreating
                    float minR = op.RangeMin, maxR = op.RangeMax;
                    if (dThreat < 4f) s -= 1.2f;
                    switch (intent)
                    {
                        case CoverIntent.Hold:
                            s -= dSelf * 0.05f;
                            if (op.Config.useSteadyMoves) s -= Mathf.Max(0f, dThreat - selfToThreat) * 0.12f;   // holding isn't retreating: no running back
                            if (dThreat < minR) s -= (minR - dThreat) * 0.06f;
                            if (dThreat > maxR) s -= (dThreat - maxR) * 0.04f;
                            break;
                        case CoverIntent.Advance:
                            s += Mathf.Min(selfToThreat - dThreat, Mathf.Max(0f, selfToThreat - op.RangeMin)) * 0.08f;   // closer is better, down to the fighting range
                            s -= dSelf * 0.02f;
                            break;
                        case CoverIntent.Retreat:
                            s += (dThreat - selfToThreat) * 0.07f;
                            if (kind == CoverKind.Low) s -= 0.2f;              // scared people want to be fully hidden
                            break;
                        case CoverIntent.Flank:
                            {
                                var player = op.Target;
                                var look = player != null ? player.Forward.Flat().normalized : (self - threatEye).Flat().normalized;
                                var fromThreat = (p - threatEye).Flat().normalized;
                                float off = Vector3.Angle(look, fromThreat);       // 0 = right where the player looks
                                s += Mathf.InverseLerp(20f, 100f, off) * 0.9f;
                                s += (selfToThreat - dThreat) * 0.03f;
                                s -= dSelf * 0.015f;
                                break;
                            }
                    }

                    // High ground is worth a little
                    s += Mathf.Clamp(p.y - threatEye.y + 1.6f, -1f, 3f) * 0.06f * (0.5f + tr.skill);

                    // Exposure on the way: midpoint visible to the threat
                    var mid = Vector3.Lerp(self, p, 0.5f) + Vector3.up * 1.2f;
                    if (!Physics.Linecast(threatEye, mid, AILayers.World, QueryTriggerInteraction.Ignore))
                        s -= 0.25f * (1f + tr.caution);

                    if (op.Squad != null && op.Squad.Crowded(p, op, op.Config.allySpacing)) s -= 0.8f;
                    // The squad remembers where people got shot: avoid his kill zones
                    if (op.Squad != null) s -= op.Squad.DangerAt(p) * 0.5f;
                    // An ordered flank goes to a named side
                    if (intent == CoverIntent.Flank && sideSign != 0f)
                    {
                        // Left or right as seen by this operative facing the threat
                        var right = Vector3.Cross(Vector3.up, (threatEye - self).Flat().normalized);
                        float side = Vector3.Dot(right, (p - self).Flat());
                        if (Mathf.Sign(side) != Mathf.Sign(sideSign)) s -= 0.7f;
                    }
                    s += Random.Range(-0.08f, 0.08f);   // two operatives never think quite alike

                    if (debug) DebugLast.Add((p, kind, s));
                    if (!best.Valid || s > best.Score)
                        best = new CoverSpot { Valid = true, Position = p, Kind = kind, PeekPosition = peek, Score = s };
                }
            }

            if (best.Valid)
            {
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(self, best.Position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    best.Valid = false;
            }
            return best;
        }

        /// <summary>
        /// A real flank: cover 60 to 120 degrees around the threat from where the squad's main line is, at this unit's
        /// fighting range. sideSign picks left (-1) or right (+1) as seen from the squad; 0 tries both.
        /// </summary>
        public static CoverSpot FindFlank(Operative op, Vector3 threatEye, float sideSign = 0f)
        {
            var best = new CoverSpot();
            var threat = threatEye - Vector3.up * 1.5f;
            // The squad's line: the average position of the living members (or just me)
            Vector3 center = op.transform.position; int n = 1;
            if (op.Squad != null)
                foreach (var m in op.Squad.Members)
                    if (m != null && m != op && !m.IsDead) { center += m.transform.position; n++; }
            center /= n;
            var baseDir = (center - threat).Flat();
            if (baseDir.sqrMagnitude < 0.01f) baseDir = (op.transform.position - threat).Flat();
            baseDir.Normalize();
            // Indoors the flank ring shrinks to the room: a flank is the other side of the room or the next doorway, not 20 m out
            float minR = Mathf.Max(op.FightSpace < 20f ? 4f : 8f, op.RangeMin + 2f), maxR = Mathf.Max(minR + 4f, op.RangeMax * 0.75f);
            var path = new NavMeshPath();
            foreach (float side in sideSign == 0f ? new[] { -1f, 1f } : new[] { Mathf.Sign(sideSign) })
                foreach (float ang in new[] { 60f, 80f, 100f, 120f })
                    foreach (float r in new[] { minR, (minR + maxR) * 0.5f, maxR })
                    {
                        // Positive yaw turns clockwise (to the right as seen from the squad looking at the threat)
                        var dir = Quaternion.Euler(0f, -side * ang, 0f) * baseDir;
                        var probe = threat + dir * r + Random.insideUnitSphere.Flat() * 1.5f;
                        if (!NavMesh.SamplePosition(probe, out var nh, 2.5f, NavMesh.AllAreas)) continue;
                        var p = nh.position;
                        var kind = Classify(p, threatEye, out var peek);
                        if (kind == CoverKind.None || kind == CoverKind.Full) continue;
                        float s = (kind == CoverKind.Low ? 1f : 0.95f) + Mathf.InverseLerp(50f, 110f, ang) * 0.3f;
                        s -= Vector3.Distance(p, op.transform.position) * 0.012f;
                        if (op.Squad != null) { s -= op.Squad.DangerAt(p) * 0.5f; if (op.Squad.Crowded(p, op, 5f)) s -= 0.8f; }
                        s += Random.Range(-0.1f, 0.1f);
                        if (best.Valid && s <= best.Score) continue;
                        if (!NavMesh.CalculatePath(op.transform.position, p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                        best = new CoverSpot { Valid = true, Position = p, Kind = kind, PeekPosition = peek, Score = s };
                    }
            return best;
        }

        /// <summary>
        /// A vantage point: somewhere at a standoff distance that sees a spot (where a noise came from, where he probably
        /// is), ideally with cover from it, so they observe a danger area instead of walking into it.
        /// </summary>
        public static Vector3? FindVantage(Operative op, Vector3 target, float minD, float maxD)
        {
            var self = op.transform.position;
            var tgt = target + Vector3.up * 1.0f;
            Vector3? best = null; float bestS = float.MinValue;
            float rot = Random.value * 360f;
            var path = new NavMeshPath();
            for (int i = 0; i < 12; i++)
                foreach (float r in new[] { minD, (minD + maxD) * 0.5f, maxD })
                {
                    var probe = target + Quaternion.Euler(0f, rot + i * 30f, 0f) * Vector3.forward * r;
                    if (!NavMesh.SamplePosition(probe, out var h, 2f, NavMesh.AllAreas)) continue;
                    var p = h.position;
                    if (Mathf.Abs(p.y - target.y) > 4f) continue;
                    if (Vector3.Distance(p, target) < minD * 0.8f) continue;
                    if (Blocked(p + Vector3.up * 1.55f, tgt)) continue;       // must actually see the spot
                    float s = 1f - Vector3.Distance(p, self) * 0.03f;
                    if (Classify(p, tgt + Vector3.up * 0.5f, out _) != CoverKind.None) s += 0.4f;
                    if (op.Squad != null)
                    {
                        if (op.Squad.Crowded(p, op, 3f)) s -= 0.6f;
                        if (op.Squad.Map != null) s -= op.Squad.Map.MassNear(p, 2.5f) * 3f;
                    }
                    s += Random.Range(-0.1f, 0.1f);
                    if (s <= bestS) continue;
                    if (!NavMesh.CalculatePath(self, p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    bestS = s; best = p;
                }
            return best;
        }

        /// <summary>How well a spot hides you from a threat eye (and where to stand to shoot back).</summary>
        public static CoverKind Classify(Vector3 p, Vector3 threatEye, out Vector3 peek)
        {
            peek = p;
            bool crouchHidden = Blocked(threatEye, p + Vector3.up * 0.95f) && Blocked(threatEye, p + Vector3.up * 0.5f);
            if (!crouchHidden) return CoverKind.None;
            // Cover has to be close (something to put your back against), not a wall 20 m away
            var toThreat = (threatEye - p).Flat().normalized;
            if (!Physics.Raycast(p + Vector3.up * 0.6f, toThreat, 2.2f, AILayers.World, QueryTriggerInteraction.Ignore)) return CoverKind.None;

            bool standHidden = Blocked(threatEye, p + Vector3.up * 1.55f);
            if (!standHidden) { peek = p; return CoverKind.Low; }

            // Full cover: look for a side-step that opens a line of fire (a corner)
            var side = Vector3.Cross(Vector3.up, toThreat);
            foreach (float s in new[] { 0.9f, -0.9f, 1.4f, -1.4f })
            {
                var step = p + side * s;
                if (!NavMesh.SamplePosition(step, out var nh, 0.4f, NavMesh.AllAreas)) continue;
                if (!Blocked(threatEye, nh.position + Vector3.up * 1.45f)) { peek = nh.position; return CoverKind.Corner; }
            }
            return CoverKind.Full;
        }

        public static bool Blocked(Vector3 from, Vector3 to) => Physics.Linecast(from, to, AILayers.World, QueryTriggerInteraction.Ignore);

        /// <summary>Is a standing (or crouched) body at p hidden from the eye?</summary>
        public static bool Hidden(Vector3 p, Vector3 eye, bool crouched)
            => Blocked(eye, p + Vector3.up * (crouched ? 0.95f : 1.55f)) && Blocked(eye, p + Vector3.up * 0.5f);
    }
}
