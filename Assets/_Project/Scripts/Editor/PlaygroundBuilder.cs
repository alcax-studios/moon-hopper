using System.Collections.Generic;
using System.IO;
using Alcax.AI;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.Core;
using Alcax.FX;
using Alcax.Player;
using Alcax.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using static Alcax.EditorTools.BuildUtil;
using Look = Alcax.EditorTools.CharacterRig.Look;
using Outfit = Alcax.EditorTools.CharacterRig.Outfit;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Builds the combat-AI playground (Playground.unity): an outdoor test map under the desert sky with a
    /// measured grid on every surface. Zones: an open yard with low and high crates, a wall line with doorways
    /// and windows, a two-floor building with interior stairs and a balcony, a catwalk to a container stack,
    /// a sniper tower, a pillar field, a corridor maze, and rolling hills with a mesa. Also builds the combat
    /// data (config, archetypes) and the operative prefabs. Realtime sun, no bake.
    /// Menu: Alcax > Build AI Playground.
    /// </summary>
    public static class PlaygroundBuilder
    {
        public const string ScenePath = Root + "/Scenes/Playground.unity";
        const string MeshPath = Root + "/Art/Meshes/Playground_Meshes.asset";
        const string DataDir = Root + "/Data/AI";
        const string PrefabDir = Root + "/Prefabs/AI";
        const float Half = 52f;

        static readonly Dictionary<string, MeshAccum> render = new();
        static readonly MeshAccum collide = new();
        static readonly List<Mesh> meshes = new();
        static readonly Dictionary<string, Material> mats = new();
        static Transform root;
        static System.Random rng;
        static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        [MenuItem("Alcax/Build AI Playground")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[PlaygroundBuilder] Exit Play mode first.");
                return;
            }
            rng = new System.Random(7);
            render.Clear(); collide.V.Clear(); collide.N.Clear(); collide.U.Clear(); collide.T.Clear(); meshes.Clear(); mats.Clear();
            EnsureLayer(CharacterRig.ActorLayer, "Actor");

            BuildMaterials();
            var config = BuildConfig();
            var roster = BuildArchetypesAndPrefabs(config);
            var factions = BuildFactions();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("Playground").transform;
            BuildLighting();
            BuildGround();
            BuildYard();
            BuildWallLine();
            BuildHouse();
            BuildCatwalkAndContainers();
            BuildTower();
            BuildPillarField();
            BuildMaze();
            BuildHills();
            BuildPerimeter();
            BuildRooms();
            Flush();
            BuildNavMesh();
            var (spawnsGround, spawnsHigh, routes) = BuildMarkers();

            var (player, head) = PlayerRig.BuildPlayer(new Vector3(0f, 0.05f, -48f), 0f, out var cam);
            player.gameObject.AddComponent<PlayerTarget>();
            var titleCam = PlayerRig.BuildCameras(cam, head, new Vector3(0f, 6f, -5f), new Vector3(0f, 14f, -50f), new Vector3(0f, 0f, 0f), 55f);
            cam.farClipPlane = 1500f;
            foreach (var vc in Object.FindObjectsByType<Unity.Cinemachine.CinemachineCamera>()) vc.Lens.FarClipPlane = 1500f;
            BuildManagers(player, titleCam, config, roster, spawnsGround, spawnsHigh, routes, factions);

            SaveMeshes();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("[PlaygroundBuilder] Built " + ScenePath);
        }

        // ---------- Layers ----------

        static void EnsureLayer(int index, string name)
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            var p = layers.GetArrayElementAtIndex(index);
            if (p.stringValue == name) return;
            p.stringValue = name;
            tm.ApplyModifiedProperties();
        }

        // ---------- Materials ----------

        static Texture2D GridTexture()
        {
            string path = $"{Root}/Art/Textures/T_PG_Grid.png";
            EnsureFolder($"{Root}/Art/Textures");
            const int size = 512;   // one texture repeat = 4 m, so 128 px per meter
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            var line = Palette.PgGridLine;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.05f, y * 0.05f) * 0.06f + Mathf.PerlinNoise(x * 0.31f, y * 0.29f) * 0.03f;
                    var c = Color.white * (0.95f - n);
                    bool major = x < 3 || y < 3;
                    bool minor = x % 128 < 1 || y % 128 < 1;
                    bool half = (x % 64 == 0 || y % 64 == 0);
                    if (major) c = Color.Lerp(c, line, 0.85f);
                    else if (minor) c = Color.Lerp(c, line, 0.55f);
                    else if (half) c = Color.Lerp(c, line, 0.18f);
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            tex.SetPixels(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.anisoLevel = 8;
            imp.filterMode = FilterMode.Trilinear;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, Color c, float smooth, Texture2D tex = null, float metallic = 0f)
        {
            var m = Lit("PG_" + name, c, smooth);
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static void BuildMaterials()
        {
            ClearCache();
            var grid = GridTexture();
            Mat("Ground", Palette.PgGround, 0.08f, grid);
            Mat("Hill", Palette.PgHill, 0.06f, grid);
            Mat("Concrete", Palette.PgConcrete, 0.12f, grid);
            Mat("Crate", Palette.PgCrate, 0.2f, grid);
            Mat("CrateLow", Palette.PgCrateLow, 0.25f, grid);
            Mat("Steel", Palette.PgSteel, 0.45f, null, 0.6f);
            Mat("Gun", Palette.OpGunmetal, 0.45f, null, 0.5f);
            Mat("Polymer", Palette.OpPolymer, 0.25f);
            Mat("Dark", Palette.OpBlack, 0.3f);
            Mat("MetalLight", Palette.StMetalLight, 0.5f, null, 0.6f);
        }

        // ---------- Combat data ----------

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static CombatAIConfig BuildConfig()
        {
            EnsureFolder(DataDir);
            // Created once with defaults; after that it is a tuning asset that the builder doesn't overwrite
            return LoadOrCreate<CombatAIConfig>($"{DataDir}/CombatAIConfig.asset");
        }

        static Faction[] BuildFactions()
        {
            var ins = LoadOrCreate<Faction>($"{DataDir}/FAC_Insurgents.asset");
            ins.displayName = "Insurgents";
            ins.jacket = Palette.FacInsurgentA;
            ins.jacketVariety = new[] { Palette.FacInsurgentA, Palette.FacInsurgentB, Palette.FacInsurgentC, Palette.FacInsurgentD, Palette.FacInsurgentE };
            ins.gear = Palette.FacInsurgentGear;
            ins.aggression = 0.1f; ins.caution = -0.1f; ins.discipline = -0.25f; ins.patience = -0.1f; ins.teamwork = -0.2f; ins.skill = -0.15f; ins.nerve = -0.1f;
            ins.reactionScale = 1.15f; ins.jitterScale = 1.8f;
            ins.radio = false; ins.calloutChance = 0.9f; ins.shoutRadius = 42f;
            ins.styles = new StyleWeights { ambusher = 1.2f, stalker = 0.5f, brawler = 1.6f, suppressor = 1.2f, survivor = 1.3f, opportunist = 1.2f, flanker = 0.6f };
            ins.adaptability = 0.3f; ins.coverDiscipline = 0.4f; ins.concealedRoutes = false; ins.patchUpChance = 0.5f; ins.leaderOrders = true;
            ins.volatility = 1.25f; ins.breakResistance = 0f; ins.canSurrender = true;
            EditorUtility.SetDirty(ins);

            var so = LoadOrCreate<Faction>($"{DataDir}/FAC_SpecOps.asset");
            so.displayName = "Special Ops";
            so.jacket = Palette.FacSpecOps;
            so.jacketVariety = new Color[0];
            so.gear = Palette.FacSpecOpsGear;
            so.aggression = 0.05f; so.caution = 0.05f; so.discipline = 0.3f; so.patience = 0.15f; so.teamwork = 0.3f; so.skill = 0.25f; so.nerve = 0.3f;
            so.reactionScale = 0.85f; so.jitterScale = 0.7f;
            so.radio = true; so.calloutChance = 0.75f; so.radioDelay = 0.4f; so.shoutRadius = 30f;
            so.styles = new StyleWeights { ambusher = 1.2f, stalker = 1.5f, brawler = 0.5f, suppressor = 1.3f, survivor = 0.7f, opportunist = 1.3f, flanker = 1.6f };
            so.adaptability = 0.8f; so.coverDiscipline = 0.85f; so.concealedRoutes = true; so.patchUpChance = 0.9f; so.leaderOrders = true;
            so.volatility = 0.6f; so.breakResistance = 0.3f; so.canSurrender = false;
            EditorUtility.SetDirty(so);
            var sec = LoadOrCreate<Faction>($"{DataDir}/FAC_Security.asset");
            sec.displayName = "Station Security";
            sec.jacket = Palette.UIPanel;
            sec.jacketVariety = new Color[0];
            sec.gear = Palette.UIDark;
            sec.aggression = -0.05f; sec.caution = 0.05f; sec.discipline = 0.05f; sec.patience = 0f; sec.teamwork = 0.1f; sec.skill = 0f; sec.nerve = -0.05f;
            sec.reactionScale = 1f; sec.jitterScale = 1f;
            sec.radio = true; sec.calloutChance = 0.85f; sec.radioDelay = 0.5f; sec.shoutRadius = 35f;
            sec.styles = new StyleWeights { ambusher = 1f, stalker = 0.4f, brawler = 0.6f, suppressor = 1f, survivor = 1.4f, opportunist = 0.9f, flanker = 0.9f };
            sec.adaptability = 0.5f; sec.coverDiscipline = 0.55f; sec.concealedRoutes = false; sec.patchUpChance = 0.85f; sec.leaderOrders = true;
            sec.volatility = 1f; sec.breakResistance = 0.1f; sec.canSurrender = true;
            sec.hostileToPlayer = false;
            ins.hostileToPlayer = true; so.hostileToPlayer = true;
            EditorUtility.SetDirty(sec);
            // Who fights whom: insurgents against special ops and station security; special ops and security are on the same side
            ins.hostileTo = new[] { so, sec };
            so.hostileTo = new[] { ins };
            sec.hostileTo = new[] { ins };
            EditorUtility.SetDirty(ins); EditorUtility.SetDirty(so); EditorUtility.SetDirty(sec);
            return new[] { ins, so, sec };
        }

        struct OpDef
        {
            public string name;
            public System.Action<EnemyArchetype> setup;
            public Look look;
            public string gun;
        }

        static List<SpawnCatalog.Entry> BuildArchetypesAndPrefabs(CombatAIConfig config)
        {
            EnsureFolder(PrefabDir);
            var defs = new[]
            {
                new OpDef
                {
                    name = "Rifleman", gun = "Rifle",
                    look = new Look { jacket = Palette.OpOlive, pants = Palette.OpCharcoal, skin = Palette.SkinMid, hair = Palette.HairBrown, accent = Palette.OpPlateTan, hairStyle = 3, height = 1f, build = 1f, outfit = Outfit.PlateCarrier },
                    setup = a =>
                    {
                        a.maxHealth = 100f; a.aggression = 0.5f; a.caution = 0.5f; a.discipline = 0.55f; a.patience = 0.5f; a.teamwork = 0.6f; a.skill = 0.55f; a.nerve = 0.5f;
                        a.reactionTime = new Vector2(0.22f, 0.36f); a.preferredRangeMin = 10f; a.preferredRangeMax = 28f; a.standoff = 7f;
                        a.weaponName = "Rifle"; a.shotSound = Sfx.RifleShot; a.damage = 13f; a.pellets = 1; a.fireInterval = 0.11f; a.magazine = 30; a.reserveRounds = 120; a.coverDiscipline = 0f; a.fitness = 1f; a.reloadTime = 2.3f; a.range = 90f;
                        a.spreadBase = 0.7f; a.spreadPerShot = 0.35f; a.spreadMax = 4f; a.spreadRecovery = 6f; a.recoilPerShot = 0.9f; a.burst = new Vector2Int(3, 6); a.noiseRadius = 65f;
                        a.canRush = false; a.meleeDamage = 25f; a.meleeRange = 1.6f;
                        a.styles = new StyleWeights { ambusher = 1f, stalker = 0.8f, brawler = 0.8f, suppressor = 1.4f, survivor = 1f, opportunist = 1f, flanker = 1.1f };
                    },
                },
                new OpDef
                {
                    name = "Pointman", gun = "Shotgun",
                    look = new Look { jacket = Palette.OpCharcoal, pants = Palette.OpBlack, skin = Palette.SkinDark, hair = Palette.HairBrown, accent = Palette.OpPlateBlack, hairStyle = 3, height = 1.02f, build = 1.06f, outfit = Outfit.PlateCarrier },
                    setup = a =>
                    {
                        a.maxHealth = 110f; a.aggression = 0.85f; a.caution = 0.2f; a.discipline = 0.35f; a.patience = 0.2f; a.teamwork = 0.5f; a.skill = 0.5f; a.nerve = 0.75f;
                        a.reactionTime = new Vector2(0.18f, 0.3f); a.preferredRangeMin = 3f; a.preferredRangeMax = 10f; a.sprintSpeed = 6f; a.standoff = 3f;
                        a.weaponName = "Shotgun"; a.shotSound = Sfx.Shotgun; a.damage = 8f; a.pellets = 8; a.fireInterval = 0.85f; a.magazine = 6; a.reserveRounds = 24; a.coverDiscipline = -0.15f; a.fitness = 1.05f; a.reloadTime = 3f; a.range = 24f;
                        a.spreadBase = 3.2f; a.spreadPerShot = 1f; a.spreadMax = 5f; a.spreadRecovery = 4f; a.recoilPerShot = 4f; a.burst = new Vector2Int(1, 1); a.noiseRadius = 70f;
                        a.canRush = true; a.meleeDamage = 35f; a.meleeRange = 1.8f;
                        a.styles = new StyleWeights { ambusher = 0.6f, stalker = 1.6f, brawler = 2.2f, suppressor = 0.3f, survivor = 0.5f, opportunist = 1.3f, flanker = 1.2f };
                    },
                },
                new OpDef
                {
                    name = "Marksman", gun = "DMR",
                    look = new Look { jacket = Palette.OpTan, pants = Palette.OpOlive, skin = Palette.SkinLight, hair = Palette.HairBlonde, accent = Palette.OpPlateTan, hairStyle = 2, height = 0.99f, build = 0.96f, outfit = Outfit.PlateCarrier },
                    setup = a =>
                    {
                        a.maxHealth = 90f; a.aggression = 0.25f; a.caution = 0.75f; a.discipline = 0.85f; a.patience = 0.85f; a.teamwork = 0.4f; a.skill = 0.8f; a.nerve = 0.55f;
                        a.reactionTime = new Vector2(0.25f, 0.4f); a.preferredRangeMin = 25f; a.preferredRangeMax = 60f; a.standoff = 14f;
                        a.weaponName = "DMR"; a.shotSound = Sfx.RifleShot; a.damage = 38f; a.pellets = 1; a.fireInterval = 0.55f; a.magazine = 10; a.reserveRounds = 30; a.coverDiscipline = 0.1f; a.fitness = 0.95f; a.reloadTime = 2.8f; a.range = 140f;
                        a.spreadBase = 0.15f; a.spreadPerShot = 0.6f; a.spreadMax = 2f; a.spreadRecovery = 3f; a.recoilPerShot = 3f; a.burst = new Vector2Int(1, 1); a.noiseRadius = 80f;
                        a.canRush = false; a.meleeDamage = 20f; a.meleeRange = 1.5f;
                        a.styles = new StyleWeights { ambusher = 2.2f, stalker = 0.4f, brawler = 0.15f, suppressor = 0.8f, survivor = 1.3f, opportunist = 1.2f, flanker = 0.4f };
                    },
                },
                new OpDef
                {
                    name = "Veteran", gun = "Carbine",
                    look = new Look { jacket = Palette.OpBlack, pants = Palette.OpCharcoal, skin = Palette.SkinDeep, hair = Palette.HairGrey, accent = Palette.OpPlateOlive, hairStyle = 3, height = 1.03f, build = 1.02f, outfit = Outfit.PlateCarrier },
                    setup = a =>
                    {
                        a.maxHealth = 120f; a.aggression = 0.6f; a.caution = 0.55f; a.discipline = 0.8f; a.patience = 0.6f; a.teamwork = 0.8f; a.skill = 0.85f; a.nerve = 0.85f;
                        a.reactionTime = new Vector2(0.16f, 0.26f); a.preferredRangeMin = 8f; a.preferredRangeMax = 25f; a.standoff = 6f;
                        a.weaponName = "Carbine"; a.shotSound = Sfx.RifleShot; a.damage = 15f; a.pellets = 1; a.fireInterval = 0.095f; a.magazine = 30; a.reserveRounds = 150; a.coverDiscipline = 0.15f; a.fitness = 1.15f; a.reloadTime = 2f; a.range = 90f;
                        a.spreadBase = 0.5f; a.spreadPerShot = 0.3f; a.spreadMax = 3.5f; a.spreadRecovery = 7f; a.recoilPerShot = 0.8f; a.burst = new Vector2Int(2, 5); a.noiseRadius = 65f;
                        a.canRush = false; a.meleeDamage = 30f; a.meleeRange = 1.7f;
                        a.styles = new StyleWeights { ambusher = 1.1f, stalker = 1.2f, brawler = 0.7f, suppressor = 1f, survivor = 0.7f, opportunist = 1.5f, flanker = 1.5f };
                    },
                },
                new OpDef
                {
                    name = "Guard", gun = "Pistol",
                    look = new Look { jacket = Palette.UIPanel, pants = Palette.UIDark, skin = Palette.SkinMid, hair = Palette.Grip, accent = Palette.StTechCyan, hairStyle = 1, height = 1.02f, build = 1.04f, outfit = Outfit.Uniform },
                    setup = a =>
                    {
                        a.maxHealth = 100f; a.aggression = 0.35f; a.caution = 0.6f; a.discipline = 0.55f; a.patience = 0.5f; a.teamwork = 0.6f; a.skill = 0.45f; a.nerve = 0.45f;
                        a.walkSpeed = 1.45f; a.runSpeed = 4.1f; a.sprintSpeed = 5.3f;
                        a.reactionTime = new Vector2(0.25f, 0.42f); a.preferredRangeMin = 7f; a.preferredRangeMax = 20f; a.standoff = 6f;
                        a.weaponName = "Pistol"; a.shotSound = Sfx.Pistol; a.damage = 16f; a.pellets = 1; a.fireInterval = 0.32f; a.magazine = 12; a.reserveRounds = 36; a.coverDiscipline = 0.05f; a.fitness = 1f; a.reloadTime = 1.8f; a.range = 55f;
                        a.spreadBase = 0.9f; a.spreadPerShot = 0.5f; a.spreadMax = 4f; a.spreadRecovery = 5f; a.recoilPerShot = 1.6f; a.burst = new Vector2Int(1, 3); a.noiseRadius = 55f;
                        a.canRush = false; a.meleeDamage = 20f; a.meleeRange = 1.5f;
                        a.styles = new StyleWeights { ambusher = 0.8f, stalker = 0.3f, brawler = 0.4f, suppressor = 0.8f, survivor = 1.6f, opportunist = 0.8f, flanker = 0.8f };
                    },
                },
                new OpDef
                {
                    name = "Rookie", gun = "Rifle",
                    look = new Look { jacket = Palette.OpSlate, pants = Palette.OpCharcoal, skin = Palette.SkinLight, hair = Palette.HairBrown, accent = Palette.OpPlateOlive, hairStyle = 3, height = 0.97f, build = 0.97f, outfit = Outfit.PlateCarrier },
                    setup = a =>
                    {
                        a.maxHealth = 90f; a.aggression = 0.35f; a.caution = 0.6f; a.discipline = 0.25f; a.patience = 0.35f; a.teamwork = 0.4f; a.skill = 0.25f; a.nerve = 0.25f;
                        a.reactionTime = new Vector2(0.3f, 0.5f); a.preferredRangeMin = 12f; a.preferredRangeMax = 30f; a.standoff = 8f;
                        a.weaponName = "Rifle"; a.shotSound = Sfx.RifleShot; a.damage = 13f; a.pellets = 1; a.fireInterval = 0.11f; a.magazine = 30; a.reserveRounds = 90; a.coverDiscipline = -0.1f; a.fitness = 0.9f; a.reloadTime = 2.8f; a.range = 90f;
                        a.spreadBase = 1f; a.spreadPerShot = 0.45f; a.spreadMax = 5f; a.spreadRecovery = 5f; a.recoilPerShot = 1.2f; a.burst = new Vector2Int(4, 9); a.noiseRadius = 65f;
                        a.canRush = false; a.meleeDamage = 20f; a.meleeRange = 1.5f;
                        a.styles = new StyleWeights { ambusher = 1f, stalker = 0.3f, brawler = 0.8f, suppressor = 1.2f, survivor = 2f, opportunist = 0.7f, flanker = 0.5f };
                    },
                },
            };

            var roster = new List<SpawnCatalog.Entry>();
            foreach (var d in defs)
            {
                var a = LoadOrCreate<EnemyArchetype>($"{DataDir}/ARC_{d.name}.asset");
                a.displayName = d.name;
                d.setup(a);
                EditorUtility.SetDirty(a);
                roster.Add(new SpawnCatalog.Entry { archetype = a, prefab = BuildOperativePrefab(d, a, config) });
            }
            return roster;
        }

        static GameObject BuildOperativePrefab(OpDef d, EnemyArchetype a, CombatAIConfig config)
        {
            var c = CharacterRig.Build(null, "OP_" + d.name, Vector3.zero, 0f, d.look, "op" + d.name,
                (k, col, sm) => Lit("Char_" + k, col, sm), mats["Dark"], mats["MetalLight"], combat: true);
            var bodyT = c.Find("Rig/Body");
            BuildWeapon(bodyT, d.gun);
            SetLayerRecursive(c.gameObject, CharacterRig.ActorLayer);

            var rb = c.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.mass = 80f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var agent = c.gameObject.AddComponent<NavMeshAgent>();
            agent.radius = 0.32f;
            agent.height = 1.8f;
            c.gameObject.AddComponent<Health>();
            c.gameObject.AddComponent<OperativeBody>();
            if (d.name == "Guard")
            {
                var npc = c.gameObject.AddComponent<Alcax.Dialogue.Npc>();
                npc.displayName = "Officer";
                npc.role = "Station Security";
                npc.drivesBody = false;
                npc.dialogue = GuardDialogue();
            }
            var op = c.gameObject.AddComponent<Operative>();
            SetRef(op, "archetype", a);
            SetRef(op, "config", config);

            string path = $"{PrefabDir}/OP_{d.name}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(c.gameObject, path);
            Object.DestroyImmediate(c.gameObject);
            return prefab;
        }

/// <summary>What an off-duty-ish guard has to say.</summary>
        static Alcax.Dialogue.DialogueTree GuardDialogue()
        {
            const string path = Root + "/Data/Dialogue/DLG_Guard.asset";
            if (!AssetDatabase.IsValidFolder(Root + "/Data/Dialogue")) AssetDatabase.CreateFolder(Root + "/Data", "Dialogue");
            AssetDatabase.DeleteAsset(path);
            var t = ScriptableObject.CreateInstance<Alcax.Dialogue.DialogueTree>();
            t.startNode = "start";
            t.Add("start", "Station Security. Everything alright?",
                ("What does security do around here?", "job"), ("Seen any trouble lately?", "trouble"), ("Where can I find weapons?", "armory"), ("Just passing through.", "bye"));
            t.Add("job", "Keep the peace, check the pads, walk the halls. Mostly we stop people doing something stupid outside the range.",
                ("Ask something else.", "start"), ("Thanks.", "bye"));
            t.Add("trouble", "Insurgent cells have been probing the outer locks. If shooting starts, get behind something solid and let us work.",
                ("Ask something else.", "start"), ("I can handle myself.", "handle"));
            t.Add("handle", "Then handle yourself on our side of the line. Shoot one of us and you're the problem.",
                ("Understood.", "start"), ("See you.", "bye"));
            t.Add("armory", "East hall, the door across from the range. The quartermaster signs everything out. Live fire on the range only.",
                ("Ask something else.", "start"), ("Thanks.", "bye"));
            t.Add("bye", "Stay safe out there.");
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        /// <summary>Weapon models from primitives. +Z forward, origin at the butt (or the grip, for a pistol).</summary>
        static void BuildWeapon(Transform body, string kind)
        {
            var w = new GameObject("Weapon").transform;
            w.SetParent(body, false);
            var gun = mats["Gun"]; var poly = mats["Polymer"];
            void P(PrimitiveType t, string n, Vector3 pos, Vector3 scale, Vector3 euler, Material m) => Prim(t, n, w, pos, scale, euler, m);
            Vector3 muzzle, gripR, gripL;
            switch (kind)
            {
                case "Pistol":
                    P(PrimitiveType.Cube, "Grip", new Vector3(0f, -0.02f, 0f), new Vector3(0.03f, 0.1f, 0.04f), new Vector3(15f, 0f, 0f), poly);
                    P(PrimitiveType.Cube, "Slide", new Vector3(0f, 0.045f, 0.06f), new Vector3(0.03f, 0.035f, 0.18f), Vector3.zero, gun);
                    muzzle = new Vector3(0f, 0.045f, 0.16f); gripR = Vector3.zero; gripL = new Vector3(-0.03f, -0.02f, -0.01f);
                    break;
                case "Shotgun":
                    P(PrimitiveType.Cube, "Stock", new Vector3(0f, -0.02f, 0.1f), new Vector3(0.045f, 0.1f, 0.2f), Vector3.zero, poly);
                    P(PrimitiveType.Cube, "Receiver", new Vector3(0f, 0.0f, 0.3f), new Vector3(0.055f, 0.08f, 0.22f), Vector3.zero, gun);
                    P(PrimitiveType.Cube, "Grip", new Vector3(0f, -0.07f, 0.2f), new Vector3(0.035f, 0.09f, 0.04f), new Vector3(15f, 0f, 0f), poly);
                    P(PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0.02f, 0.66f), new Vector3(0.028f, 0.25f, 0.028f), new Vector3(90f, 0f, 0f), gun);
                    P(PrimitiveType.Cylinder, "Tube", new Vector3(0f, -0.02f, 0.6f), new Vector3(0.026f, 0.2f, 0.026f), new Vector3(90f, 0f, 0f), gun);
                    P(PrimitiveType.Cube, "Pump", new Vector3(0f, -0.025f, 0.48f), new Vector3(0.05f, 0.05f, 0.14f), Vector3.zero, poly);
                    muzzle = new Vector3(0f, 0.02f, 0.92f); gripR = new Vector3(0f, -0.07f, 0.2f); gripL = new Vector3(0f, -0.04f, 0.47f);
                    break;
                case "DMR":
                    P(PrimitiveType.Cube, "Stock", new Vector3(0f, -0.01f, 0.1f), new Vector3(0.05f, 0.11f, 0.2f), Vector3.zero, poly);
                    P(PrimitiveType.Cube, "Receiver", new Vector3(0f, 0.0f, 0.32f), new Vector3(0.06f, 0.09f, 0.26f), Vector3.zero, gun);
                    P(PrimitiveType.Cube, "Grip", new Vector3(0f, -0.08f, 0.21f), new Vector3(0.035f, 0.1f, 0.04f), new Vector3(15f, 0f, 0f), poly);
                    P(PrimitiveType.Cube, "Mag", new Vector3(0f, -0.09f, 0.33f), new Vector3(0.035f, 0.11f, 0.07f), new Vector3(8f, 0f, 0f), poly);
                    P(PrimitiveType.Cube, "Handguard", new Vector3(0f, 0.0f, 0.58f), new Vector3(0.055f, 0.065f, 0.26f), Vector3.zero, gun);
                    P(PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0.01f, 0.86f), new Vector3(0.022f, 0.15f, 0.022f), new Vector3(90f, 0f, 0f), gun);
                    P(PrimitiveType.Cylinder, "Scope", new Vector3(0f, 0.085f, 0.33f), new Vector3(0.045f, 0.13f, 0.045f), new Vector3(90f, 0f, 0f), mats["Dark"]);
                    muzzle = new Vector3(0f, 0.01f, 1.02f); gripR = new Vector3(0f, -0.08f, 0.21f); gripL = new Vector3(0f, -0.04f, 0.45f);
                    break;
                default:   // Rifle, Carbine
                    {
                        bool carbine = kind == "Carbine";
                        float hg = carbine ? 0.2f : 0.28f;
                        P(PrimitiveType.Cube, "Stock", new Vector3(0f, -0.01f, 0.1f), new Vector3(0.05f, 0.1f, 0.2f), Vector3.zero, poly);
                        P(PrimitiveType.Cube, "Receiver", new Vector3(0f, 0.0f, 0.32f), new Vector3(0.06f, 0.09f, 0.26f), Vector3.zero, gun);
                        P(PrimitiveType.Cube, "Grip", new Vector3(0f, -0.08f, 0.21f), new Vector3(0.035f, 0.1f, 0.04f), new Vector3(15f, 0f, 0f), poly);
                        P(PrimitiveType.Cube, "Mag", new Vector3(0f, -0.11f, 0.34f), new Vector3(0.035f, 0.16f, 0.07f), new Vector3(12f, 0f, 0f), poly);
                        P(PrimitiveType.Cube, "Handguard", new Vector3(0f, 0.0f, 0.45f + hg * 0.5f), new Vector3(0.06f, 0.07f, hg), Vector3.zero, gun);
                        P(PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0.01f, 0.45f + hg + 0.08f), new Vector3(0.022f, 0.08f, 0.022f), new Vector3(90f, 0f, 0f), gun);
                        P(PrimitiveType.Cube, "Optic", new Vector3(0f, 0.075f, 0.32f), new Vector3(0.035f, 0.05f, 0.11f), Vector3.zero, mats["Dark"]);
                        muzzle = new Vector3(0f, 0.01f, 0.45f + hg + 0.17f); gripR = new Vector3(0f, -0.08f, 0.21f); gripL = new Vector3(0f, -0.045f, 0.45f);
                        break;
                    }
            }
            Node("Muzzle", w, muzzle);
            Node("GripR", w, gripR);
            Node("GripL", w, gripL);
        }

        // ---------- Geometry helpers ----------

        static MeshAccum Acc(string mat)
        {
            if (!render.TryGetValue(mat, out var a)) { a = new MeshAccum(); render[mat] = a; }
            return a;
        }

        static void Box(string mat, Vector3 min, Vector3 max, bool col = true)
        {
            Acc(mat).BoxMinMax(min, max, 4f);
            if (col) collide.BoxMinMax(min, max, 4f);
        }

        static void BoxAt(string mat, Vector3 center, Vector3 size, float yaw, bool col = true)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Acc(mat).Box(center, size, rot, 4f);
            if (col) collide.Box(center, size, rot, 4f);
        }

        static void BoxRot(string mat, Vector3 center, Vector3 size, Quaternion rot, bool col = true)
        {
            Acc(mat).Box(center, size, rot, 4f);
            if (col) collide.Box(center, size, rot, 4f);
        }

        /// <summary>A wall along X at z with openings (center x, width, bottom, top).</summary>
        static void WallX(float z, float x0, float x1, float y0, float y1, float t, params (float c, float w, float b, float top)[] holes)
            => Wall(true, z, x0, x1, y0, y1, t, holes);

        /// <summary>A wall along Z at x with openings (center z, width, bottom, top).</summary>
        static void WallZ(float x, float z0, float z1, float y0, float y1, float t, params (float c, float w, float b, float top)[] holes)
            => Wall(false, x, z0, z1, y0, y1, t, holes);

        static void Wall(bool alongX, float fixedCoord, float a0, float a1, float y0, float y1, float t, (float c, float w, float b, float top)[] holes)
        {
            void Piece(float s0, float s1, float yb, float yt)
            {
                if (s1 - s0 < 0.01f || yt - yb < 0.01f) return;
                if (alongX) Box("Concrete", new Vector3(s0, yb, fixedCoord - t * 0.5f), new Vector3(s1, yt, fixedCoord + t * 0.5f));
                else Box("Concrete", new Vector3(fixedCoord - t * 0.5f, yb, s0), new Vector3(fixedCoord + t * 0.5f, yt, s1));
            }
            var sorted = new List<(float c, float w, float b, float top)>(holes);
            sorted.Sort((p, q) => p.c.CompareTo(q.c));
            float cursor = a0;
            foreach (var h in sorted)
            {
                float h0 = h.c - h.w * 0.5f, h1 = h.c + h.w * 0.5f;
                Piece(cursor, h0, y0, y1);
                Piece(h0, h1, y0, y0 + h.b);       // sill (zero for doors)
                Piece(h0, h1, y0 + h.top, y1);     // lintel
                cursor = h1;
            }
            Piece(cursor, a1, y0, y1);
        }

        static (float, float, float, float) Door(float c, float w = 1.4f) => (c, w, 0f, 2.3f);
        static (float, float, float, float) Window(float c, float w = 1.4f) => (c, w, 1.0f, 2.0f);

        /// <summary>Solid stairs rising along +X or +Z from a start point (each step 0.25 m up).</summary>
        static void Stairs(Vector3 start, bool alongX, float dirSign, float width, float rise, float run = 0.5f)
        {
            int steps = Mathf.CeilToInt(rise / 0.25f);
            float stepH = rise / steps;
            for (int i = 0; i < steps; i++)
            {
                float a = i * run * dirSign, b = (i + 1) * run * dirSign;
                float top = start.y + (i + 1) * stepH;
                Vector3 min, max;
                if (alongX) { min = new Vector3(start.x + Mathf.Min(a, b), start.y - 0.2f, start.z); max = new Vector3(start.x + Mathf.Max(a, b), top, start.z + width); }
                else { min = new Vector3(start.x, start.y - 0.2f, start.z + Mathf.Min(a, b)); max = new Vector3(start.x + width, top, start.z + Mathf.Max(a, b)); }
                Box("Concrete", min, max);
            }
        }

        static void Rail(Vector3 a, Vector3 b)
        {
            var mid = (a + b) * 0.5f;
            var d = b - a;
            float len = d.magnitude;
            var rot = Quaternion.LookRotation(d.normalized, Vector3.up);
            BoxRot("Steel", mid + Vector3.up * 1.0f, new Vector3(0.05f, 0.05f, len), rot);
            BoxRot("Steel", mid + Vector3.up * 0.5f, new Vector3(0.03f, 0.03f, len), rot, col: false);
            int posts = Mathf.Max(2, Mathf.CeilToInt(len / 1.5f) + 1);
            for (int i = 0; i < posts; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)(posts - 1));
                Box("Steel", p + new Vector3(-0.03f, 0f, -0.03f), p + new Vector3(0.03f, 1.0f, 0.03f), col: false);
            }
        }

        // ---------- Zones ----------

        static void BuildGround()
        {
            Box("Ground", new Vector3(-Half, -1f, -Half), new Vector3(Half, 0f, 25f));
        }

        static void BuildYard()
        {
            // Open killzone: scattered low crates (crouch cover) and high crates (full cover), a truck, barriers
            foreach (var (x, z, yaw) in new[] { (-6f, -34f, 10f), (4f, -36f, -15f), (-2f, -28f, 30f), (7f, -27f, 0f), (-9f, -22f, 45f), (2f, -20f, -5f), (10f, -17f, 20f), (-4f, -42f, 0f) })
                BoxAt("CrateLow", new Vector3(x, 0.5f, z), new Vector3(1.4f, 1.0f, 1.4f), yaw);
            foreach (var (x, z, yaw) in new[] { (-4f, -31f, 5f), (8f, -32f, -20f), (-8f, -16f, 12f), (5f, -23f, 35f), (-11f, -38f, 0f) })
            {
                BoxAt("Crate", new Vector3(x, 1.1f, z), new Vector3(1.8f, 2.2f, 1.8f), yaw);
            }
            // Stacked crates: a step up to a vantage point
            BoxAt("Crate", new Vector3(11f, 1.1f, -40f), new Vector3(2.2f, 2.2f, 2.2f), 0f);
            BoxAt("CrateLow", new Vector3(11f, 2.7f, -40f), new Vector3(1.4f, 1.0f, 1.4f), 15f);
            // Concrete barriers (waist high, long)
            foreach (var (x, z, yaw) in new[] { (0f, -45f, 0f), (-8f, -46f, 20f), (9f, -46f, -25f), (-1f, -13f, 80f) })
                BoxAt("Concrete", new Vector3(x, 0.45f, z), new Vector3(3f, 0.9f, 0.55f), yaw);
            // Truck: long full cover you can't see through, with a gap underneath you can't fit through
            var truckRot = 20f;
            var truck = new Vector3(18f, 0f, -15f);
            BoxAt("CrateLow", truck + new Vector3(0f, 1.6f, 0f), new Vector3(2.4f, 2.4f, 5.5f), truckRot);
            var cab = Quaternion.Euler(0f, truckRot, 0f) * new Vector3(0f, 0f, 3.6f);
            BoxAt("Steel", truck + new Vector3(0f, 1.3f, 0f) + cab, new Vector3(2.3f, 2.2f, 1.8f), truckRot);
            BoxAt("Dark", truck + new Vector3(0f, 0.2f, 0.6f), new Vector3(2.2f, 0.4f, 7.4f), truckRot);
        }

        static void BuildWallLine()
        {
            // A wall across the map with doorways and windows: a classic chokepoint
            WallX(-8f, -16f, 16f, 0f, 3.2f, 0.4f, Door(-10f), Window(-5f, 1.6f), Door(0f, 1.6f), Window(5f, 1.6f), Door(9f));
        }

        static void BuildHouse()
        {
            const float x0 = -40f, x1 = -24f, z0 = -6f, z1 = 6f, F = 3.4f, Top = 7.0f, T = 0.3f;
            // Ground floor slab (slightly raised) and the second floor slab with a stair opening
            Box("Concrete", new Vector3(x0, -0.2f, z0), new Vector3(x1, 0.05f, z1));
            Box("Concrete", new Vector3(x0, F, z0), new Vector3(x1, F + 0.2f, 3.7f));
            Box("Concrete", new Vector3(x0, F, 3.7f), new Vector3(-38f, F + 0.2f, z1));
            Box("Concrete", new Vector3(-30f, F, 3.7f), new Vector3(x1, F + 0.2f, z1));
            Box("Concrete", new Vector3(x0, Top, z0), new Vector3(x1, Top + 0.25f, z1));

            float uy = F + 0.2f;
            (float, float, float, float) UWin(float c) => (c, 1.3f, 1.0f, 2.1f);
            (float, float, float, float) UDoor(float c) => (c, 1.2f, 0f, 2.3f);
            // Ground floor (CQB layout): a central east-west corridor (z -1..1) from the east door to a west window,
            // four rooms off it (SW, SE, NW with the stairs, NE), a connecting door SW-SE (a loop inside), and exterior
            // doors on the east (corridor), south (SE room), and north (NE room).
            // South wall
            WallX(z0, x0, x1, 0f, F, T, Door(-28f), Window(-37f));
            WallX(z0, x0, x1, F, Top, T, UWin(-37f), UWin(-32f), UWin(-27f));
            // North wall
            WallX(z1, x0, x1, 0f, F, T, Door(-26f), Window(-35f));
            WallX(z1, x0, x1, F, Top, T, UWin(-34f), UWin(-28f));
            // West wall
            WallZ(x0, z0, z1, 0f, F, T, Window(-3.5f), Window(0f, 1.0f), Window(3f));
            WallZ(x0, z0, z1, F, Top, T, UWin(-2.5f), UWin(2.5f));
            // East wall: ground door, upper door onto the balcony
            WallZ(x1, z0, z1, 0f, F, T, Door(0f), Window(-3.5f), Window(3.5f));
            WallZ(x1, z0, z1, F, Top, T, UDoor(0f), UWin(-4f), UWin(4f));
            // Interior partitions
            WallX(-1f, x0, x1, 0f, F, 0.2f, Door(-36f, 1.4f), Door(-29.5f, 1.4f));   // corridor south side
            WallX(1f, x0, x1, 0f, F, 0.2f, Door(-34.5f, 1.4f), Door(-26.5f, 1.4f));  // corridor north side
            WallZ(-32f, z0, -1f, 0f, F, 0.2f, Door(-3.5f, 1.4f));                    // SW | SE, with a connecting door
            WallZ(-29f, 1f, z1, 0f, F, 0.2f);                                         // NW | NE
            WallX(0f, x0, -33f, uy, Top, 0.2f, (-36.5f, 1.2f, 0f, 2.3f));
            // Interior stairs along the north wall: up toward +X, landing on the upper floor
            Stairs(new Vector3(-38f, 0.05f, 3.9f), true, 1f, 1.6f, uy - 0.05f, 0.55f);
            // Guard rail at the stair opening's south edge, upstairs
            Rail(new Vector3(-38f, uy, 3.75f), new Vector3(-30.3f, uy, 3.75f));
            // Some furniture cover inside
            BoxAt("CrateLow", new Vector3(-37f, 0.55f, -4.2f), new Vector3(2f, 1.0f, 0.9f), 0f);     // SW
            BoxAt("CrateLow", new Vector3(-25.6f, 0.55f, -3.2f), new Vector3(0.9f, 1.0f, 1.8f), 0f); // SE
            BoxAt("CrateLow", new Vector3(-26.5f, 0.55f, 4.4f), new Vector3(1.6f, 1.0f, 0.9f), 0f);  // NE
            BoxAt("CrateLow", new Vector3(-28f, uy + 0.5f, -3f), new Vector3(1.8f, 1.0f, 0.9f), 0f);
            // Balcony on the east side of the upper floor
            Box("Concrete", new Vector3(x1, F, -4f), new Vector3(-20f, F + 0.2f, 4f));
            Box("Concrete", new Vector3(-20.15f, uy, -4f), new Vector3(-20f, uy + 1.0f, -0.9f));
            Box("Concrete", new Vector3(-20.15f, uy, 0.9f), new Vector3(-20f, uy + 1.0f, 4f));
            Box("Concrete", new Vector3(x1, uy, -4.15f), new Vector3(-20f, uy + 1.0f, -4f));
            Box("Concrete", new Vector3(x1, uy, 4f), new Vector3(-20f, uy + 1.0f, 4.15f));
        }

/// <summary>The house's rooms and doorways for the AI's RoomGraph (layer 2 of space awareness).</summary>
        static void BuildRooms()
        {
            const float F = 3.4f, uy = 3.6f;
            var parent = new GameObject("Rooms").transform;
            parent.SetParent(root, false);
            Alcax.AI.CombatRoom Room(string n, float xa, float xb, float za, float zb, float ya, float yb)
            {
                var go = new GameObject("Room_" + n);
                go.transform.SetParent(parent, false);
                go.transform.position = new Vector3((xa + xb) * 0.5f, (ya + yb) * 0.5f, (za + zb) * 0.5f);
                var r = go.AddComponent<Alcax.AI.CombatRoom>();
                r.roomName = n;
                r.size = new Vector3(Mathf.Abs(xb - xa), Mathf.Abs(yb - ya), Mathf.Abs(zb - za));
                return r;
            }
            void AddDoor(string n, Alcax.AI.CombatRoom a, Alcax.AI.CombatRoom b, Vector3 p, float w, bool stairs = false)
            {
                var go = new GameObject("Door_" + n);
                go.transform.SetParent(parent, false);
                go.transform.position = p;
                var d = go.AddComponent<Alcax.AI.CombatDoor>();
                d.a = a; d.b = b; d.width = w; d.stairs = stairs;
            }
            var hall = Room("Corridor", -40f, -24f, -1f, 1f, 0f, F);
            var sw = Room("SW room", -40f, -32f, -6f, -1f, 0f, F);
            var se = Room("SE room", -32f, -24f, -6f, -1f, 0f, F);
            var nw = Room("NW room (stairs)", -40f, -29f, 1f, 6f, 0f, F);
            var ne = Room("NE room", -29f, -24f, 1f, 6f, 0f, F);
            var up = Room("Upper floor", -40f, -24f, -6f, 6f, uy, uy + 3.2f);
            AddDoor("East", hall, null, new Vector3(-24f, 0.05f, 0f), 1.4f);
            AddDoor("Hall-SW", hall, sw, new Vector3(-36f, 0.05f, -1f), 1.4f);
            AddDoor("Hall-SE", hall, se, new Vector3(-29.5f, 0.05f, -1f), 1.4f);
            AddDoor("Hall-NW", hall, nw, new Vector3(-34.5f, 0.05f, 1f), 1.4f);
            AddDoor("Hall-NE", hall, ne, new Vector3(-26.5f, 0.05f, 1f), 1.4f);
            AddDoor("SW-SE", sw, se, new Vector3(-32f, 0.05f, -3.5f), 1.4f);
            AddDoor("South", se, null, new Vector3(-28f, 0.05f, -6f), 1.4f);
            AddDoor("North", ne, null, new Vector3(-26f, 0.05f, 6f), 1.4f);
            AddDoor("Stairs", nw, up, new Vector3(-30.8f, uy, 4.7f), 1.6f, true);
            AddDoor("Balcony", up, null, new Vector3(-24f, uy, 0f), 1.2f);
        }

        static void BuildCatwalkAndContainers()
        {
            float y = 3.4f;
            // Catwalk from the balcony to the top of a container stack
            Box("Steel", new Vector3(-20f, y, -0.9f), new Vector3(0f, y + 0.2f, 0.9f));
            Rail(new Vector3(-20f, y + 0.2f, -0.9f), new Vector3(0f, y + 0.2f, -0.9f));
            Rail(new Vector3(-20f, y + 0.2f, 0.9f), new Vector3(0f, y + 0.2f, 0.9f));
            // Supports
            foreach (float x in new[] { -15f, -10f, -5f })
                Box("Steel", new Vector3(x - 0.12f, 0f, -0.12f), new Vector3(x + 0.12f, y, 0.12f));
            // Container stack: two containers, the top one is the platform
            Box("Crate", new Vector3(0f, 0f, -3f), new Vector3(6f, 1.7f, 3f));
            Box("CrateLow", new Vector3(0f, 1.7f, -3f), new Vector3(6f, y + 0.2f, 3f));
            // Stairs down from the platform to the east
            Stairs(new Vector3(6f + 7.2f, 0f, 1.4f), true, -1f, 1.5f, y + 0.2f, 0.5f);
            // A lone container nearby for cover
            BoxAt("Crate", new Vector3(12f, 1.3f, 9f), new Vector3(2.5f, 2.6f, 6f), 70f);
        }

        static void BuildTower()
        {
            const float y = 7f, x0 = 29.5f, x1 = 34.5f, z0 = 15.5f, z1 = 20.5f;
            foreach (var (x, z) in new[] { (x0, z0), (x1 - 2.3f, z0), (x0, z1 - 0.6f), (x1 - 0.6f, z1 - 0.6f) })
                Box("Steel", new Vector3(x, 0f, z), new Vector3(x + 0.6f, y, z + 0.6f));
            Box("Concrete", new Vector3(x0, y - 0.25f, z0), new Vector3(x1, y, z1));
            // Parapet (crouch behind it), open where the stairs arrive
            Box("Concrete", new Vector3(x0, y, z1 - 0.2f), new Vector3(x1, y + 1.05f, z1));
            Box("Concrete", new Vector3(x0, y, z0), new Vector3(x0 + 0.2f, y + 1.05f, z1));
            Box("Concrete", new Vector3(x1 - 0.2f, y, z0 + 1.6f), new Vector3(x1, y + 1.05f, z1));
            Box("Concrete", new Vector3(x0, y, z0), new Vector3(x1 - 1.6f, y + 1.05f, z0 + 0.2f));
            // Long stair up the east side, arriving at the south-east corner gap
            Stairs(new Vector3(x1 - 1.5f, 0f, z0 + 1.5f - 14f), false, 1f, 1.5f, y, 0.5f);
        }

        static void BuildPillarField()
        {
            for (float x = 15f; x <= 34f; x += 4f)
                for (float z = -40f; z <= -22f; z += 4f)
                {
                    float jx = R(-0.8f, 0.8f), jz = R(-0.8f, 0.8f);
                    bool broken = rng.NextDouble() < 0.2;
                    float h = broken ? R(1.0f, 1.3f) : 3.5f;
                    float s = broken ? 1.1f : R(0.8f, 1.2f);
                    BoxAt("Concrete", new Vector3(x + jx, h * 0.5f, z + jz), new Vector3(s, h, s), R(-10f, 10f));
                }
        }

        static void BuildMaze()
        {
            const float H = 3f, T = 0.3f;
            void Seg(float ax, float az, float bx, float bz)
            {
                if (Mathf.Abs(az - bz) < 0.01f) Box("Concrete", new Vector3(Mathf.Min(ax, bx), 0f, az - T * 0.5f), new Vector3(Mathf.Max(ax, bx), H, az + T * 0.5f));
                else Box("Concrete", new Vector3(ax - T * 0.5f, 0f, Mathf.Min(az, bz)), new Vector3(ax + T * 0.5f, H, Mathf.Max(az, bz)));
            }
            // Outer walls with entries on each side
            Seg(-50f, -42f, -41f, -42f); Seg(-38.5f, -42f, -28f, -42f);
            Seg(-50f, -18f, -36f, -18f); Seg(-33.5f, -18f, -28f, -18f);
            Seg(-50f, -42f, -50f, -32f); Seg(-50f, -29.5f, -50f, -18f);
            Seg(-28f, -42f, -28f, -33f); Seg(-28f, -30.5f, -28f, -18f);
            // Inner corridors
            Seg(-46f, -38f, -46f, -22f);
            Seg(-46f, -38f, -42.5f, -38f); Seg(-40f, -38f, -32f, -38f);
            Seg(-42f, -34f, -36f, -34f); Seg(-33.5f, -34f, -31f, -34f);
            Seg(-42f, -34f, -42f, -26f);
            Seg(-38f, -30f, -38f, -24f);
            Seg(-38f, -26f, -32f, -26f);
            Seg(-34f, -30f, -31f, -30f);
            Seg(-42f, -22f, -32f, -22f);
        }

        static float HillHeight(float x, float z)
        {
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(25f, 34f, z));
            float h = Mathf.PerlinNoise(x * 0.045f + 3.1f, z * 0.045f + 7.7f) * 4.2f
                      + Mathf.PerlinNoise(x * 0.11f + 1.3f, z * 0.11f + 2.9f) * 1.2f - 1.6f;
            // Flatten under the mesa and its ramps so they sit on the ground
            float mesa = Mathf.Max(Mathf.Abs(x + 7f) - 12f, Mathf.Abs(z - 45f) - 9f);
            h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 5f, mesa));
            return Mathf.Max(-0.3f, h) * blend;
        }

        static void BuildHills()
        {
            var mesh = AtriumMeshes.Terrain(new Vector2(-Half, 25f), new Vector2(Half, Half), 104, HillHeight, 4f);
            mesh.name = "PG_Hills";
            meshes.Add(mesh);
            var go = new GameObject("Hills", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = mats["Hill"];
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;

            // Mesa (raised plateau) with two ramps
            Box("Concrete", new Vector3(-14f, -1f, 41f), new Vector3(0f, 4f, 50f));
            var rampLen = 9f; float angle = Mathf.Atan2(4f, rampLen) * Mathf.Rad2Deg;
            BoxRot("Concrete", new Vector3(-10f, 1.9f, 41f - rampLen * 0.5f), new Vector3(2.6f, 0.4f, rampLen / Mathf.Cos(angle * Mathf.Deg2Rad)), Quaternion.Euler(-angle, 0f, 0f));
            BoxRot("Concrete", new Vector3(rampLen * 0.5f, 1.9f, 47f), new Vector3(2.6f, 0.4f, rampLen / Mathf.Cos(angle * Mathf.Deg2Rad)), Quaternion.Euler(-angle, -90f, 0f));
            // Low parapet on the mesa's south edge (prone-ish cover up high)
            Box("Concrete", new Vector3(-6f, 4f, 41f), new Vector3(-1f, 4.9f, 41.4f));

            // Boulders and broken walls across the hills
            for (int i = 0; i < 26; i++)
            {
                float x = R(-46f, 46f), z = R(29f, 49f);
                if (x > -16f && x < 10f && z > 32f && z < 52f) continue;
                float y = HillHeight(x, z);
                if (rng.NextDouble() < 0.55)
                {
                    var size = new Vector3(R(1.2f, 3f), R(1.0f, 2.4f), R(1.2f, 3f));
                    BoxRot("Hill", new Vector3(x, y + size.y * 0.35f, z), size, Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f)));
                }
                else
                {
                    float len = R(3f, 6f);
                    BoxAt("Concrete", new Vector3(x, y + 0.45f, z), new Vector3(len, 1.6f, 0.4f), R(0f, 180f));
                }
            }
        }

        static void BuildPerimeter()
        {
            float h = 8f;
            Box("Concrete", new Vector3(-Half - 1f, -1f, -Half - 1f), new Vector3(Half + 1f, h, -Half));
            Box("Concrete", new Vector3(-Half - 1f, -1f, Half), new Vector3(Half + 1f, h, Half + 1f));
            Box("Concrete", new Vector3(-Half - 1f, -1f, -Half), new Vector3(-Half, h, Half));
            Box("Concrete", new Vector3(Half, -1f, -Half), new Vector3(Half + 1f, h, Half));
        }

        // ---------- Mesh output ----------

        static void Flush()
        {
            var structure = new GameObject("Structure").transform;
            structure.SetParent(root, false);
            foreach (var kv in render)
            {
                if (kv.Value.Count == 0) continue;
                var mesh = kv.Value.Build("PG_" + kv.Key, lightmapUVs: false);
                meshes.Add(mesh);
                var go = new GameObject("M_" + kv.Key, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(structure, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial = mats[kv.Key];
                go.isStatic = true;
            }
            var colMesh = collide.Build("PG_Collision", lightmapUVs: false);
            meshes.Add(colMesh);
            var col = new GameObject("Collision", typeof(MeshCollider));
            col.transform.SetParent(structure, false);
            col.GetComponent<MeshCollider>().sharedMesh = colMesh;
            col.isStatic = true;
        }

        static void SaveMeshes()
        {
            EnsureFolder($"{Root}/Art/Meshes");
            AssetDatabase.DeleteAsset(MeshPath);
            var container = new Mesh { name = "Playground_Meshes" };
            AssetDatabase.CreateAsset(container, MeshPath);
            foreach (var m in meshes) AssetDatabase.AddObjectToAsset(m, container);
            AssetDatabase.SaveAssets();
        }

        static void BuildNavMesh()
        {
            var navGo = new GameObject("NavMesh");
            navGo.transform.SetParent(root, false);
            var surf = navGo.AddComponent<Unity.AI.Navigation.NavMeshSurface>();
            surf.collectObjects = Unity.AI.Navigation.CollectObjects.All;
            surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surf.layerMask = ~((1 << 2) | (1 << CharacterRig.ActorLayer));
            surf.BuildNavMesh();
            string path = $"{Root}/Scenes/Playground_NavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surf.navMeshData, path);
            EditorUtility.SetDirty(surf);
        }

        static Transform Marker(Transform parent, string name, Vector3 pos)
        {
            // Drop onto whatever is below
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out var hit, 12f, ~((1 << 2) | (1 << CharacterRig.ActorLayer)))) pos = hit.point;
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.position = pos;
            return t;
        }

        static (Transform[], Transform[], PatrolRoute[]) BuildMarkers()
        {
            Physics.SyncTransforms();
            var sp = new GameObject("Spawns").transform;
            sp.SetParent(root, false);
            var ground = new List<Transform>();
            foreach (var (n, p) in new[] { ("North", new Vector3(8f, 6f, 47f)), ("NorthEast", new Vector3(42f, 6f, 44f)), ("NorthWest", new Vector3(-40f, 6f, 45f)),
                                            ("East", new Vector3(46f, 0f, 2f)), ("SouthEast", new Vector3(44f, 0f, -30f)), ("West", new Vector3(-47f, 0f, 4f)) })
                ground.Add(Marker(sp, "Ground_" + n, p));
            var high = new List<Transform>();
            foreach (var (n, p) in new[] { ("Tower", new Vector3(31.5f, 7.5f, 18.5f)), ("HouseUpper", new Vector3(-28f, 3.7f, -3f)), ("Containers", new Vector3(3f, 3.7f, 0f)), ("Mesa", new Vector3(-7f, 4.2f, 47f)) })
                high.Add(Marker(sp, "High_" + n, p));

            var rp = new GameObject("PatrolRoutes").transform;
            rp.SetParent(root, false);
            var routes = new List<PatrolRoute>();
            PatrolRoute Route(string name, params Vector3[] pts)
            {
                var r = new GameObject(name).AddComponent<PatrolRoute>();
                r.transform.SetParent(rp, false);
                for (int i = 0; i < pts.Length; i++) Marker(r.transform, "P" + i, pts[i]);
                routes.Add(r);
                return r;
            }
            Route("Yard", new Vector3(-8f, 0f, -30f), new Vector3(8f, 0f, -30f), new Vector3(8f, 0f, -14f), new Vector3(-8f, 0f, -14f));
            Route("House", new Vector3(-21f, 0f, -9f), new Vector3(-43f, 0f, -9f), new Vector3(-43f, 0f, 9f), new Vector3(-21f, 0f, 9f));
            Route("Pillars", new Vector3(16f, 0f, -42f), new Vector3(34f, 0f, -42f), new Vector3(36f, 0f, -20f), new Vector3(14f, 0f, -20f));
            Route("Hills", new Vector3(-22f, 5f, 32f), new Vector3(20f, 5f, 33f), new Vector3(18f, 5f, 46f), new Vector3(-26f, 5f, 46f));
            return (ground.ToArray(), high.ToArray(), routes.ToArray());
        }

        // ---------- Lighting and managers ----------

        static void BuildLighting()
        {
            var toSun = new Vector3(-0.45f, 0.62f, 0.64f).normalized;
            var sunGo = new GameObject("Sun");
            sunGo.transform.rotation = Quaternion.LookRotation(-toSun);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Palette.StSun;
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.lightmapBakeType = LightmapBakeType.Realtime;

            // A copy of the atrium sky with this map's sun
            string skyPath = $"{Root}/Art/Materials/M_PG_Sky.mat";
            var atriumSky = AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Art/Materials/Atrium/M_Sky.mat");
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null)
            {
                sky = atriumSky != null ? new Material(atriumSky) : new Material(Shader.Find("Alcax/AlienSky"));
                AssetDatabase.CreateAsset(sky, skyPath);
            }
            else if (atriumSky != null) sky.CopyPropertiesFromMaterial(atriumSky);
            sky.SetVector("_SunDir", toSun);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.StSkyZenith * 1.1f;
            RenderSettings.ambientEquatorColor = Palette.StSkyHaze * 0.75f;
            RenderSettings.ambientGroundColor = Palette.StSandShadow * 0.6f;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.5f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0035f;
            RenderSettings.fogColor = Palette.StSkyHaze * 0.9f;

            string vp = $"{Root}/Settings/VP_Playground.asset";
            AssetDatabase.DeleteAsset(vp);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, vp);
            profile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = 0.35f; bloom.threshold.value = 1.1f;
            var vig = profile.Add<Vignette>(true);
            vig.intensity.value = 0.15f; vig.color.value = Palette.UIDark;
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.value = 0.2f; ca.contrast.value = 8f; ca.saturation.value = -2f;
            ca.colorFilter.overrideState = false; ca.hueShift.overrideState = false;
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            var vol = new GameObject("Global Volume").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            string lp = $"{Root}/Settings/LS_Playground.lighting";
            var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(lp);
            if (ls == null) { ls = new LightingSettings { name = "LS_Playground" }; AssetDatabase.CreateAsset(ls, lp); }
            ls.bakedGI = false;
            ls.realtimeGI = false;
            EditorUtility.SetDirty(ls);
            Lightmapping.lightingSettings = ls;
        }

/// <summary>The admin tools' spawn catalog (Resources/SpawnCatalog): roster, factions, config, and the spawn menu presets.</summary>
        static void BuildSpawnCatalog(CombatAIConfig config, List<SpawnCatalog.Entry> roster, Faction[] factions)
        {
            const string dirPath = Root + "/Resources";
            if (!AssetDatabase.IsValidFolder(dirPath)) AssetDatabase.CreateFolder(Root, "Resources");
            var cat = LoadOrCreate<SpawnCatalog>(dirPath + "/SpawnCatalog.asset");
            cat.config = config;
            // Reload by path (the new scene unloaded the earlier handles)
            var fresh = new List<Faction>();
            foreach (var f in factions) if (f != null) fresh.Add(AssetDatabase.LoadAssetAtPath<Faction>(AssetDatabase.GetAssetPath(f)) ?? f);
            cat.factions = fresh.ToArray();
            var entries = new List<SpawnCatalog.Entry>();
            foreach (var e in roster) entries.Add(new SpawnCatalog.Entry { archetype = AssetDatabase.LoadAssetAtPath<EnemyArchetype>(AssetDatabase.GetAssetPath(e.archetype)) ?? e.archetype, prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(e.prefab)) ?? e.prefab });
            cat.roster = entries.ToArray();
            cat.presets = new[]
            {
                new SpawnCatalog.Preset { label = "Patrol (unaware, 2)", patrol = true },
                new SpawnCatalog.Preset { label = "Rifle squad (4)", names = new[] { "Rifleman", "Rifleman", "Rifleman", "Rookie" } },
                new SpawnCatalog.Preset { label = "Pointmen, shotgun rushers (2)", names = new[] { "Pointman", "Pointman" } },
                new SpawnCatalog.Preset { label = "Marksman + Veteran (high ground)", names = new[] { "Marksman", "Veteran" }, high = true },
                new SpawnCatalog.Preset { label = "Mixed squad (5)", names = new[] { "Veteran", "Rifleman", "Pointman", "Rookie", "Marksman" } },
                new SpawnCatalog.Preset { label = "Big squad (10)", names = new[] { "Veteran", "Rifleman", "Rifleman", "Rifleman", "Pointman", "Pointman", "Rookie", "Rookie", "Rifleman", "Marksman" } },
                new SpawnCatalog.Preset { label = "Security guards (2)", names = new[] { "Guard", "Guard" }, faction = "Station Security" },
                new SpawnCatalog.Preset { label = "Insurgent assault squad (4)", names = new[] { "Rifleman", "Rifleman", "Pointman", "Rookie" }, faction = "Insurgents" },
                new SpawnCatalog.Preset { label = "Skirmish: insurgents vs special ops (4 v 4)", names = new[] { "Rifleman", "Rifleman", "Pointman", "Rookie" }, faction = "Insurgents", versusFaction = "Special Ops", versusNames = new[] { "Veteran", "Rifleman", "Rifleman", "Marksman" } },
            };
            EditorUtility.SetDirty(cat);
            AssetDatabase.SaveAssets();
        }

        static void BuildManagers(PlayerController player, GameObject titleCam, CombatAIConfig config, List<SpawnCatalog.Entry> roster,
            Transform[] ground, Transform[] high, PatrolRoute[] routes, Faction[] factions)
        {
            var game = new GameObject("Game");
            var gm = game.AddComponent<GameManager>();
            SetRef(gm, "titleCamera", titleCam);
            game.AddComponent<FeelManager>();
            var sm = game.AddComponent<SoundManager>();
            SetEnum(sm, "ambientKind", (int)AmbientKind.Station);
            SetFloat(sm, "ambientVolume", 0.12f);
            var smSo = new SerializedObject(sm);
            smSo.FindProperty("poolSize").intValue = 48;
            smSo.ApplyModifiedPropertiesWithoutUndo();
            SetRef(sm, "bank", SoundOverrides.Refresh());
            var fx = game.AddComponent<FXManager>();
            SetRef(fx, "particleMaterial", AssetDatabase.LoadAssetAtPath<Material>($"{Root}/Art/Materials/M_Particle.mat"));

            var ai = new GameObject("AI");
            var dir = ai.AddComponent<PlaygroundDirector>();
            // Reload by path: opening the new scene unloads assets nothing references yet, which nulls the earlier handle
            config = AssetDatabase.LoadAssetAtPath<CombatAIConfig>($"{DataDir}/CombatAIConfig.asset");
            BuildSpawnCatalog(config, roster, factions);
            SetRefArray(dir, "groundSpawns", ground);
            SetRefArray(dir, "highSpawns", high);
            SetRefArray(dir, "routes", routes);
            WeaponData LoadW(string n) => AssetDatabase.LoadAssetAtPath<WeaponData>($"{Root}/Data/Weapons/{n}.asset");
            SetRefArray(dir, "playerWeapons", new Object[] { LoadW("W_Pistol"), LoadW("W_Shotgun"), LoadW("W_Rifle") });

            var ui = new GameObject("UI");
            var hud = ui.AddComponent<HUD>();
            SetRef(hud, "player", player);
            SetString(hud, "objectiveOverride", "AI PLAYGROUND   /   F1 HELP");
            ui.AddComponent<Alcax.Dialogue.DialogueUI>();
            ui.AddComponent<CalloutSubtitles>();
            var shell = ui.AddComponent<ShellUI>();
            SetString(shell, "gameTitle", "");
            SetString(shell, "tagline", "");
        }
    }
}
