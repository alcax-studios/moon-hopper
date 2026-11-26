using System.Collections.Generic;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// A squad's probability distribution over where the player is, on the NavGraph
    /// (after Isla, "Probabilistic Target Tracking and Search Using Occupancy Maps", AI Game Programming Wisdom 3).
    ///  - Sighting: collapse onto the seen position.
    ///  - Unseen: diffuse to neighbors at about the player's walking speed (mass is conserved).
    ///  - Observation: ground an operative actually sees (line of sight at a crouched head height) is emptied,
    ///    because the player is not there. This negative information is what makes the search smart.
    ///  - Noise and hits: multiply by a Gaussian likelihood around the estimated source.
    /// It also records when each node was last seen by anyone in the squad ("cleared" ground).
    /// Everything comes from the squad's own senses, so there are no wallhacks.
    /// </summary>
    public class OccupancyMap
    {
        public readonly float[] P;
        public readonly float[] Seen;
        float[] next;
        public bool HasMass { get; private set; }
        public float LastCollapse { get; private set; } = -100f;
        public Vector3 LastAnchor { get; private set; }
        public float MaxP { get; private set; }

        static readonly List<int> near = new();
        static readonly List<(int node, float pri)> cands = new();

        public OccupancyMap()
        {
            NavGraph.EnsureBuilt();
            int n = NavGraph.Count;
            P = new float[n];
            Seen = new float[n];
            next = new float[n];
            for (int i = 0; i < n; i++) Seen[i] = -1000f;
        }

        public void Collapse(Vector3 pos)
        {
            int c = NavGraph.Nearest(pos, 5f);
            if (c < 0) return;
            System.Array.Clear(P, 0, P.Length);
            P[c] = 0.6f;
            int s = NavGraph.NStart[c], e = NavGraph.NStart[c + 1];
            float share = e > s ? 0.4f / (e - s) : 0f;
            for (int k = s; k < e; k++) P[NavGraph.NList[k]] += share;
            if (e == s) P[c] = 1f;
            HasMass = true;
            LastCollapse = Time.time;
            LastAnchor = pos;
        }

        /// <summary>A Gaussian prior (when there is no information yet) around a position.</summary>
        public void Seed(Vector3 pos, float sigma)
        {
            System.Array.Clear(P, 0, P.Length);
            NavGraph.Query(pos, sigma * 2.5f, near);
            float total = 0f, s2 = 2f * sigma * sigma;
            foreach (int k in near)
            {
                var d = NavGraph.Pos[k] - pos;
                float w = Mathf.Exp(-(d.x * d.x + d.z * d.z + d.y * d.y * 4f) / s2) * HidePrior(k);
                P[k] = w; total += w;
            }
            if (total <= 0f) { Collapse(pos); return; }
            for (int i = 0; i < P.Length; i++) P[i] /= total;
            HasMass = true;
            LastAnchor = pos;
        }

        /// <summary>Bayesian update with a noise or hit estimate: posterior is proportional to prior times likelihood.</summary>
        public void Observe(Vector3 pos, float sigma)
        {
            if (!HasMass) { Seed(pos, Mathf.Max(2f, sigma)); return; }
            float s2 = 2f * Mathf.Max(1.5f, sigma) * Mathf.Max(1.5f, sigma);
            float total = 0f;
            for (int i = 0; i < P.Length; i++)
            {
                if (P[i] <= 0f) continue;
                var d = NavGraph.Pos[i] - pos;
                P[i] *= Mathf.Exp(-(d.x * d.x + d.z * d.z + d.y * d.y * 4f) / s2) + 0.02f;
                total += P[i];
            }
            if (total < 1e-6f) Seed(pos, Mathf.Max(2f, sigma));
            else Normalize(total);
        }

        public void Diffuse(float dt, float speed)
        {
            if (!HasMass) return;
            float lambda = Mathf.Clamp(speed * dt / 1.6f, 0f, 0.8f);
            System.Array.Clear(next, 0, next.Length);
            var st = NavGraph.NStart; var nl = NavGraph.NList;
            for (int i = 0; i < P.Length; i++)
            {
                float p = P[i];
                if (p <= 1e-7f) continue;
                int s = st[i], e = st[i + 1], deg = e - s;
                if (deg == 0) { next[i] += p; continue; }
                next[i] += p * (1f - lambda);
                float share = p * lambda / deg;
                for (int k = s; k < e; k++) next[nl[k]] += share;
            }
            System.Array.Copy(next, P, P.Length);
        }

        /// <summary>
        /// Empty what this operative can see right now (a budget of line-of-sight checks, highest probability and
        /// longest-unseen first). Far ground is cleared less surely than near ground.
        /// </summary>
/// <summary>A room was cleared: nobody is in it now.</summary>
        public void ClearRoom(CombatRoom r)
        {
            if (r == null) return;
            float now = Time.time;
            for (int k = 0; k < P.Length; k++)
                if (r.Contains(NavGraph.Pos[k])) { P[k] *= 0.02f; Seen[k] = now; }
        }

        public void ObserveFrom(Operative op, int budget, float range = 34f)
        {
            var eye = op.EyePosition;
            var fwd = op.LookForward.Flat().normalized;
            float now = Time.time;
            NavGraph.Query(eye, range, near);
            cands.Clear();
            foreach (int k in near)
            {
                var v = (NavGraph.Pos[k] - eye).Flat();
                float d = v.magnitude;
                if (d < 0.5f) { Seen[k] = now; P[k] *= 0.05f; continue; }
                if (Vector3.Angle(fwd, v) > 70f) continue;
                float pri = P[k] * 1000f + Mathf.Min(60f, now - Seen[k]) * 0.05f - d * 0.01f;
                cands.Add((k, pri));
            }
            cands.Sort((a, b) => b.pri.CompareTo(a.pri));
            int n = Mathf.Min(budget, cands.Count);
            for (int i = 0; i < n; i++)
            {
                int k = cands[i].node;
                var target = NavGraph.Pos[k] + Vector3.up * 1.0f;
                if (Physics.Linecast(eye, target, AILayers.World, QueryTriggerInteraction.Ignore)) continue;
                float d = Vector3.Distance(eye, target);
                P[k] *= Mathf.Lerp(0.03f, 0.35f, d / range);
                Seen[k] = now;
            }
        }

        /// <summary>Renormalize. If everything has been cleared, he moved somewhere nobody has looked: spread over unseen ground.</summary>
        public void Renormalize(float searchRadius = 40f)
        {
            if (!HasMass) return;
            float total = 0f, max = 0f;
            for (int i = 0; i < P.Length; i++) { total += P[i]; if (P[i] > max) max = P[i]; }
            if (total < 1e-4f)
            {
                NavGraph.Query(LastAnchor, searchRadius, near);
                float now = Time.time;
                total = 0f;
                // Somewhere nobody has looked, and more likely somewhere a person would hide
                foreach (int k in near) { float w = Mathf.Clamp01((now - Seen[k]) / 20f) * HidePrior(k); P[k] = w; total += w; }
                if (total <= 0f) { HasMass = false; return; }
            }
            Normalize(total);
        }

        void Normalize(float total)
        {
            float inv = 1f / total, max = 0f;
            for (int i = 0; i < P.Length; i++) { P[i] *= inv; if (P[i] > max) max = P[i]; }
            MaxP = max;
        }

        /// <summary>The assumption: open ground is a poor hiding place, corners and walls are good ones.</summary>
        static float HidePrior(int k) => NavGraph.Enclosure != null ? 0.35f + 1.3f * NavGraph.Enclosure[k] : 1f;

        public float MassNear(Vector3 p, float r)
        {
            if (!HasMass) return 0f;
            NavGraph.Query(p, r, near);
            float m = 0f;
            foreach (int k in near) if (Mathf.Abs(NavGraph.Pos[k].y - p.y) < 2.5f) m += P[k];
            return m;
        }

        /// <summary>How long ago the squad last saw this ground (seconds, capped).</summary>
        public float Unseen(int node) => Mathf.Min(120f, Time.time - Seen[node]);
    }
}
