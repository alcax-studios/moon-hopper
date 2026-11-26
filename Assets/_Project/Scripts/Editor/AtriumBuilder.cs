using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.Core;
using Alcax.FX;
using Alcax.Player;
using Alcax.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Alcax.EditorTools.BuildUtil;
using Look = Alcax.EditorTools.CharacterRig.Look;
using Outfit = Alcax.EditorTools.CharacterRig.Outfit;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Builds the station's Atrium C from code: a dark, glossy, brutalist and tech interior with a giant
    /// window onto a desert planet at sunset under a ringed gas giant. Realistic-leaning: PBR scans and
    /// procedural maps, baked GI with area lights, a box-projected reflection probe, light probes,
    /// light shafts, steam, and dust.
    /// Menu: Alcax > Build Atrium Level (rebuilds everything, then starts the lightmap bake).
    /// </summary>
    public static class AtriumBuilder
    {
        public const string ScenePath = Root + "/Scenes/Atrium.unity";
        const string MeshPath = Root + "/Art/Meshes/Atrium_Meshes.asset";

        // Room dimensions (meters)
        const float HX = 28f, HZ = 18f, CeilY = 20f, WallT = 1.2f, MezzY = 6f, MezzEdgeZ = -12.5f;

        static readonly Dictionary<string, MeshAccum> render = new();
        static readonly Dictionary<string, MeshAccum> collide = new();
        static readonly Dictionary<string, Material> mats = new();
        static readonly List<Mesh> meshes = new();
        static Transform root, exterior, plants;
        static System.Random rng;
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        [MenuItem("Alcax/Build Atrium Level")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[AtriumBuilder] Exit Play mode first.");
                return;
            }
            rng = new System.Random(42);
            render.Clear(); collide.Clear(); mats.Clear(); meshes.Clear();

            AtriumTextures.BuildAll();
            BuildMaterials();
            TuneRenderer();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("Atrium").transform;
            exterior = new GameObject("Exterior").transform;
            plants = new GameObject("Plants").transform;

            var sun = BuildLightingAndSky();
            BuildShell();
            BuildWindowWall();
            BuildFrames();
            BuildCeiling();
            BuildServices();
            BuildSlotVents();
            BuildMezzanineAndStair();
            BuildDoorsAndSignage();
            BuildWing();
            BuildArmory();
            BuildFoodCourt();
            BuildRestrooms();
            BuildHotel();
            BuildFurniture();
            BuildPlants();
            BuildExterior();
            BuildShips();
            FlushMeshes();
            BuildAtriumNavMesh();
            BuildStationPoints();
            BuildNpc();
            BuildResidents();
            BuildPatrons();
            BuildStaff();
            BuildHotelPeople();
            BuildSecurity();
            BuildExtraAmbience();
            BuildFX(sun);
            BuildProbes();

            var (player, head) = PlayerRig.BuildPlayer(new Vector3(0f, MezzY + 0.05f, -14.6f), 0f, out var cam);
            player.gameObject.AddComponent<Alcax.AI.PlayerTarget>();   // station security and the combat AI see and hear the player
            var titleCam = PlayerRig.BuildCameras(cam, head, new Vector3(0f, 7f, 2f), new Vector3(0f, 1.5f, -15f), new Vector3(0f, 2.5f, 8f), 62f);
            SetFloat(titleCam.transform.parent.GetComponent<TitleOrbit>(), "degreesPerSecond", 2.5f);
            // See the whole desert and incoming shuttles (the forest rig stops at 170 m)
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 4000f;
            foreach (var vc in Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>())
            {
                vc.Lens.FarClipPlane = 4000f;
                vc.Lens.NearClipPlane = Mathf.Max(vc.Lens.NearClipPlane, 0.05f);
            }
            BuildManagers(player, titleCam);

            SaveMeshAsset();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[AtriumBuilder] Built " + ScenePath + ". Starting lightmap bake...");
            // Start the bake on the next editor tick: baking right after the save sometimes threw inside the lightmapper.
            EditorApplication.delayCall += Bake;
        }

        [MenuItem("Alcax/Bake Atrium Lighting")]
        public static void Bake()
        {
            Lightmapping.bakeCompleted -= OnBakeDone;
            Lightmapping.bakeCompleted += OnBakeDone;
            Lightmapping.BakeAsync();
        }

        static void OnBakeDone()
        {
            Lightmapping.bakeCompleted -= OnBakeDone;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log("[AtriumBuilder] Lightmap bake complete and scene saved.");
        }

        // ---------- Materials ----------

        static Material Mat(string name, Shader shader)
        {
            string path = $"{Root}/Art/Materials/Atrium/M_{name}.mat";
            EnsureFolder($"{Root}/Art/Materials/Atrium");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            mats[name] = m;
            return m;
        }

        static Material PBR(string name, Texture2D albedo, Texture2D normal, Texture2D mask, Color tint, float normalScale = 1f,
            float smoothness = 1f, Texture2D ao = null)
        {
            var m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", albedo);
            if (normal != null) { m.SetTexture("_BumpMap", normal); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
            if (mask != null)
            {
                m.SetTexture("_MetallicGlossMap", mask);
                m.EnableKeyword("_METALLICSPECGLOSSMAP");
                m.SetFloat("_Smoothness", smoothness);
                m.SetFloat("_SmoothnessTextureChannel", 0f);
            }
            else m.SetFloat("_Smoothness", smoothness);
            if (ao != null) { m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 0.8f); m.EnableKeyword("_OCCLUSIONMAP"); }
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Flat(string name, Color c, float smooth, float metallic = 0f, Color? emission = null, float emissionIntensity = 0f, Texture2D emissionMap = null)
        {
            var m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value * emissionIntensity);
                if (emissionMap != null) m.SetTexture("_EmissionMap", emissionMap);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Cutout(string name, Texture2D tex, Color tint)
        {
            var m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", tint);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.45f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_Smoothness", 0.35f);
            m.doubleSidedGI = true;
            m.renderQueue = (int)RenderQueue.AlphaTest;
            m.SetOverrideTag("RenderType", "TransparentCutout");
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Glass(string name)
        {
            var m = Mat(name, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", Palette.WithAlpha(Palette.StGlass, 0.08f));
            m.SetFloat("_Smoothness", 0.96f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void BuildMaterials()
        {
            PBR("Concrete", AtriumTextures.AndoWallA, AtriumTextures.AndoWallN, AtriumTextures.AndoWallM, Palette.Neutral, 1f, 1f);
            PBR("ConcreteDark", AtriumTextures.AndoWallA, AtriumTextures.AndoWallN, AtriumTextures.AndoWallM, Palette.StConcreteDark * 1.12f, 1f, 1f);
            PBR("Floor", AtriumTextures.AndoFloorA, AtriumTextures.AndoFloorN, AtriumTextures.AndoFloorM, Palette.Neutral, 0.8f, 1f);
            PBR("Metal", AtriumTextures.MetalA, AtriumTextures.MetalN, AtriumTextures.MetalM, Palette.Neutral, 0.8f, 1f);
            PBR("MetalLight", AtriumTextures.MetalA, AtriumTextures.MetalN, AtriumTextures.MetalM, Palette.StMetalLight * 1.2f, 0.6f, 1f);
            PBR("Sand", AtriumTextures.SandA, AtriumTextures.SandN, null, Palette.Neutral, 1f, 0.08f);
            PBR("Rock", AtriumTextures.SandA, AtriumTextures.SandN, null, Palette.StRock * 1.6f, 1.5f, 0.1f);
            PBR("Bark", AtriumTextures.BarkA, AtriumTextures.BarkN, null, Palette.Neutral, 1f, 0.15f);
            PBR("Hazard", AtriumTextures.Hazard, null, null, Palette.Neutral, 1f, 0.35f);
            PBR("Soil", AtriumTextures.Soil, null, null, Palette.Neutral, 1f, 0.05f);
            Flat("Seat", Palette.StFabric, 0.15f);
            Flat("Wood", Palette.StWood, 0.35f);
            Flat("WarmLight", Palette.StAccentOrange, 0.2f, 0f, Palette.StAccentOrange, 2.2f);
            Flat("MenuBoard", Palette.UIDark, 0.6f, 0f, Palette.StPanelLight, 0.9f);
            Flat("WhiteCabinet", Palette.UIBone, 0.45f);
            Flat("PanelLight", Palette.StPanelLight, 0.2f, 0f, Palette.StPanelLight, 2.6f);
            Flat("CeilingLight", Palette.StPanelLight, 0.2f, 0f, Palette.StPanelLight, 3.2f);
            Flat("CyanStrip", Palette.StTechCyan, 0.2f, 0f, Palette.StTechCyan, 3f);
            Flat("OrangeStrip", Palette.StAccentOrange, 0.2f, 0f, Palette.StAccentOrange, 3.5f);
            Flat("ConsoleTop", Palette.StPanelLight, 0.6f, 0f, Palette.StPanelLight, 1.8f);
            Flat("Screen", Palette.UIDark, 0.85f, 0f, Palette.Neutral, 1.6f, AtriumTextures.Screen);
            Flat("RedLight", Palette.Medkit, 0.2f, 0f, Palette.BloodBright, 4f);
            Flat("Water", Palette.StWater, 0.98f);
            Glass("Glass");
            Cutout("Frond", AtriumTextures.Frond, Palette.Neutral);
            Cutout("Leaf", AtriumTextures.Leaf, Palette.Neutral);
            Cutout("Blade", AtriumTextures.Blade, Palette.Neutral);
            Cutout("Ivy", AtriumTextures.Ivy, Palette.Neutral);
            Flat("SeatMetal", Palette.StMetalLight, 0.55f, 0.75f);
            PBR("Pipe", AtriumTextures.MetalA, AtriumTextures.MetalN, AtriumTextures.MetalM, Palette.StMetalLight * 1.1f, 0.3f, 1f);
            PBR("PipeDark", AtriumTextures.MetalA, AtriumTextures.MetalN, AtriumTextures.MetalM, Palette.StMetal * 0.55f, 0.4f, 1f);
            Flat("RedPipe", Palette.Medkit, 0.45f);
            Flat("Cable", Palette.Grip, 0.3f);
            Flat("TargetSteel", Palette.StMetal * 0.7f, 0.5f, 0.8f);
            Flat("Rubber", Palette.Grip, 0.25f);
            Flat("Pebble", Palette.StConcreteDark * 0.7f, 0.2f);
            Flat("NpcJacket", Palette.StMetalLight, 0.3f);
            Flat("NpcPants", Palette.UIPanel, 0.25f);
            Flat("NpcSkin", Palette.PlayerSkin, 0.35f);
            Flat("NpcHair", Palette.Grip, 0.3f);
            PBR("ShipHull", AtriumTextures.MetalA, AtriumTextures.MetalN, AtriumTextures.MetalM, Palette.StPanelLight * 1.1f, 0.6f, 1f);
            PBR("ShipDark", AtriumTextures.MetalA, AtriumTextures.MetalN, AtriumTextures.MetalM, Palette.StMetal * 0.45f, 0.6f, 1f);
            Flat("EngineGlow", Palette.StTechCyan, 0.2f, 0f, Palette.StPanelLight, 6f);

            var shaft = Mat("Shaft", Shader.Find("Alcax/LightShaft"));
            shaft.SetColor("_Color", Palette.StSun);
            shaft.SetFloat("_Intensity", 0.014f);
            shaft.SetFloat("_NearFade", 8f);   // fade out when the player is close to or inside a shaft
            EditorUtility.SetDirty(shaft);

            var sky = Mat("Sky", Shader.Find("Alcax/AlienSky"));
            sky.SetColor("_Zenith", Palette.StSkyZenith);
            sky.SetColor("_Haze", Palette.StSkyHaze);
            sky.SetColor("_Ground", Palette.StSandShadow);
            sky.SetColor("_SunColor", Palette.StSun);
            sky.SetColor("_PlanetA", Palette.StPlanetA);
            sky.SetColor("_PlanetB", Palette.StPlanetB);
            sky.SetColor("_RingColor", Palette.StRing);
            sky.SetVector("_PlanetDir", new Vector3(0.42f, 0.36f, 0.83f).normalized);
            sky.SetFloat("_PlanetRadius", 0.26f);
            sky.SetVector("_RingTilt", new Vector3(0.32f, 0.9f, -0.28f).normalized);
            EditorUtility.SetDirty(sky);
        }

        static void TuneRenderer()
        {
            foreach (var a in new[] { GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset })
            {
                if (a == null) continue;
                a.shadowDistance = 90f;
                a.shadowCascadeCount = 4;
                a.supportsHDR = true;
                EditorUtility.SetDirty(a);
            }
            // Stronger SSAO on the PC renderer for contact shadows on the concrete.
            var rd = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>("Assets/Settings/PC_Renderer.asset");
            if (rd != null)
                foreach (var f in rd.rendererFeatures)
                    if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion")
                    {
                        var so = new SerializedObject(f);
                        var intensity = so.FindProperty("m_Settings.Intensity");
                        var radius = so.FindProperty("m_Settings.Radius");
                        if (intensity != null) intensity.floatValue = 0.9f;
                        if (radius != null) radius.floatValue = 0.4f;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
        }

        // ---------- Geometry helpers ----------

        static float TileFor(string mat, float tile) =>
            mat == "Concrete" || mat == "ConcreteDark" || mat == "Floor" ? AtriumTextures.AndoTile : tile;

        static void Add(string mat, Vector3 min, Vector3 max, float tile, bool col = true, bool skipBottom = false)
        {
            tile = TileFor(mat, tile);
            Acc(render, mat).BoxMinMax(min, max, tile, skipBottom);
            if (col) Acc(collide, "col").BoxMinMax(min, max, tile);
        }

        static void AddRot(string mat, Vector3 center, Vector3 size, Quaternion rot, float tile, bool col = true)
        {
            tile = TileFor(mat, tile);
            Acc(render, mat).Box(center, size, rot, tile);
            if (col) Acc(collide, "col").Box(center, size, rot, tile);
        }

        /// <summary>A beam between two points with a rectangular section (w across, d deep).</summary>
        static void Beam(string mat, Vector3 a, Vector3 b, float w, float d, float tile, bool col = true)
        {
            var dir = b - a;
            var rot = Quaternion.LookRotation(dir.normalized, Mathf.Abs(dir.normalized.y) > 0.95f ? Vector3.forward : Vector3.up);
            AddRot(mat, (a + b) * 0.5f, new Vector3(w, d, dir.magnitude), rot, tile, col);
        }

        static MeshAccum Acc(Dictionary<string, MeshAccum> d, string key)
        {
            if (!d.TryGetValue(key, out var a)) { a = new MeshAccum(); d[key] = a; }
            return a;
        }

        static void FlushMeshes()
        {
            var structure = new GameObject("Structure").transform;
            structure.SetParent(root, false);
            foreach (var kv in render)
            {
                if (kv.Value.Count == 0) continue;
                bool glow = kv.Key.Contains("Light") || kv.Key.Contains("Strip") || kv.Key == "ConsoleTop" || kv.Key == "Screen";
                bool glass = kv.Key == "Glass";
                var mesh = kv.Value.Build("Atrium_" + kv.Key, lightmapUVs: !glass);
                meshes.Add(mesh);
                var go = new GameObject("M_" + kv.Key, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(structure, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mats[kv.Key];
                if (glass || glow) r.shadowCastingMode = ShadowCastingMode.Off;
                var flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
                if (!glass) flags |= StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
            }
            if (collide.TryGetValue("col", out var c) && c.Count > 0)
            {
                var cm = c.Build("Atrium_Collision", false);
                meshes.Add(cm);
                var go = new GameObject("Collision");
                go.transform.SetParent(structure, false);
                go.AddComponent<MeshCollider>().sharedMesh = cm;
            }
        }

        static void SaveMeshAsset()
        {
            EnsureFolder($"{Root}/Art/Meshes");
            AssetDatabase.DeleteAsset(MeshPath);
            var container = new Mesh { name = "Atrium_Meshes" };
            AssetDatabase.CreateAsset(container, MeshPath);
            foreach (var m in meshes) AssetDatabase.AddObjectToAsset(m, container);
            AssetDatabase.SaveAssets();
        }

        static GameObject MeshGO(string name, Mesh mesh, string mat, Transform parent, bool staticGI, bool shadows = true)
        {
            meshes.Add(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mats[mat];
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            var flags = StaticEditorFlags.BatchingStatic;
            if (staticGI) flags |= StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            return go;
        }

        // ---------- Lighting and sky ----------

        static Light BuildLightingAndSky()
        {
            var toSun = new Vector3(-0.26f, 0.2f, 1f).normalized;
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.LookRotation(-toSun);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.StSun;
            sun.intensity = 2.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;
            sun.lightmapBakeType = LightmapBakeType.Mixed;

            mats["Sky"].SetVector("_SunDir", toSun);
            RenderSettings.skybox = mats["Sky"];
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.6f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0011f;
            RenderSettings.fogColor = Palette.StSkyHaze * 0.85f;

            // Post-processing: cool, contrasty interior, glowing accents
            string vp = $"{Root}/Settings/VP_Atrium.asset";
            AssetDatabase.DeleteAsset(vp);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, vp);
            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = 0.6f; bloom.threshold.value = 0.95f; bloom.scatter.value = 0.72f;
            var vig = profile.Add<Vignette>(true);
            vig.intensity.value = 0.18f; vig.smoothness.value = 0.5f; vig.color.value = Palette.UIDark;
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.55f; ca.contrast.value = 6f; ca.saturation.value = 2f;
            ca.colorFilter.overrideState = false; ca.hueShift.overrideState = false;
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.value = -6f; wb.tint.value = 0f;
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            // Bake settings
            string lp = $"{Root}/Settings/LS_Atrium.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lp);
            if (ls == null) { ls = new LightingSettings { name = "LS_Atrium" }; AssetDatabase.CreateAsset(ls, lp); }
            ls.bakedGI = true;
            ls.realtimeGI = false;
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
            ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
            ls.lightmapResolution = 8f;
            ls.lightmapPadding = 2;
            ls.lightmapMaxSize = 2048;
            ls.directSampleCount = 32;
            ls.indirectSampleCount = 256;
            ls.environmentSampleCount = 128;
            ls.maxBounces = 3;
            ls.ao = true;
            ls.aoMaxDistance = 1.2f;
            ls.directionalityMode = LightmapsMode.CombinedDirectional;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
            return sun;
        }

        // ---------- Architecture ----------

        static void BuildShell()
        {
            // Floor slab (with the floor's own tile size) and the outer walls
            Add("Floor", new Vector3(-HX - WallT, -1f, -HZ - WallT), new Vector3(HX + WallT, 0f, HZ + 0.2f), 4f);
            // South wall, with a doorway for the east blast door into the service hall
            Add("Concrete", new Vector3(-HX - WallT, -1f, -HZ - WallT), new Vector3(WDoorX0, CeilY + 1.5f, -HZ), 2.1f);
            Add("Concrete", new Vector3(WDoorX1, -1f, -HZ - WallT), new Vector3(DoorX0, CeilY + 1.5f, -HZ), 2.1f);
            Add("Concrete", new Vector3(WDoorX0, DoorH, -HZ - WallT), new Vector3(WDoorX1, CeilY + 1.5f, -HZ), 2.1f);
            Add("Concrete", new Vector3(DoorX1, -1f, -HZ - WallT), new Vector3(HX + WallT, CeilY + 1.5f, -HZ), 2.1f);
            Add("Concrete", new Vector3(DoorX0, DoorH, -HZ - WallT), new Vector3(DoorX1, CeilY + 1.5f, -HZ), 2.1f);
            Add("Concrete", new Vector3(HX, -1f, -HZ), new Vector3(HX + WallT, CeilY + 1.5f, HtlDoorZ0), 2.1f);                // east, south of the hotel door
            Add("Concrete", new Vector3(HX, -1f, HtlDoorZ1), new Vector3(HX + WallT, CeilY + 1.5f, HZ + 0.6f), 2.1f);          // east, north of it
            Add("Concrete", new Vector3(HX, -1f, HtlDoorZ0), new Vector3(HX + WallT, MezzY, HtlDoorZ1), 2.1f);                 // below the doorway
            Add("Concrete", new Vector3(HX, MezzY + 3.4f, HtlDoorZ0), new Vector3(HX + WallT, CeilY + 1.5f, HtlDoorZ1), 2.1f); // above it
            Add("Concrete", new Vector3(-HX - WallT, -1f, -HZ), new Vector3(-HX, CeilY + 1.5f, HZ + 0.6f), 2.1f);            // west

            // Lower tech cladding band on the side walls, with a cyan base strip
            foreach (float s in new[] { -1f, 1f })
            {
                float x0 = s > 0 ? HX - 0.1f : -HX, x1 = s > 0 ? HX : -HX + 0.1f;
                Add("Metal", new Vector3(x0, 0f, -HZ), new Vector3(x1, 3.2f, HZ), 2f, false);
                float sx0 = s > 0 ? HX - 0.16f : -HX + 0.1f, sx1 = s > 0 ? HX - 0.1f : -HX + 0.16f;
                Add("CyanStrip", new Vector3(sx0, 0.06f, -HZ), new Vector3(sx1, 0.12f, HZ), 1f, false);
                Add("Metal", new Vector3(sx0 - (s > 0 ? 0.05f : 0f), 3.2f, -HZ), new Vector3(sx1 + (s > 0 ? 0f : 0.05f), 3.4f, HZ), 2f, false);
            }
            foreach (var (x0, x1) in new[] { (-HX, WDoorX0 - 0.5f), (WDoorX1 + 0.5f, DoorX0 - 0.5f), (DoorX1 + 0.5f, HX) })
            {
                Add("Metal", new Vector3(x0, 0f, -HZ), new Vector3(x1, 3.2f, -HZ + 0.1f), 2f, false);
                Add("CyanStrip", new Vector3(x0, 0.06f, -HZ + 0.1f), new Vector3(x1, 0.12f, -HZ + 0.16f), 1f, false);
            }
        }

        static void BuildWindowWall()
        {
            float z = HZ;
            // Sill and header
            Add("Concrete", new Vector3(-HX - WallT, -1f, z), new Vector3(HX + WallT, 0.6f, z + 1.2f), 2.1f);
            Add("Concrete", new Vector3(-HX - WallT, 18.6f, z - 0.6f), new Vector3(HX + WallT, CeilY + 1.5f, z + 1.2f), 2.1f);
            // Deep metal grid: 8 bays across (7 m), 3 rows (6 m)
            for (int i = 0; i <= 8; i++)
            {
                float x = -HX + i * 7f;
                Add("Metal", new Vector3(x - 0.3f, 0.6f, z - 0.7f), new Vector3(x + 0.3f, 18.6f, z + 0.5f), 2f);
            }
            foreach (float y in new[] { 6.6f, 12.6f })
                Add("Metal", new Vector3(-HX, y - 0.25f, z - 0.7f), new Vector3(HX, y + 0.25f, z + 0.5f), 2f);
            Add("Metal", new Vector3(-HX, 0.6f, z - 0.9f), new Vector3(HX, 0.9f, z + 0.5f), 2f);
            // Glass, one pane per bay
            var glass = Acc(render, "Glass");
            for (int i = 0; i < 8; i++)
                for (int j = 0; j < 3; j++)
                {
                    float x = -HX + 3.5f + i * 7f;
                    float y0 = j == 0 ? 0.9f : (j == 1 ? 6.85f : 12.85f), y1 = j == 0 ? 6.35f : (j == 1 ? 12.35f : 18.6f);
                    glass.Card(new Vector3(x, (y0 + y1) * 0.5f, z), Vector3.right * 3.2f, Vector3.up * ((y1 - y0) * 0.5f));
                }
            // Invisible barrier
            Acc(collide, "col").BoxMinMax(new Vector3(-HX, 0f, z - 0.05f), new Vector3(HX, 18.6f, z + 0.1f), 1f);
            // Planters sit along the glass (see BuildPlants)
        }

        /// <summary>The signature chevron frames on both side walls, with glowing panels hung between them.</summary>
        static void BuildFrames()
        {
            float[] zs = { -12f, -4f, 4f, 12f };
            foreach (float s in new[] { -1f, 1f })
            {
                foreach (float z in zs)
                {
                    var a = new Vector3(s * 24.6f, 0f, z);
                    var b = new Vector3(s * 27.4f, 9f, z);
                    var c = new Vector3(s * 23.2f, 17.4f, z);
                    var d = new Vector3(s * 23.2f, CeilY, z);
                    Beam("ConcreteDark", a, b, 1.3f, 1f, 2f);
                    Beam("ConcreteDark", b, c, 1.3f, 1f, 2f);
                    Beam("ConcreteDark", c, d, 1.3f, 1f, 2f, false);
                    // Knee brace into the wall, with a thin cyan inset
                    Add("Metal", new Vector3(Mathf.Min(s * 27.4f, s * HX), 8.4f, z - 0.7f), new Vector3(Mathf.Max(s * 27.4f, s * HX), 9.6f, z + 0.7f), 2f, false);
                    Beam("CyanStrip", a + new Vector3(0f, 0.2f, 0.52f), b + new Vector3(0f, -0.2f, 0.52f), 0.06f, 0.06f, 1f, false);
                }
                // Light panels hung along the upper legs, between frames
                for (int k = 0; k < zs.Length - 1; k++)
                {
                    float zc = (zs[k] + zs[k + 1]) * 0.5f;
                    var b = new Vector3(s * 27.4f, 9f, zc);
                    var c = new Vector3(s * 23.2f, 17.4f, zc);
                    var mid = (b + c) * 0.5f + new Vector3(-s * 0.55f, -0.35f, 0f);
                    var up = (c - b).normalized * 3.9f;
                    var right = Vector3.forward * 3.4f;
                    Acc(render, "PanelLight").Card(mid, right, up, true);
                    // Metal frame behind the panel
                    var normalIn = Vector3.Cross(up, right).normalized;
                    if (Vector3.Dot(normalIn, new Vector3(-s, 0, 0)) < 0f) normalIn = -normalIn;
                    AddRot("Metal", mid - normalIn * 0.12f, new Vector3(6.9f, 7.9f, 0.18f),
                        Quaternion.LookRotation(normalIn, (c - b).normalized), 2f, false);
                    // Baked area light for the panel's bounce
                    var lg = new GameObject("PanelAreaLight");
                    lg.transform.SetParent(root, false);
                    lg.transform.position = mid + normalIn * 0.05f;
                    lg.transform.rotation = Quaternion.LookRotation(normalIn, (c - b).normalized);
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Rectangle;
                    l.areaSize = new Vector2(6.8f, 7.8f);
                    l.color = Palette.StPanelLight;
                    l.intensity = 3f;
                    l.lightmapBakeType = LightmapBakeType.Baked;
                }
            }
        }

        static void BuildCeiling()
        {
            Add("ConcreteDark", new Vector3(-HX - WallT, CeilY, -HZ - WallT), new Vector3(HX + WallT, CeilY + 1.5f, HZ + 1.2f), 2f);
            // Coffer beams: along z every 7 m (on the window grid), along x every 6 m
            for (int i = 0; i <= 8; i++)
            {
                float x = -HX + i * 7f;
                Add("Metal", new Vector3(x - 0.35f, CeilY - 1.3f, -HZ), new Vector3(x + 0.35f, CeilY, HZ), 2f, false);
            }
            for (float z = -HZ + 6f; z < HZ; z += 6f)
                Add("Metal", new Vector3(-HX, CeilY - 1.3f, z - 0.35f), new Vector3(HX, CeilY, z + 0.35f), 2f, false);
            // Small ceiling light slots in every other coffer
            for (int i = 0; i < 8; i++)
                for (float z = -HZ + 3f; z < HZ; z += 6f)
                {
                    float x = -HX + 3.5f + i * 7f;
                    Add("CeilingLight", new Vector3(x - 2.2f, CeilY - 0.06f, z - 0.35f), new Vector3(x + 2.2f, CeilY - 0.02f, z + 0.35f), 1f, false);
                    var lg = new GameObject("CofferAreaLight");
                    lg.transform.SetParent(root, false);
                    lg.transform.position = new Vector3(x, CeilY - 0.08f, z);
                    lg.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    var al = lg.AddComponent<Light>();
                    al.type = LightType.Rectangle;
                    al.areaSize = new Vector2(4.4f, 0.7f);
                    al.color = Palette.StPanelLight;
                    al.intensity = 6f;
                    al.lightmapBakeType = LightmapBakeType.Baked;
                }
        }

        static void BuildMezzanineAndStair()
        {
            // South mezzanine with a heavy fascia and a cyan underglow
            Add("ConcreteDark", new Vector3(-HX, MezzY - 0.6f, -HZ), new Vector3(HX, MezzY, MezzEdgeZ), 2f);
            Add("Floor", new Vector3(-HX, MezzY, -HZ), new Vector3(HX, MezzY + 0.02f, MezzEdgeZ), 4f, false);
            Add("Concrete", new Vector3(-HX, MezzY - 1.6f, MezzEdgeZ), new Vector3(HX, MezzY + 0.1f, MezzEdgeZ + 0.6f), 2.1f);
            Add("CyanStrip", new Vector3(-HX, MezzY - 1.65f, MezzEdgeZ + 0.15f), new Vector3(HX, MezzY - 1.6f, MezzEdgeZ + 0.5f), 1f, false);
            // Railing: glass panels with a metal top rail (gap for the stair)
            var glass = Acc(render, "Glass");
            foreach (var (x0, x1) in new[] { (-HX, -2.6f), (2.6f, HX) })
            {
                Add("Metal", new Vector3(x0, MezzY + 1.05f, MezzEdgeZ + 0.2f), new Vector3(x1, MezzY + 1.15f, MezzEdgeZ + 0.4f), 2f);
                glass.Card(new Vector3((x0 + x1) * 0.5f, MezzY + 0.55f, MezzEdgeZ + 0.3f), Vector3.right * ((x1 - x0) * 0.5f), Vector3.up * 0.5f);
                Acc(collide, "col").BoxMinMax(new Vector3(x0, MezzY, MezzEdgeZ + 0.2f), new Vector3(x1, MezzY + 1.15f, MezzEdgeZ + 0.4f), 1f);
            }
            // Columns under the mezzanine
            foreach (float x in new[] { -24f, -16f, -8f, 8f, 16f, 24f })
                Add("ConcreteDark", new Vector3(x - 0.55f, 0f, MezzEdgeZ - 0.2f), new Vector3(x + 0.55f, MezzY - 0.6f, MezzEdgeZ + 0.9f), 2f);

            // Monolithic grand stair: a solid stepped wedge with sloped cheek walls
            const int steps = 34;
            float run = 10f, rise = MezzY;
            float zTop = MezzEdgeZ, zBottom = MezzEdgeZ + run;
            for (int i = 0; i < steps; i++)
            {
                float y1 = (i + 1) * rise / steps;
                float za = zBottom - i * run / steps, zb = zBottom - (i + 1) * run / steps;
                Add("ConcreteDark", new Vector3(-2.5f, 0f, zb), new Vector3(2.5f, y1, za), 2f, true, true);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                var a = new Vector3(s * 2.75f, 0.55f, zBottom + 0.3f);
                var b = new Vector3(s * 2.75f, rise + 0.55f, zTop);
                Beam("Concrete", a, b, 0.5f, 1.7f, 2.1f);
                Beam("OrangeStrip", a + new Vector3(-s * 0.26f, -0.5f, 0f), b + new Vector3(-s * 0.26f, -0.5f, 0f), 0.04f, 0.04f, 1f, false);
            }
        }

        static void BuildDoorsAndSignage()
        {
            // East blast door: hazard jambs and header around the working doorway, with a cyan "open" light
            Add("Hazard", new Vector3(DoorX0 - 0.5f, 0f, -HZ), new Vector3(DoorX0, DoorH + 0.5f, -HZ + 0.25f), 1.2f, false);
            Add("Hazard", new Vector3(DoorX1, 0f, -HZ), new Vector3(DoorX1 + 0.5f, DoorH + 0.5f, -HZ + 0.25f), 1.2f, false);
            Add("Hazard", new Vector3(DoorX0 - 0.5f, DoorH, -HZ), new Vector3(DoorX1 + 0.5f, DoorH + 0.5f, -HZ + 0.25f), 1.2f, false);
            Add("CyanStrip", new Vector3(12.7f, DoorH + 0.55f, -HZ), new Vector3(13.3f, DoorH + 0.75f, -HZ + 0.1f), 1f, false);
            // West blast door (food court): the same jambs and header, with a warm "open" light
            Add("Hazard", new Vector3(WDoorX0 - 0.5f, 0f, -HZ), new Vector3(WDoorX0, DoorH + 0.5f, -HZ + 0.25f), 1.2f, false);
            Add("Hazard", new Vector3(WDoorX1, 0f, -HZ), new Vector3(WDoorX1 + 0.5f, DoorH + 0.5f, -HZ + 0.25f), 1.2f, false);
            Add("Hazard", new Vector3(WDoorX0 - 0.5f, DoorH, -HZ), new Vector3(WDoorX1 + 0.5f, DoorH + 0.5f, -HZ + 0.25f), 1.2f, false);
            Add("WarmLight", new Vector3(-13.3f, DoorH + 0.55f, -HZ), new Vector3(-12.7f, DoorH + 0.75f, -HZ + 0.1f), 1f, false);
            Sign("FOOD COURT", new Vector3(-13f, DoorH + 1.35f, -HZ + 0.06f), 5f, Palette.StAccentOrange, 0.9f);
            Sign("ARMORY  /  RANGE", new Vector3(13f, DoorH + 1.35f, -HZ + 0.06f), 5f, Palette.StTechCyan, 0.85f);
            // Wall screens between the frames on the east wall
            foreach (float z in new[] { -8f, 0f, 8f })
            {
                Add("Metal", new Vector3(HX - 0.35f, 3.6f, z - 1.7f), new Vector3(HX - 0.1f, 6.6f, z + 1.7f), 2f, false);
                var screen = Acc(render, "Screen");
                screen.Card(new Vector3(HX - 0.37f, 5.1f, z), Vector3.forward * -1.55f, Vector3.up * 1.35f, false);
            }
            // Big station lettering high on the south wall
            Sign("ATRIUM C", new Vector3(0f, 13.5f, -HZ + 0.05f), 32f, Palette.StTechCyan, 0.55f);
            Sign("HYDROPONICS DECK", new Vector3(0f, 10.8f, -HZ + 0.05f), 8f, Palette.StPanelLight, 0.35f);
            Sign("03", new Vector3(-HX + 0.05f, 13f, 0f), 60f, Palette.StConcreteDark, 1f, 90f);
        }

        static void Sign(string text, Vector3 pos, float size, Color color, float alpha, float yaw = 0f)
        {
            var go = new GameObject("Sign_" + text);
            go.transform.SetParent(root, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, 180f + yaw, 0f);
            var t = go.AddComponent<TextMeshPro>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.characterSpacing = 18f;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Palette.WithAlpha(color, alpha);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(60f, 12f);
        }

        static void BuildFurniture()
        {
            seatPoints.Clear();
            charMats.Clear();
            // Airport-style beam seating along both sides of the hall, rows back to back
            foreach (float s in new[] { -1f, 1f })
                foreach (float zc in new[] { -6f, 6f })
                {
                    BeamBench(new Vector3(s * 15f - 0.42f, 0f, zc), Vector3.left, 6);
                    BeamBench(new Vector3(s * 15f + 0.42f, 0f, zc), Vector3.right, 6);
                }
            // Consoles with glowing tops
            foreach (var (x, z, yaw) in new[] { (-21.5f, 0f, 90f), (21.5f, 0.5f, -90f), (-9.5f, -8.5f, 0f), (9.5f, -8.5f, 0f) })
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var c = new Vector3(x, 0.55f, z);
                AddRot("Metal", c, new Vector3(2.4f, 1.1f, 1.2f), rot, 2f);
                AddRot("ConsoleTop", c + Vector3.up * 0.58f, new Vector3(2.2f, 0.06f, 1.0f), rot, 1f, false);
                AddRot("OrangeStrip", c + new Vector3(0f, -0.5f, 0f), new Vector3(2.44f, 0.04f, 1.24f), rot, 1f, false);
            }
            BuildInfoDesk();
        }

        /// <summary>
        /// A row of airport beam seating: brushed metal seat pans and backs on a single beam,
        /// T-legs, and armrests between seats. facing is the direction a seated person looks.
        /// </summary>
        static void BeamBench(Vector3 center, Vector3 facing, int seats)
        {
            var rot = Quaternion.LookRotation(facing, Vector3.up);
            Vector3 W(float along, float y, float fwd) => center + rot * new Vector3(along, y, fwd);
            const float pitch = 0.64f;
            float len = seats * pitch;
            // Beam and legs
            AddRot("SeatMetal", W(0f, 0.34f, -0.08f), new Vector3(len + 0.2f, 0.1f, 0.12f), rot, 1f);
            foreach (float a in new[] { -len * 0.5f + 0.15f, 0f, len * 0.5f - 0.15f })
            {
                AddRot("SeatMetal", W(a, 0.17f, -0.08f), new Vector3(0.08f, 0.34f, 0.08f), rot, 1f, false);
                AddRot("SeatMetal", W(a, 0.02f, -0.08f), new Vector3(0.08f, 0.04f, 0.6f), rot, 1f, false);
            }
            for (int i = 0; i < seats; i++)
            {
                float a = -len * 0.5f + pitch * (i + 0.5f);
                AddRot("SeatMetal", W(a, 0.45f, 0.08f), new Vector3(0.54f, 0.035f, 0.46f), rot * Quaternion.Euler(-4f, 0f, 0f), 1f, false);
                seatPoints.Add((W(a, 0f, 0.02f), facing));
                AddRot("SeatMetal", W(a, 0.78f, -0.2f), new Vector3(0.54f, 0.56f, 0.03f), rot * Quaternion.Euler(-12f, 0f, 0f), 1f, false);
            }
            for (int i = 0; i <= seats; i++)
            {
                float a = -len * 0.5f + pitch * i;
                AddRot("SeatMetal", W(a, 0.56f, 0.06f), new Vector3(0.04f, 0.22f, 0.04f), rot, 1f, false);
                AddRot("Rubber", W(a, 0.68f, 0.04f), new Vector3(0.06f, 0.035f, 0.42f), rot, 1f, false);
            }
            // One collision block for the whole row
            Acc(collide, "col").Box(W(0f, 0.45f, -0.02f), new Vector3(len, 0.9f, 0.6f), rot, 1f);
        }

        /// <summary>The information desk at the foot of the grand stair, facing the stair, with a screen pylon behind.</summary>
        static void BuildInfoDesk()
        {
            const float zf = 10.5f;  // front face, facing the stair (-z); the desk sits near the glass
            Add("Concrete", new Vector3(-2.4f, 0f, zf), new Vector3(2.4f, 1.08f, zf + 0.3f), 1f);
            Add("Concrete", new Vector3(-2.4f, 0f, zf + 0.3f), new Vector3(-2.1f, 1.08f, zf + 1.9f), 1f);
            Add("Concrete", new Vector3(2.1f, 0f, zf + 0.3f), new Vector3(2.4f, 1.08f, zf + 1.9f), 1f);
            Add("MetalLight", new Vector3(-2.55f, 1.08f, zf - 0.12f), new Vector3(2.55f, 1.13f, zf + 0.7f), 1f, false);
            Add("MetalLight", new Vector3(-2.1f, 0.76f, zf + 0.3f), new Vector3(2.1f, 0.8f, zf + 1.2f), 1f, false);
            Add("CyanStrip", new Vector3(-2.5f, 1.05f, zf - 0.13f), new Vector3(2.5f, 1.07f, zf - 0.11f), 1f, false);
            Add("OrangeStrip", new Vector3(-2.4f, 0.02f, zf - 0.03f), new Vector3(2.4f, 0.05f, zf - 0.01f), 1f, false);
            Sign("INFORMATION", new Vector3(0f, 0.62f, zf - 0.02f), 4.2f, Palette.StTechCyan, 0.9f, 180f);
            // Screen pylon behind the attendant
            Add("Metal", new Vector3(-1.5f, 0f, zf + 2.3f), new Vector3(1.5f, 2.9f, zf + 2.6f), 2f);
            Acc(render, "Screen").Card(new Vector3(0f, 1.9f, zf + 2.28f), Vector3.right * 1.3f, Vector3.up * 0.85f, false);
            Add("CyanStrip", new Vector3(-1.5f, 2.9f, zf + 2.3f), new Vector3(1.5f, 2.94f, zf + 2.6f), 1f, false);
        }

        // ---------- Plants (selective) ----------

        static Mesh PalmMesh(int seed, float height, out Mesh trunk)
        {
            var r = new System.Random(seed);
            float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var lean = new Vector3(Rr(-0.08f, 0.08f), 0f, Rr(-0.08f, 0.08f));
            var t = new MeshAccum();
            t.Tube(Vector3.zero, lean, height, 0.24f, 0.17f, 10, 24, 2f);
            trunk = t.Build("PalmTrunk" + seed, false);
            var top = Vector3.up * height + lean * height;
            var f = new MeshAccum();
            int fronds = 14;
            for (int i = 0; i < fronds; i++)
            {
                float yaw = i / (float)fronds * 360f + Rr(-10f, 10f);
                float pitch = Rr(-10f, 35f);
                var dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                var side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                f.Strip(top, dir, side, Rr(2.8f, 3.6f), Rr(1.2f, 1.5f), Rr(0.35f, 0.6f), 10, Rr(-20f, 20f));
            }
            return f.Build("PalmFronds" + seed, false);
        }

        static Mesh BladeClump(int seed, int count, float len, float spread)
        {
            var r = new System.Random(seed);
            float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var m = new MeshAccum();
            for (int i = 0; i < count; i++)
            {
                float yaw = Rr(0f, 360f);
                float tilt = Rr(10f, spread);
                var dir = Quaternion.Euler(-(90f - tilt), yaw, 0f) * Vector3.forward;
                var side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                m.Strip(Vector3.up * 0.05f, dir, side, len * Rr(0.7f, 1.1f), Rr(0.09f, 0.14f), Rr(0.05f, 0.25f), 6, Rr(-30f, 30f));
            }
            return m.Build("Blades" + seed, false);
        }

        static Mesh FernClump(int seed, int count, float len)
        {
            var r = new System.Random(seed);
            float Rr(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var m = new MeshAccum();
            for (int i = 0; i < count; i++)
            {
                float yaw = i / (float)count * 360f + Rr(-15f, 15f);
                var dir = Quaternion.Euler(-Rr(25f, 55f), yaw, 0f) * Vector3.forward;
                var side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                m.Strip(Vector3.up * 0.05f, dir, side, len * Rr(0.8f, 1.15f), len * 0.45f, Rr(0.6f, 0.9f), 8);
            }
            return m.Build("Fern" + seed, false);
        }

        static void Plant(string kind, Vector3 pos, int seed)
        {
            var t = new GameObject(kind).transform;
            t.SetParent(plants, false);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
            switch (kind)
            {
                case "Palm":
                    var fronds = PalmMesh(seed, R(6.5f, 9f), out var trunk);
                    MeshGO("Trunk", trunk, "Bark", t, false);
                    MeshGO("Fronds", fronds, "Frond", t, false);
                    break;
                case "Blades":
                    MeshGO("Blades", BladeClump(seed, 26, R(1.1f, 1.7f), 40f), "Blade", t, false);
                    break;
                case "Fern":
                    MeshGO("Fern", FernClump(seed, 12, R(0.8f, 1.2f)), "Frond", t, false);
                    break;
                case "Broadleaf":
                    var m = new MeshAccum();
                    var r = new System.Random(seed);
                    for (int i = 0; i < 9; i++)
                    {
                        float yaw = (float)r.NextDouble() * 360f, h = 0.5f + (float)r.NextDouble() * 0.9f;
                        var dir = Quaternion.Euler(-(20f + (float)r.NextDouble() * 30f), yaw, 0f) * Vector3.forward;
                        var side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                        m.Strip(Vector3.up * h, dir, side, 0.75f, 0.7f, 0.35f, 4);
                    }
                    MeshGO("Leaves", m.Build("Broadleaf" + seed, false), "Leaf", t, false);
                    break;
            }
        }

        struct PlanterArea { public Vector3 min, max; public float soilTop; public int seed; }
        static readonly List<PlanterArea> planterAreas = new();

        /// <summary>Soil surface height inside a planter: a gentle mound with lumps, below the rim.</summary>
        static float SoilY(float x, float z)
        {
            foreach (var p in planterAreas)
            {
                if (x < p.min.x || x > p.max.x || z < p.min.z || z > p.max.z) continue;
                float u = Mathf.InverseLerp(p.min.x, p.max.x, x) * 2f - 1f;
                float v = Mathf.InverseLerp(p.min.z, p.max.z, z) * 2f - 1f;
                float dome = (1f - u * u) * (1f - v * v);
                float lumps = (Mathf.PerlinNoise(x * 2.3f + p.seed, z * 2.3f) - 0.5f) * 0.05f
                            + (Mathf.PerlinNoise(x * 7f, z * 7f + p.seed) - 0.5f) * 0.015f;
                return p.soilTop + dome * 0.08f + lumps * (0.3f + dome);
            }
            return 0f;
        }

        /// <summary>A concrete planter with a real soil bed: an uneven mounded dirt surface sitting below the rim, plus pebbles.</summary>
        static void Planter(Vector3 min, Vector3 max)
        {
            const float wall = 0.15f;
            // Rim walls (hollow box) and a base
            Add("Concrete", min, new Vector3(max.x, min.y + 0.1f, max.z), 1f);
            Add("Concrete", new Vector3(min.x, min.y, min.z), new Vector3(max.x, max.y, min.z + wall), 1f);
            Add("Concrete", new Vector3(min.x, min.y, max.z - wall), new Vector3(max.x, max.y, max.z), 1f);
            Add("Concrete", new Vector3(min.x, min.y, min.z + wall), new Vector3(min.x + wall, max.y, max.z - wall), 1f);
            Add("Concrete", new Vector3(max.x - wall, min.y, min.z + wall), new Vector3(max.x, max.y, max.z - wall), 1f);
            Add("CyanStrip", new Vector3(min.x, min.y + 0.04f, max.z), new Vector3(max.x, min.y + 0.08f, max.z + 0.03f), 1f, false);

            var area = new PlanterArea
            {
                min = new Vector3(min.x + wall, 0f, min.z + wall),
                max = new Vector3(max.x - wall, 0f, max.z - wall),
                soilTop = max.y - 0.09f,
                seed = planterAreas.Count * 17 + 3,
            };
            planterAreas.Add(area);

            // Soil: a grid surface following SoilY, skirted down to the base so no gap shows
            var soil = Acc(render, "Soil");
            const float step = 0.18f;
            int nx = Mathf.Max(2, Mathf.CeilToInt((area.max.x - area.min.x) / step));
            int nz = Mathf.Max(2, Mathf.CeilToInt((area.max.z - area.min.z) / step));
            int start = soil.V.Count;
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = Mathf.Lerp(area.min.x, area.max.x, i / (float)nx);
                    float z = Mathf.Lerp(area.min.z, area.max.z, j / (float)nz);
                    float y = SoilY(Mathf.Clamp(x, area.min.x + 0.001f, area.max.x - 0.001f), Mathf.Clamp(z, area.min.z + 0.001f, area.max.z - 0.001f));
                    float hx = SoilY(Mathf.Clamp(x + 0.05f, area.min.x, area.max.x), z) - SoilY(Mathf.Clamp(x - 0.05f, area.min.x, area.max.x), z);
                    float hz = SoilY(x, Mathf.Clamp(z + 0.05f, area.min.z, area.max.z)) - SoilY(x, Mathf.Clamp(z - 0.05f, area.min.z, area.max.z));
                    soil.V.Add(new Vector3(x, y, z));
                    soil.N.Add(new Vector3(-hx / 0.1f, 1f, -hz / 0.1f).normalized);
                    soil.U.Add(new Vector2(x, z) / 0.8f);
                }
            int row = nx + 1;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = start + j * row + i;
                    soil.T.Add(a); soil.T.Add(a + row); soil.T.Add(a + 1);
                    soil.T.Add(a + 1); soil.T.Add(a + row); soil.T.Add(a + row + 1);
                }
            soil.BoxMinMax(new Vector3(area.min.x, min.y + 0.1f, area.min.z), new Vector3(area.max.x, area.soilTop - 0.01f, area.max.z), 0.8f);

            // Pebbles scattered on the soil
            var pebbleMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Art/Meshes/RockB.asset");
            int pebbles = Mathf.RoundToInt((area.max.x - area.min.x) * (area.max.z - area.min.z) * 3f);
            for (int k = 0; k < pebbles && pebbleMesh != null; k++)
            {
                float x = R(area.min.x + 0.05f, area.max.x - 0.05f), z = R(area.min.z + 0.05f, area.max.z - 0.05f);
                float s = R(0.03f, 0.09f);
                var go = new GameObject("Pebble", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(plants, false);
                go.transform.position = new Vector3(x, SoilY(x, z) - s * 0.25f, z);
                go.transform.rotation = Quaternion.Euler(R(0, 360), R(0, 360), R(0, 360));
                go.transform.localScale = new Vector3(s, s * R(0.5f, 0.9f), s * R(0.7f, 1.1f));
                go.GetComponent<MeshFilter>().sharedMesh = pebbleMesh;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = mats["Pebble"];
                r.shadowCastingMode = ShadowCastingMode.Off;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        /// <summary>Places a plant with its base sunk into the soil.</summary>
        static void PlantIn(string kind, float x, float z, int seed) => Plant(kind, new Vector3(x, SoilY(x, z) - 0.04f, z), seed);

        static void BuildPlants()
        {
            planterAreas.Clear();
            // Long planters along the glass
            foreach (var (x0, x1) in new[] { (-24f, -12f), (12f, 24f) })
            {
                Planter(new Vector3(x0, 0f, 14.6f), new Vector3(x1, 0.85f, 16.8f));
                PlantIn("Palm", x0 + 2.5f, 15.7f, 1 + (int)x0);
                PlantIn("Palm", x1 - 3f, 15.6f, 7 + (int)x1);
                for (float x = x0 + 1f; x < x1 - 0.5f; x += 1.5f)
                    PlantIn(rng.NextDouble() < 0.5 ? "Blades" : "Fern", x + R(-0.3f, 0.3f), 15.7f + R(-0.45f, 0.45f), (int)(x * 13));
                PlantIn("Broadleaf", (x0 + x1) * 0.5f, 15.4f, 99 + (int)x0);
            }
            // A low central planter at the window
            Planter(new Vector3(-4f, 0f, 15.2f), new Vector3(4f, 0.55f, 16.6f));
            for (float x = -3.3f; x <= 3.3f; x += 1.1f) PlantIn("Blades", x, 15.9f + R(-0.2f, 0.2f), (int)(x * 31) + 500);
            // Two planters flanking the stair foot
            foreach (float s in new[] { -1f, 1f })
            {
                Planter(new Vector3(s * 3.6f - 0.9f, 0f, -2.2f), new Vector3(s * 3.6f + 0.9f, 0.7f, -0.4f));
                PlantIn("Blades", s * 3.6f, -1.3f, 700 + (int)(s * 3));
                PlantIn("Fern", s * 3.6f + 0.4f, -0.9f, 710 + (int)(s * 3));
            }
            // Ivy hanging over the mezzanine edge
            var ivy = Acc(render, "Ivy");
            for (float x = -HX + 1.5f; x < HX - 1f; x += R(1.2f, 2.6f))
            {
                if (Mathf.Abs(x) < 3.2f) continue;
                float len = R(1.4f, 3.2f);
                ivy.Card(new Vector3(x, MezzY + 0.1f - len * 0.5f, MezzEdgeZ + 0.65f), Vector3.right * 0.35f, Vector3.up * (len * 0.5f));
            }
        }

        // ---------- Exterior ----------

        static float DesertHeight(float x, float z)
        {
            float d = Mathf.Max(0f, z - 30f);
            var p = new Vector2(x, z);
            float dunes = 0f;
            float amp = Mathf.Lerp(0.5f, 28f, Mathf.Clamp01(d / 900f));
            for (int o = 0; o < 3; o++)
            {
                float freq = 0.006f * (1 + o * 1.7f);
                var dir = new Vector2(Mathf.Cos(0.6f + o), Mathf.Sin(0.6f + o));
                float w = Mathf.PerlinNoise(x * 0.002f + o * 9f, z * 0.002f) * 4f;
                float ridge = 1f - Mathf.Abs(Mathf.Sin(Vector2.Dot(p, dir) * freq + w));
                dunes += Mathf.Pow(ridge, 1.6f) * amp / (1 + o * 1.5f);
            }
            float far = Mathf.Clamp01((z - 1800f) / 400f);
            float mesa = Mathf.Clamp(Mathf.PerlinNoise(x * 0.0012f + 3f, z * 0.0012f) * 420f - 150f, 0f, 160f) * far;
            // A sand berm drifted up against the base of the window wall (meets the sill outside at about -0.8 m)
            float bt = Mathf.Clamp01((z - 19f) / 30f);
            float berm = 2.7f * (1f - bt * bt * (3f - 2f * bt));
            return -3.5f + dunes + mesa + berm;
        }

        static void BuildExterior()
        {
            var terrain = AtriumMeshes.Terrain(new Vector2(-1600f, 18.8f), new Vector2(1600f, 3200f), 240, DesertHeight, 8f);
            var tg = MeshGO("Desert", terrain, "Sand", exterior, false);
            GameObjectUtility.SetStaticEditorFlags(tg, StaticEditorFlags.BatchingStatic);

            // Base structures (no terrace: the dunes run right up to the glass)
            var metal = new MeshAccum();
            // Comms mast
            metal.BoxMinMax(new Vector3(-46.6f, -3f, 58.4f), new Vector3(-45.4f, 42f, 59.6f), 2f);
            for (float y = 4f; y < 42f; y += 6f) metal.BoxMinMax(new Vector3(-48f, y, 58.9f), new Vector3(-44f, y + 0.3f, 59.1f), 2f);
            // Solar array rows
            for (int row = 0; row < 5; row++)
                for (int k = 0; k < 6; k++)
                {
                    var c = new Vector3(-90f + k * 7f, -2f + row * 0.1f, 70f + row * 7f);
                    metal.Box(c + Vector3.up * 1.6f, new Vector3(6.2f, 0.12f, 3.2f), Quaternion.Euler(-28f, 0f, 0f), 2f);
                    metal.BoxMinMax(c + new Vector3(-0.1f, -1.5f, -0.1f), c + new Vector3(0.1f, 1.5f, 0.1f), 2f);
                }
            // Habitat modules
            metal.Box(new Vector3(62f, 1f, 52f), new Vector3(18f, 8f, 10f), Quaternion.Euler(0f, -18f, 0f), 2f);
            metal.Box(new Vector3(78f, 0f, 44f), new Vector3(10f, 6f, 10f), Quaternion.Euler(0f, 12f, 0f), 2f);
            MeshGO("BaseStructures", metal.Build("Atrium_ExteriorMetal", false), "MetalLight", exterior, false);
            var redLight = new GameObject("MastBeacon");
            redLight.transform.SetParent(exterior, false);
            var beacon = new MeshAccum();
            beacon.BoxMinMax(new Vector3(-46.5f, 42f, 58.5f), new Vector3(-45.5f, 42.8f, 59.5f), 1f);
            MeshGO("Beacon", beacon.Build("Atrium_Beacon", false), "RedLight", exterior, false, false);

            // Rocks and distant buttes
            var rocks = new[] { "RockA", "RockB", "RockC" };
            for (int i = 0; i < 40; i++)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Root}/Art/Meshes/{rocks[i % 3]}.asset");
                if (mesh == null) continue;
                float x = R(-400f, 400f), z = R(60f, 900f);
                float s = R(2f, 14f) * (1f + z / 300f);
                var go = new GameObject("Rock", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(exterior, false);
                go.transform.position = new Vector3(x, DesertHeight(x, z) - s * 0.2f, z);
                go.transform.rotation = Quaternion.Euler(R(-8f, 8f), R(0f, 360f), R(-8f, 8f));
                go.transform.localScale = new Vector3(s, s * R(0.4f, 0.9f), s * R(0.8f, 1.2f));
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = mats["Rock"];
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        // ---------- Loft-style exposed services (ceiling) ----------

        static void Rod(Vector3 bottom, float topY)
            => Acc(render, "PipeDark").Cylinder(bottom, new Vector3(bottom.x, topY, bottom.z), 0.018f, 6, 1f, false);

        static void BuildServices()
        {
            var pipe = Acc(render, "Pipe");
            var dark = Acc(render, "PipeDark");
            var red = Acc(render, "RedPipe");
            float slabUnder = CeilY;

            // Two big round spiral ducts running the length of the hall, with seam rings, straps, and diffusers
            foreach (float z in new[] { -7.5f, 7.5f })
            {
                float y = 17.4f;
                var a = new Vector3(-HX + 0.6f, y, z);
                var b = new Vector3(HX - 0.6f, y, z);
                pipe.Cylinder(a, b, 0.6f, 20, 2f);
                for (float x = -HX + 1.5f; x < HX - 1f; x += 1.5f)
                    pipe.Cylinder(new Vector3(x - 0.03f, y, z), new Vector3(x + 0.03f, y, z), 0.615f, 20, 1f, false);
                for (float x = -HX + 3.5f; x < HX; x += 3.5f)
                {
                    dark.Cylinder(new Vector3(x - 0.04f, y, z), new Vector3(x + 0.04f, y, z), 0.64f, 20, 1f, false);
                    Rod(new Vector3(x, y + 0.62f, z), slabUnder);
                }
                for (float x = -HX + 7f; x < HX - 1f; x += 7f)
                {
                    Add("PipeDark", new Vector3(x - 0.4f, y - 0.85f, z - 0.4f), new Vector3(x + 0.4f, y - 0.55f, z + 0.4f), 1f, false);
                    Add("Pipe", new Vector3(x - 0.32f, y - 0.87f, z - 0.32f), new Vector3(x + 0.32f, y - 0.85f, z + 0.32f), 1f, false);
                    pipe.Cylinder(new Vector3(x, y - 0.55f, z), new Vector3(x, y - 0.3f, z), 0.22f, 12, 1f, false);
                }
            }

            // A pipe bundle down the middle on trapeze hangers, with flanged joints
            float by = 18.25f;
            float[] offs = { -0.38f, 0f, 0.32f };
            float[] radii = { 0.13f, 0.09f, 0.07f };
            for (int i = 0; i < 3; i++)
            {
                float z = offs[i];
                var acc = i == 2 ? red : pipe;
                acc.Cylinder(new Vector3(-HX + 0.3f, by, z), new Vector3(HX - 0.3f, by, z), radii[i], 12, 1f);
                for (float x = -HX + 4f; x < HX - 1f; x += 6f)
                    acc.Cylinder(new Vector3(x - 0.05f, by, z), new Vector3(x + 0.05f, by, z), radii[i] + 0.035f, 12, 1f, false);
            }
            for (float x = -HX + 2f; x < HX; x += 4f)
            {
                Add("PipeDark", new Vector3(x - 0.03f, by - 0.18f, -0.6f), new Vector3(x + 0.03f, by - 0.13f, 0.55f), 1f, false);
                Rod(new Vector3(x, by - 0.15f, -0.58f), slabUnder);
                Rod(new Vector3(x, by - 0.15f, 0.53f), slabUnder);
            }

            // Two cross runs along z, dropping down the walls as risers into the floor
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 10.5f;
                pipe.Cylinder(new Vector3(x, 18.6f, -HZ + 0.4f), new Vector3(x, 18.6f, HZ - 1.4f), 0.1f, 12, 1f);
                pipe.Cylinder(new Vector3(x + s * 0.3f, 18.75f, -HZ + 0.4f), new Vector3(x + s * 0.3f, 18.75f, HZ - 1.4f), 0.06f, 10, 1f);
                for (float z = -HZ + 2f; z < HZ - 1f; z += 4f)
                {
                    Rod(new Vector3(x, 18.7f, z), slabUnder);
                    dark.Cylinder(new Vector3(x, 18.6f, z - 0.04f), new Vector3(x, 18.6f, z + 0.04f), 0.13f, 12, 1f, false);
                }
                // Riser down the south wall to the floor, with a valve wheel
                float rx = x + s * 0.0f;
                pipe.Cylinder(new Vector3(rx, 18.6f, -HZ + 0.4f), new Vector3(rx, 0f, -HZ + 0.4f), 0.1f, 12, 1f, false);
                dark.Cylinder(new Vector3(rx, 0f, -HZ + 0.4f), new Vector3(rx, 0.08f, -HZ + 0.4f), 0.17f, 12, 1f);
                dark.Cylinder(new Vector3(rx, 1.3f, -HZ + 0.4f), new Vector3(rx, 1.4f, -HZ + 0.4f), 0.15f, 12, 1f);
                red.Cylinder(new Vector3(rx, 1.35f, -HZ + 0.55f), new Vector3(rx, 1.35f, -HZ + 0.62f), 0.2f, 14, 1f);
            }

            // Sprinkler lines with heads
            foreach (float z in new[] { -3.2f, 3.2f })
            {
                red.Cylinder(new Vector3(-HX + 0.4f, 18.85f, z), new Vector3(HX - 0.4f, 18.85f, z), 0.035f, 8, 1f);
                for (float x = -HX + 1.75f; x < HX; x += 3.5f)
                {
                    pipe.Cylinder(new Vector3(x, 18.85f, z), new Vector3(x, 18.65f, z), 0.015f, 6, 1f, false);
                    pipe.Cylinder(new Vector3(x, 18.65f, z), new Vector3(x, 18.62f, z), 0.04f, 8, 1f);
                }
            }

            // Cable trays with cables
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * 6f;
                float y = 18.4f;
                Add("PipeDark", new Vector3(x - 0.3f, y, -HZ + 0.5f), new Vector3(x + 0.3f, y + 0.025f, HZ - 1.5f), 1f, false);
                Add("PipeDark", new Vector3(x - 0.3f, y, -HZ + 0.5f), new Vector3(x - 0.28f, y + 0.12f, HZ - 1.5f), 1f, false);
                Add("PipeDark", new Vector3(x + 0.28f, y, -HZ + 0.5f), new Vector3(x + 0.3f, y + 0.12f, HZ - 1.5f), 1f, false);
                var cable = Acc(render, "Cable");
                for (int k = 0; k < 4; k++)
                    cable.Cylinder(new Vector3(x - 0.18f + k * 0.12f, y + 0.06f, -HZ + 0.5f), new Vector3(x - 0.18f + k * 0.12f, y + 0.06f, HZ - 1.5f), 0.035f, 6, 1f, false);
                for (float z = -HZ + 2f; z < HZ - 1f; z += 3f)
                {
                    Rod(new Vector3(x - 0.28f, y, z), slabUnder);
                    Rod(new Vector3(x + 0.28f, y, z), slabUnder);
                }
            }
        }

        // ---------- Wall slot vents (floor-wall joint) ----------

        static readonly List<(Vector3 center, Vector3 size)> slotVents = new();

        static void BuildSlotVents()
        {
            slotVents.Clear();
            foreach (float s in new[] { -1f, 1f })
                foreach (var (z0, z1) in new[] { (-10f, -6f), (6f, 10f), (-2f, 2f) })
                {
                    float xIn = s * (HX - 0.55f), xOut = s * (HX - 0.15f);
                    float x0 = Mathf.Min(xIn, xOut), x1 = Mathf.Max(xIn, xOut);
                    Add("PipeDark", new Vector3(x0 - 0.04f, -0.15f, z0 - 0.04f), new Vector3(x1 + 0.04f, 0.012f, z1 + 0.04f), 1f, false);
                    for (float z = z0 + 0.06f; z < z1; z += 0.09f)
                        Add("MetalLight", new Vector3(x0, 0.012f, z - 0.018f), new Vector3(x1, 0.02f, z + 0.018f), 1f, false);
                    slotVents.Add((new Vector3((x0 + x1) * 0.5f, 0.05f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.1f, z1 - z0)));
                }
        }

        // ---------- Target plates ----------

        static void TargetPlateStand(Vector3 basePos)
        {
            var t = new GameObject("TargetPlate").transform;
            t.SetParent(root, false);
            t.position = basePos;
            t.rotation = Quaternion.Euler(0f, -90f, 0f);   // faces -x (toward the armory)
            Prim(PrimitiveType.Cube, "Base", t, new Vector3(0f, 0.05f, 0f), new Vector3(0.8f, 0.1f, 0.5f), Vector3.zero, mats["PipeDark"], true);
            Prim(PrimitiveType.Cube, "Post", t, new Vector3(0f, 0.85f, -0.05f), new Vector3(0.08f, 1.6f, 0.08f), Vector3.zero, mats["PipeDark"]);
            Prim(PrimitiveType.Cube, "Arm", t, new Vector3(0f, 1.65f, 0.02f), new Vector3(0.5f, 0.06f, 0.06f), Vector3.zero, mats["PipeDark"]);
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(t, false);
            hinge.localPosition = new Vector3(0f, 1.62f, 0.06f);
            var plate = Prim(PrimitiveType.Cylinder, "Plate", hinge, new Vector3(0f, -0.42f, 0.02f), new Vector3(0.6f, 0.012f, 0.6f), new Vector3(90f, 0f, 0f), mats["TargetSteel"]);
            var ring = Prim(PrimitiveType.Cylinder, "Bullseye", hinge, new Vector3(0f, -0.42f, 0.035f), new Vector3(0.2f, 0.004f, 0.2f), new Vector3(90f, 0f, 0f), mats["OrangeStrip"], false, false);
            var col = plate.AddComponent<BoxCollider>();
            col.size = new Vector3(1f, 6f, 1f);
            t.gameObject.AddComponent<Health>().SetMax(60f);
            var tp = t.gameObject.AddComponent<TargetPlate>();
            SetRef(tp, "hinge", hinge);
            SetRef(tp, "plate", plate.GetComponent<Renderer>());
        }

        // ---------- South wing: service hall and firing range ----------

        // Doorway in the atrium's south wall (east blast door)
        const float DoorX0 = 10.3f, DoorX1 = 15.7f, DoorH = 4.4f;
        // West doorway into the food court (mirror of the east one)
        const float WDoorX0 = -15.7f, WDoorX1 = -10.3f;
        // Hall interior
        const float HallX0 = 10f, HallX1 = 16f, HallZ1 = -19.2f, HallZ0 = -36f, HallH = 4.6f;
        // Range interior
        const float RangeX0 = 16.4f, RangeX1 = 52f, RangeZ0 = -40f, RangeZ1 = -22f, RangeH = 8f;
        // Hall-to-range door (in the shared wall at x = 16..16.4)
        const float RDoorZ0 = -24.2f, RDoorZ1 = -22.6f, RDoorH = 2.6f;

        static GameObject DoorLeaf(string name, Transform parent, Vector3 center, Vector3 size)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, center, size, Vector3.zero, mats["Metal"], true);
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var mod = go.AddComponent<Unity.AI.Navigation.NavMeshModifier>();
            mod.ignoreFromBuild = true;
            Prim(PrimitiveType.Cube, "Seam", go.transform, new Vector3(0f, 0.02f, 0f), new Vector3(1.002f, 0.012f, 1.04f), Vector3.zero, mats["CyanStrip"]);
            Prim(PrimitiveType.Cube, "Stripe", go.transform, new Vector3(0f, -0.38f, 0f), new Vector3(1.002f, 0.06f, 1.04f), Vector3.zero, mats["Hazard"]);
            return go;
        }

        static AutoDoor MakeDoor(string name, Vector3 sensor, bool locked, out Transform t)
        {
            t = new GameObject(name).transform;
            t.SetParent(root, false);
            t.position = sensor;
            var door = t.gameObject.AddComponent<AutoDoor>();
            SetBool(door, "locked", locked);
            return door;
        }

        static void BuildWing()
        {
            // Floor, outer walls, and ceilings for the whole wing
            Add("Floor", new Vector3(HallX0 - 0.4f, -1f, Mathf.Min(HallZ0, RangeZ0) - 0.4f), new Vector3(RangeX1 + 0.4f, 0f, HallZ1), 4f);

            // Hall walls
            Add("Concrete", new Vector3(HallX0 - 0.4f, -1f, HallZ0 - 0.4f), new Vector3(HallX0, HallH + 0.6f, RDoorZ0), 1f);          // west, south of the armory door
            Add("Concrete", new Vector3(HallX0 - 0.4f, RDoorH, RDoorZ0), new Vector3(HallX0, HallH + 0.6f, RDoorZ1), 1f);            // armory door lintel
            Add("Concrete", new Vector3(HallX0 - 0.4f, -1f, RDoorZ1), new Vector3(HallX0, HallH + 0.6f, HallZ1), 1f);                // west, north of it
            Add("Concrete", new Vector3(HallX0 - 0.4f, -1f, HallZ0 - 0.4f), new Vector3(HallX1 + 0.4f, HallH + 0.6f, HallZ0), 1f);  // south end
            Add("ConcreteDark", new Vector3(HallX0 - 0.4f, HallH, HallZ0 - 0.4f), new Vector3(HallX1 + 0.4f, HallH + 0.6f, HallZ1), 1f);  // ceiling

            // Shared hall/range wall (x 16..16.4): solid ends, a window band, and the range door
            float wx0 = HallX1, wx1 = HallX1 + 0.4f, top = RangeH + 0.6f;
            Add("Concrete", new Vector3(wx0, -1f, Mathf.Min(HallZ0, RangeZ0) - 0.4f), new Vector3(wx1, top, -34.4f), 1f);
            Add("Concrete", new Vector3(wx0, -1f, -34.4f), new Vector3(wx1, 1.0f, -25f), 1f);          // below the windows
            Add("Concrete", new Vector3(wx0, 2.4f, -34.4f), new Vector3(wx1, top, -25f), 1f);           // above the windows
            Add("Concrete", new Vector3(wx0, -1f, -25f), new Vector3(wx1, top, RDoorZ0), 1f);           // pier
            Add("Concrete", new Vector3(wx0, RDoorH, RDoorZ0), new Vector3(wx1, top, RDoorZ1), 1f);     // door lintel
            Add("Concrete", new Vector3(wx0, -1f, RDoorZ1), new Vector3(wx1, top, HallZ1), 1f);
            var glass = Acc(render, "Glass");
            float paneW = (34.4f - 25f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float z0 = -34.4f + i * paneW, z1 = z0 + paneW;
                glass.Card(new Vector3(wx0 + 0.2f, 1.7f, (z0 + z1) * 0.5f), Vector3.forward * (paneW * 0.5f - 0.08f), Vector3.up * 0.7f);
                if (i > 0) Add("Metal", new Vector3(wx0 + 0.1f, 1.0f, z0 - 0.08f), new Vector3(wx1 - 0.1f, 2.4f, z0 + 0.08f), 1f);
            }
            Add("Metal", new Vector3(wx0 - 0.04f, 0.96f, -34.4f), new Vector3(wx1 + 0.04f, 1.04f, -25f), 1f, false);   // sill
            Acc(collide, "col").BoxMinMax(new Vector3(wx0 + 0.15f, 1.0f, -34.4f), new Vector3(wx1 - 0.15f, 2.4f, -25f), 1f);

            // Range shell
            Add("Concrete", new Vector3(RangeX0, -1f, RangeZ1), new Vector3(RangeX1 + 0.4f, top, RangeZ1 + 0.4f), 1f);   // north
            Add("Concrete", new Vector3(RangeX0, -1f, RangeZ0 - 0.4f), new Vector3(RangeX1 + 0.4f, top, RangeZ0), 1f);   // south
            Add("Concrete", new Vector3(RangeX1, -1f, RangeZ0), new Vector3(RangeX1 + 0.4f, top, RangeZ1), 1f);           // east (downrange)
            Add("ConcreteDark", new Vector3(RangeX0 - 0.4f, RangeH, RangeZ0 - 0.4f), new Vector3(RangeX1 + 0.4f, top, RangeZ1 + 0.4f), 1f);

            // Hall finishes: metal cladding band with a cyan base strip set slightly proud of it (no z-fighting),
            // broken at the window band and the range door
            void Clad(float xWall, float dir, float z0, float z1)
            {
                float c0 = xWall, c1 = xWall + dir * 0.08f, s1 = c1 + dir * 0.015f;
                Add("Metal", new Vector3(Mathf.Min(c0, c1), 0f, z0), new Vector3(Mathf.Max(c0, c1), 1.1f, z1), 2f, false);
                Add("CyanStrip", new Vector3(Mathf.Min(c1, s1), 0.05f, z0 + 0.02f), new Vector3(Mathf.Max(c1, s1), 0.1f, z1 - 0.02f), 1f, false);
            }
            Clad(HallX0, 1f, HallZ0, RDoorZ0);
            Clad(HallX0, 1f, RDoorZ1, HallZ1);
            Clad(HallX1, -1f, HallZ0, -34.4f);
            Clad(HallX1, -1f, -25f, RDoorZ0);
            Clad(HallX1, -1f, RDoorZ1, HallZ1);
            for (float z = HallZ1 - 2.5f; z > HallZ0 + 1f; z -= 4f)
            {
                Add("CeilingLight", new Vector3(12.2f, HallH - 0.05f, z - 1.2f), new Vector3(13.8f, HallH - 0.01f, z + 1.2f), 1f, false);
                AreaLight(new Vector3(13f, HallH - 0.07f, z), new Vector2(1.5f, 2.3f), 5f);
            }
            Sign("FIRING RANGE  >", new Vector3(HallX1 - 0.04f, 3.1f, -23.4f), 3.6f, Palette.StTechCyan, 0.9f, -90f);
            Sign("<  ARMORY", new Vector3(HallX0 + 0.04f, 3.1f, -23.4f), 3.6f, Palette.StAccentOrange, 0.9f, 90f);
            Sign("SECTOR C-2", new Vector3(13f, 3.6f, HallZ0 + 0.04f), 5f, Palette.StPanelLight, 0.6f, 0f);
            // Sealed door at the end of the hall (future expansion)
            Add("Hazard", new Vector3(11.4f, 0f, HallZ0), new Vector3(14.6f, 3.3f, HallZ0 + 0.12f), 1.2f, false);
            Add("Metal", new Vector3(11.7f, 0f, HallZ0 + 0.12f), new Vector3(14.3f, 3f, HallZ0 + 0.24f), 2f, false);
            Add("RedLight", new Vector3(12.8f, 3.1f, HallZ0 + 0.12f), new Vector3(13.2f, 3.25f, HallZ0 + 0.2f), 1f, false);

            // Range: an open shooting floor with a firing-line counter, a floor stripe, a backstop, and a light grid
            float lineX = 19.4f;
            const float cz0 = -35.5f, cz1 = -22.6f;
            Add("Metal", new Vector3(lineX - 0.8f, 0f, cz0), new Vector3(lineX, 0.98f, cz1), 2f);
            Add("MetalLight", new Vector3(lineX - 0.86f, 0.98f, cz0 - 0.05f), new Vector3(lineX + 0.06f, 1.02f, cz1 + 0.05f), 1f, false);
            Add("OrangeStrip", new Vector3(lineX, 0.03f, cz0), new Vector3(lineX + 0.02f, 0.06f, cz1), 1f, false);
            Add("Hazard", new Vector3(lineX + 0.15f, 0.001f, RangeZ0), new Vector3(lineX + 0.45f, 0.012f, RangeZ1), 1.2f, false);
            // Backstop: dark acoustic panels and a sloped catch berm
            for (float z = RangeZ0 + 0.6f; z < RangeZ1 - 0.5f; z += 1.6f)
                for (float y = 0.4f; y < RangeH - 0.5f; y += 1.6f)
                    Add("PipeDark", new Vector3(RangeX1 - 0.18f, y, z), new Vector3(RangeX1, y + 1.45f, z + 1.45f), 1f, false);
            AddRot("PipeDark", new Vector3(RangeX1 - 1.6f, 0.9f, (RangeZ0 + RangeZ1) * 0.5f), new Vector3(2.8f, 0.7f, RangeZ1 - RangeZ0), Quaternion.Euler(0f, 0f, 35f), 1f);
            Add("RedLight", new Vector3(RangeX0 + 0.02f, RDoorH + 0.3f, RDoorZ0 + 0.4f), new Vector3(RangeX0 + 0.1f, RDoorH + 0.5f, RDoorZ1 - 0.4f), 1f, false);
            foreach (float z in new[] { -37f, -31f, -25f })
            {
                Add("CeilingLight", new Vector3(RangeX0 + 2f, RangeH - 0.05f, z - 0.3f), new Vector3(RangeX1 - 2.5f, RangeH - 0.01f, z + 0.3f), 1f, false);
                AreaLight(new Vector3((RangeX0 + RangeX1) * 0.5f + 0.0f, RangeH - 0.07f, z), new Vector2(RangeX1 - RangeX0 - 4.5f, 0.6f), 7f);
            }
            Sign("RANGE  /  EYES AND EARS ON", new Vector3(RangeX0 + 0.04f, 5.6f, -31f), 5f, Palette.StHazard, 0.85f, 90f);

            // Weapons and supplies on the counter
            // Weapons, ammo and medkits are in the armory now
            // Targets scattered across the open floor at mixed distances
            var targets = new[] { (8f, -26f), (12f, -33f), (16f, -29f), (21f, -37f), (25f, -24.5f), (30f, -31.5f) };
            foreach (var (d, z) in targets)
                TargetPlateStand(new Vector3(lineX + d, 0f, z));

            // Doors: the atrium blast door into the hall, and the hall door into the range
            var main = MakeDoor("Door_Hall", new Vector3((DoorX0 + DoorX1) * 0.5f, 0f, -HZ - WallT * 0.5f), false, out var mt);
            float half = (DoorX1 - DoorX0) * 0.5f;
            var l = DoorLeaf("LeafL", mt, new Vector3(-half * 0.5f, DoorH * 0.5f, 0f), new Vector3(half + 0.02f, DoorH, 0.3f));
            var r = DoorLeaf("LeafR", mt, new Vector3(half * 0.5f, DoorH * 0.5f, 0f), new Vector3(half + 0.02f, DoorH, 0.3f));
            SetRefArray(main, "leaves", new Object[] { l.transform, r.transform });
            SetVectorArray(main, "openOffsets", new[] { new Vector3(-half + 0.1f, 0f, 0f), new Vector3(half - 0.1f, 0f, 0f) });
            SetFloat(main, "openDistance", 5f);

            var rd = MakeDoor("Door_Range", new Vector3(HallX1 + 0.2f, 0f, (RDoorZ0 + RDoorZ1) * 0.5f), false, out var rt);
            var leaf = DoorLeaf("Leaf", rt, new Vector3(0f, RDoorH * 0.5f, 0f), new Vector3(0.2f, RDoorH, RDoorZ1 - RDoorZ0 + 0.02f));
            SetRefArray(rd, "leaves", new Object[] { leaf.transform });
            SetVectorArray(rd, "openOffsets", new[] { new Vector3(0f, 0f, RDoorZ1 - RDoorZ0 - 0.05f) });
            SetFloat(rd, "openDistance", 3.2f);

            // Atrium ambience, contained by walls: heard through the hall door, then the range door
            var amb = new GameObject("AtriumAmbience").AddComponent<Alcax.FX.RoomAmbience>();
            amb.transform.SetParent(root, false);
            SetRef(amb, "clip", AssetDatabase.LoadAssetAtPath<AudioClip>($"{Root}/Audio/Ambience/atrium_ambience.mp3"));
            var ambSo = new SerializedObject(amb);
            ambSo.FindProperty("roomMin").vector3Value = new Vector3(-HX, -1f, -HZ);
            ambSo.FindProperty("roomMax").vector3Value = new Vector3(HX, CeilY + 1f, HZ + 2f);
            ambSo.ApplyModifiedPropertiesWithoutUndo();
            Alcax.FX.AmbienceZone Zone(string name, Vector3 min, Vector3 max, AutoDoor door, Vector3 portal, Alcax.FX.AmbienceZone next)
            {
                var z = new GameObject("AmbienceZone_" + name).AddComponent<Alcax.FX.AmbienceZone>();
                z.transform.SetParent(amb.transform, false);
                var p = new GameObject("Portal").transform;
                p.SetParent(z.transform, false);
                p.position = portal;
                var so = new SerializedObject(z);
                so.FindProperty("min").vector3Value = min;
                so.FindProperty("max").vector3Value = max;
                so.FindProperty("door").objectReferenceValue = door;
                so.FindProperty("portal").objectReferenceValue = p;
                so.FindProperty("next").objectReferenceValue = next;
                so.ApplyModifiedPropertiesWithoutUndo();
                return z;
            }
            var hallZone = Zone("Hall", new Vector3(HallX0 - 0.4f, -1f, HallZ0 - 0.4f), new Vector3(HallX1 + 0.4f, HallH + 1f, -HZ),
                main, new Vector3((DoorX0 + DoorX1) * 0.5f, 2.2f, -HZ - WallT * 0.5f), null);
            var rangeZone = Zone("Range", new Vector3(RangeX0, -1f, RangeZ0 - 0.4f), new Vector3(RangeX1 + 0.4f, RangeH + 1f, RangeZ1 + 0.4f),
                rd, new Vector3(HallX1 + 0.2f, 1.3f, (RDoorZ0 + RDoorZ1) * 0.5f), hallZone);
            SetRefArray(amb, "zones", new Object[] { hallZone, rangeZone });
            ambRef = amb; hallZoneRef = hallZone;

            // Probes for the wing
            WingProbe(new Vector3((HallX0 + HallX1) * 0.5f, 2.3f, (HallZ0 + HallZ1) * 0.5f), new Vector3(HallX1 - HallX0, HallH, HallZ1 - HallZ0));
            WingProbe(new Vector3((RangeX0 + RangeX1) * 0.5f, 2.6f, (RangeZ0 + RangeZ1) * 0.5f), new Vector3(RangeX1 - RangeX0, RangeH, RangeZ1 - RangeZ0));
        }

/// <summary>A restocking display slot holding one pickup (see PickupRack).</summary>
        static void Rack(Transform parent, string prefabName, Vector3 pos, float restock, float yaw = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/Pickups/{prefabName}.prefab");
            if (prefab == null) { Debug.LogWarning("[AtriumBuilder] Missing pickup prefab " + prefabName); return; }
            var slot = new GameObject("Rack_" + prefabName).transform;
            slot.SetParent(parent, false);
            slot.position = pos;
            slot.rotation = Quaternion.Euler(0f, yaw, 0f);
            var rack = slot.gameObject.AddComponent<Alcax.Loot.PickupRack>();
            SetRef(rack, "prefab", prefab);
            SetFloat(rack, "restockSeconds", restock);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot);
            go.transform.SetPositionAndRotation(pos, slot.rotation);
        }

        static void WallClad(float xWall, float dir, float z0, float z1, string strip = "CyanStrip")
        {
            float c0 = xWall, c1 = xWall + dir * 0.08f, s1 = c1 + dir * 0.015f;
            Add("Metal", new Vector3(Mathf.Min(c0, c1), 0f, z0), new Vector3(Mathf.Max(c0, c1), 1.1f, z1), 2f, false);
            Add(strip, new Vector3(Mathf.Min(c1, s1), 0.05f, z0 + 0.02f), new Vector3(Mathf.Max(c1, s1), 0.1f, z1 - 0.02f), 1f, false);
        }

        static void WallCladZ(float zWall, float dir, float x0, float x1, string strip = "CyanStrip")
        {
            float c0 = zWall, c1 = zWall + dir * 0.08f, s1 = c1 + dir * 0.015f;
            Add("Metal", new Vector3(x0, 0f, Mathf.Min(c0, c1)), new Vector3(x1, 1.1f, Mathf.Max(c0, c1)), 2f, false);
            Add(strip, new Vector3(x0 + 0.02f, 0.05f, Mathf.Min(c1, s1)), new Vector3(x1 - 0.02f, 0.1f, Mathf.Max(c1, s1)), 1f, false);
        }

        // ---------- Armory (west of the hall, door across from the range door) ----------

        const float AX0 = 2.6f, AX1 = HallX0 - 0.4f, AZ0 = -34f, AZ1 = -20.2f, AH = 4.2f;
        static AutoDoor armoryDoor, foodDoor;
        static Alcax.FX.RoomAmbience ambRef;
        static Alcax.FX.AmbienceZone hallZoneRef;

        static void BuildArmory()
        {
            // Shell
            Add("Floor", new Vector3(AX0 - 0.4f, -1f, AZ0 - 0.4f), new Vector3(AX1, 0f, AZ1 + 0.4f), 4f);
            Add("Concrete", new Vector3(AX0 - 0.4f, -1f, AZ0 - 0.4f), new Vector3(AX0, AH + 0.6f, AZ1 + 0.4f), 1f);   // west
            Add("Concrete", new Vector3(AX0, -1f, AZ0 - 0.4f), new Vector3(AX1, AH + 0.6f, AZ0), 1f);                 // south
            Add("Concrete", new Vector3(AX0, -1f, AZ1), new Vector3(AX1, AH + 0.6f, AZ1 + 0.4f), 1f);                 // north
            Add("ConcreteDark", new Vector3(AX0 - 0.4f, AH, AZ0 - 0.4f), new Vector3(AX1, AH + 0.6f, AZ1 + 0.4f), 1f);  // ceiling
            WallClad(AX1, -1f, AZ0, RDoorZ0, "OrangeStrip");
            WallClad(AX1, -1f, RDoorZ1, AZ1, "OrangeStrip");
            WallCladZ(AZ0, 1f, AX0, AX1, "OrangeStrip");
            WallCladZ(AZ1, -1f, AX0, AX1, "OrangeStrip");

            // Weapon wall: a dark pegboard with an orange rail, a long lit display counter, and a label over each slot
            Add("PipeDark", new Vector3(AX0, 1.05f, -32.9f), new Vector3(AX0 + 0.06f, 3.35f, -21.6f), 1f, false);
            Add("OrangeStrip", new Vector3(AX0 + 0.06f, 3.35f, -32.9f), new Vector3(AX0 + 0.09f, 3.39f, -21.6f), 1f, false);
            Add("Metal", new Vector3(AX0 + 0.3f, 0f, -32.6f), new Vector3(AX0 + 1.1f, 0.95f, -21.8f), 2f);
            Add("MetalLight", new Vector3(AX0 + 0.26f, 0.95f, -32.65f), new Vector3(AX0 + 1.14f, 0.99f, -21.75f), 1f, false);
            Add("CyanStrip", new Vector3(AX0 + 1.1f, 0.05f, -32.6f), new Vector3(AX0 + 1.12f, 0.1f, -21.8f), 1f, false);
            var armory = new GameObject("Armory").transform;
            armory.SetParent(root, false);
            foreach (var (prefab, label, z) in new[] { ("P_Pistol", "SIDEARM", -31.4f), ("P_Shotgun", "SHOTGUN", -29.2f), ("P_Rifle", "RIFLE", -27f), ("P_Machete", "MACHETE", -24.8f), ("P_Bat", "BAT", -22.8f) })
            {
                Rack(armory, prefab, new Vector3(AX0 + 0.7f, 1.02f, z), 20f, 90f);
                Sign(label, new Vector3(AX0 + 0.1f, 2.85f, z), 1.8f, Palette.StPanelLight, 0.85f, 90f);
                Add("PanelLight", new Vector3(AX0 + 0.07f, 3.05f, z - 0.55f), new Vector3(AX0 + 0.11f, 3.1f, z + 0.55f), 1f, false);
            }
            AreaLight(new Vector3(AX0 + 1.1f, AH - 0.07f, -27.2f), new Vector2(1.1f, 10.5f), 4.5f);

            // Ammo shelving along the north wall: lower shelf restocks ammo, upper shelf holds crates
            float sz0 = AZ1 - 0.75f, sz1 = AZ1 - 0.05f;
            foreach (float x in new[] { 3.3f, 5.9f, 8.5f }) Add("PipeDark", new Vector3(x - 0.05f, 0f, sz0), new Vector3(x + 0.05f, 2.4f, sz1), 1f, false);
            foreach (float y in new[] { 0.9f, 1.75f }) Add("MetalLight", new Vector3(3.25f, y - 0.04f, sz0), new Vector3(8.55f, y, sz1), 1f, false);
            Acc(collide, "col").BoxMinMax(new Vector3(3.25f, 0f, sz0), new Vector3(8.55f, 2.4f, sz1), 1f);
            foreach (var (prefab, x) in new[] { ("P_AmmoLight", 4.0f), ("P_Shells", 5.3f), ("P_AmmoRifle", 6.6f), ("P_AmmoLight", 7.8f) })
                Rack(armory, prefab, new Vector3(x, 0.93f, AZ1 - 0.42f), 12f, 180f);
            foreach (var (x, w, mat) in new[] { (3.7f, 0.7f, "Hazard"), (4.7f, 0.5f, "Metal"), (5.6f, 0.8f, "PipeDark"), (6.8f, 0.6f, "Hazard"), (7.8f, 0.6f, "Metal") })
                Add(mat, new Vector3(x - w * 0.5f, 1.75f, sz0 + 0.1f), new Vector3(x + w * 0.5f, 2.15f, sz1 - 0.1f), 1f, false);

            // Medical cabinet with a red cross on the south wall, and a medkit bench beside it
            Add("WhiteCabinet", new Vector3(3.4f, 1.0f, AZ0), new Vector3(5.0f, 2.3f, AZ0 + 0.35f), 1f, false);
            Add("RedLight", new Vector3(4.12f, 1.35f, AZ0 + 0.35f), new Vector3(4.28f, 1.95f, AZ0 + 0.38f), 1f, false);
            Add("RedLight", new Vector3(3.9f, 1.57f, AZ0 + 0.35f), new Vector3(4.5f, 1.73f, AZ0 + 0.38f), 1f, false);
            Add("Metal", new Vector3(5.4f, 0f, AZ0 + 0.1f), new Vector3(6.9f, 0.85f, AZ0 + 0.8f), 2f);
            Add("MetalLight", new Vector3(5.38f, 0.85f, AZ0 + 0.08f), new Vector3(6.92f, 0.89f, AZ0 + 0.82f), 1f, false);
            Rack(armory, "P_Medkit", new Vector3(5.8f, 0.92f, AZ0 + 0.45f), 25f);
            Rack(armory, "P_Medkit", new Vector3(6.5f, 0.92f, AZ0 + 0.45f), 25f);
            // Lockers in the south-east corner
            for (int i = 0; i < 3; i++)
            {
                float x = 7.35f + i * 0.58f;
                Add("Metal", new Vector3(x, 0f, AZ0), new Vector3(x + 0.55f, 2.05f, AZ0 + 0.55f), 2f);
                Add("CyanStrip", new Vector3(x + 0.05f, 1.75f, AZ0 + 0.55f), new Vector3(x + 0.5f, 1.77f, AZ0 + 0.57f), 1f, false);
                Add("PipeDark", new Vector3(x + 0.42f, 1.0f, AZ0 + 0.55f), new Vector3(x + 0.46f, 1.2f, AZ0 + 0.58f), 1f, false);
            }

            // Cleaning bench in the middle, with magazine boxes and an ammo can
            Add("Metal", new Vector3(5.0f, 0f, -29.6f), new Vector3(7.4f, 0.88f, -28.4f), 2f);
            Add("MetalLight", new Vector3(4.96f, 0.88f, -29.64f), new Vector3(7.44f, 0.92f, -28.36f), 1f, false);
            Add("PipeDark", new Vector3(5.3f, 0.92f, -29.2f), new Vector3(5.75f, 1.02f, -28.85f), 1f, false);
            Add("PipeDark", new Vector3(5.85f, 0.92f, -29.25f), new Vector3(6.25f, 1.0f, -28.95f), 1f, false);
            Add("Hazard", new Vector3(6.6f, 0.92f, -29.25f), new Vector3(7.05f, 1.18f, -28.9f), 1f, false);
            // Quartermaster's desk by the door
            Add("Metal", new Vector3(7.2f, 0f, -26.6f), new Vector3(8.9f, 1.0f, -25.9f), 2f);
            Add("ConsoleTop", new Vector3(7.25f, 1.0f, -26.55f), new Vector3(8.85f, 1.04f, -25.95f), 1f, false);

            // Light, signs, threshold
            foreach (float z in new[] { -31.5f, -27f, -22.5f })
            {
                Add("CeilingLight", new Vector3(5.2f, AH - 0.05f, z - 1.1f), new Vector3(6.8f, AH - 0.01f, z + 1.1f), 1f, false);
                AreaLight(new Vector3(6f, AH - 0.07f, z), new Vector2(1.5f, 2.1f), 4f);
            }
            Sign("ARMORY", new Vector3(6.1f, 3.45f, AZ1 - 0.06f), 4.2f, Palette.StAccentOrange, 0.9f, 180f);
            Sign("SIGN OUT EVERY WEAPON", new Vector3(6.1f, 2.95f, AZ1 - 0.06f), 1.6f, Palette.StPanelLight, 0.6f, 180f);
            Add("Hazard", new Vector3(AX1 - 0.6f, 0.001f, RDoorZ0), new Vector3(AX1, 0.012f, RDoorZ1), 1.2f, false);
            Add("WarmLight", new Vector3(AX1 - 0.1f, RDoorH + 0.3f, RDoorZ0 + 0.4f), new Vector3(AX1 - 0.02f, RDoorH + 0.5f, RDoorZ1 - 0.4f), 1f, false);

            // Door (slides into the wall toward the south)
            armoryDoor = MakeDoor("Door_Armory", new Vector3(HallX0 - 0.2f, 0f, (RDoorZ0 + RDoorZ1) * 0.5f), false, out var at);
            var leaf = DoorLeaf("Leaf", at, new Vector3(0f, RDoorH * 0.5f, 0f), new Vector3(0.2f, RDoorH, RDoorZ1 - RDoorZ0 + 0.02f));
            SetRefArray(armoryDoor, "leaves", new Object[] { leaf.transform });
            SetVectorArray(armoryDoor, "openOffsets", new[] { new Vector3(0f, 0f, -(RDoorZ1 - RDoorZ0 - 0.05f)) });
            SetFloat(armoryDoor, "openDistance", 3.2f);

            WingProbe(new Vector3((AX0 + AX1) * 0.5f, 2.1f, (AZ0 + AZ1) * 0.5f), new Vector3(AX1 - AX0, AH, AZ1 - AZ0));
        }

        // ---------- Food court (through the west blast door) ----------

        const float FX0 = -27f, FX1 = -1f, FZ0 = -35f, FZ1 = -HZ - WallT, FH = 5.5f;
static readonly List<(Vector3 pos, Vector3 facing)> cafeSeats = new();

// ---------- Airport hotel (east of the atrium): hall and lobby at mezzanine level, a room upstairs ----------

        const float HtlDoorZ0 = -16.4f, HtlDoorZ1 = -13.6f;              // the doorway in the atrium's east wall at mezzanine level
        const float HtlHallX1 = 35f;                                      // hall from the atrium wall to the lobby
        const float LobX0 = 35f, LobX1 = 47f, LobZ0 = -18f, LobZ1 = -5f, LobTop = MezzY + 5f;
        const float ShX0 = 40.1f, ShX1 = 41.9f, ShZ0 = -4.6f, ShZ1 = -2.8f;   // elevator shaft interior
        const float UpY = MezzY + 6f;                                     // the hotel floor
        const float LndX0 = 36f, LndX1 = 46f, LndZ0 = -2.4f, LndZ1 = 3.2f, LndTop = UpY + 3f;
        const float RmX0 = 30.5f, RmX1 = 46f, RmZ0 = 3.6f, RmZ1 = 17.6f, RmTop = UpY + 3.8f;
        static AutoDoor hotelLobbyDoor, liftLobbyDoor, liftUpDoor, roomDoor, bathDoor;
        static readonly List<(Vector3 pos, Vector3 facing)> lobbySeats = new();

        static void BuildHotel()
        {
            lobbySeats.Clear();
            float m = MezzY, u = UpY;
            var hotel = new GameObject("AirportHotel").transform;
            hotel.SetParent(root, false);

            // ---- Structure: a solid block under everything, so the floors sit on something and the dunes stay outside ----
            Add("Concrete", new Vector3(HX + WallT, -1f, -HZ - 0.4f), new Vector3(LobX1 + 0.4f, m - 0.4f, LobZ1), 1f);           // under the hall and lobby
            Add("Concrete", new Vector3(HX + WallT, -1f, LobZ1), new Vector3(ShX0 - 0.3f, u - 0.4f, RmZ1 + 0.2f), 1f);           // under the upper floor, west of the shaft
            Add("Concrete", new Vector3(ShX1 + 0.3f, -1f, LobZ1), new Vector3(LobX1 + 0.4f, u - 0.4f, RmZ1 + 0.2f), 1f);         // east of the shaft
            Add("Concrete", new Vector3(ShX0 - 0.3f, -1f, ShZ1 + 0.4f), new Vector3(ShX1 + 0.3f, u - 0.4f, RmZ1 + 0.2f), 1f);   // north of the shaft
            Add("Concrete", new Vector3(ShX0 - 0.3f, -1f, LobZ1), new Vector3(ShX1 + 0.3f, m - 0.6f, ShZ1 + 0.4f), 1f);         // shaft pit floor

            // ---- Hall: from the atrium mezzanine to the lobby ----
            Add("Floor", new Vector3(HX + WallT, m - 0.4f, HtlDoorZ0), new Vector3(HtlHallX1, m, HtlDoorZ1), 2f);
            Add("Concrete", new Vector3(HX + WallT, m - 0.4f, HtlDoorZ0 - 0.4f), new Vector3(HtlHallX1, m + 3.8f, HtlDoorZ0), 1f);
            Add("Concrete", new Vector3(HX + WallT, m - 0.4f, HtlDoorZ1), new Vector3(HtlHallX1, m + 3.8f, HtlDoorZ1 + 0.4f), 1f);
            Add("ConcreteDark", new Vector3(HX + WallT, m + 3.4f, HtlDoorZ0 - 0.4f), new Vector3(HtlHallX1, m + 3.8f, HtlDoorZ1 + 0.4f), 1f);
            Add("CyanStrip", new Vector3(HX + WallT, m + 0.05f, HtlDoorZ0), new Vector3(HtlHallX1, m + 0.1f, HtlDoorZ0 + 0.02f), 1f, false);
            Add("CyanStrip", new Vector3(HX + WallT, m + 0.05f, HtlDoorZ1 - 0.02f), new Vector3(HtlHallX1, m + 0.1f, HtlDoorZ1), 1f, false);
            Add("CeilingLight", new Vector3(30.2f, m + 3.35f, -15.4f), new Vector3(34.2f, m + 3.39f, -14.6f), 1f, false);
            AreaLight(new Vector3(32.2f, m + 3.3f, -15f), new Vector2(3.8f, 0.7f), 4f);
            Sign("AIRPORT HOTEL  >", new Vector3(HX - 0.06f, m + 3.95f, (HtlDoorZ0 + HtlDoorZ1) * 0.5f), 3.2f, Palette.StTechCyan, 0.9f, -90f);

            // ---- Lobby ----
            Add("Floor", new Vector3(LobX0, m - 0.4f, LobZ0), new Vector3(LobX1, m, LobZ1), 2f);
            Add("Concrete", new Vector3(LobX0 - 0.4f, m - 0.4f, LobZ0 - 0.4f), new Vector3(LobX0, LobTop + 0.4f, HtlDoorZ0), 1f);    // west, south of the entrance
            Add("Concrete", new Vector3(LobX0 - 0.4f, m - 0.4f, HtlDoorZ1), new Vector3(LobX0, LobTop + 0.4f, LobZ1 + 0.4f), 1f);    // west, north of it
            Add("Concrete", new Vector3(LobX0 - 0.4f, m + 3.0f, HtlDoorZ0), new Vector3(LobX0, LobTop + 0.4f, HtlDoorZ1), 1f);        // over the entrance
            Add("Concrete", new Vector3(LobX0, m - 0.4f, LobZ0 - 0.4f), new Vector3(LobX1 + 0.4f, LobTop + 0.4f, LobZ0), 1f);         // south
            Add("Concrete", new Vector3(LobX1, m - 0.4f, LobZ0), new Vector3(LobX1 + 0.4f, LobTop + 0.4f, LobZ1 + 0.4f), 1f);         // east
            Add("Concrete", new Vector3(LobX0, m - 0.4f, LobZ1), new Vector3(ShX0 + 0.3f, LobTop + 0.4f, LobZ1 + 0.4f), 1f);          // north, west of the lift door
            Add("Concrete", new Vector3(ShX1 - 0.3f, m - 0.4f, LobZ1), new Vector3(LobX1, LobTop + 0.4f, LobZ1 + 0.4f), 1f);          // north, east of it
            Add("Concrete", new Vector3(ShX0 + 0.3f, m + 2.4f, LobZ1), new Vector3(ShX1 - 0.3f, LobTop + 0.4f, LobZ1 + 0.4f), 1f);    // over the lift door
            Add("ConcreteDark", new Vector3(LobX0 - 0.4f, LobTop, LobZ0 - 0.4f), new Vector3(LobX1 + 0.4f, LobTop + 0.4f, LobZ1 + 0.4f), 1f);
            Add("Concrete", new Vector3(LobX0 - 0.4f, LobTop + 0.4f, LobZ0 - 0.4f), new Vector3(LobX1 + 0.4f, u - 0.4f, LobZ1 + 0.4f), 1f);   // mass above the lobby
            // Finishes: warm wood floor inlay, white wainscot with a cyan reveal, a backlit feature wall behind the desk
            Add("Wood", new Vector3(LobX0 + 1f, m + 0.001f, LobZ0 + 1f), new Vector3(LobX1 - 1f, m + 0.012f, LobZ1 - 1f), 2f, false);
            Add("WhiteCabinet", new Vector3(LobX0, m, LobZ0), new Vector3(LobX0 + 0.05f, m + 1.2f, HtlDoorZ0), 1f, false);
            Add("WhiteCabinet", new Vector3(LobX0, m, LobZ0), new Vector3(LobX1, m + 1.2f, LobZ0 + 0.05f), 1f, false);
            Add("CyanStrip", new Vector3(LobX0, m + 1.2f, LobZ0 + 0.05f), new Vector3(LobX1, m + 1.23f, LobZ0 + 0.08f), 1f, false);
            Add("MenuBoard", new Vector3(LobX1 - 0.08f, m + 1.3f, -14.5f), new Vector3(LobX1 - 0.02f, m + 4.2f, -8.5f), 1f, false);
            Add("PanelLight", new Vector3(LobX1 - 0.1f, m + 4.2f, -14.5f), new Vector3(LobX1 - 0.04f, m + 4.26f, -8.5f), 1f, false);
            Sign("AIRPORT HOTEL", new Vector3(LobX1 - 0.12f, m + 3.4f, -11.5f), 3.6f, Palette.StTechCyan, 0.95f, -90f);
            Sign("SKYPORT", new Vector3(LobX1 - 0.12f, m + 2.75f, -11.5f), 1.6f, Palette.StPanelLight, 0.7f, -90f);
            // Reception desk (faces the entrance), with a glowing front band
            Add("Metal", new Vector3(43.4f, m, -14.2f), new Vector3(44.3f, m + 1.08f, -8.8f), 2f);
            Add("WhiteCabinet", new Vector3(43.35f, m + 1.08f, -14.25f), new Vector3(44.35f, m + 1.13f, -8.75f), 1f, false);
            Add("CyanStrip", new Vector3(43.36f, m + 0.25f, -14.2f), new Vector3(43.38f, m + 0.3f, -8.8f), 1f, false);
            Add("ConsoleTop", new Vector3(43.9f, m + 1.13f, -12.8f), new Vector3(44.2f, m + 1.4f, -12.1f), 1f, false);
            Add("ConsoleTop", new Vector3(43.9f, m + 1.13f, -10.6f), new Vector3(44.2f, m + 1.4f, -9.9f), 1f, false);
            // Lounge: two sofas facing each other over a low table, planters in the corners, a luggage cart
            foreach (var (z, facing) in new[] { (-16.6f, Vector3.forward), (-11.6f, Vector3.back) })
            {
                Add("Seat", new Vector3(36.6f, m, z - 0.4f), new Vector3(40.2f, m + 0.45f, z + 0.4f), 1f);
                Add("Seat", new Vector3(36.6f, m + 0.45f, facing.z > 0 ? z - 0.4f : z + 0.2f), new Vector3(40.2f, m + 0.95f, facing.z > 0 ? z - 0.2f : z + 0.4f), 1f, false);
                for (int i = 0; i < 3; i++) lobbySeats.Add((new Vector3(37.2f + i * 1.2f, m, z + facing.z * 0.05f), facing));
            }
            Add("Wood", new Vector3(37.2f, m, -14.7f), new Vector3(39.6f, m + 0.38f, -13.5f), 1f);
            foreach (var (x, z) in new[] { (35.8f, -17.2f), (46.2f, -17.2f), (35.8f, -5.8f) })
            {
                Add("Metal", new Vector3(x - 0.45f, m, z - 0.45f), new Vector3(x + 0.45f, m + 0.75f, z + 0.45f), 1f);
                Add("Soil", new Vector3(x - 0.38f, m + 0.75f, z - 0.38f), new Vector3(x + 0.38f, m + 0.78f, z + 0.38f), 1f, false);
            }
            Add("MetalLight", new Vector3(45.2f, m + 0.15f, -6.9f), new Vector3(46.3f, m + 0.2f, -6.1f), 1f);
            foreach (var (x, z) in new[] { (45.25f, -6.85f), (46.25f, -6.85f), (45.25f, -6.15f), (46.25f, -6.15f) })
                Add("PipeDark", new Vector3(x - 0.02f, m, z - 0.02f), new Vector3(x + 0.02f, m + 1.6f, z + 0.02f), 1f, false);
            Add("PipeDark", new Vector3(45.2f, m + 1.6f, -6.9f), new Vector3(46.3f, m + 1.64f, -6.1f), 1f, false);
            Add("Hazard", new Vector3(45.4f, m + 0.2f, -6.8f), new Vector3(45.9f, m + 0.75f, -6.4f), 1f, false);
            Add("Metal", new Vector3(45.9f, m + 0.2f, -6.7f), new Vector3(46.2f, m + 0.55f, -6.2f), 1f, false);
            foreach (var (x, z) in new[] { (38f, -15f), (38f, -9f), (43f, -15f), (43f, -9f) })
            {
                Add("CeilingLight", new Vector3(x - 1.1f, LobTop - 0.05f, z - 0.8f), new Vector3(x + 1.1f, LobTop - 0.01f, z + 0.8f), 1f, false);
                AreaLight(new Vector3(x, LobTop - 0.07f, z), new Vector2(2f, 1.5f), 4f);
            }
            Sign("ELEVATOR", new Vector3(41f, m + 2.85f, LobZ1 - 0.06f), 1.8f, Palette.StPanelLight, 0.8f, 180f);
            // Entrance: glass double doors from the hall
            hotelLobbyDoor = MakeDoor("Door_HotelLobby", new Vector3(LobX0 - 0.2f, m, (HtlDoorZ0 + HtlDoorZ1) * 0.5f), false, out var ht);
            float hw = (HtlDoorZ1 - HtlDoorZ0) * 0.5f;
            var hl = DoorLeaf("LeafL", ht, new Vector3(0f, 1.5f, -hw * 0.5f), new Vector3(0.12f, 3f, hw + 0.02f));
            var hr = DoorLeaf("LeafR", ht, new Vector3(0f, 1.5f, hw * 0.5f), new Vector3(0.12f, 3f, hw + 0.02f));
            SetRefArray(hotelLobbyDoor, "leaves", new Object[] { hl.transform, hr.transform });
            SetVectorArray(hotelLobbyDoor, "openOffsets", new[] { new Vector3(0f, 0f, -hw + 0.05f), new Vector3(0f, 0f, hw - 0.05f) });

            // ---- Elevator shaft and car ----
            Add("Concrete", new Vector3(ShX0 - 0.3f, m - 0.6f, ShZ0), new Vector3(ShX0, u + 3.4f, ShZ1), 1f);       // west shaft wall
            Add("Concrete", new Vector3(ShX1, m - 0.6f, ShZ0), new Vector3(ShX1 + 0.3f, u + 3.4f, ShZ1), 1f);       // east shaft wall
            Add("Concrete", new Vector3(ShX0 - 0.3f, LobTop + 0.4f, LobZ1), new Vector3(ShX1 + 0.3f, u + 3.4f, LobZ1 + 0.4f), 1f);   // south face above the lobby
            Add("Concrete", new Vector3(ShX0 - 0.3f, m - 0.6f, ShZ1), new Vector3(ShX1 + 0.3f, u, ShZ1 + 0.4f), 1f);                // north face below the hotel floor
            Add("Concrete", new Vector3(ShX0 - 0.3f, u + 2.4f, ShZ1), new Vector3(ShX1 + 0.3f, u + 3.4f, ShZ1 + 0.4f), 1f);        // north face over the upper door
            Add("ConcreteDark", new Vector3(ShX0 - 0.3f, u + 3.4f, ShZ0 - 0.4f), new Vector3(ShX1 + 0.3f, u + 3.8f, ShZ1 + 0.4f), 1f);   // shaft top
            var lift = new GameObject("Elevator").transform;
            lift.SetParent(hotel, false);
            var car = new GameObject("ElevatorCar").transform;
            car.SetParent(lift, false);
            car.position = new Vector3((ShX0 + ShX1) * 0.5f, m, (ShZ0 + ShZ1) * 0.5f);
            Prim(PrimitiveType.Cube, "Floor", car, new Vector3(0f, -0.06f, 0f), new Vector3(1.78f, 0.12f, 1.78f), Vector3.zero, mats["MetalLight"], true);
            Prim(PrimitiveType.Cube, "WallW", car, new Vector3(-0.86f, 1.25f, 0f), new Vector3(0.05f, 2.5f, 1.78f), Vector3.zero, mats["WhiteCabinet"], true);
            Prim(PrimitiveType.Cube, "WallE", car, new Vector3(0.86f, 1.25f, 0f), new Vector3(0.05f, 2.5f, 1.78f), Vector3.zero, mats["WhiteCabinet"], true);
            Prim(PrimitiveType.Cube, "Ceiling", car, new Vector3(0f, 2.52f, 0f), new Vector3(1.78f, 0.05f, 1.78f), Vector3.zero, mats["PanelLight"], true);
            Prim(PrimitiveType.Cube, "Rail", car, new Vector3(0.8f, 0.95f, 0f), new Vector3(0.04f, 0.04f, 1.4f), Vector3.zero, mats["MetalLight"]);
            Prim(PrimitiveType.Cube, "Strip", car, new Vector3(-0.83f, 0.08f, 0f), new Vector3(0.01f, 0.04f, 1.7f), Vector3.zero, mats["CyanStrip"]);
            var carLeaves = new List<Object>(); var carFloors = new List<int>(); var carOffsets = new List<Vector3>();
            foreach (var (side, floorIdx) in new[] { (-1f, 0), (1f, 1) })
                foreach (float half in new[] { -1f, 1f })
                {
                    var leafGo = Prim(PrimitiveType.Cube, side < 0 ? "CarDoorFront" : "CarDoorBack", car, new Vector3(half * 0.43f, 1.25f, side * 0.86f), new Vector3(0.86f, 2.5f, 0.04f), Vector3.zero, mats["MetalLight"], true);
                    Prim(PrimitiveType.Cube, "Seam", leafGo.transform, new Vector3(-half * 0.49f, 0f, 0f), new Vector3(0.02f, 1f, 1.2f), Vector3.zero, mats["CyanStrip"]);
                    carLeaves.Add(leafGo.transform); carFloors.Add(floorIdx); carOffsets.Add(new Vector3(half * 0.8f, 0f, 0f));
                }
            var el = lift.gameObject.AddComponent<Elevator>();
            SetRefArray(el, "carDoorLeaves", carLeaves);
            SetVectorArray(el, "carDoorOffsets", carOffsets);
            {
                var cso = new SerializedObject(el);
                var cf = cso.FindProperty("carDoorFloor"); cf.arraySize = carFloors.Count;
                for (int i = 0; i < carFloors.Count; i++) cf.GetArrayElementAtIndex(i).intValue = carFloors[i];
                cso.ApplyModifiedPropertiesWithoutUndo();
            }
            liftLobbyDoor = MakeDoor("Door_LiftLobby", new Vector3(41f, m, LobZ1 + 0.2f), false, out var dl);
            var ll = DoorLeaf("Leaf", dl, new Vector3(0f, 1.2f, 0f), new Vector3(1.26f, 2.4f, 0.12f));
            SetRefArray(liftLobbyDoor, "leaves", new Object[] { ll.transform });
            SetVectorArray(liftLobbyDoor, "openOffsets", new[] { new Vector3(1.2f, 0f, 0f) });
            liftUpDoor = MakeDoor("Door_LiftUpper", new Vector3(41f, u, ShZ1 + 0.2f), true, out var du);
            var lu = DoorLeaf("Leaf", du, new Vector3(0f, 1.2f, 0f), new Vector3(1.26f, 2.4f, 0.12f));
            SetRefArray(liftUpDoor, "leaves", new Object[] { lu.transform });
            SetVectorArray(liftUpDoor, "openOffsets", new[] { new Vector3(1.2f, 0f, 0f) });
            SetRef(el, "car", car);
            var floorsSo = new SerializedObject(el);
            var fl = floorsSo.FindProperty("floors"); fl.arraySize = 2;
            fl.GetArrayElementAtIndex(0).floatValue = m; fl.GetArrayElementAtIndex(1).floatValue = u;
            floorsSo.ApplyModifiedPropertiesWithoutUndo();
            SetRefArray(el, "landingDoors", new Object[] { liftLobbyDoor, liftUpDoor });
            // Buttons: a call button at each landing, UP and DOWN inside the car
            void Button(string name, Transform parent, Vector3 worldPos, Vector3 facing, string label, int floor, int step, string mat)
            {
                var b = Prim(PrimitiveType.Cube, name, parent, Vector3.zero, new Vector3(0.12f, 0.12f, 0.04f), Vector3.zero, mats[mat]);
                b.transform.position = worldPos;
                b.transform.rotation = Quaternion.LookRotation(facing);
                var eb = b.AddComponent<ElevatorButton>();
                SetRef(eb, "elevator", el);
                SetString(eb, "label", label);
                SetInt(eb, "floor", floor);
                SetInt(eb, "step", step);
            }
            Button("CallLobby", lift, new Vector3(42.3f, m + 1.25f, LobZ1 - 0.03f), Vector3.back, "CALL ELEVATOR", 0, 0, "CyanStrip");
            Button("CallUpper", lift, new Vector3(42.3f, u + 1.25f, ShZ1 + 0.43f), Vector3.forward, "CALL ELEVATOR", 1, 0, "CyanStrip");
            Button("CarUp", car, car.position + new Vector3(0.82f, 1.32f, 0.35f), Vector3.left, "ELEVATOR: UP", -1, 1, "CyanStrip");
            Button("CarDown", car, car.position + new Vector3(0.82f, 1.12f, 0.35f), Vector3.left, "ELEVATOR: DOWN", -1, -1, "OrangeStrip");
            Sign("UP", car.position + new Vector3(0.83f, 1.32f, 0.12f), 0.6f, Palette.StPanelLight, 0.8f, -90f);
            Sign("DOWN", car.position + new Vector3(0.83f, 1.12f, 0.12f), 0.6f, Palette.StPanelLight, 0.8f, -90f);

            // ---- Upper landing: the room's door, two locked neighbors, the elevator ----
            Add("Floor", new Vector3(LndX0, u - 0.4f, ShZ1 + 0.4f), new Vector3(LndX1, u, LndZ1), 2f);
            Add("Concrete", new Vector3(LndX0 - 0.4f, u - 0.4f, ShZ1), new Vector3(LndX0, LndTop + 0.4f, LndZ1 + 0.4f), 1f);   // west
            Add("Concrete", new Vector3(LndX1, u - 0.4f, ShZ1), new Vector3(LndX1 + 0.4f, LndTop + 0.4f, LndZ1 + 0.4f), 1f);   // east
            Add("Concrete", new Vector3(LndX0, u - 0.4f, ShZ1), new Vector3(ShX0 + 0.3f, LndTop + 0.4f, ShZ1 + 0.4f), 1f);    // south, west of the lift door (the opening matches the 1.2 m door)
            Add("Concrete", new Vector3(ShX1 - 0.3f, u - 0.4f, ShZ1), new Vector3(LndX1, LndTop + 0.4f, ShZ1 + 0.4f), 1f);    // south, east of it
            Add("Concrete", new Vector3(LndX0, u - 0.4f, LndZ1), new Vector3(40.3f, LndTop + 0.4f, LndZ1 + 0.4f), 1f);         // north, west of the room door
            Add("Concrete", new Vector3(41.7f, u - 0.4f, LndZ1), new Vector3(LndX1, LndTop + 0.4f, LndZ1 + 0.4f), 1f);         // north, east of it
            Add("Concrete", new Vector3(40.3f, u + 2.6f, LndZ1), new Vector3(41.7f, LndTop + 0.4f, LndZ1 + 0.4f), 1f);          // over the room door
            Add("ConcreteDark", new Vector3(LndX0 - 0.4f, LndTop, ShZ1), new Vector3(LndX1 + 0.4f, LndTop + 0.4f, LndZ1 + 0.4f), 1f);
            Add("Wood", new Vector3(LndX0 + 0.6f, u + 0.001f, ShZ1 + 1f), new Vector3(LndX1 - 0.6f, u + 0.012f, LndZ1 - 0.6f), 2f, false);
            Add("CeilingLight", new Vector3(39.6f, LndTop - 0.05f, -0.4f), new Vector3(42.4f, LndTop - 0.01f, 1.4f), 1f, false);
            AreaLight(new Vector3(41f, LndTop - 0.07f, 0.5f), new Vector2(2.6f, 1.6f), 3.5f);
            Sign("1202", new Vector3(41f, u + 2.95f, LndZ1 - 0.06f), 1.5f, Palette.StTechCyan, 0.9f, 180f);
            // Neighbors' doors: locked, a red light, room numbers
            foreach (var (name, x, facingX, number) in new[] { ("1201", LndX0, 1f, "1201"), ("1203", LndX1, -1f, "1203") })
            {
                var d = MakeDoor("Door_" + name, new Vector3(x + facingX * 0.08f, u, 0.4f), true, out var dt);
                var leaf = DoorLeaf("Leaf", dt, new Vector3(0f, 1.25f, 0f), new Vector3(0.1f, 2.5f, 1.3f));
                SetRefArray(d, "leaves", new Object[] { leaf.transform });
                SetVectorArray(d, "openOffsets", new[] { Vector3.zero });
                Add("RedLight", new Vector3(x + facingX * 0.12f - 0.02f, u + 2.62f, 0.2f), new Vector3(x + facingX * 0.12f + 0.02f, u + 2.7f, 0.6f), 1f, false);
                Sign(number, new Vector3(x + facingX * 0.14f, u + 2.95f, 0.4f), 1.3f, Palette.StPanelLight, 0.8f, facingX > 0f ? 90f : -90f);
            }
            Add("Metal", new Vector3(36.4f, u, 2.4f), new Vector3(37.8f, u + 0.45f, 3.0f), 1f);   // a bench by the window wall
            // The room's door
            roomDoor = MakeDoor("Door_Room1202", new Vector3(41f, u, LndZ1 + 0.2f), false, out var rd);
            var rl = DoorLeaf("Leaf", rd, new Vector3(0f, 1.3f, 0f), new Vector3(1.42f, 2.6f, 0.12f));
            SetRefArray(roomDoor, "leaves", new Object[] { rl.transform });
            SetVectorArray(roomDoor, "openOffsets", new[] { new Vector3(1.38f, 0f, 0f) });

            BuildHotelRoom();

            WingProbe(new Vector3(32f, m + 1.8f, -15f), new Vector3(HtlHallX1 - HX - WallT, 3.4f, HtlDoorZ1 - HtlDoorZ0));
            WingProbe(new Vector3((LobX0 + LobX1) * 0.5f, m + 2.5f, (LobZ0 + LobZ1) * 0.5f), new Vector3(LobX1 - LobX0, LobTop - m, LobZ1 - LobZ0));
            WingProbe(new Vector3((LndX0 + LndX1) * 0.5f, u + 1.5f, (ShZ1 + LndZ1) * 0.5f), new Vector3(LndX1 - LndX0, LndTop - u, LndZ1 - ShZ1));
        }

        /// <summary>
        /// Room 1202: one open studio (kitchen, living, sleeping) under a lit coffered ceiling, glass floor-to-ceiling to
        /// the north over the landing pads; only the bathroom is a separate room with a door. Modern, with a sci-fi edge:
        /// gloss white, dark metal, recessed light lines.
        /// </summary>
        static void BuildHotelRoom()
        {
            float u = UpY;
            // Shell
            Add("Floor", new Vector3(RmX0, u - 0.4f, RmZ0), new Vector3(RmX1, u, RmZ1), 2f);
            Add("Concrete", new Vector3(RmX0 - 0.4f, u - 0.4f, RmZ0 - 0.4f), new Vector3(RmX0, RmTop + 0.4f, RmZ1 + 0.2f), 1f);    // west
            Add("Concrete", new Vector3(RmX1, u - 0.4f, RmZ0 - 0.4f), new Vector3(RmX1 + 0.4f, RmTop + 0.4f, RmZ1 + 0.2f), 1f);    // east
            Add("Concrete", new Vector3(RmX0, u - 0.4f, RmZ0 - 0.4f), new Vector3(LndX0, RmTop + 0.4f, RmZ0), 1f);                 // south, west of the landing
            Add("Concrete", new Vector3(LndX0, LndTop + 0.4f, RmZ0 - 0.4f), new Vector3(RmX1, RmTop + 0.4f, RmZ0), 1f);            // south, above the landing
            Add("ConcreteDark", new Vector3(RmX0 - 0.4f, RmTop, RmZ0 - 0.4f), new Vector3(RmX1 + 0.4f, RmTop + 0.4f, RmZ1 + 0.2f), 1f);
            // The view: a full glass wall to the north (mullions every 2.6 m, a dark sill and head)
            Add("PipeDark", new Vector3(RmX0, u, RmZ1), new Vector3(RmX1, u + 0.12f, RmZ1 + 0.2f), 1f);
            Add("PipeDark", new Vector3(RmX0, RmTop - 0.14f, RmZ1), new Vector3(RmX1, RmTop, RmZ1 + 0.2f), 1f);
            var glass = Acc(render, "Glass");
            int panes = 6; float pw = (RmX1 - RmX0) / panes;
            for (int i = 0; i < panes; i++)
            {
                float x0 = RmX0 + i * pw, x1 = x0 + pw;
                glass.Card(new Vector3((x0 + x1) * 0.5f, (u + RmTop) * 0.5f, RmZ1 + 0.1f), Vector3.right * (pw * 0.5f - 0.04f), Vector3.up * ((RmTop - u) * 0.5f - 0.13f));
                if (i > 0) Add("PipeDark", new Vector3(x0 - 0.05f, u, RmZ1), new Vector3(x0 + 0.05f, RmTop, RmZ1 + 0.2f), 1f);
            }
            Acc(collide, "col").BoxMinMax(new Vector3(RmX0, u, RmZ1 + 0.05f), new Vector3(RmX1, RmTop, RmZ1 + 0.15f), 1f);
            // Ceiling: three coffers with cyan cove lines (one over each zone), and a warm perimeter line at the glass
            foreach (var (x0, x1) in new[] { (31.2f, 35.4f), (35.8f, 41.4f), (41.8f, 45.4f) })
            {
                Add("WhiteCabinet", new Vector3(x0, RmTop - 0.3f, 5f), new Vector3(x1, RmTop - 0.25f, 16.6f), 1f, false);
                Add("CyanStrip", new Vector3(x0 - 0.03f, RmTop - 0.25f, 5f), new Vector3(x0, RmTop - 0.2f, 16.6f), 1f, false);
                Add("CyanStrip", new Vector3(x1, RmTop - 0.25f, 5f), new Vector3(x1 + 0.03f, RmTop - 0.2f, 16.6f), 1f, false);
            }
            Add("WarmLight", new Vector3(RmX0, RmTop - 0.2f, RmZ1 - 0.06f), new Vector3(RmX1, RmTop - 0.16f, RmZ1), 1f, false);
            // Lighting
            foreach (var (x, z, w, d, i) in new[] { (33.3f, 9.5f, 3.6f, 5f, 3.5f), (38.6f, 11f, 4.8f, 5f, 3.5f), (43.6f, 13f, 3.2f, 4.5f, 3f), (38.6f, 5.5f, 4.5f, 2.5f, 2.5f) })
                AreaLight(new Vector3(x, RmTop - 0.32f, z), new Vector2(w, d), i);

            // ---- Kitchen (west): a long white run with dark tops, a tall fridge, an island with stools under pendants ----
            Add("WhiteCabinet", new Vector3(RmX0, u, 5.2f), new Vector3(RmX0 + 0.65f, u + 0.88f, 13.4f), 1f);
            Add("PipeDark", new Vector3(RmX0, u + 0.88f, 5.15f), new Vector3(RmX0 + 0.7f, u + 0.93f, 13.45f), 1f, false);
            Add("WhiteCabinet", new Vector3(RmX0, u + 1.55f, 5.2f), new Vector3(RmX0 + 0.38f, u + 2.4f, 13.4f), 1f, false);
            Add("CyanStrip", new Vector3(RmX0 + 0.38f, u + 1.52f, 5.2f), new Vector3(RmX0 + 0.41f, u + 1.55f, 13.4f), 1f, false);
            Add("MetalLight", new Vector3(RmX0 + 0.1f, u + 0.93f, 7.6f), new Vector3(RmX0 + 0.6f, u + 0.95f, 8.5f), 1f, false);   // cooktop
            Add("Metal", new Vector3(RmX0 + 0.12f, u + 0.9f, 10.2f), new Vector3(RmX0 + 0.58f, u + 0.94f, 11f), 1f, false);       // sink
            Add("MetalLight", new Vector3(RmX0 + 0.25f, u + 0.93f, 10.55f), new Vector3(RmX0 + 0.3f, u + 1.25f, 10.65f), 1f, false);
            Add("MetalLight", new Vector3(RmX0, u, 13.6f), new Vector3(RmX0 + 0.8f, u + 2.3f, 14.5f), 1f);                        // fridge
            Add("CyanStrip", new Vector3(RmX0 + 0.8f, u + 1.0f, 14.3f), new Vector3(RmX0 + 0.82f, u + 1.6f, 14.34f), 1f, false);
            Add("WhiteCabinet", new Vector3(32.8f, u, 8f), new Vector3(34.2f, u + 0.9f, 11.6f), 1f);                               // island
            Add("PipeDark", new Vector3(32.7f, u + 0.9f, 7.9f), new Vector3(34.4f, u + 0.95f, 11.7f), 1f, false);
            Add("OrangeStrip", new Vector3(34.2f, u + 0.05f, 8.05f), new Vector3(34.22f, u + 0.09f, 11.55f), 1f, false);
            foreach (float z in new[] { 8.6f, 9.8f, 11f })
            {
                Add("PipeDark", new Vector3(34.6f, u, z - 0.03f), new Vector3(34.66f, u + 0.7f, z + 0.03f), 1f, false);
                Add("Seat", new Vector3(34.4f, u + 0.7f, z - 0.2f), new Vector3(34.86f, u + 0.76f, z + 0.2f), 1f, false);
                Add("PanelLight", new Vector3(33.4f, u + 2.1f, z - 0.12f), new Vector3(33.64f, u + 2.22f, z + 0.12f), 1f, false);   // pendant
                Add("Cable", new Vector3(33.51f, u + 2.22f, z - 0.01f), new Vector3(33.53f, RmTop - 0.3f, z + 0.01f), 1f, false);
            }

            // ---- Living (center): an L sofa turned to the view, a low table, a dark rug, a holo table glowing cyan ----
            Add("MenuBoard", new Vector3(35.6f, u + 0.001f, 9.2f), new Vector3(41.2f, u + 0.012f, 14.2f), 1f, false);
            Add("Seat", new Vector3(36f, u, 9.4f), new Vector3(40.2f, u + 0.42f, 10.3f), 1f);
            Add("Seat", new Vector3(36f, u + 0.42f, 9.4f), new Vector3(40.2f, u + 0.85f, 9.65f), 1f, false);
            Add("Seat", new Vector3(40.2f, u, 9.4f), new Vector3(41.1f, u + 0.42f, 12.4f), 1f);
            Add("Seat", new Vector3(40.85f, u + 0.42f, 9.4f), new Vector3(41.1f, u + 0.85f, 12.4f), 1f, false);
            Add("WarmLight", new Vector3(36f, u + 0.02f, 10.3f), new Vector3(41.1f, u + 0.05f, 10.33f), 1f, false);
            Add("Wood", new Vector3(37f, u, 11f), new Vector3(39.4f, u + 0.36f, 12.2f), 1f);
            Add("PipeDark", new Vector3(36.2f, u, 13f), new Vector3(37.2f, u + 0.75f, 14f), 1f);
            Add("CyanStrip", new Vector3(36.3f, u + 0.75f, 13.1f), new Vector3(37.1f, u + 0.78f, 13.9f), 1f, false);
            foreach (float x in new[] { 37.2f, 38.4f, 39.6f }) lobbySeats.Add((new Vector3(x, u, 9.95f), Vector3.forward));   // (used only for the room's own seat points)

            // ---- Sleeping (east): a low floating bed against an upholstered wall, nightstands with lamps ----
            Add("PipeDark", new Vector3(43.5f, u + 0.08f, 12.2f), new Vector3(45.9f, u + 0.32f, 14.6f), 1f);
            Add("Hazard", new Vector3(43.5f, u + 0.02f, 12.25f), new Vector3(43.52f, u + 0.07f, 14.55f), 1f, false);
            Add("WhiteCabinet", new Vector3(43.6f, u + 0.32f, 12.3f), new Vector3(45.85f, u + 0.58f, 14.5f), 1f);
            Add("Seat", new Vector3(43.6f, u + 0.58f, 12.3f), new Vector3(44.6f, u + 0.62f, 14.5f), 1f, false);   // a folded throw
            Add("WhiteCabinet", new Vector3(45.3f, u + 0.58f, 12.5f), new Vector3(45.75f, u + 0.75f, 13.3f), 1f, false);
            Add("WhiteCabinet", new Vector3(45.3f, u + 0.58f, 13.5f), new Vector3(45.75f, u + 0.75f, 14.3f), 1f, false);
            Add("Seat", new Vector3(RmX1 - 0.1f, u + 0.35f, 11.8f), new Vector3(RmX1, u + 1.6f, 15f), 1f, false);
            Add("CyanStrip", new Vector3(RmX1 - 0.12f, u + 1.6f, 11.8f), new Vector3(RmX1 - 0.1f, u + 1.64f, 15f), 1f, false);
            foreach (float z in new[] { 11.4f, 14.9f })
            {
                Add("Metal", new Vector3(45.2f, u, z - 0.3f), new Vector3(45.9f, u + 0.5f, z + 0.3f), 1f);
                Add("PanelLight", new Vector3(45.45f, u + 0.5f, z - 0.08f), new Vector3(45.65f, u + 0.75f, z + 0.08f), 1f, false);
            }

            // ---- Bathroom (south-east corner): the one room with a door ----
            const float bx0 = 42.4f, bz1 = 8.8f;
            Add("Concrete", new Vector3(bx0, u, RmZ0), new Vector3(bx0 + 0.2f, RmTop, bz1), 1f);                                  // west partition
            Add("Concrete", new Vector3(bx0, u, bz1 - 0.2f), new Vector3(43.25f, RmTop, bz1), 1f);                                 // north partition, west of the door
            Add("Concrete", new Vector3(44.75f, u, bz1 - 0.2f), new Vector3(RmX1, RmTop, bz1), 1f);                                // east of it
            Add("Concrete", new Vector3(43.25f, u + 2.4f, bz1 - 0.2f), new Vector3(44.75f, RmTop, bz1), 1f);                         // over the door
            Add("WhiteCabinet", new Vector3(bx0 + 0.2f, u, RmZ0), new Vector3(bx0 + 0.25f, u + 2.3f, bz1 - 0.2f), 1f, false);
            Add("WhiteCabinet", new Vector3(bx0 + 0.2f, u, RmZ0), new Vector3(RmX1, u + 2.3f, RmZ0 + 0.05f), 1f, false);
            Add("WhiteCabinet", new Vector3(RmX1 - 0.05f, u, RmZ0), new Vector3(RmX1, u + 2.3f, bz1 - 0.2f), 1f, false);
            // Walk-in shower (glass screen, a dark tray), vanity with a mirror, toilet
            Add("PipeDark", new Vector3(44.6f, u + 0.001f, RmZ0 + 0.05f), new Vector3(RmX1 - 0.05f, u + 0.03f, 5.4f), 1f, false);
            glass.Card(new Vector3(44.6f, u + 1.05f, 4.5f), Vector3.forward * 0.85f, Vector3.up * 1.0f);
            Add("MetalLight", new Vector3(45.6f, u + 2.0f, 4.4f), new Vector3(45.8f, u + 2.05f, 4.6f), 1f, false);
            Add("WhiteCabinet", new Vector3(bx0 + 0.25f, u, 5.9f), new Vector3(bx0 + 0.85f, u + 0.85f, 7.7f), 1f);
            Add("PipeDark", new Vector3(bx0 + 0.35f, u + 0.85f, 6.4f), new Vector3(bx0 + 0.75f, u + 0.88f, 7.2f), 1f, false);
            Add("Glass", new Vector3(bx0 + 0.26f, u + 1.1f, 5.95f), new Vector3(bx0 + 0.28f, u + 2.0f, 7.65f), 1f, false);
            Add("PanelLight", new Vector3(bx0 + 0.26f, u + 2.05f, 5.95f), new Vector3(bx0 + 0.32f, u + 2.1f, 7.65f), 1f, false);
            Add("WhiteCabinet", new Vector3(RmX1 - 0.75f, u, 6.4f), new Vector3(RmX1 - 0.05f, u + 0.42f, 6.95f), 1f);
            Add("WhiteCabinet", new Vector3(RmX1 - 0.25f, u + 0.42f, 6.4f), new Vector3(RmX1 - 0.05f, u + 0.8f, 6.95f), 1f, false);
            AreaLight(new Vector3(44.2f, RmTop - 0.07f, 6.2f), new Vector2(2.4f, 3f), 3f);
            bathDoor = MakeDoor("Door_Bath1202", new Vector3(44f, u, bz1 - 0.1f), false, out var bd);
            var bl = DoorLeaf("Leaf", bd, new Vector3(0f, 1.2f, 0f), new Vector3(1.52f, 2.4f, 0.1f));
            SetRefArray(bathDoor, "leaves", new Object[] { bl.transform });
            SetVectorArray(bathDoor, "openOffsets", new[] { new Vector3(1.3f, 0f, 0f) });

            WingProbe(new Vector3((RmX0 + RmX1) * 0.5f, u + 1.9f, (RmZ0 + RmZ1) * 0.5f), new Vector3(RmX1 - RmX0, RmTop - u, RmZ1 - RmZ0));
        }

        /// <summary>Hotel staff and guests in the lobby (shootable people, like everyone in the atrium).</summary>
        static void BuildHotelPeople()
        {
            float m = MezzY;
            var staff = new (string name, string role, Look look, Vector3 pos, float yaw, string greet, string q, string a, string bye)[]
            {
                ("Selin Kaya", "Front Desk",
                    new Look { jacket = Palette.UIBone, pants = Palette.UIDark, skin = Palette.SkinMid, hair = Palette.Grip, accent = Palette.StTechCyan, hairStyle = 0, height = 0.98f, build = 0.95f, outfit = Outfit.Uniform },
                    new Vector3(44.9f, m, -11.5f), -90f, "Welcome to the Airport Hotel. Your room is 1202, up the elevator.", "Anything I should know?", "Checkout is whenever your shuttle leaves. The view from the twelfth floor is worth the trip down from orbit.", "Enjoy your stay."),
                ("Marco Bellini", "Concierge",
                    new Look { jacket = Palette.UIPanel, pants = Palette.UIDark, skin = Palette.SkinLight, hair = Palette.HairGrey, accent = Palette.StAccentOrange, hairStyle = 1, height = 1.03f, build = 1.02f, outfit = Outfit.Uniform },
                    new Vector3(41.2f, m, -7.2f), 200f, "Shuttle times, dinner reservations, a quiet corner. Ask me.", "What's worth seeing?", "The landing pads at dusk. Watch from your window when the cargo runs come in. Everyone stops talking for a minute.", "At your service."),
            };
            for (int i = 0; i < staff.Length; i++)
            {
                var s = staff[i];
                string id = "Hotel" + i;
                var c = BuildCharacter("NPC_" + s.name.Replace(" ", ""), s.pos, s.yaw, s.look, id, true);
                MakeCivilian(c);
                var npc = c.gameObject.AddComponent<Alcax.Dialogue.Npc>();
                npc.displayName = s.name; npc.role = s.role;
                npc.dialogue = SmallTalk(id, s.greet, s.q, s.a, s.bye);
            }
            // Two guests who live in the lobby lounge
            var guests = new (string name, Look look, Vector3 pos, string greet, string q, string a, string bye)[]
            {
                ("Dana Okafor", new Look { jacket = Palette.StPilotOlive, pants = Palette.UIPanel, skin = Palette.SkinDark, hair = Palette.Grip, accent = Palette.StAccentOrange, hairStyle = 2, height = 1.01f, build = 1f, outfit = Outfit.FlightJacket },
                    new Vector3(37.4f, m, -16.4f), "Layover. Fourteen hours. I've counted every tile.", "Where are you headed?", "The ring, then home. Assuming the shuttle doesn't get bumped again.", "Safe travels."),
                ("Elias Varga", new Look { jacket = Palette.StMetalLight, pants = Palette.UIDark, skin = Palette.SkinLight, hair = Palette.HairBrown, accent = Palette.StTechCyan, hairStyle = 1, height = 0.99f, build = 0.98f, outfit = Outfit.Casual },
                    new Vector3(39.8f, m, -11.8f), "Ines at the info desk is my cousin. Don't tell her I'm here.", "Why not?", "She'll make me help with the lost luggage. Again.", "Mum's the word."),
            };
            for (int i = 0; i < guests.Length; i++)
            {
                var g = guests[i];
                string id = "Guest" + i;
                var c = BuildCharacter("NPC_" + g.name.Replace(" ", ""), g.pos, 0f, g.look, id, true);
                MakeCivilian(c);
                var agent = c.gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
                agent.radius = 0.32f; agent.height = 1.8f; agent.avoidancePriority = 80 + i;
                var npc = c.gameObject.AddComponent<Alcax.Dialogue.Npc>();
                npc.displayName = g.name; npc.role = "Hotel Guest";
                npc.dialogue = SmallTalk(id, g.greet, g.q, g.a, g.bye);
                var brain = c.gameObject.AddComponent<Alcax.Dialogue.AmbientNpc>();
                SetEnum(brain, "temperament", 0);
                var so = new SerializedObject(brain);
                so.FindProperty("hasHome").boolValue = true;
                so.FindProperty("homeMin").vector3Value = new Vector3(LobX0 + 0.5f, m - 0.5f, LobZ0 + 0.5f);
                so.FindProperty("homeMax").vector3Value = new Vector3(LobX1 - 0.5f, m + 1.5f, LobZ1 - 0.5f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ---------- Restrooms (west of the food court) ----------

        const float WCZ0 = -28.4f, WCZ1 = -26.8f;                       // the door in the food court's west wall
        const float RX0 = -35.6f, RX1 = FX0 - 0.4f, RZ0 = -34.5f, RZ1 = -22f, RRH = 3.3f;
        static AutoDoor restroomDoor;

        static void BuildRestrooms()
        {
            Add("Floor", new Vector3(RX0 - 0.4f, -1f, RZ0 - 0.4f), new Vector3(RX1, 0f, RZ1 + 0.4f), 2f);
            Add("Concrete", new Vector3(RX0 - 0.4f, -1f, RZ0 - 0.4f), new Vector3(RX0, RRH + 0.6f, RZ1 + 0.4f), 1f);   // west
            Add("Concrete", new Vector3(RX0, -1f, RZ0 - 0.4f), new Vector3(RX1, RRH + 0.6f, RZ0), 1f);                 // south
            Add("Concrete", new Vector3(RX0, -1f, RZ1), new Vector3(RX1, RRH + 0.6f, RZ1 + 0.4f), 1f);                 // north
            Add("ConcreteDark", new Vector3(RX0 - 0.4f, RRH, RZ0 - 0.4f), new Vector3(RX1, RRH + 0.6f, RZ1 + 0.4f), 1f);
            // Clean white tiling to head height on every wall, with a cyan base strip
            Add("WhiteCabinet", new Vector3(RX0, 0f, RZ0), new Vector3(RX0 + 0.05f, 2.2f, RZ1), 1f, false);
            Add("WhiteCabinet", new Vector3(RX0, 0f, RZ0), new Vector3(RX1, 2.2f, RZ0 + 0.05f), 1f, false);
            Add("WhiteCabinet", new Vector3(RX0, 0f, RZ1 - 0.05f), new Vector3(RX1, 2.2f, RZ1), 1f, false);
            Add("WhiteCabinet", new Vector3(RX1 - 0.05f, 0f, RZ0), new Vector3(RX1, 2.2f, WCZ0), 1f, false);
            Add("WhiteCabinet", new Vector3(RX1 - 0.05f, 0f, WCZ1), new Vector3(RX1, 2.2f, RZ1), 1f, false);
            Add("CyanStrip", new Vector3(RX0 + 0.05f, 2.2f, RZ0 + 0.05f), new Vector3(RX0 + 0.08f, 2.24f, RZ1 - 0.05f), 1f, false);

            // Vanity on the north wall: a counter, three basins, a long mirror, and a light bar over it
            float vz0 = RZ1 - 0.65f, vz1 = RZ1 - 0.05f;
            Add("Metal", new Vector3(-34.2f, 0f, vz0), new Vector3(-29.6f, 0.82f, vz1), 2f);
            Add("WhiteCabinet", new Vector3(-34.25f, 0.82f, vz0 - 0.03f), new Vector3(-29.55f, 0.88f, vz1), 1f, false);
            foreach (float x in new[] { -33.4f, -31.9f, -30.4f })
            {
                Add("PipeDark", new Vector3(x - 0.25f, 0.86f, vz0 + 0.12f), new Vector3(x + 0.25f, 0.89f, vz1 - 0.12f), 1f, false);
                Add("MetalLight", new Vector3(x - 0.03f, 0.88f, vz1 - 0.12f), new Vector3(x + 0.03f, 1.12f, vz1 - 0.06f), 1f, false);
            }
            Add("Glass", new Vector3(-34.1f, 1.15f, RZ1 - 0.07f), new Vector3(-29.7f, 2.05f, RZ1 - 0.05f), 1f, false);
            Add("PanelLight", new Vector3(-34.1f, 2.1f, RZ1 - 0.12f), new Vector3(-29.7f, 2.16f, RZ1 - 0.05f), 1f, false);

            // Three stalls along the west wall: partitions, open-front doors ajar, a toilet in each
            float sx0 = RX0, sx1 = RX0 + 1.55f;
            for (int i = 0; i <= 3; i++)
            {
                float z = RZ0 + 0.6f + i * 1.45f;
                Add("MetalLight", new Vector3(sx0, 0.15f, z - 0.03f), new Vector3(sx1, 2.0f, z + 0.03f), 1f);
            }
            for (int i = 0; i < 3; i++)
            {
                float z0 = RZ0 + 0.6f + i * 1.45f, z1 = z0 + 1.45f;
                Add("WhiteCabinet", new Vector3(sx0 + 0.05f, 0f, (z0 + z1) * 0.5f - 0.22f), new Vector3(sx0 + 0.75f, 0.42f, (z0 + z1) * 0.5f + 0.22f), 1f);
                Add("WhiteCabinet", new Vector3(sx0 + 0.05f, 0.42f, (z0 + z1) * 0.5f - 0.2f), new Vector3(sx0 + 0.25f, 0.85f, (z0 + z1) * 0.5f + 0.2f), 1f, false);
                // Door ajar, swung into the stall
                AddRot("MetalLight", new Vector3(sx1 - 0.25f, 1.05f, z0 + 0.4f), new Vector3(0.04f, 1.8f, 0.75f), Quaternion.Euler(0f, 35f, 0f), 1f, false);
                Add("CyanStrip", new Vector3(sx1 + 0.01f, 1.9f, z0 + 0.1f), new Vector3(sx1 + 0.03f, 1.95f, z0 + 0.3f), 1f, false);
            }
            // A hand dryer and a bin by the door
            Add("MetalLight", new Vector3(RX1 - 0.35f, 1.1f, WCZ1 + 0.4f), new Vector3(RX1 - 0.05f, 1.45f, WCZ1 + 0.75f), 1f, false);
            Add("PipeDark", new Vector3(RX1 - 0.45f, 0f, WCZ0 - 0.6f), new Vector3(RX1 - 0.1f, 0.7f, WCZ0 - 0.25f), 1f);

            foreach (float z in new[] { -31f, -24.5f })
            {
                Add("CeilingLight", new Vector3(-32.5f, RRH - 0.05f, z - 1f), new Vector3(-30.5f, RRH - 0.01f, z + 1f), 1f, false);
                AreaLight(new Vector3(-31.5f, RRH - 0.07f, z), new Vector2(1.8f, 1.8f), 3.5f);
            }
            Sign("RESTROOMS", new Vector3(-31.6f, 2.75f, RZ0 + 0.07f), 2.2f, Palette.StTechCyan, 0.8f, 0f);

            restroomDoor = MakeDoor("Door_Restrooms", new Vector3(FX0 - 0.2f, 0f, (WCZ0 + WCZ1) * 0.5f), false, out var rt);
            var leaf = DoorLeaf("Leaf", rt, new Vector3(0f, RDoorH * 0.5f, 0f), new Vector3(0.2f, RDoorH, WCZ1 - WCZ0 + 0.02f));
            SetRefArray(restroomDoor, "leaves", new Object[] { leaf.transform });
            SetVectorArray(restroomDoor, "openOffsets", new[] { new Vector3(0f, 0f, -(WCZ1 - WCZ0 - 0.05f)) });

            WingProbe(new Vector3((RX0 + RX1) * 0.5f, 1.7f, (RZ0 + RZ1) * 0.5f), new Vector3(RX1 - RX0, RRH, RZ1 - RZ0));
        }

        static void BuildFoodCourt()
        {
            cafeSeats.Clear();
            Add("Floor", new Vector3(FX0 - 0.4f, -1f, FZ0 - 0.4f), new Vector3(FX1 + 0.4f, 0f, FZ1), 4f);
            Add("Concrete", new Vector3(FX0 - 0.4f, -1f, FZ0 - 0.4f), new Vector3(FX0, FH + 0.6f, WCZ0), 1f);   // west, south of the restroom door
            Add("Concrete", new Vector3(FX0 - 0.4f, RDoorH, WCZ0), new Vector3(FX0, FH + 0.6f, WCZ1), 1f);      // its lintel
            Add("Concrete", new Vector3(FX0 - 0.4f, -1f, WCZ1), new Vector3(FX0, FH + 0.6f, FZ1), 1f);           // west, north of it
            Add("Concrete", new Vector3(FX1, -1f, FZ0 - 0.4f), new Vector3(FX1 + 0.4f, FH + 0.6f, FZ1), 1f);   // east
            Add("Concrete", new Vector3(FX0, -1f, FZ0 - 0.4f), new Vector3(FX1, FH + 0.6f, FZ0), 1f);           // south
            Add("ConcreteDark", new Vector3(FX0 - 0.4f, FH, FZ0 - 0.4f), new Vector3(FX1 + 0.4f, FH + 0.6f, FZ1), 1f);
            WallClad(FX0, 1f, FZ0, WCZ0, "OrangeStrip");
            WallClad(FX0, 1f, WCZ1, FZ1, "OrangeStrip");
            Sign("RESTROOMS", new Vector3(FX0 + 0.06f, 3.1f, (WCZ0 + WCZ1) * 0.5f), 2.6f, Palette.StTechCyan, 0.9f, 90f);
            WallClad(FX1, -1f, FZ0, FZ1, "OrangeStrip");
            WallCladZ(FZ1, -1f, FX0, WDoorX0 - 0.2f, "OrangeStrip");
            WallCladZ(FZ1, -1f, WDoorX1 + 0.2f, FX1, "OrangeStrip");
            // A warm wood floor zone under the seating
            Add("Wood", new Vector3(-25.5f, 0.001f, -30.5f), new Vector3(-2.5f, 0.012f, -20.6f), 2f, false);

            // Three stalls along the south wall: counter, kitchen backsplash, canopy with a lit edge, menu board, name sign
            var stalls = new (string name, float x, Color color)[] { ("NOODLE BAR", -21.5f, Palette.StAccentOrange), ("GRILL 03", -13f, Palette.StHazard), ("ORBIT COFFEE", -4.5f, Palette.StTechCyan) };
            foreach (var (name, x, color) in stalls)
            {
                float x0 = x - 3.1f, x1 = x + 3.1f, cz = FZ0 + 2.6f;
                Add("Metal", new Vector3(x0, 0f, cz), new Vector3(x1, 1.05f, cz + 0.8f), 2f);
                Add("MetalLight", new Vector3(x0 - 0.04f, 1.05f, cz - 0.04f), new Vector3(x1 + 0.04f, 1.09f, cz + 0.84f), 1f, false);
                Add("WarmLight", new Vector3(x0, 0.05f, cz + 0.8f), new Vector3(x1, 0.1f, cz + 0.82f), 1f, false);
                Add("ConcreteDark", new Vector3(x0 - 0.2f, 0f, FZ0), new Vector3(x1 + 0.2f, 3.2f, FZ0 + 0.15f), 1f, false);
                Add("MenuBoard", new Vector3(x - 1.8f, 2.1f, FZ0 + 0.15f), new Vector3(x + 1.8f, 3.0f, FZ0 + 0.18f), 1f, false);
                Add("Metal", new Vector3(x0 - 0.2f, 3.2f, FZ0), new Vector3(x1 + 0.2f, 3.35f, cz + 1.2f), 2f, false);
                Add(color == Palette.StTechCyan ? "CyanStrip" : "OrangeStrip", new Vector3(x0 - 0.2f, 3.2f, cz + 1.2f), new Vector3(x1 + 0.2f, 3.35f, cz + 1.23f), 1f, false);
                Sign(name, new Vector3(x, 3.75f, FZ0 + 0.08f), 3.2f, color, 0.95f);
                // Kitchen props: a stove, a tall fridge, a prep table
                Add("PipeDark", new Vector3(x0 + 0.2f, 0f, FZ0 + 0.2f), new Vector3(x0 + 1.4f, 0.9f, FZ0 + 0.9f), 1f);
                Add("MetalLight", new Vector3(x1 - 0.9f, 0f, FZ0 + 0.2f), new Vector3(x1 - 0.2f, 2.0f, FZ0 + 0.85f), 1f);
                Add("Metal", new Vector3(x - 0.8f, 0f, FZ0 + 0.2f), new Vector3(x + 0.8f, 0.9f, FZ0 + 0.8f), 1f);
                AreaLight(new Vector3(x, 3.15f, cz + 0.6f), new Vector2(5.5f, 0.6f), 4f);
            }

            // Tables with four chairs each (two rows, a clear aisle down the middle from the door)
            foreach (float tx in new[] { -23.5f, -19f, -7f, -2.8f })
                foreach (float tz in new[] { -28.2f, -23.4f })
                    CafeTable(new Vector3(tx, 0f, tz));
            foreach (float tz in new[] { -28.2f, -23.4f }) CafeTable(new Vector3(-16.2f, 0f, tz - 0f) + new Vector3(0f, 0f, 0f), true);
            foreach (float tz in new[] { -28.2f, -23.4f }) CafeTable(new Vector3(-9.8f, 0f, tz), true);


            // Lights: a ceiling grid, and warm pendants over the tables
            foreach (float x in new[] { -21f, -13f, -5f })
                foreach (float z in new[] { -29f, -22.5f })
                {
                    Add("CeilingLight", new Vector3(x - 1.2f, FH - 0.05f, z - 0.8f), new Vector3(x + 1.2f, FH - 0.01f, z + 0.8f), 1f, false);
                    AreaLight(new Vector3(x, FH - 0.07f, z), new Vector2(2.2f, 1.5f), 4.5f);
                }
            Sign("FOOD COURT", new Vector3(-13f, 4.6f, FZ1 - 0.06f), 5f, Palette.StAccentOrange, 0.9f, 180f);
            Sign("OPEN ALL CYCLES", new Vector3(-13f, 4.0f, FZ1 - 0.06f), 1.8f, Palette.StPanelLight, 0.6f, 180f);

            // Door: two leaves like the east blast door
            foodDoor = MakeDoor("Door_FoodCourt", new Vector3((WDoorX0 + WDoorX1) * 0.5f, 0f, -HZ - WallT * 0.5f), false, out var ft);
            float half = (WDoorX1 - WDoorX0) * 0.5f;
            var l = DoorLeaf("LeafL", ft, new Vector3(-half * 0.5f, DoorH * 0.5f, 0f), new Vector3(half + 0.02f, DoorH, 0.3f));
            var r = DoorLeaf("LeafR", ft, new Vector3(half * 0.5f, DoorH * 0.5f, 0f), new Vector3(half + 0.02f, DoorH, 0.3f));
            SetRefArray(foodDoor, "leaves", new Object[] { l.transform, r.transform });
            SetVectorArray(foodDoor, "openOffsets", new[] { new Vector3(-half + 0.1f, 0f, 0f), new Vector3(half - 0.1f, 0f, 0f) });
            SetFloat(foodDoor, "openDistance", 5f);

            WingProbe(new Vector3((FX0 + FX1) * 0.5f, 2.6f, (FZ0 + FZ1) * 0.5f), new Vector3(FX1 - FX0, FH, FZ1 - FZ0));
        }

        /// <summary>A square cafe table with four chairs; each chair is a Seat point (approached from its side).</summary>
        static void CafeTable(Vector3 c, bool metal = false)
        {
            Add(metal ? "MetalLight" : "Wood", c + new Vector3(-0.45f, 0.72f, -0.45f), c + new Vector3(0.45f, 0.76f, 0.45f), 1f);
            Add("PipeDark", c + new Vector3(-0.05f, 0f, -0.05f), c + new Vector3(0.05f, 0.72f, 0.05f), 1f, false);
            Add("PipeDark", c + new Vector3(-0.3f, 0f, -0.3f), c + new Vector3(0.3f, 0.03f, 0.3f), 1f, false);
            Acc(collide, "col").BoxMinMax(c + new Vector3(-0.45f, 0f, -0.45f), c + new Vector3(0.45f, 0.76f, 0.45f), 1f);
            foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                var seat = c + d * 0.78f;
                var rot = Quaternion.LookRotation(-d, Vector3.up);
                AddRot("Seat", seat + Vector3.up * 0.45f, new Vector3(0.44f, 0.06f, 0.44f), rot, 1f, false);
                AddRot("Seat", seat + Vector3.up * 0.75f + d * 0.2f, new Vector3(0.44f, 0.5f, 0.05f), rot * Quaternion.Euler(-8f, 0f, 0f), 1f, false);
                AddRot("PipeDark", seat + Vector3.up * 0.22f, new Vector3(0.05f, 0.44f, 0.05f), rot, 1f, false);
                cafeSeats.Add((seat, -d));
            }
        }

        // ---------- Staff (shootable, standing NPCs) ----------

/// <summary>Food court regulars: they start seated, share tables, and mostly stay in the food court.</summary>
        static void BuildPatrons()
        {
            var names = new[] { "Priya Nair", "Oskar Lind", "Ama Mensah", "Hugo Brandt", "Yara Haddad", "Ken Watanabe", "Zofia Nowak", "Tariq Aziz" };
            var jackets = new[] { Palette.StMetalLight, Palette.StVisitorTeal, Palette.StPilotOlive, Palette.UIBone, Palette.StFoliage, Palette.UIPanel, Palette.StMetal, Palette.StWood };
            var skins = new[] { Palette.SkinLight, Palette.SkinMid, Palette.SkinDark, Palette.SkinDeep };
            var hairs = new[] { Palette.Grip, Palette.HairBrown, Palette.HairBlonde, Palette.HairGrey };
            var outfits = new[] { Outfit.Casual, Outfit.Uniform, Outfit.Vest, Outfit.FlightJacket, Outfit.Casual, Outfit.LabCoat, Outfit.Uniform, Outfit.Casual };
            var lines = new (string greet, string q, string a, string bye)[]
            {
                ("Grab a seat, the noodles are worth the wait.", "Come here often?", "Every shift. It's the only room on the station that smells like a planet.", "Enjoy your meal."),
                ("Mind the tray, I'm a messy eater.", "What's good here?", "Grill 03 on a good day. On a bad day, also Grill 03, but with regret.", "See you."),
                ("Break time. Ten whole minutes.", "Busy day?", "Hydroponics had a pump fail. I've been elbow deep in nutrient slurry since dawn.", "Back to the slurry."),
                ("Pull up a chair if you like.", "What do you do?", "Cargo logistics. If it lands on this station, it crosses my desk twice.", "Take care."),
                ("Coffee first, conversation after.", "How's the coffee?", "Noor does something to it. I don't ask. I just drink it.", "Cheers."),
                ("They say the view from the mezzanine is better. They're right.", "Why eat here then?", "Because the food is here and the view doesn't fit on a tray.", "Bye now."),
                ("Shh, I'm on my lunch. Kidding. Hi.", "How long have you been here?", "Fourteen months. My cactus is the only one who hasn't complained once.", "Say hi to the cactus."),
                ("Did you hear the range earlier? Somebody's having fun.", "Does it bother you?", "Only when the walls shake. Which is always. You get used to it.", "Stay safe."),
            };
            var spots = new[] { new Vector3(-23.5f, 0f, -27.4f), new Vector3(-22.7f, 0f, -28.2f), new Vector3(-19f, 0f, -22.6f), new Vector3(-7f, 0f, -27.4f),
                                new Vector3(-6.2f, 0f, -28.2f), new Vector3(-2.8f, 0f, -22.6f), new Vector3(-16.2f, 0f, -24.6f), new Vector3(-9.8f, 0f, -29.4f) };
            for (int i = 0; i < names.Length; i++)
            {
                string id = "Patron" + i;
                var look = new Look { jacket = jackets[i], pants = i % 2 == 0 ? Palette.UIPanel : Palette.UIDark, skin = skins[(i * 3) % skins.Length], hair = hairs[(i * 5 + 1) % hairs.Length],
                                      accent = i % 3 == 0 ? Palette.StAccentOrange : Palette.StTechCyan, hairStyle = i % 3, height = 0.95f + (i % 4) * 0.03f, build = 0.94f + (i % 3) * 0.06f, outfit = outfits[i] };
                var c = BuildCharacter("NPC_" + names[i].Replace(" ", ""), spots[i], rng.Next(0, 360), look, id, true);
                MakeCivilian(c);
                var agent = c.gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
                agent.radius = 0.32f; agent.height = 1.8f; agent.avoidancePriority = 60 + i;
                var npc = c.gameObject.AddComponent<Alcax.Dialogue.Npc>();
                npc.displayName = names[i];
                npc.role = "Station Crew";
                var l = lines[i];
                npc.dialogue = SmallTalk(id, l.greet, l.q, l.a, l.bye);
                var brain = c.gameObject.AddComponent<Alcax.Dialogue.AmbientNpc>();
                SetEnum(brain, "temperament", i == 3 || i == 6 ? 2 : 0);   // mostly settlers (seated); two socials walk around and chat
                var so = new SerializedObject(brain);
                so.FindProperty("hasHome").boolValue = true;
                so.FindProperty("homeMin").vector3Value = new Vector3(FX0 + 0.5f, -0.5f, FZ0 + 4f);
                so.FindProperty("homeMax").vector3Value = new Vector3(FX1 - 0.5f, 1.5f, FZ1 - 0.6f);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static Alcax.Dialogue.Civilian quartermaster;
        static Color quartermasterJacket;

        static void BuildStaff()
        {
            var staff = new (string name, string role, Look look, Vector3 pos, float yaw, string greet, string q, string a, string bye)[]
            {
                ("Bayo Adeyemi", "Quartermaster",
                    new Look { jacket = Palette.StPilotOlive, pants = Palette.UIDark, skin = Palette.SkinDeep, hair = Palette.Grip, accent = Palette.StAccentOrange, hairStyle = 1, height = 1.04f, build = 1.12f, outfit = Outfit.Vest },
                    new Vector3(8.05f, 0f, -27.2f), 0f, "Armory. Everything on the wall is signed out to you the moment you lift it.", "Any rules?", "Live fire on the range only. Fire anywhere else and security will be on you before the brass stops rolling.", "Bring them back clean."),
                ("Mei Lin", "Noodle Bar",
                    new Look { jacket = Palette.UIBone, pants = Palette.UIPanel, skin = Palette.SkinLight, hair = Palette.Grip, accent = Palette.StAccentOrange, hairStyle = 0, height = 0.96f, build = 0.95f, outfit = Outfit.Casual },
                    new Vector3(-21.5f, 0f, FZ0 + 1.6f), 0f, "Noodles, broth, the spicy one if you're brave.", "What's good?", "The hydro greens came in fresh this cycle. Order the green broth before the night shift eats it all.", "Come back hungry."),
                ("Diego Vargas", "Grill 03",
                    new Look { jacket = Palette.StMetalLight, pants = Palette.StWood, skin = Palette.SkinMid, hair = Palette.HairBrown, accent = Palette.StHazard, hairStyle = 2, height = 1.02f, build = 1.1f, outfit = Outfit.Vest },
                    new Vector3(-13f, 0f, FZ0 + 1.6f), 0f, "Grill's hot. Protein is real, mostly.", "Mostly?", "Mostly. Don't ask the follow-up question.", "Enjoy."),
                ("Noor Rahimi", "Orbit Coffee",
                    new Look { jacket = Palette.StVisitorTeal, pants = Palette.UIDark, skin = Palette.SkinMid, hair = Palette.Grip, accent = Palette.UIBone, hairStyle = 0, height = 0.98f, build = 0.96f, outfit = Outfit.Casual },
                    new Vector3(-4.5f, 0f, FZ0 + 1.6f), 0f, "Coffee's the only thing on this station that's never late.", "Busy today?", "Shift change in an hour. Then it's a line out the door and somebody always cries about oat milk.", "Stay caffeinated."),
            };
            for (int i = 0; i < staff.Length; i++)
            {
                var s = staff[i];
                string id = "Staff" + i;
                var c = BuildCharacter("NPC_" + s.name.Replace(" ", ""), s.pos, s.yaw, s.look, id, true);
                MakeCivilian(c);
                if (i == 0) { quartermaster = c.GetComponent<Alcax.Dialogue.Civilian>(); quartermasterJacket = s.look.jacket; }
                var npc = c.gameObject.AddComponent<Alcax.Dialogue.Npc>();
                npc.displayName = s.name;
                npc.role = s.role;
                npc.dialogue = SmallTalk(id, s.greet, s.q, s.a, s.bye);
            }
        }

        /// <summary>Makes a rig character shootable: a kinematic body for the death collapse, Health, and Civilian.</summary>
        static void MakeCivilian(Transform c)
        {
            var rb = c.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = 70f;
            c.gameObject.AddComponent<Health>();
            c.gameObject.AddComponent<Alcax.Dialogue.Civilian>();
        }

        // ---------- Station security ----------

        static void BuildSecurity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Prefabs/AI/OP_Guard.prefab");
            var arc = AssetDatabase.LoadAssetAtPath<Alcax.AI.EnemyArchetype>($"{Root}/Data/AI/ARC_Guard.asset");
            var fac = AssetDatabase.LoadAssetAtPath<Alcax.AI.Faction>($"{Root}/Data/AI/FAC_Security.asset");
            var cfg = AssetDatabase.LoadAssetAtPath<Alcax.AI.CombatAIConfig>($"{Root}/Data/AI/CombatAIConfig.asset");
            if (prefab == null || arc == null || fac == null || cfg == null)
            {
                Debug.LogWarning("[AtriumBuilder] Guard assets missing: build the AI Playground first (Alcax > Build AI Playground).");
                return;
            }
            var sec = new GameObject("StationSecurity");
            sec.transform.SetParent(root, false);
            var ss = sec.AddComponent<Alcax.AI.StationSecurity>();
            Alcax.AI.PatrolRoute Route(string name, params Vector3[] pts)
            {
                var r = new GameObject(name).AddComponent<Alcax.AI.PatrolRoute>();
                r.transform.SetParent(sec.transform, false);
                for (int i = 0; i < pts.Length; i++)
                {
                    var t = new GameObject("P" + i).transform;
                    t.SetParent(r.transform, false);
                    t.position = pts[i];
                }
                return r;
            }
            var routes = new Object[]
            {
                Route("Patrol_Atrium", new Vector3(-19f, 0f, -12f), new Vector3(-19f, 0f, 9f), new Vector3(0f, 0f, 4f), new Vector3(19f, 0f, 9f), new Vector3(19f, 0f, -12f), new Vector3(0f, 0f, -14f)),
                Route("Patrol_FoodCourt", new Vector3(-13f, 0f, -22f), new Vector3(-21f, 0f, -31f), new Vector3(-5f, 0f, -31f), new Vector3(-13f, 0f, -14f), new Vector3(-8f, MezzY, -15f), new Vector3(8f, MezzY, -15f)),
                Route("Patrol_Wing", new Vector3(13f, 0f, -16f), new Vector3(13f, 0f, -32f), new Vector3(6f, 0f, -24f), new Vector3(13f, 0f, -23.4f), new Vector3(21f, 0f, -4f)),
            };
            SetRef(ss, "guardPrefab", prefab);
            SetRef(ss, "guardArchetype", arc);
            SetRef(ss, "config", cfg);
            SetRef(ss, "faction", fac);
            SetRefArray(ss, "routes", routes);
if (quartermaster != null)
            {
                SetRefArray(ss, "armedStaff", new Object[] { quartermaster });
                var so = new SerializedObject(ss);
                var arr = so.FindProperty("armedStaffJacket");
                arr.arraySize = 1;
                arr.GetArrayElementAtIndex(0).colorValue = quartermasterJacket;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            SetVectorArray(ss, "freeFireMin", new[] { new Vector3(RangeX0, -1f, RangeZ0) });
            SetVectorArray(ss, "freeFireMax", new[] { new Vector3(RangeX1, RangeH, RangeZ1) });
        }

        /// <summary>Ambience zones for the armory and the food court (the atrium loop, heard through their doors).</summary>
        static void BuildExtraAmbience()
        {
            if (ambRef == null || hallZoneRef == null) return;
            Alcax.FX.AmbienceZone Zone(string name, Vector3 min, Vector3 max, AutoDoor door, Vector3 portal, Alcax.FX.AmbienceZone next)
            {
                var z = new GameObject("AmbienceZone_" + name).AddComponent<Alcax.FX.AmbienceZone>();
                z.transform.SetParent(ambRef.transform, false);
                var pt = new GameObject("Portal").transform;
                pt.SetParent(z.transform, false);
                pt.position = portal;
                var so = new SerializedObject(z);
                so.FindProperty("min").vector3Value = min;
                so.FindProperty("max").vector3Value = max;
                so.FindProperty("door").objectReferenceValue = door;
                so.FindProperty("portal").objectReferenceValue = pt;
                so.FindProperty("next").objectReferenceValue = next;
                so.ApplyModifiedPropertiesWithoutUndo();
                return z;
            }
            var zones = new List<Object>();
            var so0 = new SerializedObject(ambRef);
            var arr = so0.FindProperty("zones");
            for (int i = 0; i < arr.arraySize; i++) zones.Add(arr.GetArrayElementAtIndex(i).objectReferenceValue);
            zones.Add(Zone("Armory", new Vector3(AX0 - 0.4f, -1f, AZ0 - 0.4f), new Vector3(AX1, AH + 1f, AZ1 + 0.4f), armoryDoor, new Vector3(HallX0 - 0.2f, 1.3f, (RDoorZ0 + RDoorZ1) * 0.5f), hallZoneRef));
            var fcZone = Zone("FoodCourt", new Vector3(FX0 - 0.4f, -1f, FZ0 - 0.4f), new Vector3(FX1 + 0.4f, FH + 1f, FZ1), foodDoor, new Vector3(-13f, 2.2f, -HZ - WallT * 0.5f), null);
            zones.Add(fcZone);
var hotelHall = Zone("HotelHall", new Vector3(HX + WallT, MezzY - 0.5f, HtlDoorZ0 - 0.4f), new Vector3(HtlHallX1, MezzY + 3.8f, HtlDoorZ1 + 0.4f), null, new Vector3(HX + WallT * 0.5f, MezzY + 1.6f, (HtlDoorZ0 + HtlDoorZ1) * 0.5f), null);
            zones.Add(hotelHall);
            zones.Add(Zone("HotelLobby", new Vector3(LobX0, MezzY - 0.5f, LobZ0), new Vector3(LobX1, LobTop, LobZ1), hotelLobbyDoor, new Vector3(LobX0 - 0.2f, MezzY + 1.5f, (HtlDoorZ0 + HtlDoorZ1) * 0.5f), hotelHall));
            zones.Add(Zone("Restrooms", new Vector3(RX0 - 0.4f, -1f, RZ0 - 0.4f), new Vector3(RX1, RRH + 1f, RZ1 + 0.4f), restroomDoor, new Vector3(FX0 - 0.2f, 1.3f, (WCZ0 + WCZ1) * 0.5f), fcZone));
            SetRefArray(ambRef, "zones", zones);
        }

        static void AreaLight(Vector3 pos, Vector2 size, float intensity)
        {
            var lg = new GameObject("WingAreaLight");
            lg.transform.SetParent(root, false);
            lg.transform.position = pos;
            lg.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            var al = lg.AddComponent<Light>();
            al.type = LightType.Rectangle;
            al.areaSize = size;
            al.color = Palette.StPanelLight;
            al.intensity = intensity;
            al.lightmapBakeType = LightmapBakeType.Baked;
        }

        static void WingProbe(Vector3 center, Vector3 size)
        {
            var rp = new GameObject("WingReflectionProbe").AddComponent<ReflectionProbe>();
            rp.transform.SetParent(root, false);
            rp.transform.position = center;
            rp.mode = ReflectionProbeMode.Baked;
            rp.boxProjection = true;
            rp.size = size + Vector3.one * 0.2f;
            rp.resolution = 256;
            rp.importance = 2;
            var lpg = new GameObject("WingLightProbes").AddComponent<LightProbeGroup>();
            lpg.transform.SetParent(root, false);
            var pts = new List<Vector3>();
            for (float x = -size.x * 0.5f + 0.5f; x <= size.x * 0.5f - 0.5f; x += 3f)
                for (float z = -size.z * 0.5f + 0.5f; z <= size.z * 0.5f - 0.5f; z += 3f)
                    foreach (float y in new[] { -1.7f, 0f, 1.6f })
                        pts.Add(new Vector3(x, y, z));
            lpg.transform.position = center;
            lpg.probePositions = pts.ToArray();
        }

        // ---------- Characters (shared rig for Ines and the residents) ----------

        static readonly Dictionary<string, Material> charMats = new();

        static Material CharMat(string key, Color c, float smooth = 0.3f)
        {
            if (charMats.TryGetValue(key, out var m)) return m;
            m = Flat("Char_" + key, c, smooth);
            charMats[key] = m;
            return m;
        }


        /// <summary>Builds a character on the shared rig (see CharacterRig).</summary>
        static Transform BuildCharacter(string objName, Vector3 pos, float yaw, Look look, string id, bool shootable = false)
            => CharacterRig.Build(root, objName, pos, yaw, look, id, (k, c, sm) => CharMat(k, c, sm), mats["NpcHair"], mats["MetalLight"], combat: shootable);

        static Alcax.Dialogue.DialogueTree SmallTalk(string id, string greet, string topicQ, string topicA, string bye)
        {
            string path = $"{Root}/Data/Dialogue/DLG_{id}.asset";
            EnsureFolder($"{Root}/Data/Dialogue");
            AssetDatabase.DeleteAsset(path);
            var d = ScriptableObject.CreateInstance<Alcax.Dialogue.DialogueTree>();
            d.startNode = "start";
            d.Add("start", greet, (topicQ, "topic"), ("See you around.", "bye"));
            d.Add("topic", topicA, ("Ask something else.", "start"), ("Take care.", "bye"));
            d.Add("bye", bye);
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        // ---------- Points of interest and residents ----------

        static readonly List<(Vector3 pos, Vector3 facing)> seatPoints = new();

        static void BuildStationPoints()
        {
            var parent = new GameObject("StationPoints").transform;
            parent.SetParent(root, false);
            void Point(Alcax.Dialogue.PointKind kind, Vector3 pos, Vector3 facing)
            {
                var t = new GameObject(kind.ToString()).transform;
                t.SetParent(parent, false);
                t.position = pos;
                t.rotation = Quaternion.LookRotation(facing, Vector3.up);
                t.gameObject.AddComponent<Alcax.Dialogue.StationPoint>().kind = kind;
            }
            foreach (var (pos, facing) in seatPoints) Point(Alcax.Dialogue.PointKind.Seat, pos, facing);
            foreach (var (pos, facing) in lobbySeats) Point(Alcax.Dialogue.PointKind.Seat, pos, facing);
            foreach (var (pos, facing) in cafeSeats)
            {
                Point(Alcax.Dialogue.PointKind.Seat, pos, facing);
                parent.GetChild(parent.childCount - 1).GetComponent<Alcax.Dialogue.StationPoint>().approachOffset = new Vector3(0.6f, 0f, 0.05f);   // from the side (the table is in front)
            }
            foreach (float x in new[] { -20f, -16.5f, -9f, -6.5f, 6.5f, 9f, 16.5f, 20f })
                Point(Alcax.Dialogue.PointKind.Window, new Vector3(x, 0f, x > -12f && x < 12f ? 14.4f : 13.7f), Vector3.forward);
            Point(Alcax.Dialogue.PointKind.Console, new Vector3(-20.2f, 0f, 0f), Vector3.left);
            Point(Alcax.Dialogue.PointKind.Console, new Vector3(20.2f, 0f, 0.5f), Vector3.right);
            Point(Alcax.Dialogue.PointKind.Console, new Vector3(-9.5f, 0f, -7.3f), Vector3.back);
            Point(Alcax.Dialogue.PointKind.Console, new Vector3(9.5f, 0f, -7.3f), Vector3.back);
            foreach (float x in new[] { -20f, -11f, 6f, 15f, 23f })
                Point(Alcax.Dialogue.PointKind.Railing, new Vector3(x, MezzY, MezzEdgeZ - 0.4f), Vector3.forward);
        }

        static void BuildResidents()
        {
            var residents = new (string name, string role, Look look, Vector3 pos, string greet, string q, string a, string bye)[]
            {
                ("Tomas Reyes", "Life Support Engineer",
                    new Look { jacket = Palette.StMetal, pants = Palette.UIPanel, skin = Palette.SkinMid, hair = Palette.Grip, accent = Palette.StHazard, hairStyle = 2, height = 1.03f, build = 1.08f, outfit = Outfit.Vest },
                    new Vector3(-6f, 0f, 6f), "Don't mind the pipes. Half of them hum in B-flat.", "What do you work on?", "Life support. Air, water, pressure. If it ever smells like pennies in here, come find me. Fast.", "Breathe easy."),
                ("Dr. Amara Osei", "Hydroponics Lead",
                    new Look { jacket = Palette.UIBone, pants = Palette.UIPanel, skin = Palette.SkinDeep, hair = Palette.Grip, accent = Palette.StTechCyan, hairStyle = 0, height = 0.97f, build = 0.95f, outfit = Outfit.LabCoat },
                    new Vector3(5f, 0f, 9f), "The palms are a morale experiment, believe it or not.", "Is it working?", "Productivity's up four percent since we planted them. Or that's the new coffee. Hard to isolate variables on a moon.", "Mind the soil. It's imported, every grain."),
                ("Rhea Kalnins", "Shuttle Pilot",
                    new Look { jacket = Palette.StPilotOlive, pants = Palette.UIPanel, skin = Palette.SkinLight, hair = Palette.HairBlonde, accent = Palette.StAccentOrange, hairStyle = 1, height = 1.0f, build = 0.98f, outfit = Outfit.FlightJacket },
                    new Vector3(12f, 0f, 2f), "Just brought a supply run down from the ring.", "How's the landing?", "Kharon's thermals make every approach exciting. Pad Two's crosswind is the worst. I love it.", "Clear skies."),
                ("Malik Haddad", "Security",
                    new Look { jacket = Palette.UIPanel, pants = Palette.UIDark, skin = Palette.SkinMid, hair = Palette.Grip, accent = Palette.StTechCyan, hairStyle = 1, height = 1.06f, build = 1.12f, outfit = Outfit.Uniform },
                    new Vector3(-12f, 0f, -3f), "Keep it civil in the atrium. The range is through the east door if you need to blow off steam.", "What's going on with the labs?", "Locked down since Tuesday. Don't ask me why. I genuinely don't know why, and nobody's telling me.", "Stay out of trouble."),
                ("Sefa Tupou", "Botanist",
                    new Look { jacket = Palette.StFoliage, pants = Palette.StSoil, skin = Palette.SkinDark, hair = Palette.HairBrown, accent = Palette.UIBone, hairStyle = 0, height = 0.95f, build = 1.0f, outfit = Outfit.Casual },
                    new Vector3(-16f, 0f, 10f), "Careful by the planters. I just turned that soil.", "What grows best here?", "Anything stubborn. The yuccas love it. The ferns sulk unless I talk to them.", "Say hi to the ferns."),
                ("Ivo Petrov", "Systems Technician",
                    new Look { jacket = Palette.StMetalLight, pants = Palette.StMetal, skin = Palette.SkinLight, hair = Palette.HairGrey, accent = Palette.StTechCyan, hairStyle = 1, height = 1.01f, build = 1.04f, outfit = Outfit.Uniform },
                    new Vector3(16f, 0f, -6f), "The info desk screen glitched again. Third time today.", "Is that normal?", "Nothing on this station is normal. But the glitches line up with the lab doors cycling. I'm writing it down.", "If the lights flicker, it wasn't me."),
                ("Lena Moreau", "Operations Crew",
                    new Look { jacket = Palette.StMetalLight, pants = Palette.UIPanel, skin = Palette.SkinLight, hair = Palette.HairBrown, accent = Palette.StAccentOrange, hairStyle = 0, height = 0.98f, build = 0.96f, outfit = Outfit.Uniform },
                    new Vector3(2f, 0f, -8f), "Shift change in twenty. Then coffee. Then sleep. In that order.", "Busy shift?", "Two shuttles, a pressure alarm, and somebody left a wrench in a vent. So, a normal Thursday.", "Don't wake me."),
                ("Jun Park", "New Arrival",
                    new Look { jacket = Palette.StVisitorTeal, pants = Palette.StWood, skin = Palette.SkinMid, hair = Palette.Grip, accent = Palette.UIBone, hairStyle = 1, height = 0.99f, build = 0.97f, outfit = Outfit.Casual },
                    new Vector3(-3f, 0f, 2f), "First week on station. I keep getting lost on the mezzanine.", "How are you finding it?", "The view is unreal. The food is... real. Too real. Do you know where the canteen is? Nobody does.", "See you, I guess. If I can find you."),
            };
            for (int i = 0; i < residents.Length; i++)
            {
                var r = residents[i];
                string id = "Res" + i;
                var c = BuildCharacter("NPC_" + r.name.Replace(" ", "").Replace(".", ""), r.pos, rng.Next(0, 360), r.look, id, true);
                MakeCivilian(c);
                var agent = c.gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
                agent.radius = 0.32f;
                agent.height = 1.8f;
                agent.speed = 1.2f + (float)rng.NextDouble() * 0.25f;
                agent.avoidancePriority = 40 + i;
                var npc = c.gameObject.AddComponent<Alcax.Dialogue.Npc>();
                npc.displayName = r.name;
                npc.role = r.role;
                npc.dialogue = SmallTalk(id, r.greet, r.q, r.a, r.bye);
                var brain = c.gameObject.AddComponent<Alcax.Dialogue.AmbientNpc>();
                // Index order: Tomas, Amara, Rhea, Malik, Sefa, Ivo, Lena, Jun
                var temperaments = new[] { 1, 1, 2, 0, 0, 1, 0, 2 };   // 0 settler, 1 worker, 2 social
                SetEnum(brain, "temperament", temperaments[i]);
            }
        }

        static void BuildAtriumNavMesh()
        {
            var navGo = new GameObject("NavMesh");
            navGo.transform.SetParent(root, false);
            var surf = navGo.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surf.collectObjects = Unity.AI.Navigation.CollectObjects.All;
            surf.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surf.layerMask = ~(1 << 2);
            surf.BuildNavMesh();
            string path = $"{Root}/Scenes/Atrium_NavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surf.navMeshData, path);
            EditorUtility.SetDirty(surf);
        }

        // ---------- NPC ----------

        static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, string mat)
        {
            var go = Prim(type, name, parent, pos, scale, euler, mats[mat]);
            return go;
        }

        /// <summary>Ines, the information desk attendant: a stylized human on a primitive rig, in a light station uniform.</summary>
        static void BuildNpc()
        {
            var root = BuildCharacter("NPC_Ines", new Vector3(0f, 0f, 12.05f), 180f,
                new Look { jacket = Palette.StMetalLight, pants = Palette.UIPanel, skin = Palette.PlayerSkin, hair = Palette.Grip, accent = Palette.StTechCyan, hairStyle = 0, height = 1f, build = 1f, outfit = Outfit.Uniform }, "Ines", true);
            MakeCivilian(root);
            var inesAgent = root.gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();   // only enabled while she flees
            inesAgent.radius = 0.32f; inesAgent.height = 1.8f; inesAgent.speed = 3.8f; inesAgent.acceleration = 10f; inesAgent.avoidancePriority = 30;
            var npc = root.gameObject.AddComponent<Alcax.Dialogue.Npc>();
            npc.displayName = "Ines Varga";
            npc.role = "Guest Services, Atrium C";
            npc.dialogue = BuildInfoDialogue();
        }

        static Alcax.Dialogue.DialogueTree BuildInfoDialogue()
        {
            string path = $"{Root}/Data/Dialogue/DLG_InfoDesk.asset";
            EnsureFolder($"{Root}/Data/Dialogue");
            AssetDatabase.DeleteAsset(path);
            var d = ScriptableObject.CreateInstance<Alcax.Dialogue.DialogueTree>();
            d.startNode = "start";
            d.Add("start", "Welcome to Atrium C. I'm Ines, guest services. What can I do for you?",
                ("Where am I, exactly?", "where"),
                ("What's that planet out there?", "planet"),
                ("Are those ships landing outside?", "ships"),
                ("Anything I should know about?", "rumor"),
                ("Nothing for now, thanks.", "bye"));
            d.Add("where", "Hydroponics Deck, Atrium C. These gardens feed about four hundred crew. The mezzanine is open to visitors. The blast doors lead to the labs, and those are crew only.",
                ("Ask something else.", "start"),
                ("Thanks.", "bye"));
            d.Add("planet", "That's Tessaly, a gas giant. We're on its moon, Kharon. All that sand out there is silica: sixty-one degrees in the shade at noon, and minus forty at night. The glass is three layers thick, so don't worry.",
                ("How long is a day here?", "day"),
                ("Ask something else.", "start"));
            d.Add("day", "Kharon is tidally locked, so Tessaly never moves in our sky. We run on station time. You'll get used to the sunsets lasting forever.",
                ("Ask something else.", "start"),
                ("Good to know.", "bye"));
            d.Add("ships", "Supply shuttles up from the orbital ring. Pads One and Two get a drop every few minutes: water ice, seed stock, spare parts. If you're lucky you'll catch one lifting off at dusk.",
                ("Can I ride one?", "ride"),
                ("Ask something else.", "start"));
            d.Add("ride", "Only with a transfer permit, and the permit office is... also me. Come back when you've got paperwork.",
                ("Ask something else.", "start"),
                ("Fair enough.", "bye"));
            d.Add("rumor", "Between us? Night shift says the lab doors have been cycling on their own. Probably a sensor fault. Probably.",
                ("Who could I ask about that?", "rumor2"),
                ("Ask something else.", "start"),
                ("I'll keep my eyes open.", "bye"));
            d.Add("rumor2", "The lab supervisor, Dr. Hale, if you can find her. She hasn't checked in since Tuesday. Don't tell anyone I said that.",
                ("Ask something else.", "start"),
                ("My lips are sealed.", "bye"));
            d.Add("bye", "Enjoy the view. It never gets old.");
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        // ---------- Ships ----------

        static Mesh ShipPart(System.Action<MeshAccum> build, string name)
        {
            var a = new MeshAccum();
            build(a);
            return a.Build(name, false);
        }

        static Transform LandingPad(Vector3 center, float size, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var pad = new MeshAccum();
            pad.Box(center + Vector3.down * 0.3f, new Vector3(size, 0.6f, size), rot, 4f);
            var padMesh = pad.Build("Pad_" + Mathf.RoundToInt(center.x), false);
            MeshGO("LandingPad", padMesh, "Hazard", exterior, false);
            var lights = new MeshAccum();
            float h = size * 0.5f - 0.6f;
            foreach (var (x, z) in new[] { (-h, -h), (h, -h), (-h, h), (h, h), (0f, -h), (0f, h), (-h, 0f), (h, 0f) })
                lights.Box(center + rot * new Vector3(x, 0.1f, z), new Vector3(0.6f, 0.2f, 0.6f), rot, 1f);
            MeshGO("PadLights", lights.Build("PadLights_" + Mathf.RoundToInt(center.x), false), "OrangeStrip", exterior, false, false);
            var t = new GameObject("PadAnchor").transform;
            t.SetParent(exterior, false);
            t.position = center;
            t.rotation = rot;
            return t;
        }

        static ParticleSystem MakeParticles(string name, Transform parent, Vector3 localPos, Vector3 euler, Material mat)
        {
            var ps = new GameObject(name).AddComponent<ParticleSystem>();
            ps.transform.SetParent(parent, false);
            ps.transform.localPosition = localPos;
            ps.transform.localRotation = Quaternion.Euler(euler);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>A VTOL supply shuttle on a loop: approaches from the horizon, lands on its pad, waits, and lifts off.</summary>
        static void BuildShip(string name, Transform pad, Vector3 approachFrom, Vector3 departTo, float startDelay, float groundTime, Material particle)
        {
            var ship = new GameObject(name).transform;
            ship.SetParent(exterior, false);
            var model = new GameObject("Model").transform;
            model.SetParent(ship, false);

            var hull = ShipPart(a =>
            {
                a.Box(new Vector3(0f, 0f, 0f), new Vector3(3.4f, 2.4f, 9f), Quaternion.identity, 2f);
                a.Box(new Vector3(0f, -0.15f, 5.6f), new Vector3(2.7f, 1.8f, 3f), Quaternion.Euler(6f, 0f, 0f), 2f);
                a.Box(new Vector3(0f, -0.35f, 7.4f), new Vector3(1.9f, 1.1f, 1.4f), Quaternion.Euler(14f, 0f, 0f), 2f);
                a.Box(new Vector3(0f, 0.25f, -5.8f), new Vector3(2.8f, 2.1f, 3f), Quaternion.identity, 2f);
                foreach (float s in new[] { -1f, 1f })
                {
                    a.Box(new Vector3(s * 4.2f, -0.45f, -1.2f), new Vector3(5.4f, 0.25f, 4.2f), Quaternion.Euler(0f, s * 6f, s * -5f), 2f);
                    a.Box(new Vector3(s * 6.7f, 0.35f, -2.4f), new Vector3(0.22f, 1.7f, 2.4f), Quaternion.Euler(-12f, 0f, 0f), 2f);
                    a.Box(new Vector3(s * 1.0f, 1.9f, -6.2f), new Vector3(0.2f, 1.6f, 2.2f), Quaternion.Euler(-20f, 0f, s * 8f), 2f);
                }
            }, name + "_Hull");
            var dark = ShipPart(a =>
            {
                foreach (float s in new[] { -1f, 1f })
                    foreach (float z in new[] { 1.8f, -3.2f })
                    {
                        a.Box(new Vector3(s * 3.4f, -0.7f, z), new Vector3(1.4f, 1.3f, 2.6f), Quaternion.identity, 2f);
                        a.Box(new Vector3(s * 3.4f, -2.0f, z), new Vector3(0.22f, 1.3f, 0.22f), Quaternion.identity, 2f);
                        a.Box(new Vector3(s * 3.4f, -2.65f, z), new Vector3(0.7f, 0.08f, 0.7f), Quaternion.identity, 2f);
                    }
                a.Box(new Vector3(0f, -1.25f, 0f), new Vector3(2.6f, 0.15f, 8f), Quaternion.identity, 2f);
                a.Box(new Vector3(0f, 0.2f, -7.35f), new Vector3(2.4f, 1.6f, 0.3f), Quaternion.identity, 2f);
            }, name + "_Dark");
            var glow = ShipPart(a =>
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    foreach (float z in new[] { 1.8f, -3.2f })
                        a.Box(new Vector3(s * 3.4f, -1.37f, z), new Vector3(1.0f, 0.06f, 2.0f), Quaternion.identity, 1f);
                    a.Box(new Vector3(s * 0.7f, 0.2f, -7.52f), new Vector3(0.7f, 0.7f, 0.05f), Quaternion.identity, 1f);
                }
            }, name + "_Glow");
            var visor = ShipPart(a => a.Box(new Vector3(0f, 0.72f, 5.0f), new Vector3(2.1f, 0.6f, 1.9f), Quaternion.Euler(-18f, 0f, 0f), 1f), name + "_Visor");
            var stripe = ShipPart(a =>
            {
                foreach (float s in new[] { -1f, 1f })
                    a.Box(new Vector3(s * 1.72f, 0.3f, 0f), new Vector3(0.03f, 0.5f, 8f), Quaternion.identity, 1.2f);
            }, name + "_Stripe");

            void Piece(Mesh m, string mat, bool shadows = true)
            {
                var go = MeshGO(m.name, m, mat, model, false, shadows);
                GameObjectUtility.SetStaticEditorFlags(go, 0);
            }
            Piece(hull, "ShipHull");
            Piece(dark, "ShipDark");
            Piece(glow, "EngineGlow", false);
            Piece(visor, "Water");
            Piece(stripe, "Hazard");

            var lightGo = new GameObject("EngineLight");
            lightGo.transform.SetParent(ship, false);
            lightGo.transform.localPosition = new Vector3(0f, -3f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = Palette.StTechCyan;
            l.range = 45f;
            l.intensity = 0f;
            l.shadows = LightShadows.None;

            var exhaust = MakeParticles("Exhaust", ship, new Vector3(0f, -1.5f, -0.6f), new Vector3(90f, 0f, 0f), particle);
            var em = exhaust.main;
            em.loop = true;
            em.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(14f, 22f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            em.startColor = Palette.WithAlpha(Palette.StPanelLight, 0.5f);
            em.simulationSpace = ParticleSystemSimulationSpace.World;
            em.maxParticles = 300;
            var esh = exhaust.shape; esh.shapeType = ParticleSystemShapeType.Box; esh.scale = new Vector3(7f, 5f, 0.1f);
            var ee = exhaust.emission; ee.rateOverTime = 0f;
            var ecol = exhaust.colorOverLifetime; ecol.enabled = true;
            var eg = new Gradient();
            eg.SetKeys(new[] { new GradientColorKey(Palette.StPanelLight, 0f), new GradientColorKey(Palette.StTechCyan, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
            ecol.color = eg;
            exhaust.Play();

            var dust = MakeParticles("PadDust", pad, new Vector3(0f, 0.3f, 0f), new Vector3(-90f, 0f, 0f), particle);
            var dm = dust.main;
            dm.loop = true;
            dm.playOnAwake = false;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            dm.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            dm.startSize = new ParticleSystem.MinMaxCurve(3f, 7f);
            dm.startColor = Palette.WithAlpha(Palette.StSand, 0.28f);
            dm.simulationSpace = ParticleSystemSimulationSpace.World;
            dm.maxParticles = 250;
            var dsh = dust.shape; dsh.shapeType = ParticleSystemShapeType.Circle; dsh.radius = 7f; dsh.radiusThickness = 0.2f;
            var de = dust.emission; de.rateOverTime = 60f;
            var dsol = dust.sizeOverLifetime; dsol.enabled = true; dsol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.6f));
            var dcol = dust.colorOverLifetime; dcol.enabled = true;
            var dg = new Gradient();
            dg.SetKeys(new[] { new GradientColorKey(Palette.StSand, 0f), new GradientColorKey(Palette.StSandShadow, 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
            dcol.color = dg;
            var dvel = dust.velocityOverLifetime; dvel.enabled = true; dvel.radial = new ParticleSystem.MinMaxCurve(6f);

            var traffic = ship.gameObject.AddComponent<ShipTraffic>();
            SetRef(traffic, "pad", pad);
            SetRef(traffic, "engineLight", l);
            SetRef(traffic, "exhaust", exhaust);
            SetRef(traffic, "padDust", dust);
            var so = new SerializedObject(traffic);
            so.FindProperty("approachFrom").vector3Value = approachFrom;
            so.FindProperty("departTo").vector3Value = departTo;
            so.FindProperty("startDelay").floatValue = startDelay;
            so.FindProperty("groundTime").floatValue = groundTime;
            so.ApplyModifiedPropertiesWithoutUndo();
            ship.position = pad.position + Vector3.up * 2000f;
        }

        static void BuildShips()
        {
            var particle = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Art/Materials/M_Particle.mat");
            var padA = LandingPad(new Vector3(30f, -2.4f, 74f), 24f, 0f);
            var padB = LandingPad(new Vector3(-55f, -2.6f, 150f), 26f, 25f);
            BuildShip("Shuttle_A", padA, new Vector3(-0.8f, 0.35f, 1f), new Vector3(0.7f, 0.55f, 1f), 4f, 18f, particle);
            BuildShip("Shuttle_B", padB, new Vector3(0.9f, 0.3f, 0.7f), new Vector3(-0.6f, 0.5f, 1f), 26f, 26f, particle);
        }

        // ---------- FX, probes, managers ----------

        static void BuildFX(Light sun)
        {
            var particle = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Art/Materials/M_Particle.mat");

            // Light shafts through each window bay
            var L = sun.transform.forward;
            var shafts = new MeshAccum();
            for (int i = 0; i < 8; i++)
                for (int j = 0; j < 3; j++)
                {
                    float x = -HX + 3.5f + i * 7f;
                    float y = 3.6f + j * 6f;
                    float len = Mathf.Min(40f, (y + 0.5f) / Mathf.Max(0.05f, -L.y));
                    var start = new Vector3(x, y, HZ - 0.2f);
                    var center = start + L * (len * 0.5f);
                    var right = Vector3.right * 3.1f;
                    var along = L * (len * 0.5f);
                    shafts.Card(center, right, along, false);
                    var vert = Vector3.Cross(L, Vector3.right).normalized * 2.6f;
                    shafts.Card(center, vert, along, false);
                }
            var sm = shafts.Build("Atrium_Shafts", false);
            // Card UVs run 0..1 along "up" (the shaft length): that's what the shader fades on.
            var sgo = MeshGO("LightShafts", sm, "Shaft", root, false, false);
            GameObjectUtility.SetStaticEditorFlags(sgo, 0);

            // Steam from the floor grates
            foreach (var (c, ventSize) in slotVents)
            {
                var ps = new GameObject("Steam").AddComponent<ParticleSystem>();
                ps.transform.SetParent(root, false);
                ps.transform.position = c;
                ps.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                var main = ps.main;
                main.loop = true; main.prewarm = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = Palette.WithAlpha(Palette.StSteam, 0.07f);
                main.maxParticles = 80;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = 6f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(ventSize.x, ventSize.z, 0.05f);
                var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.2f));
                var col = ps.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Palette.StSteam, 0f), new GradientColorKey(Palette.StSteam, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.25f;
                var pr = ps.GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = particle;
                pr.shadowCastingMode = ShadowCastingMode.Off;
            }

            // Dust motes drifting in the sunlit half of the room
            var dust = new GameObject("DustMotes").AddComponent<ParticleSystem>();
            dust.transform.SetParent(root, false);
            dust.transform.position = new Vector3(0f, 6f, 6f);
            var dm = dust.main;
            dm.loop = true; dm.prewarm = true;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            dm.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            dm.startColor = Palette.WithAlpha(Palette.StPanelLight, 0.22f);
            dm.maxParticles = 600;
            dm.simulationSpace = ParticleSystemSimulationSpace.World;
            var dem = dust.emission; dem.rateOverTime = 25f;
            var dsh = dust.shape; dsh.shapeType = ParticleSystemShapeType.Box; dsh.scale = new Vector3(50f, 10f, 22f);
            var dn = dust.noise; dn.enabled = true; dn.strength = 0.1f; dn.frequency = 0.1f;
            var dpr = dust.GetComponent<ParticleSystemRenderer>();
            dpr.sharedMaterial = particle;
            dpr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildProbes()
        {
            var rp = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
            rp.transform.SetParent(root, false);
            rp.transform.position = new Vector3(0f, 4f, 0f);
            rp.mode = ReflectionProbeMode.Baked;
            rp.boxProjection = true;
            rp.size = new Vector3(HX * 2f, CeilY + 2f, HZ * 2f + 1f);
            rp.center = new Vector3(0f, CeilY * 0.5f - 4f + 0.5f, 0.3f);
            rp.resolution = 512;
            rp.intensity = 1f;
            rp.blendDistance = 1f;

            // A dimmer probe around the window so the glass mirrors the interior at about 40% (keeps the view outside readable).
            var gp = new GameObject("GlassReflectionProbe").AddComponent<ReflectionProbe>();
            gp.transform.SetParent(root, false);
            gp.transform.position = new Vector3(0f, 9f, 14f);
            gp.mode = ReflectionProbeMode.Baked;
            gp.boxProjection = false;
            gp.size = new Vector3(HX * 2f + 0.5f, 19f, 3.2f);
            gp.center = new Vector3(0f, 0.3f, 4f);
            gp.importance = 2;
            gp.intensity = 0.4f;
            gp.resolution = 256;
            gp.blendDistance = 0f;

            var lpg = new GameObject("LightProbes").AddComponent<LightProbeGroup>();
            lpg.transform.SetParent(root, false);
            var pts = new List<Vector3>();
            for (float x = -HX + 1f; x <= HX - 1f; x += 4.5f)
                for (float z = -HZ + 1f; z <= HZ - 1f; z += 4.5f)
                    foreach (float y in new[] { 0.6f, 2.2f, 4.5f, 8f, 13f, 18f })
                        pts.Add(new Vector3(x, y, z));
            foreach (float x in new[] { -20f, -10f, 0f, 10f, 20f })
                foreach (float y in new[] { MezzY + 0.6f, MezzY + 2f })
                    pts.Add(new Vector3(x, y, -15.5f));
            lpg.probePositions = pts.ToArray();
        }

        static void BuildManagers(PlayerController player, GameObject titleCam)
        {
            var game = new GameObject("Game");
            var gm = game.AddComponent<GameManager>();
            SetRef(gm, "titleCamera", titleCam);
            game.AddComponent<FeelManager>();
            var sm = game.AddComponent<SoundManager>();
            SetEnum(sm, "ambientKind", (int)AmbientKind.Station);
            SetRef(sm, "bank", SoundOverrides.Refresh());
            var fx = game.AddComponent<FXManager>();
            SetRef(fx, "particleMaterial", AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Art/Materials/M_Particle.mat"));

            var ui = new GameObject("UI");
            var hud = ui.AddComponent<HUD>();
            SetRef(hud, "player", player);
            SetString(hud, "objectiveOverride", "EXPLORE THE STATION");
            ui.AddComponent<Alcax.Dialogue.DialogueUI>();
            ui.AddComponent<Alcax.AI.CalloutSubtitles>();
            var shell = ui.AddComponent<ShellUI>();
            SetString(shell, "gameTitle", "");
            SetString(shell, "tagline", "ATRIUM C   /   HYDROPONICS DECK   /   WALKTHROUGH BUILD");
        }
    }
}
