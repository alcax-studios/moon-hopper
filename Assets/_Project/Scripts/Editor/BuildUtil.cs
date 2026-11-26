using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Alcax.EditorTools
{
    /// <summary>Shared helpers for the level builder: folders, materials, primitives, mesh generation, and serialized fields.</summary>
    public static class BuildUtil
    {
        public const string Root = "Assets/_Project";

        // ---------- Folders and assets ----------

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static T SaveAsset<T>(T asset, string path) where T : Object
        {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static readonly Dictionary<string, Material> matCache = new();

        public static void ClearCache() => matCache.Clear();

        public static Material Lit(string name, Color color, float smoothness = 0.08f, Color? emission = null, float emissionIntensity = 0f)
        {
            if (matCache.TryGetValue(name, out var cached)) return cached;
            string path = $"{Root}/Art/Materials/M_{name}.mat";
            EnsureFolder($"{Root}/Art/Materials");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value * emissionIntensity);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Alcax.Core.Palette.Clear);
            }
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            matCache[name] = mat;
            return mat;
        }

        // ---------- Scene objects ----------

        public static Transform Node(string name, Transform parent, Vector3 localPos, Vector3? euler = null, Vector3? scale = null)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            t.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            t.localScale = scale ?? Vector3.one;
            return t;
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale,
            Vector3 euler, Material mat, bool collider = false, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static GameObject MeshObj(Mesh mesh, string name, Transform parent, Vector3 localPos, Vector3 scale,
            Vector3 euler, Material mat, bool shadows = true)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            if (!shadows) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        public static void MarkStatic(GameObject go)
        {
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            foreach (Transform c in go.transform) MarkStatic(c.gameObject);
        }

        // ---------- Serialized private fields ----------

        static SerializedProperty Prop(Object target, string field, out SerializedObject so)
        {
            so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) throw new System.Exception($"Field '{field}' not found on {target.GetType().Name}");
            return p;
        }

        public static void SetRef(Object target, string field, Object value)
        {
            var p = Prop(target, field, out var so);
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetString(Object target, string field, string value)
        {
            var p = Prop(target, field, out var so);
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetEnum(Object target, string field, int index)
        {
            var p = Prop(target, field, out var so);
            p.enumValueIndex = index;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetBool(Object target, string field, bool value)
        {
            var p = Prop(target, field, out var so);
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetVectorArray(Object target, string field, IList<Vector3> values)
        {
            var p = Prop(target, field, out var so);
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).vector3Value = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetFloat(Object target, string field, float value)
        {
            var p = Prop(target, field, out var so);
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetInt(Object target, string field, int value)
        {
            var p = Prop(target, field, out var so);
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetRefArray(Object target, string field, IList<Object> values)
        {
            var p = Prop(target, field, out var so);
            p.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- Mesh generation (flat-shaded low-poly) ----------

        class MeshBuilder
        {
            public readonly List<Vector3> V = new();
            public readonly List<int> T = new();

            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                T.Add(i); T.Add(i + 1); T.Add(i + 2);
            }

            public Mesh Build(string name, float uvScale = 0f)
            {
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V);
                m.SetTriangles(T, 0);
                var uv = new Vector2[V.Count];
                if (uvScale > 0f) for (int i = 0; i < V.Count; i++) uv[i] = new Vector2(V[i].x, V[i].z) * uvScale;
                m.uv = uv;
                m.RecalculateNormals();
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        /// <summary>A cone with its base at y=0 and its apex at y=1, radius 1.</summary>
        public static Mesh Cone(int sides)
        {
            var b = new MeshBuilder();
            Vector3 apex = Vector3.up;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                b.Tri(p0, apex, p1, (p0 + p1) * 0.5f + Vector3.up * 0.3f);
                b.Tri(Vector3.zero, p0, p1, Vector3.down);
            }
            return b.Build("Cone" + sides);
        }

        /// <summary>A lumpy, faceted rock: an icosahedron subdivided once, with jittered radii.</summary>
        public static Mesh Rock(int seed)
        {
            var rng = new System.Random(seed);
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            int[] faces =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var mid = new Dictionary<long, int>();
            int Mid(int a, int c)
            {
                long key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                verts.Add(((verts[a] + verts[c]) * 0.5f).normalized);
                mid[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var sub = new List<int>();
            for (int i = 0; i < faces.Length; i += 3)
            {
                int a = faces[i], bb = faces[i + 1], c = faces[i + 2];
                int ab = Mid(a, bb), bc = Mid(bb, c), ca = Mid(c, a);
                sub.AddRange(new[] { a, ab, ca, bb, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < verts.Count; i++)
            {
                float r = 0.78f + (float)rng.NextDouble() * 0.36f;
                var v = verts[i] * r;
                v.y = v.y > 0f ? v.y * 0.85f : v.y * 0.55f;
                verts[i] = v;
            }
            var b = new MeshBuilder();
            for (int i = 0; i < sub.Count; i += 3)
            {
                var a = verts[sub[i]]; var c1 = verts[sub[i + 1]]; var c2 = verts[sub[i + 2]];
                b.Tri(a, c1, c2, (a + c1 + c2) / 3f);
            }
            return b.Build(seed == 11 ? "RockA" : seed == 23 ? "RockB" : seed == 37 ? "RockC" : "Rock" + seed);
        }

        /// <summary>A triangular gable (width 1 along x, height 1, two-sided).</summary>
        public static Mesh Gable()
        {
            var b = new MeshBuilder();
            var p0 = new Vector3(-0.5f, 0f, 0f);
            var p1 = new Vector3(0.5f, 0f, 0f);
            var p2 = new Vector3(0f, 1f, 0f);
            b.Tri(p0, p1, p2, Vector3.forward);
            b.Tri(p0, p1, p2, Vector3.back);
            return b.Build("Gable");
        }

        /// <summary>A flat-shaded heightfield grid centered on the origin.</summary>
        public static Mesh Ground(float size, int res, System.Func<float, float, float> height, float uvScale)
        {
            var b = new MeshBuilder();
            float step = size / res;
            float half = size * 0.5f;
            Vector3 P(int i, int j)
            {
                float x = -half + i * step, z = -half + j * step;
                return new Vector3(x, height(x, z), z);
            }
            for (int i = 0; i < res; i++)
                for (int j = 0; j < res; j++)
                {
                    var a = P(i, j); var c = P(i + 1, j + 1);
                    var bb = P(i, j + 1); var d = P(i + 1, j);
                    if (((i + j) & 1) == 0)
                    {
                        b.Tri(a, bb, c, Vector3.up);
                        b.Tri(a, c, d, Vector3.up);
                    }
                    else
                    {
                        b.Tri(a, bb, d, Vector3.up);
                        b.Tri(bb, c, d, Vector3.up);
                    }
                }
            return b.Build("ForestGround", uvScale);
        }

        // ---------- Textures ----------

        public static Texture2D NoiseTexture(string name, int size, Color baseColor, Color blotch, float variation, int seed)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            float ox = seed * 13.1f, oy = seed * 7.7f;
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    // Tileable noise: sample a torus-wrapped domain.
                    float n = TileNoise(u, v, 4f, ox, oy) * 0.6f + TileNoise(u, v, 12f, ox + 3f, oy + 5f) * 0.4f;
                    float m = TileNoise(u, v, 3f, ox + 20f, oy + 9f);
                    var c = baseColor * (1f - variation + n * variation * 2f);
                    c = Color.Lerp(c, blotch, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.75f, m)) * 0.7f);
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static float TileNoise(float u, float v, float freq, float ox, float oy)
        {
            float a = Mathf.PerlinNoise(ox + u * freq, oy + v * freq);
            float b = Mathf.PerlinNoise(ox + (u - 1f) * freq, oy + v * freq);
            float c = Mathf.PerlinNoise(ox + u * freq, oy + (v - 1f) * freq);
            float d = Mathf.PerlinNoise(ox + (u - 1f) * freq, oy + (v - 1f) * freq);
            float x1 = Mathf.Lerp(a, b, u), x2 = Mathf.Lerp(c, d, u);
            return Mathf.Lerp(x1, x2, v);
        }

        public static Texture2D SoftCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "T_SoftCircle", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
