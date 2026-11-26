using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.AI
{
    /// <summary>
    /// Plans a route that stays out of the threat's sight.
    /// Graph nodes: the ordinary shortest NavMesh path (subdivided every few meters, so a route always exists)
    /// plus NavMesh points sampled in a corridor around it. Legs link nearby nodes that can be walked more or
    /// less straight. Dijkstra then picks the cheapest route, where a leg costs its length times
    /// (1 + exposure x penalty): exposure is the fraction of the leg the threat can see at the mover's head
    /// height (lower when crouch-walking), plus squad danger spots. The result only detours when the detour
    /// is actually better hidden. Waypoints come back in order; the NavMesh walks between them.
    /// </summary>
    public static class ConcealedRoute
    {
        static readonly List<Vector3> nodes = new();
        static readonly List<int> backbone = new();
        const float ExposurePenalty = 6f;

        public static bool Plan(Operative op, Vector3 goal, Vector3 threatEye, bool crouched, List<Vector3> route)
        {
            route.Clear();
            var start = op.transform.position;
            if (!NavMesh.SamplePosition(goal, out var gh, 3f, NavMesh.AllAreas)) return false;
            goal = gh.position;
            var direct = new NavMeshPath();
            if (!NavMesh.CalculatePath(start, goal, NavMesh.AllAreas, direct) || direct.status != NavMeshPathStatus.PathComplete) return false;
            float eye = crouched ? 1.0f : 1.5f;

            nodes.Clear();
            backbone.Clear();
            nodes.Add(start);
            nodes.Add(goal);
            // Backbone: the direct path every 5 m (consecutive backbone nodes are always linked)
            var c = direct.corners;
            for (int i = 1; i < c.Length; i++)
            {
                float len = Vector3.Distance(c[i - 1], c[i]);
                int steps = Mathf.Max(1, Mathf.CeilToInt(len / 5f));
                for (int k = 1; k <= steps; k++)
                {
                    if (i == c.Length - 1 && k == steps) break;   // the goal is node 1
                    backbone.Add(nodes.Count);
                    nodes.Add(Vector3.Lerp(c[i - 1], c[i], k / (float)steps));
                }
            }
            // Corridor samples around the straight line
            float span = Vector3.Distance(start, goal);
            var axis = (goal - start).Flat();
            var perp = Vector3.Cross(Vector3.up, axis.sqrMagnitude > 0.01f ? axis.normalized : Vector3.forward);
            float width = span * 0.35f + 6f;
            // Around the real path (it bends around buildings): a few points near each backbone node
            int bb = backbone.Count;
            for (int i = 0; i < bb; i++)
                for (int k = 0; k < 3; k++)
                {
                    var probe = nodes[backbone[i]] + Random.insideUnitSphere.Flat() * 8f;
                    if (NavMesh.SamplePosition(probe, out var bh, 2f, NavMesh.AllAreas) && Mathf.Abs(bh.position.y - nodes[backbone[i]].y) < 2f) nodes.Add(bh.position);
                }
            int samples = Mathf.Clamp(Mathf.RoundToInt(span * 1.0f), 20, 48);
            int target = nodes.Count + samples;
            for (int i = 0; i < samples * 2 && nodes.Count < target; i++)
            {
                float u = Random.Range(-0.1f, 1.1f), v = Random.Range(-1f, 1f);
                var probe = Vector3.Lerp(start, goal, u) + perp * v * width;
                if (!NavMesh.SamplePosition(probe, out var h, 2f, NavMesh.AllAreas)) continue;
                if (Mathf.Abs(h.position.y - start.y) > 6f && Mathf.Abs(h.position.y - goal.y) > 6f) continue;
                nodes.Add(h.position);
            }

            int n = nodes.Count;
            // Probable-threat ground costs extra (pass wide around a corner he may be behind)
            var mass = new float[n];
            var map = op.Squad != null ? op.Squad.Map : null;
            if (map != null && map.HasMass && op.Config.useStandoff)
                for (int i = 0; i < n; i++) mass[i] = map.MassNear(nodes[i], 3f);
            spineIdx = new int[n];
            for (int i = 0; i < n; i++) spineIdx[i] = int.MinValue;
            spineIdx[0] = -1;
            spineIdx[1] = backbone.Count;
            for (int i = 0; i < backbone.Count; i++) spineIdx[backbone[i]] = i;
            var exposedNode = new bool[n];
            for (int i = 0; i < n; i++) exposedNode[i] = !CoverFinder.Blocked(threatEye, nodes[i] + Vector3.up * eye);
            var dist = new float[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[0] = 0f;
            const float maxLeg = 11f;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1; float best = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0 || u == 1) break;
                done[u] = true;
                for (int v = 0; v < n; v++)
                {
                    if (done[v] || v == u) continue;
                    float len = Vector3.Distance(nodes[u], nodes[v]);
                    bool spine = IsSpine(u, v);
                    if (!spine)
                    {
                        if (len > maxLeg) continue;
                        if (Mathf.Abs(nodes[u].y - nodes[v].y) > 1.2f) continue;
                        if (NavMesh.Raycast(nodes[u], nodes[v], out _, NavMesh.AllAreas)) continue;
                    }
                    float exposure = exposedNode[u] && exposedNode[v] ? 1f : Exposure(nodes[u], nodes[v], threatEye, eye);
                    float cost = len * (1f + exposure * ExposurePenalty) + Danger(op, nodes[v]) + mass[v] * 40f;
                    if (dist[u] + cost < dist[v]) { dist[v] = dist[u] + cost; prev[v] = u; }
                }
            }
            if (prev[1] < 0) return false;

            for (int at = 1; at > 0; at = prev[at]) route.Add(nodes[at]);
            route.Reverse();
            // Drop waypoints that are just the backbone in a straight run (fewer, cleaner turns)
            Simplify(route, start);
            return true;
        }

        static int[] spineIdx;

        /// <summary>start, then backbone 0..k, then goal: neighbors along that chain are always linked.</summary>
        static bool IsSpine(int u, int v)
        {
            int iu = spineIdx[u], iv = spineIdx[v];
            if (iu == int.MinValue || iv == int.MinValue) return false;
            return Mathf.Abs(iu - iv) == 1;
        }

        static void Simplify(List<Vector3> route, Vector3 start)
        {
            for (int i = route.Count - 2; i >= 0; i--)
            {
                var a = i == 0 ? start : route[i - 1];
                var b = route[i + 1];
                var m = route[i];
                // Nearly collinear and directly walkable: the middle point adds nothing
                var ab = (b - a).Flat();
                float off = ab.sqrMagnitude > 0.01f ? Vector3.Cross(ab.normalized, (m - a).Flat()).magnitude : 0f;
                if (off < 0.6f && !NavMesh.Raycast(a, b, out _, NavMesh.AllAreas)) route.RemoveAt(i);
            }
        }

        /// <summary>Fraction of the leg (sampled every meter or so) that the threat can see.</summary>
        public static float Exposure(Vector3 a, Vector3 b, Vector3 eye, float h)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / 1.5f), 2, 8);
            int seen = 0;
            for (int i = 1; i <= steps; i++)
                if (!CoverFinder.Blocked(eye, Vector3.Lerp(a, b, i / (float)steps) + Vector3.up * h)) seen++;
            return seen / (float)steps;
        }

        static float Danger(Operative op, Vector3 p) => op.Squad != null ? op.Squad.DangerAt(p) * 8f : 0f;
    }
}
