using System.Collections.Generic;
using Alcax.Core;
using Alcax.Player;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Alcax.AI
{
    /// <summary>
    /// The admin tools, on every map (a core dev feature, not tied to the AI Playground). Created once at startup and
    /// kept across map loads.
    ///   F1 help   F2 spawn menu (number keys; Shift = they know where you are; Tab = faction; stays open)
    ///   F3 AI overlay   F4 or double-tap Space: no-clip   F9 clear AI   F10 freeze AI   F11 god mode   [ ] dev text size
    /// Spawns land on random reachable points anywhere on the walkable map, away from the player.
    /// </summary>
    public class AdminTools : MonoBehaviour
    {
        public static AdminTools Instance { get; private set; }

        SpawnCatalog catalog;
        bool help, menu;
        int factionMode = -1;   // -1: mixed (each squad rolls); otherwise a faction index
        int squadCounter;
        string toast;
        float toastUntil, lastSpaceTap = -1f;
        GUIStyle style;
        static readonly string[] Callsigns = { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel" };

        public SpawnCatalog Catalog => catalog;
        public CombatAIConfig Config => catalog != null ? catalog.config : null;
        public Faction[] Factions => catalog != null ? catalog.factions : null;
        string FactionLabel => Factions == null || Factions.Length == 0 ? "none" : factionMode >= 0 && factionMode < Factions.Length ? Factions[factionMode].displayName : "Mixed";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("AdminTools");
            DontDestroyOnLoad(go);
            go.AddComponent<AdminTools>();
        }

        void Awake()
        {
            Instance = this;
            catalog = SpawnCatalog.Load();
            if (GetComponent<AIDebugOverlay>() == null) gameObject.AddComponent<AIDebugOverlay>();
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            Operative.Frozen = false;
            menu = false;
            AdminSettings.DevMenuOpen = false;
            // The test map opens with the help showing; elsewhere it stays out of the way until F1
            help = FindAnyObjectByType<PlaygroundDirector>() != null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !GameManager.IsPlaying) { AdminSettings.DevMenuOpen = false; return; }
            EnsurePlayerTarget();
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (kb.f1Key.wasPressedThisFrame) help = !help;
            if (kb.f2Key.wasPressedThisFrame) menu = !menu;
            AdminSettings.DevMenuOpen = menu;
            if (menu && catalog != null && catalog.presets != null)
            {
                if (kb.tabKey.wasPressedThisFrame && Factions != null && Factions.Length > 0)
                    factionMode = factionMode + 1 >= Factions.Length ? -1 : factionMode + 1;
                var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
                for (int d = 0; d < digits.Length && d < catalog.presets.Length; d++)
                {
                    if (!digits[d].wasPressedThisFrame) continue;
                    var sp = catalog.presets[d];
                    if (sp.patrol) SpawnPatrol();
                    else if (!string.IsNullOrEmpty(sp.versusFaction)) SpawnSkirmish(sp);
                    else Spawn(sp.names, sp.high, shift, catalog.FactionNamed(sp.faction));
                    break;
                }
            }
            bool doubleSpace = false;
            if (kb.spaceKey.wasPressedThisFrame)
            {
                doubleSpace = Time.unscaledTime - lastSpaceTap < 0.3f;
                lastSpaceTap = doubleSpace ? -1f : Time.unscaledTime;
            }
            if (kb.f4Key.wasPressedThisFrame || doubleSpace)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) { pc.SetNoClip(!pc.NoClip); Toast(pc.NoClip ? "NO-CLIP ON  (Space up, C down, Shift fast)" : "NO-CLIP OFF"); }
            }
            if (kb.leftBracketKey.wasPressedThisFrame) { DevGUI.UserScale = Mathf.Max(0.6f, DevGUI.UserScale - 0.1f); Toast($"DEV TEXT {DevGUI.UserScale:0.0}x"); }
            if (kb.rightBracketKey.wasPressedThisFrame) { DevGUI.UserScale = Mathf.Min(2.5f, DevGUI.UserScale + 0.1f); Toast($"DEV TEXT {DevGUI.UserScale:0.0}x"); }
            if (kb.f9Key.wasPressedThisFrame) ClearAll();
            if (kb.f10Key.wasPressedThisFrame) { Operative.Frozen = !Operative.Frozen; Toast(Operative.Frozen ? "AI FROZEN" : "AI RESUMED"); }
            if (kb.f11Key.wasPressedThisFrame) { AdminSettings.GodMode = !AdminSettings.GodMode; Toast(AdminSettings.GodMode ? "GOD MODE ON" : "GOD MODE OFF"); }
        }

        static void EnsurePlayerTarget()
        {
            if (PlayerTarget.Instance != null) return;
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null && pc.GetComponent<PlayerTarget>() == null) pc.gameObject.AddComponent<PlayerTarget>();
        }

        // ---------- Spawning (also used by AIExperiment) ----------

        public Squad NewSquad()
        {
            var go = new GameObject("Squad " + Callsigns[squadCounter % Callsigns.Length]);
            var sq = go.AddComponent<Squad>();
            sq.callsign = Callsigns[squadCounter % Callsigns.Length];
            squadCounter++;
            return sq;
        }

        public SpawnCatalog.Entry Find(string name) => catalog != null ? catalog.Find(name) : null;

        public Operative SpawnOne(SpawnCatalog.Entry e, Vector3 pos, Squad squad, PatrolRoute route, Faction faction)
        {
            if (e == null || e.prefab == null || !NavMesh.SamplePosition(pos, out var hit, 4f, NavMesh.AllAreas)) return null;
            var player = PlayerTarget.Instance;
            var face = player != null ? Quaternion.LookRotation((player.Position - hit.position).Flat().normalized + Vector3.forward * 0.001f) : Quaternion.identity;
            var go = Instantiate(e.prefab, hit.position, face, squad.transform);
            go.name = e.archetype.displayName;
            var op = go.GetComponent<Operative>();
            op.Init(e.archetype, Config, faction, squad, route);
            return op;
        }

        Faction PickFaction()
        {
            if (Factions == null || Factions.Length == 0) return null;
            return factionMode >= 0 && factionMode < Factions.Length ? Factions[factionMode] : Factions[Random.Range(0, Factions.Length)];
        }

/// <summary>
        /// A random reachable point anywhere on the walkable map (from the NavGraph, which covers the whole NavMesh),
        /// at least 18 m from the player. "High" prefers points above the player.
        /// </summary>
        Vector3 PickSpawn(bool high)
        {
            var player = PlayerTarget.Instance;
            var origin = player != null ? player.Position : transform.position;
            NavGraph.EnsureBuilt();
            var path = new NavMeshPath();
            bool hasStart = NavMesh.SamplePosition(origin, out var o, 3f, NavMesh.AllAreas);
            Vector3 fallback = origin + Vector3.forward * 10f;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < 60 && NavGraph.Count > 0; i++)
                {
                    var c = NavGraph.Pos[Random.Range(0, NavGraph.Count)];
                    if (Vector3.Distance(c, origin) < 18f) continue;
                    if (high && pass == 0 && c.y < origin.y + 2f) continue;
                    if (hasStart && (!NavMesh.CalculatePath(o.position, c, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)) continue;
                    return c;
                }
            return fallback;
        }

        /// <summary>A throwaway patrol route: a few reachable random points around a spot.</summary>
        PatrolRoute MakeRoute(Vector3 around)
        {
            var go = new GameObject("Patrol (spawned)");
            var route = go.AddComponent<PatrolRoute>();
            var path = new NavMeshPath();
            int made = 0;
            for (int i = 0; i < 30 && made < 4; i++)
            {
                var d = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(8f, 22f);
                if (!NavMesh.SamplePosition(around + d, out var h, 3f, NavMesh.AllAreas)) continue;
                if (!NavMesh.CalculatePath(around, h.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                var t = new GameObject("P" + made).transform;
                t.SetParent(go.transform, false);
                t.position = h.position;
                made++;
            }
            if (made == 0) { var t = new GameObject("P0").transform; t.SetParent(go.transform, false); t.position = around; }
            return route;
        }

        List<Operative> Spawn(string[] names, bool high, bool knowing, Faction forced = null, Vector3? origin = null)
        {
            var made = new List<Operative>();
            if (names == null || names.Length == 0) return made;
            var squad = NewSquad();
            var faction = forced != null ? forced : PickFaction();
            var player = PlayerTarget.Instance;
            var spawn = origin ?? PickSpawn(high);
            int n = 0;
            for (int i = 0; i < names.Length; i++)
            {
                var at = names[i] == "Marksman" && high ? PickSpawn(true) : spawn;
                var op = SpawnOne(Find(names[i]), at + Random.insideUnitSphere.Flat() * 2.5f, squad, null, faction);
                if (op == null) continue;
                n++;
                made.Add(op);
                if (knowing) op.Perception.ForceCombat();
                else if (player != null)
                {
                    // They heard something in your general direction
                    op.Perception.Awareness = op.Config.alertAt + 0.05f;
                    op.Perception.State = Alertness.Alert;
                    op.Perception.NoisePosition = player.Position + Random.insideUnitSphere.Flat() * 10f;
                    op.Perception.NoiseTime = Time.time;
                }
            }
            Toast($"{(faction != null ? faction.displayName.ToUpper() : "")} SQUAD {squad.callsign.ToUpper()}: {n} {(knowing ? "(knows where you are)" : "(alerted)")}");
            return made;
        }

        /// <summary>Two hostile squads 25 to 35 m apart, each with a rough fix on the other.</summary>
        void SpawnSkirmish(SpawnCatalog.Preset sp)
        {
            var a = PickSpawn(false);
            Vector3 b = a;
            var path = new NavMeshPath();
            NavGraph.EnsureBuilt();
            for (int i = 0; i < 80 && NavGraph.Count > 0; i++)
            {
                var c = NavGraph.Pos[Random.Range(0, NavGraph.Count)];
                float d = Vector3.Distance(c, a);
                if (d < 25f || d > 35f || Mathf.Abs(c.y - a.y) > 3f) continue;
                if (NavMesh.CalculatePath(a, c, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) { b = c; break; }
            }
            var sideA = Spawn(sp.names, false, false, catalog.FactionNamed(sp.faction), a);
            var sideB = Spawn(sp.versusNames ?? sp.names, false, false, catalog.FactionNamed(sp.versusFaction), b);
            void Aware(List<Operative> us, List<Operative> them, Vector3 where)
            {
                if (them.Count == 0) return;
                foreach (var op in us)
                {
                    var foe = them[Random.Range(0, them.Count)];
                    op.FocusOn(foe);
                    op.Perception.ForceCombatAt(where + Random.insideUnitSphere.Flat() * 4f, 6f);
                }
            }
            Aware(sideA, sideB, b);
            Aware(sideB, sideA, a);
            Toast($"SKIRMISH: {sp.faction?.ToUpper()} ({sideA.Count}) vs {sp.versusFaction?.ToUpper()} ({sideB.Count})");
        }

void SpawnPatrol()
        {
            var squad = NewSquad();
            var faction = PickFaction();
            var at = PickSpawn(false);
            var route = MakeRoute(at);
            route.transform.SetParent(squad.transform, true);   // cleared with the squad
            for (int i = 0; i < 2; i++)
                SpawnOne(Find(i == 0 ? "Rifleman" : "Rookie"), route.Point(i), squad, route, faction);
            Toast($"PATROL {squad.callsign.ToUpper()} ({route.Count} points)");
        }

        void ClearAll()
        {
            // Leaves station security alone: only spawned squads go
            var security = StationSecurity.Instance;
            foreach (var s in Squad.All.ToArray())
            {
                if (s == null || (security != null && s.callsign == "Security")) continue;
                Destroy(s.gameObject);
            }
            Toast("CLEARED");
        }

        void Toast(string msg) { toast = msg; toastUntil = Time.unscaledTime + 2.5f; }

        // ---------- GUI ----------

        void OnGUI()
        {
            if (!GameManager.IsPlaying && !menu && !help && Time.unscaledTime > toastUntil) return;
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            float k = DevGUI.Scale;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k;
            if (Time.unscaledTime < toastUntil)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(W * 0.5f - 240, 84, 480, 30), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(W * 0.5f - 240, 86, 480, 26), $"<b>{toast}</b>", new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });
            }
            int alive = 0; foreach (var op in Operative.All) if (!op.IsDead) alive++;
            var pcNow = FindAnyObjectByType<PlayerController>();
            bool noclip = pcNow != null && pcNow.NoClip;
            if (menu && catalog != null && catalog.presets != null)
            {
                float mh = 64 + catalog.presets.Length * 22;
                GUI.color = new Color(0f, 0f, 0f, 0.72f);
                GUI.DrawTexture(new Rect(W * 0.5f - 210, 130, 420, mh), Texture2D.whiteTexture);
                GUI.color = Color.white;
                var sb = new System.Text.StringBuilder();
                sb.Append($"<b>SPAWN</b>   faction <color=#ffd27a>{FactionLabel}</color> <size=12>(Tab)</size>\n");
                for (int d = 0; d < catalog.presets.Length && d < 9; d++) sb.Append($"<b>{d + 1}</b>  {catalog.presets[d].label}\n");
                sb.Append("<size=12><color=#9cf>pick as many as you like    hold Shift: they know where you are    F2 close</color></size>");
                GUI.Label(new Rect(W * 0.5f - 198, 136, 400, mh), sb.ToString(), new GUIStyle(style) { fontSize = 15 });
            }
            else if (menu)
                GUI.Label(new Rect(W * 0.5f - 198, 136, 400, 40), "<b>SPAWN</b>  no spawn catalog (build the AI Playground once)", style);
            if (!help) { GUI.matrix = saved; return; }
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(12, 12, 330, 176), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(22, 18, 320, 172),
                "<b>ADMIN</b>\n" +
                "F2  spawn menu        F3  AI overlay\n" +
                $"F4 / 2x Space  no-clip {(noclip ? "<color=#9fe0a0>ON</color>" : "off")}   F9  clear\n" +
                "F10 freeze AI         F11 god mode\n" +
                "C  crouch   [ ]  dev text   F1  hide\n" +
                "Esc  pause menu: switch maps\n" +
                $"<color=#9cf>alive: {alive}   god: {(AdminSettings.GodMode ? "on" : "off")}   frozen: {(Operative.Frozen ? "yes" : "no")}</color>", style);
            GUI.matrix = saved;
        }
    }
}
