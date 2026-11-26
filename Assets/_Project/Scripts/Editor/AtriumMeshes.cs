using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Accumulates geometry in world space with world-scaled UVs (textures keep a constant texel density
    /// on any box size and line up across neighbouring boxes). One accumulator per material is merged into
    /// a single mesh with generated lightmap UVs.
    /// </summary>
    public class MeshAccum
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector3> N = new();
        public readonly List<Vector2> U = new();
        public readonly List<int> T = new();

        public int Count => V.Count;

        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector3 uAxis, Vector3 vAxis, float tile)
        {
            int i = V.Count;
            foreach (var p in new[] { p0, p1, p2, p3 })
            {
                V.Add(p);
                N.Add(n);
                U.Add(new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis)) / tile);
            }
            bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) < 0f;
            if (!flip) { T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3); }
            else { T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2); }
        }

        /// <summary>An oriented box. tile is the meters covered by one texture repeat.</summary>
        public void Box(Vector3 center, Vector3 size, Quaternion rot, float tile, bool skipBottom = false)
        {
            var h = size * 0.5f;
            Vector3 X = rot * Vector3.right, Y = rot * Vector3.up, Z = rot * Vector3.forward;
            Vector3 P(float sx, float sy, float sz) => center + X * (sx * h.x) + Y * (sy * h.y) + Z * (sz * h.z);
            // +X / -X
            Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), X, Z, Y, tile);
            Quad(P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), -X, Z, Y, tile);
            // +Y / -Y
            Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), Y, X, Z, tile);
            if (!skipBottom) Quad(P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), -Y, X, Z, tile);
            // +Z / -Z
            Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Z, X, Y, tile);
            Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -Z, X, Y, tile);
        }

        public void BoxMinMax(Vector3 min, Vector3 max, float tile, bool skipBottom = false)
            => Box((min + max) * 0.5f, max - min, Quaternion.identity, tile, skipBottom);

        /// <summary>A double-sided quad (two faces) with 0..1 UVs, for leaves, glass, and light panels.</summary>
        public void Card(Vector3 center, Vector3 right, Vector3 up, bool doubleSided = true)
        {
            var n = Vector3.Cross(up, right).normalized;
            void Add(Vector3 nn, bool rev)
            {
                int i = V.Count;
                V.Add(center - right - up); V.Add(center + right - up); V.Add(center + right + up); V.Add(center - right + up);
                for (int k = 0; k < 4; k++) N.Add(nn);
                U.Add(new Vector2(0, 0)); U.Add(new Vector2(1, 0)); U.Add(new Vector2(1, 1)); U.Add(new Vector2(0, 1));
                bool flip = Vector3.Dot(Vector3.Cross(V[i + 1] - V[i], V[i + 2] - V[i]), nn) < 0f;
                if (flip ^ rev) { T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2); }
                else { T.Add(i); T.Add(i + 1); T.Add(i + 2); T.Add(i); T.Add(i + 2); T.Add(i + 3); }
            }
            Add(n, false);
            if (doubleSided) Add(-n, false);
        }

        /// <summary>A curved strip from base along a bent path: for fronds and blades (u across, v along).</summary>
        public void Strip(Vector3 basePos, Vector3 dir, Vector3 side, float length, float width, float droop, int segments, float twist = 0f)
        {
            int start = V.Count;
            dir.Normalize();
            side.Normalize();
            var up = Vector3.Cross(side, dir).normalized;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                // Arc: forward along dir and bending down by droop
                var center = basePos + dir * (length * t) - Vector3.up * (droop * t * t * length);
                var tangent = (dir - Vector3.up * (2f * droop * t)).normalized;
                var s = Quaternion.AngleAxis(twist * t, tangent) * side;
                var n = Vector3.Cross(tangent, s).normalized;
                float w = width * 0.5f;
                V.Add(center - s * w); V.Add(center + s * w);
                N.Add(n); N.Add(n);
                U.Add(new Vector2(0, t)); U.Add(new Vector2(1, t));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = start + i * 2;
                T.Add(a); T.Add(a + 2); T.Add(a + 1);
                T.Add(a + 1); T.Add(a + 2); T.Add(a + 3);
            }
        }

        /// <summary>A tapered, gently curved tube (palm trunk). UV u around, v along (in meters / tile).</summary>
        public void Tube(Vector3 basePos, Vector3 lean, float height, float r0, float r1, int sides, int rings, float tileV)
        {
            int start = V.Count;
            for (int j = 0; j <= rings; j++)
            {
                float t = j / (float)rings;
                var c = basePos + Vector3.up * (height * t) + lean * (t * t * height);
                float r = Mathf.Lerp(r0, r1, t) * (1f + 0.08f * Mathf.Sin(t * 40f));
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    V.Add(c + n * r);
                    N.Add(n);
                    U.Add(new Vector2(i / (float)sides, height * t / tileV));
                }
            }
            int row = sides + 1;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    int a = start + j * row + i;
                    T.Add(a); T.Add(a + row); T.Add(a + 1);
                    T.Add(a + 1); T.Add(a + row); T.Add(a + row + 1);
                }
        }

        /// <summary>A cylinder between two points (pipes, ducts, rods). UVs wrap in meters / tile.</summary>
        public void Cylinder(Vector3 a, Vector3 b, float radius, int sides, float tile, bool caps = true)
        {
            var axis = b - a;
            float len = axis.magnitude;
            if (len < 1e-4f) return;
            var dir = axis / len;
            var u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var w = Vector3.Cross(dir, u);
            int start = V.Count;
            float circ = 2f * Mathf.PI * radius;
            for (int i = 0; i <= sides; i++)
            {
                float t = i / (float)sides * Mathf.PI * 2f;
                var n = u * Mathf.Cos(t) + w * Mathf.Sin(t);
                V.Add(a + n * radius); N.Add(n); U.Add(new Vector2(i / (float)sides * circ / tile, 0f));
                V.Add(b + n * radius); N.Add(n); U.Add(new Vector2(i / (float)sides * circ / tile, len / tile));
            }
            for (int i = 0; i < sides; i++)
            {
                int k = start + i * 2;
                T.Add(k); T.Add(k + 2); T.Add(k + 1);
                T.Add(k + 1); T.Add(k + 2); T.Add(k + 3);
            }
            // Make the winding face outward (checked on the first quad)
            var p0 = V[start]; var p1 = V[start + 2]; var p2 = V[start + 1];
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), N[start]) < 0f)
                for (int i = T.Count - sides * 6; i < T.Count; i += 3) { int tmp = T[i + 1]; T[i + 1] = T[i + 2]; T[i + 2] = tmp; }
            if (!caps) return;
            foreach (var (c, nn) in new[] { (a, -dir), (b, dir) })
            {
                int ci = V.Count;
                V.Add(c); N.Add(nn); U.Add(Vector2.zero);
                for (int i = 0; i <= sides; i++)
                {
                    float t = i / (float)sides * Mathf.PI * 2f;
                    V.Add(c + (u * Mathf.Cos(t) + w * Mathf.Sin(t)) * radius); N.Add(nn); U.Add(Vector2.zero);
                }
                for (int i = 0; i < sides; i++)
                {
                    int x0 = ci + 1 + i, x1 = ci + 2 + i;
                    bool flip = Vector3.Dot(Vector3.Cross(V[x0] - V[ci], V[x1] - V[ci]), nn) < 0f;
                    if (flip) { T.Add(ci); T.Add(x1); T.Add(x0); } else { T.Add(ci); T.Add(x0); T.Add(x1); }
                }
            }
        }

        public Mesh Build(string name, bool lightmapUVs, bool recalcNormals = false)
        {
            var m = new Mesh { name = name };
            if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(V);
            m.SetNormals(N);
            m.SetUVs(0, U);
            m.SetTriangles(T, 0);
            if (recalcNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            if (lightmapUVs && V.Count > 0)
            {
                UnwrapParam.SetDefaults(out var p);
                p.packMargin = 0.01f;
                Unwrapping.GenerateSecondaryUVSet(m, p);
            }
            return m;
        }
    }

    public static class AtriumMeshes
    {
        /// <summary>A smooth desert heightfield (shared vertices, smooth normals).</summary>
        public static Mesh Terrain(Vector2 min, Vector2 max, int res, System.Func<float, float, float> height, float tile)
        {
            var acc = new MeshAccum();
            for (int j = 0; j <= res; j++)
                for (int i = 0; i <= res; i++)
                {
                    float x = Mathf.Lerp(min.x, max.x, i / (float)res);
                    float z = Mathf.Lerp(min.y, max.y, j / (float)res);
                    acc.V.Add(new Vector3(x, height(x, z), z));
                    acc.N.Add(Vector3.up);
                    acc.U.Add(new Vector2(x, z) / tile);
                }
            int row = res + 1;
            for (int j = 0; j < res; j++)
                for (int i = 0; i < res; i++)
                {
                    int a = j * row + i;
                    acc.T.Add(a); acc.T.Add(a + row); acc.T.Add(a + 1);
                    acc.T.Add(a + 1); acc.T.Add(a + row); acc.T.Add(a + row + 1);
                }
            return acc.Build("DesertTerrain", false, recalcNormals: true);
        }
    }
}
