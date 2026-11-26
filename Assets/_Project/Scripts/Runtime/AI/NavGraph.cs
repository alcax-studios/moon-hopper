using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.AI
{
    /// <summary>
    /// A point graph over the walkable NavMesh (about one node per 1.5 m, on every floor), built once per scene from
    /// the NavMesh triangulation. Neighbors are linked only if the NavMesh connects them directly (so stairs link,
    /// and floors don't link through ceilings). The occupancy maps and the attention model run on it.
    /// </summary>
public struct SpaceInfo
    {
        public const float MaxRay = 40f;
        public bool Indoor;
        /// <summary>Average free distance at eye height (m): roughly the size of the space.</summary>
        public float MeanFree;
        /// <summary>Longest sightline from here (m).</summary>
        public float MaxSight;
        /// <summary>Narrowest wall-to-wall width (m): small in corridors and doorways.</summary>
        public float Width;
        /// <summary>Fraction of directions with more than 6 m of free space.</summary>
        public float OpenFraction;
        public static SpaceInfo Open => new SpaceInfo { Indoor = false, MeanFree = MaxRay, MaxSight = MaxRay, Width = MaxRay * 2f, OpenFraction = 1f };
        /// <summary>How big the fight space is (m): indoors the room's size, outside effectively unlimited.</summary>
        public float Size => Indoor ? MeanFree : Mathf.Max(MeanFree * 2f, 25f);
        public string Label => Indoor ? (Width < 3f ? "corridor" : MeanFree < 6f ? "small room" : "room") : MeanFree < 10f ? "tight outside" : "open";
    }

    public static class NavGraph
    {
        public static Vector3[] Pos;
        /// <summary>Neighbors in CSR form: node i's neighbors are NList[NStart[i] .. NStart[i+1]).</summary>
        public static int[] NStart, NList;
        /// <summary>
        /// How enclosed each node is (0 open ground, 1 a tight corner): the fraction of 8 short rays at crouch height
        /// that hit something within 2.5 m. People hide in corners and beside cover, so this is the prior for "where
        /// would someone be" when the squad has to guess.
        /// </summary>
        public static float[] Enclosure;
        /// <summary>
        /// The spatial fingerprint of each node: is there a roof, how far you can see on average and at most (16 rays at eye
        /// height), and how wide the narrowest axis is. The AI's stand-in for "looking around a space": a room, a corridor,
        /// a courtyard, open ground.
        /// </summary>
        public static SpaceInfo[] Space;
        public static int Count => Pos != null ? Pos.Length : 0;
        public static float BuildMs { get; private set; }

        const float Spacing = 1.6f, Merge = 1.2f, Link = 2.6f, Cell = 3f;
        static readonly Dictionary<long, List<int>> hash = new();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            Pos = null;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => Pos = null;   // a new map has a new NavMesh
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        static int C(float v) => Mathf.FloorToInt(v / Cell);

        public static void EnsureBuilt()
        {
            if (Pos != null) return;
            Build();
        }

        static void Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            hash.Clear();
            var tri = NavMesh.CalculateTriangulation();
            var pts = new List<Vector3>();
            for (int t = 0; t + 2 < tri.indices.Length; t += 3)
            {
                var a = tri.vertices[tri.indices[t]];
                var b = tri.vertices[tri.indices[t + 1]];
                var c = tri.vertices[tri.indices[t + 2]];
                float longest = Mathf.Max((b - a).magnitude, Mathf.Max((c - a).magnitude, (c - b).magnitude));
                int n = Mathf.Max(1, Mathf.CeilToInt(longest / Spacing));
                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n - i; j++)
                    {
                        var p = a + (b - a) * (i / (float)n) + (c - a) * (j / (float)n);
                        TryAdd(pts, p);
                    }
                TryAdd(pts, (a + b + c) / 3f);
            }
            Pos = pts.ToArray();

            // Links: near, and directly connected on the NavMesh
            var start = new int[Pos.Length + 1];
            var list = new List<int>(Pos.Length * 8);
            var near = new List<int>();
            for (int i = 0; i < Pos.Length; i++)
            {
                start[i] = list.Count;
                Query(Pos[i], Link, near);
                foreach (int j in near)
                {
                    if (j == i) continue;
                    var d = Pos[j] - Pos[i];
                    if (Mathf.Abs(d.y) > 1.4f) continue;
                    if (NavMesh.Raycast(Pos[i], Pos[j], out _, NavMesh.AllAreas)) continue;
                    list.Add(j);
                }
            }
            start[Pos.Length] = list.Count;
            NStart = start;
            NList = list.ToArray();
            Enclosure = new float[Pos.Length];
            for (int i = 0; i < Pos.Length; i++)
            {
                int hits = 0;
                var o = Pos[i] + Vector3.up * 1.0f;
                for (int r = 0; r < 8; r++)
                    if (Physics.Raycast(o, Quaternion.Euler(0f, r * 45f, 0f) * Vector3.forward, 2.5f, AILayers.World, QueryTriggerInteraction.Ignore)) hits++;
                Enclosure[i] = hits / 8f;
            }
            Space = new SpaceInfo[Pos.Length];
            var dists = new float[16];
            for (int i = 0; i < Pos.Length; i++)
            {
                var eye = Pos[i] + Vector3.up * 1.5f;
                var info = new SpaceInfo { Indoor = Physics.Raycast(Pos[i] + Vector3.up * 1.0f, Vector3.up, 8f, AILayers.World, QueryTriggerInteraction.Ignore) || RoomGraph.RoomAt(Pos[i]) != null };
                float sum = 0f, max = 0f, open = 0f;
                for (int r = 0; r < 16; r++)
                {
                    var dir = Quaternion.Euler(0f, r * 22.5f, 0f) * Vector3.forward;
                    float d = Physics.Raycast(eye, dir, out var hit, SpaceInfo.MaxRay, AILayers.World, QueryTriggerInteraction.Ignore) ? hit.distance : SpaceInfo.MaxRay;
                    dists[r] = d; sum += d; max = Mathf.Max(max, d);
                    if (d > 6f) open += 1f;
                }
                float width = float.MaxValue;
                for (int r = 0; r < 8; r++) width = Mathf.Min(width, dists[r] + dists[r + 8]);
                info.MeanFree = sum / 16f;
                info.MaxSight = max;
                info.Width = width;
                info.OpenFraction = open / 16f;
                Space[i] = info;
            }
            BuildMs = (float)sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[NavGraph] {Pos.Length} nodes, {NList.Length} links in {BuildMs:0} ms");
        }

        static void TryAdd(List<Vector3> pts, Vector3 p)
        {
            int cx = C(p.x), cz = C(p.z);
            for (int x = cx - 1; x <= cx + 1; x++)
                for (int z = cz - 1; z <= cz + 1; z++)
                    if (hash.TryGetValue(Key(x, z), out var l))
                        foreach (int k in l)
                        {
                            var q = pts[k];
                            if (Mathf.Abs(q.y - p.y) < 0.7f && (new Vector2(q.x - p.x, q.z - p.z)).sqrMagnitude < Merge * Merge) return;
                        }
            int id = pts.Count;
            pts.Add(p);
            var key = Key(cx, cz);
            if (!hash.TryGetValue(key, out var cell)) { cell = new List<int>(8); hash[key] = cell; }
            cell.Add(id);
        }

        /// <summary>All nodes within r (horizontal distance, any height) of p.</summary>
        public static void Query(Vector3 p, float r, List<int> result)
        {
            result.Clear();
            int x0 = C(p.x - r), x1 = C(p.x + r), z0 = C(p.z - r), z1 = C(p.z + r);
            float r2 = r * r;
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    if (hash.TryGetValue(Key(x, z), out var l))
                        foreach (int k in l)
                        {
                            var q = Pos[k];
                            float dx = q.x - p.x, dz = q.z - p.z;
                            if (dx * dx + dz * dz <= r2) result.Add(k);
                        }
        }

        static readonly List<int> tmp = new();

/// <summary>The space at a point (from the nearest node); open ground if there is no graph or no node nearby.</summary>
        public static SpaceInfo SpaceAt(Vector3 p)
        {
            EnsureBuilt();
            if (Space == null) return SpaceInfo.Open;
            int n = Nearest(p, 3f);
            return n >= 0 ? Space[n] : SpaceInfo.Open;
        }

        /// <summary>The nearest node (preferring the same floor), or -1.</summary>
        public static int Nearest(Vector3 p, float maxR = 4f)
        {
            Query(p, maxR, tmp);
            int best = -1; float bd = float.MaxValue;
            foreach (int k in tmp)
            {
                var d = Pos[k] - p;
                float s = d.x * d.x + d.z * d.z + d.y * d.y * 6f;
                if (s < bd) { bd = s; best = k; }
            }
            return best;
        }
    }
}
