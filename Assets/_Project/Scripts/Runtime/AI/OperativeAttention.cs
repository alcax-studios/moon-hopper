using System.Collections.Generic;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Where an operative looks when there's no target in sight. Gaze is a sequence of fixations (about 0.4 to 1.5 s)
    /// chosen by scoring 16 directions around them, the way people look in a task: at what matters for the task, not at
    /// what is merely there (Hayhoe and Ballard 2005). Each direction scores:
    ///   threat:   squad occupancy probability visible along it (where he could be),
    ///   unknown:  ground nobody in the squad has seen lately,
    ///   edges:    depth discontinuities (a doorway or corner, where someone can appear: the CQB "danger area"),
    ///   open:     how far one can see (a wall a meter away is not worth a look),
    ///   ahead:    the way they're moving (people look where they're going),
    ///   cues:     the last-known position and recent noises,
    /// minus inhibition of return (don't re-look at what was just checked; Posner and Cohen 1984) and minus
    /// overlap with a nearby squadmate's gaze (natural 360-degree security sectors).
    /// The choice is a softmax: disciplined units are consistent, rookies are erratic.
    /// The pitch follows the height of the likely ground (looking up the stairs, onto a balcony).
    /// </summary>
    public class OperativeAttention
    {
        const int Bins = 16;
        const float Range = 30f;
        readonly Operative op;
        readonly float[] depth = new float[Bins], threat = new float[Bins], unknown = new float[Bins], heightSum = new float[Bins], heightW = new float[Bins], score = new float[Bins];
        readonly (Vector3 dir, float t)[] recent = new (Vector3, float)[5];
        int recentIdx;
        float fixUntil;
        static readonly List<int> near = new();

        public Vector3 GazeDir { get; private set; } = Vector3.forward;
        public Vector3 GazePoint { get; private set; }
        public string Why { get; private set; } = "";
        /// <summary>When the current fixation began, and how long it was planned to last.</summary>
        public float FixStart { get; private set; }
        public float FixLength { get; private set; }
        /// <summary>The best score last time (debug).</summary>
        public float Best { get; private set; }

        public OperativeAttention(Operative op)
        {
            this.op = op;
            GazeDir = op.transform.forward;
        }

        /// <summary>Force a new fixation soon (a noise, a callout, a new room).</summary>
        public void Interrupt() => fixUntil = Mathf.Min(fixUntil, Time.time + 0.1f);

        public void Tick(Vector3 moveDir, bool moving)
        {
            if (Time.time < fixUntil) return;
            Choose(moveDir, moving);
        }

        static Vector3 BinDir(int i) => Quaternion.Euler(0f, i * (360f / Bins), 0f) * Vector3.forward;

        void Choose(Vector3 moveDir, bool moving)
        {
            var eye = op.EyePosition;
            var map = op.Squad != null ? op.Squad.Map : null;
            float now = Time.time;

            // 1. Depth ring at eye height
            for (int i = 0; i < Bins; i++)
            {
                depth[i] = Physics.Raycast(eye, BinDir(i), out var hit, Range, AILayers.World, QueryTriggerInteraction.Ignore) ? hit.distance : Range;
                threat[i] = unknown[i] = heightSum[i] = heightW[i] = 0f;
            }

            // 2. What lies along each direction (nodes nearer than the wall in that direction, roughly the same floor or visible levels)
            if (NavGraph.Count > 0)
            {
                NavGraph.Query(eye, Range, near);
                foreach (int k in near)
                {
                    var v = NavGraph.Pos[k] - eye;
                    var flat = v.Flat();
                    float d = flat.magnitude;
                    if (d < 1.2f) continue;
                    float ang = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    int bin = ((Mathf.RoundToInt(ang / (360f / Bins)) % Bins) + Bins) % Bins;
                    float dy = NavGraph.Pos[k].y - (eye.y - 1.6f);
                    bool sameFloor = Mathf.Abs(dy) < 1.5f;
                    // Same floor: only up to the wall in that direction. Other levels: only if openly in view (balconies, stairs)
                    if (sameFloor ? d > depth[bin] + 1.2f : (Mathf.Abs(dy) > 8f || CoverFinder.Blocked(eye, NavGraph.Pos[k] + Vector3.up * 1.0f))) continue;
                    float w = 1f / (1f + d / 12f);
                    float p = map != null && map.HasMass ? map.P[k] : 0f;
                    float u = map != null ? Mathf.Clamp01(map.Unseen(k) / 30f) : 1f;
                    threat[bin] += p;
                    unknown[bin] += u * w;
                    heightSum[bin] += (NavGraph.Pos[k].y + 1.3f) * (p * 50f + u * w);
                    heightW[bin] += p * 50f + u * w;
                }
            }
            float tMax = 1e-5f, uMax = 1e-5f;
            for (int i = 0; i < Bins; i++) { tMax = Mathf.Max(tMax, threat[i]); uMax = Mathf.Max(uMax, unknown[i]); }

            // 3. Cues: last known position and fresh noises
            var b = op.Perception.Belief;
            int beliefBin = -1, noiseBin = -1;
            float beliefW = 0f;
            if (b.Has && b.Age < 10f) { beliefBin = BinOf(b.Predicted - eye); beliefW = 1f - b.Age / 10f; }
            if (Time.time - op.Perception.NoiseTime < 6f) noiseBin = BinOf(op.Perception.NoisePosition - eye);
            // Where the way to him leaves view (a corner, the hall's end, a doorway, the stairs)
            int routeBin = -1;
            var rh = op.RouteHint;
            if (rh.HasValue && (rh.Value - eye).Flat().sqrMagnitude > 1f) routeBin = BinOf(rh.Value - eye);
            bool spaceAim = op.Config.useSpaceAim;

            // 4. Allies' gaze nearby
            float discipline = op.Traits.discipline, team = op.Traits.teamwork;
            for (int i = 0; i < Bins; i++)
            {
                var dir = BinDir(i);
                int l = (i + Bins - 1) % Bins, r = (i + 1) % Bins;
                float edge = Mathf.Clamp01((depth[i] - Mathf.Min(depth[l], depth[r])) / 8f);
                float open = depth[i] / Range;
                float s = 2.2f * (threat[i] / tMax) * (map != null && map.HasMass ? 1f : 0f)
                        + 0.7f * (unknown[i] / uMax)
                        + 0.55f * edge
                        + 0.25f * open;
                if (depth[i] < 2.5f) s -= 0.9f;                                   // a wall right there
                if (moving)
                {
                    float fwd = Vector3.Dot(dir, moveDir);
                    s += 0.35f * Mathf.Max(0f, fwd);
                    if (fwd < -0.5f) s -= 0.35f;                                  // rarely look back while walking
                }
                // Cues behind a wall right in front of me fade: look where he'd come from, not at the wall
                float openCue = spaceAim ? Mathf.Clamp01((depth[i] - 1.5f) / 3f) : 1f;
                if (i == beliefBin) s += 1.1f * beliefW * openCue;
                if (i == noiseBin) s += 0.8f * openCue;
                if (i == routeBin) s += 0.9f;
                // Inhibition of return
                foreach (var (rd, rt) in recent)
                    if (rt > 0f && now - rt < 2.5f && Vector3.Angle(rd.Flat(), dir) < 30f) s -= 0.5f * (1f - (now - rt) / 2.5f);
                // Sectors: don't duplicate a nearby squadmate
                if (op.Squad != null)
                    foreach (var m in op.Squad.Members)
                    {
                        if (m == null || m == op || m.IsDead || m.Attention == null) continue;
                        if ((m.transform.position - op.transform.position).sqrMagnitude > 12f * 12f) continue;
                        if (Vector3.Angle(m.Attention.GazeDir.Flat(), dir) < 25f) s -= 0.35f * team;
                    }
                score[i] = s;
            }

            // 5. Softmax choice
            float temp = Mathf.Lerp(0.4f, 0.12f, discipline);
            float max = float.MinValue;
            for (int i = 0; i < Bins; i++) max = Mathf.Max(max, score[i]);
            float sum = 0f;
            for (int i = 0; i < Bins; i++) { score[i] = Mathf.Exp((score[i] - max) / temp); sum += score[i]; }
            float pick = Random.value * sum;
            int chosen = 0;
            for (int i = 0; i < Bins; i++) { pick -= score[i]; if (pick <= 0f) { chosen = i; break; } }

            var flatDir = Quaternion.Euler(0f, Random.Range(-7f, 7f), 0f) * BinDir(chosen);
            float lookDist = Mathf.Clamp(depth[chosen] * 0.7f, 3f, 18f);
            float h = heightW[chosen] > 0f ? heightSum[chosen] / heightW[chosen] : eye.y;
            GazePoint = eye + flatDir * lookDist;
            GazePoint = new Vector3(GazePoint.x, Mathf.Lerp(eye.y - 0.2f, h, 0.8f), GazePoint.z);
            GazeDir = (GazePoint - eye).normalized;
            Best = max;
            Why = chosen == beliefBin ? "last seen" : chosen == noiseBin ? "noise"
                : threat[chosen] / tMax > 0.5f && map != null && map.HasMass ? "likely spot"
                : Mathf.Clamp01((depth[chosen] - Mathf.Min(depth[(chosen + 15) % 16], depth[(chosen + 1) % 16])) / 8f) > 0.5f ? "doorway/corner"
                : unknown[chosen] / uMax > 0.6f ? "unchecked" : moving ? "ahead" : "open";
            recent[recentIdx] = (GazeDir, now);
            recentIdx = (recentIdx + 1) % recent.Length;
            float strength = Mathf.Clamp01(threat[chosen] / tMax);
            fixUntil = now + Random.Range(0.45f, 1.1f) * (1f + strength * 0.6f) * Mathf.Lerp(1.2f, 0.85f, discipline);
            FixStart = now;
            FixLength = fixUntil - now;
        }

        static int BinOf(Vector3 v)
        {
            var f = v.Flat();
            float ang = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            return ((Mathf.RoundToInt(ang / (360f / Bins)) % Bins) + Bins) % Bins;
        }
    }
}
