using System.Collections.Generic;
using System.IO;
using Alcax.Audio;
using Alcax.Core;
using Alcax.FX;
using Alcax.Player;
using Alcax.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Builds Kelxor.unity, the exterior slice of pipeline test 2 (docs/tech/pipeline-test-plan.md): generated
    /// terrain (TerrainSynth), four scanned ground layers painted by rules with height blending, scanned rocks placed
    /// by rules, the sunset light and sky, fog, post, wind, and the player. Menu: Alcax > Build Kelxor Exterior.
    /// The scene is generated: change this builder, not the scene (ADR 0004 until its art pass).
    /// </summary>
    public static class KelxorBuilder
    {
        public const string ScenePath = Root + "/Scenes/Kelxor.unity";
        const string Dir = Root + "/Art/Terrain/Kelxor";
        const string RockDir = Root + "/Art/Models/Environment";

        // Ground layers: scan id, real-world tile size in meters
        static readonly (string id, float tile, string target)[] Layers =
        {
            ("sand_02", 2.4f, "#B8875E"),            // 0 fine sand (default)
            ("aerial_beach_01", 9f, "#BE8A5C"),      // 1 wind-rippled dune sand
            ("sandy_gravel_02", 2.6f, "#9C7657"),    // 2 desert gravel flats
            ("rock_face", 3.2f, "#8B6A52"),          // 3 rock
        };

        [MenuItem("Alcax/Build Kelxor Exterior")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[KelxorBuilder] Exit Play mode first."); return; }
            var t0 = System.DateTime.Now;
            var synth = new TerrainSynth();
            var pad = new Rect(18f, 104f, 46f, 46f);
            synth.flats.Add(new TerrainSynth.Flat { rect = pad, y = 6f, falloff = 14f });
            synth.flats.Add(new TerrainSynth.Flat { rect = new Rect(-10f, pad.center.y - 4f, pad.x + 10f, 8f), y = 6f, falloff = 8f });   // road
            synth.Generate();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var terrain = BuildTerrain(synth, Dir + "/TD_Kelxor.asset", true);
            var root = new GameObject("Kelxor").transform;
            ScatterRocks(synth, terrain, root, 77, 1f);
            ScatterPlants(synth, terrain, root, 78, 1f, new Vector2(128f, 128f), 400f);
            BuildPad(pad, 6f, root);
            BuildLighting();

            var start = new Vector3(pad.center.x + 6f, 6.1f, pad.yMax - 4f);
            var (player, head) = PlayerRig.BuildPlayer(start, 105f, out var cam);
            var titleCam = PlayerRig.BuildCameras(cam, head, start + new Vector3(0f, 4f, 0f), new Vector3(0f, 5f, -8f), Vector3.zero, 55f);
            cam.farClipPlane = 2000f;
            foreach (var vc in Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>()) vc.Lens.FarClipPlane = 2000f;
            BuildManagers(player, titleCam);

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[KelxorBuilder] Built {ScenePath} in {(System.DateTime.Now - t0).TotalSeconds:F1} s");
        }

        // ---------- Terrain ----------

        /// <summary>A Unity Terrain from a generated TerrainSynth, placed at its world origin, with the four
        /// color-matched ground layers painted by rules and our terrain shader. giStatic: contributes to baked GI
        /// (off where a scene bakes its interior and the desert is lit live by the sun).</summary>
        internal static Terrain BuildTerrain(TerrainSynth s, string dataPath, bool giStatic)
        {
            EnsureFolder(Dir);
            AssetDatabase.DeleteAsset(dataPath);
            float hi = float.MinValue;
            for (int z = 0; z < s.res; z++) for (int x = 0; x < s.res; x++) hi = Mathf.Max(hi, s.height[x, z]);
            float range = Mathf.Max(10f, hi - s.TerrainY + 1f);
            var td = new TerrainData { heightmapResolution = s.res };
            td.size = new Vector3(s.size, range, s.size);
            var h = new float[s.res, s.res];
            for (int z = 0; z < s.res; z++)
                for (int x = 0; x < s.res; x++)
                    h[z, x] = (s.height[x, z] - s.TerrainY) / range;   // TerrainData is [row = z, col = x]
            td.SetHeights(0, 0, h);
            // Save it as an asset first: only an asset TerrainData keeps its alphamap (paint) textures
            AssetDatabase.CreateAsset(td, dataPath);

            var layers = new TerrainLayer[Layers.Length];
            for (int i = 0; i < Layers.Length; i++) layers[i] = Layer(Layers[i].id, Layers[i].tile);

            td.terrainLayers = layers;
            td.alphamapResolution = s.size > 400f ? 1024 : 512;
            td.baseMapResolution = 1024;
            Paint(s, td);
            EditorUtility.SetDirty(td);
            AssetDatabase.SaveAssets();

            var go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.position = new Vector3(s.origin.x, s.TerrainY, s.origin.y);
            // Collision against a lighter copy of the heights: the physics heightfield is built at load, and at
            // 2049 x 2049 it alone took about 2 s. 1025 x 1025 (0.8 m on the big desert) is plenty for walking and bullets.
            if (s.res > 1025)
            {
                int cr = 1025;
                var cd = new TerrainData { heightmapResolution = cr };
                cd.size = td.size;
                var ch = new float[cr, cr];
                float step = (s.res - 1f) / (cr - 1f);
                for (int z = 0; z < cr; z++)
                    for (int x = 0; x < cr; x++)
                        ch[z, x] = h[Mathf.Min(s.res - 1, Mathf.RoundToInt(z * step)), Mathf.Min(s.res - 1, Mathf.RoundToInt(x * step))];
                cd.SetHeights(0, 0, ch);
                var cpath = dataPath.Replace(".asset", "_Collision.asset");
                AssetDatabase.DeleteAsset(cpath);
                AssetDatabase.CreateAsset(cd, cpath);
                go.GetComponent<TerrainCollider>().terrainData = cd;
            }
            var t = go.GetComponent<Terrain>();
            // Our terrain shader: height blend, triplanar cliffs, anti-tiling, color-matched layers
            var mat = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/M_KelxorTerrain.mat");
            var sh = Shader.Find("Alcax/Kelxor Terrain");
            if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, Dir + "/M_KelxorTerrain.mat"); }
            mat.shader = sh;
            for (int i = 0; i < Layers.Length; i++) mat.SetColor("_Tint" + i, LayerTint(Layers[i].id, Layers[i].target));
            mat.SetVector("_Tile", new Vector4(Layers[0].tile, Layers[1].tile, Layers[2].tile, Layers[3].tile));
            EditorUtility.SetDirty(mat);
            t.materialTemplate = mat;
            t.heightmapPixelError = 3f;
            t.basemapDistance = 4000f;    // always our full shader (no low-res base map)
            t.drawInstanced = false;
            t.shadowCastingMode = ShadowCastingMode.On;
            var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
            if (giStatic) flags |= StaticEditorFlags.ContributeGI;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            return t;
        }

        /// <summary>A terrain layer from a scan set: color, normal, and a mask map (URP: R metallic, G occlusion,
        /// B height for height blending, A smoothness) built from the scan's ARM and height maps.</summary>
        static TerrainLayer Layer(string id, float tile)
        {
            string folder = $"{Dir}/{id}";
            string Tex(string map) => $"{folder}/T_{id}_{map}.jpg";
            SetImport(Tex("BaseColor"), TextureImporterType.Default, true);
            SetImport(Tex("Normal"), TextureImporterType.NormalMap, false);
            var maskPath = $"{folder}/T_{id}_TerrainMask.png";
            if (!File.Exists(maskPath))
            {
                var arm = Load(Tex("ARM"));
                var hgt = Load(Tex("Height"));
                int w = arm.width, hh = arm.height;
                var a = arm.GetPixels();
                var hp = hgt.width == w ? hgt.GetPixels() : Resize(hgt, w, hh);
                var outPx = new Color[a.Length];
                for (int i = 0; i < a.Length; i++) outPx[i] = new Color(a[i].b, a[i].r, hp[i].r, 1f - a[i].g);
                var m = new Texture2D(w, hh, TextureFormat.RGBA32, true, true);
                m.SetPixels(outPx); m.Apply();
                File.WriteAllBytes(maskPath, m.EncodeToPNG());
                Object.DestroyImmediate(m); Object.DestroyImmediate(arm); Object.DestroyImmediate(hgt);
                AssetDatabase.ImportAsset(maskPath);
            }
            SetImport(maskPath, TextureImporterType.Default, false);
            var layerPath = $"{folder}/TL_{id}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (layer == null) { layer = new TerrainLayer(); AssetDatabase.CreateAsset(layer, layerPath); }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex("BaseColor"));
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex("Normal"));
            layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            layer.tileSize = new Vector2(tile, tile);
            layer.normalScale = 1f;
            layer.metallic = 0f;
            layer.smoothness = 1f;
            layer.maskMapRemapMin = Vector4.zero;
            layer.maskMapRemapMax = Vector4.one;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>A per-layer multiplier that brings the scan's average color to the target (in linear space), so the
        /// layers share one palette instead of jumping between photo sources.</summary>
        static Color LayerTint(string id, string targetHex)
        {
            var tex = Load($"{Dir}/{id}/T_{id}_BaseColor.jpg");
            double r = 0, g = 0, b = 0; int n = 0;
            for (int y = 0; y < tex.height; y += 16)
                for (int x = 0; x < tex.width; x += 16)
                {
                    var c = tex.GetPixel(x, y).linear;
                    r += c.r; g += c.g; b += c.b; n++;
                }
            Object.DestroyImmediate(tex);
            ColorUtility.TryParseHtmlString(targetHex, out var target);
            var tl = target.linear;
            return new Color(tl.r / (float)(r / n), tl.g / (float)(g / n), tl.b / (float)(b / n), 1f).gamma;
        }

        static Texture2D Load(string path)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        static Color[] Resize(Texture2D src, int w, int h)
        {
            var o = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    o[y * w + x] = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
            return o;
        }

        static void SetImport(string path, TextureImporterType type, bool srgb)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
            if (ti.textureType == type && ti.sRGBTexture == srgb && ti.maxTextureSize == 2048) return;
            ti.textureType = type;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = 2048;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        /// <summary>Paint the layers by rules: rock on steep or shelf ground, rippled sand on the dunes, gravel on the
        /// open flats and around rock bases, fine sand in deposits and everywhere else.</summary>
        static void Paint(TerrainSynth s, TerrainData td)
        {
            int ar = td.alphamapResolution;
            var maps = new float[ar, ar, Layers.Length];
            float step = (s.res - 1f) / (ar - 1f);
            for (int az = 0; az < ar; az++)
                for (int ax = 0; ax < ar; ax++)
                {
                    int x = Mathf.RoundToInt(ax * step), z = Mathf.RoundToInt(az * step);
                    float slope = s.SlopeDeg(x, z);
                    float wx = s.WorldX(x), wz = s.WorldZ(z);
                    float n = Mathf.PerlinNoise(wx * 0.08f + 3f, wz * 0.08f + 7f);
                    float n2 = Mathf.PerlinNoise(wx * 0.02f + 31f, wz * 0.02f + 2f);
                    float rockW = Mathf.Max(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(34f, 46f, slope + n * 6f)), s.rock[x, z] > 0.5f && slope > 18f ? 0.8f : 0f);
                    float dune = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, s.sand[x, z])) * Mathf.Clamp01(1f - slope / 34f);
                    float dep = Mathf.Clamp01(s.deposit[x, z] * 0.6f);
                    float gravel = Mathf.Clamp01((1f - s.sand[x, z]) * (0.45f + n2) - dep) * (1f - rockW);
                    float fine = 0.35f + dep + n * 0.2f;
                    float[] w = { fine, dune, gravel, rockW * 1.6f };
                    float sum = 0f;
                    for (int i = 0; i < w.Length; i++) { w[i] = Mathf.Max(0f, w[i]); sum += w[i]; }
                    for (int i = 0; i < w.Length; i++) maps[az, ax, i] = w[i] / sum;
                }
            td.SetAlphamaps(0, 0, maps);
        }

        // ---------- Rocks ----------

        static GameObject Rock(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{RockDir}/{name}/{name}.fbx");

        /// <summary>Rules-based rock placement: cliff pieces along steep shelf edges, big boulders at their feet
        /// (talus), small boulders and clusters scattered in clumps over the flats; nothing on the pad, the road, or
        /// dune crests. Each rock is sunk into the ground and leans with the slope.</summary>
        internal static void ScatterRocks(TerrainSynth s, Terrain t, Transform root, int seed, float density)
        {
            var parent = new GameObject("Rocks").transform;
            parent.SetParent(root, false);
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var big = new[] { Rock("SM_Rock_Boulder03"), Rock("SM_Rock_Boulder04"), Rock("SM_Rock_Boulder02") };
            var small = new[] { Rock("SM_Rock_Boulder05"), Rock("SM_Rock_Boulder06"), Rock("SM_Rock_Cluster01") };
            int placed = 0;
            bool Free(float wx, float wz) => !s.InFlat(wx, wz, 6f);
            int Gx(float wx) => Mathf.Clamp(Mathf.RoundToInt((wx - s.origin.x) / s.Cell), 0, s.res - 1);
            int Gz(float wz) => Mathf.Clamp(Mathf.RoundToInt((wz - s.origin.y) / s.Cell), 0, s.res - 1);

            void Place(GameObject prefab, float wx, float wz, float scale, float sink, float lean)
            {
                if (prefab == null) return;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                float y = t.SampleHeight(new Vector3(wx, 0f, wz)) + t.transform.position.y;   // SampleHeight is relative to the terrain
                var n = t.terrainData.GetInterpolatedNormal((wx - s.origin.x) / s.size, (wz - s.origin.y) / s.size);
                var up = Vector3.Slerp(Vector3.up, n, lean);
                go.transform.SetPositionAndRotation(new Vector3(wx, y - sink * scale, wz),
                    Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, R() * 360f, 0f));
                go.transform.localScale = Vector3.one * scale;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    if (mf.name.EndsWith("_LOD0")) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                foreach (Transform c in go.GetComponentsInChildren<Transform>()) c.gameObject.isStatic = true;
                placed++;
            }

            // Cliffs along steep shelf edges
            for (float wz = s.origin.y + 6f; wz < s.origin.y + s.size - 6f; wz += 13f / Mathf.Sqrt(density))
                for (float wx = s.origin.x + 6f; wx < s.origin.x + s.size - 6f; wx += 13f / Mathf.Sqrt(density))
                {
                    float jx = wx + (R() - 0.5f) * 9f, jz = wz + (R() - 0.5f) * 9f;
                    int x = Gx(jx), z = Gz(jz);
                    // The cliff scan is a one-sided rock face; it needs hand placement against a slope, so the
                    // shelf edges get big boulders instead
                    if (s.rock[x, z] > 0.2f && s.SlopeDeg(x, z) > 30f && Free(jx, jz) && R() < 0.6f) Place(big[rng.Next(big.Length)], jx, jz, 1.2f + R() * 1.3f, 0.3f, 0.4f);
                }
            // Talus boulders at the foot of the rock, small clumps on the flats
            for (float wz = s.origin.y + 3f; wz < s.origin.y + s.size - 3f; wz += 4.5f / Mathf.Sqrt(density))
                for (float wx = s.origin.x + 3f; wx < s.origin.x + s.size - 3f; wx += 4.5f / Mathf.Sqrt(density))
                {
                    float jx = wx + (R() - 0.5f) * 4f, jz = wz + (R() - 0.5f) * 4f;
                    if (!Free(jx, jz)) continue;
                    int x = Gx(jx), z = Gz(jz);
                    float slope = s.SlopeDeg(x, z);
                    float nearRock = NearRock(s, x, z, 14);
                    float clump = Mathf.PerlinNoise(jx * 0.05f + 11f, jz * 0.05f + 3f);
                    if (nearRock > 0.15f && slope < 32f && s.rock[x, z] < 0.4f && R() < 0.35f * nearRock)
                        Place(big[rng.Next(big.Length)], jx, jz, 0.5f + R() * 0.9f, 0.18f, 0.6f);
                    else if (s.sand[x, z] < 0.45f && slope < 25f && clump > 0.62f && R() < 0.35f)
                        Place(small[rng.Next(small.Length)], jx, jz, 0.6f + R() * 1.0f, 0.2f, 0.85f);
                }
            Debug.Log($"[KelxorBuilder] {placed} rocks placed");
        }

        static GameObject Plant(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Art/Models/Foliage/{name}/{name}.fbx");

        /// <summary>Rules-based plants: grass in clumps on sand and gravel flats, scrub around rock bases and where
        /// eroded material settled, agaves sparse on the gravel; nothing on dune crests, steep ground, or flat zones.
        /// No colliders (bodies push them aside through the wind shader); each gets an LOD group so small plants
        /// stop drawing at distance. maxDistance: only within this range of center (plants are about the near view).</summary>
        internal static void ScatterPlants(TerrainSynth s, Terrain t, Transform root, int seed, float density, Vector2 center, float maxDistance)
        {
            var parent = new GameObject("Plants").transform;
            parent.SetParent(root, false);
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var grass = new[] { Plant("SM_Plant_Grass01"), Plant("SM_Plant_Grass02"), Plant("SM_Plant_Grass03") };
            var scrub = new[] { Plant("SM_Plant_Scrub01"), Plant("SM_Plant_Scrub02"), Plant("SM_Plant_Scrub03") };
            var agave = new[] { Plant("SM_Plant_Succulent01"), Plant("SM_Plant_Succulent02"), Plant("SM_Plant_Succulent03") };
            if (grass[0] == null) { Debug.LogWarning("[KelxorBuilder] no plant models yet"); return; }
            int placed = 0;

            void Place(GameObject prefab, float wx, float wz, float scale, float cull)
            {
                if (prefab == null) return;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                float y = t.SampleHeight(new Vector3(wx, 0f, wz)) + t.transform.position.y;
                var n = t.terrainData.GetInterpolatedNormal((wx - s.origin.x) / s.size, (wz - s.origin.y) / s.size);
                go.transform.SetPositionAndRotation(new Vector3(wx, y - 0.02f, wz),
                    Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, n, 0.5f)) * Quaternion.Euler(0f, R() * 360f, 0f));
                go.transform.localScale = Vector3.one * scale;
                if (go.GetComponentInChildren<LODGroup>() == null)
                {
                    var lg = go.AddComponent<LODGroup>();
                    lg.SetLODs(new[] { new LOD(cull, go.GetComponentsInChildren<Renderer>()) });
                    lg.RecalculateBounds();
                }
                else
                {
                    var lg = go.GetComponentInChildren<LODGroup>();
                    var lods = lg.GetLODs();
                    lods[lods.Length - 1].screenRelativeTransitionHeight = cull;
                    lg.SetLODs(lods);
                }
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                placed++;
            }

            float step = 2.2f / Mathf.Sqrt(density);
            for (float wz = s.origin.y + 2f; wz < s.origin.y + s.size - 2f; wz += step)
                for (float wx = s.origin.x + 2f; wx < s.origin.x + s.size - 2f; wx += step)
                {
                    float jx = wx + (R() - 0.5f) * step, jz = wz + (R() - 0.5f) * step;
                    if (Vector2.Distance(new Vector2(jx, jz), center) > maxDistance || s.InFlat(jx, jz, 3f)) continue;
                    int x = Mathf.Clamp(Mathf.RoundToInt((jx - s.origin.x) / s.Cell), 0, s.res - 1);
                    int z = Mathf.Clamp(Mathf.RoundToInt((jz - s.origin.y) / s.Cell), 0, s.res - 1);
                    float slope = s.SlopeDeg(x, z);
                    if (slope > 22f || s.rock[x, z] > 0.3f) continue;
                    float clump = Mathf.PerlinNoise(jx * 0.08f + 5f, jz * 0.08f + 9f);
                    float patch = Mathf.PerlinNoise(jx * 0.02f + 40f, jz * 0.02f + 13f);
                    float near = NearRock(s, x, z, 18);
                    float dep = Mathf.Clamp01(s.deposit[x, z] * 0.6f);
                    float dune = s.sand[x, z];
                    float roll = R();
                    if (dune > 0.55f && slope > 8f) continue;                       // live dunes stay bare
                    if (roll < (0.05f + near * 0.25f + dep * 0.3f) * patch)
                        Place(scrub[rng.Next(scrub.Length)], jx, jz, 0.7f + R() * 0.7f, 0.02f);
                    else if (roll < 0.36f && clump > 0.55f && dune < 0.6f)
                        for (int g = 0; g < 3; g++)
                            Place(grass[rng.Next(grass.Length)], jx + (R() - 0.5f) * 1.2f, jz + (R() - 0.5f) * 1.2f, 0.8f + R() * 0.6f, 0.03f);
                    else if (roll > 0.985f && dune < 0.4f)
                        Place(agave[rng.Next(agave.Length)], jx, jz, 0.7f + R() * 0.8f, 0.015f);
                }
            Debug.Log($"[KelxorBuilder] {placed} plants placed");
        }

        static float NearRock(TerrainSynth s, int x, int z, int r)
        {
            float best = 0f;
            for (int dz = -r; dz <= r; dz += 4)
                for (int dx = -r; dx <= r; dx += 4)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= s.res || nz >= s.res) continue;
                    if (s.rock[nx, nz] > 0.4f) best = Mathf.Max(best, 1f - Mathf.Sqrt(dx * dx + dz * dz) / r);
                }
            return best;
        }



        // ---------- Pad (greybox until the spaceport kit) ----------

        static void BuildPad(Rect padRect, float padY, Transform root)
        {
            var mat = Lit("KelxorPad", new Color(0.36f, 0.33f, 0.3f), 0.12f);   // dusty concrete (greybox)
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "LandingPad";
            pad.transform.SetParent(root, false);
            pad.transform.position = new Vector3(padRect.center.x, padY - 0.25f, padRect.center.y);
            pad.transform.localScale = new Vector3(padRect.width - 6f, 0.6f, padRect.height - 6f);
            pad.GetComponent<Renderer>().sharedMaterial = mat;
            pad.isStatic = true;
        }

        // ---------- Light, sky, post ----------

        static void BuildLighting()
        {
            var toSun = new Vector3(-0.62f, 0.24f, 0.74f).normalized;   // a low sun: Kelxor's permanent sunset
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.LookRotation(-toSun);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.StSun;
            sun.intensity = 2.6f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.92f;

            string skyPath = $"{Root}/Art/Materials/M_Kelxor_Sky.mat";
            var atriumSky = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Art/Materials/Atrium/M_Sky.mat");
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null) { sky = atriumSky != null ? new Material(atriumSky) : new Material(Shader.Find("Alcax/AlienSky")); AssetDatabase.CreateAsset(sky, skyPath); }
            else if (atriumSky != null) sky.CopyPropertiesFromMaterial(atriumSky);
            sky.SetVector("_SunDir", toSun);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.StSkyZenith * 1.0f;
            RenderSettings.ambientEquatorColor = Palette.StSkyHaze * 0.8f;
            RenderSettings.ambientGroundColor = Palette.StSandShadow * 0.7f;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.6f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.004f;
            RenderSettings.fogColor = Palette.StSkyHaze * 0.85f;

            string vp = $"{Root}/Settings/VP_Kelxor.asset";
            AssetDatabase.DeleteAsset(vp);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, vp);
            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = 0.3f; bloom.threshold.value = 1.1f;
            var vig = profile.Add<Vignette>(true);
            vig.intensity.value = 0.18f;
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.1f; ca.contrast.value = 10f; ca.saturation.value = 4f;
            ca.colorFilter.overrideState = false; ca.hueShift.overrideState = false;
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            string lp = $"{Root}/Settings/LS_Kelxor.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lp);
            if (ls == null) { ls = new LightingSettings { name = "LS_Kelxor" }; AssetDatabase.CreateAsset(ls, lp); }
            ls.bakedGI = false;
            ls.realtimeGI = false;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;

            new GameObject("Wind").AddComponent<WindManager>();
            new GameObject("BlowingSand").AddComponent<BlowingSand>();
        }

        static void BuildManagers(PlayerController player, GameObject titleCam)
        {
            var game = new GameObject("Game");
            var gm = game.AddComponent<GameManager>();
            SetRef(gm, "titleCamera", titleCam);
            game.AddComponent<FeelManager>();
            var sm = game.AddComponent<SoundManager>();
            SetFloat(sm, "ambientVolume", 0.1f);
            SetRef(sm, "bank", SoundOverrides.Refresh());
            var fx = game.AddComponent<FXManager>();
            SetRef(fx, "particleMaterial", AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Art/Materials/M_Particle.mat"));
            var ui = new GameObject("UI");
            var hud = ui.AddComponent<HUD>();
            SetRef(hud, "player", player);
            SetString(hud, "objectiveOverride", "KELXOR EXTERIOR   /   F1 ADMIN");
            ui.AddComponent<Alcax.Dialogue.DialogueUI>();
            var shell = ui.AddComponent<ShellUI>();
            SetString(shell, "gameTitle", "");
            SetString(shell, "tagline", "");
        }
    }
}
