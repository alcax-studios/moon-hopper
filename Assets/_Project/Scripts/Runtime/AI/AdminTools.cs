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
    /// The admin console, on every map (a core dev feature, not tied to any map). Created once at startup and kept
    /// across map loads. F1 opens it (the cursor frees up; you can still walk). Sections: Spawn (one unit per click,
    /// unaware, where the crosshair points; Shift+click: it knows where you are), AI, Player, Maps, View.
    /// Hotkeys work with it closed: F3 AI overlay, F4 or double-tap Space no-clip, F9 clear, F10 freeze AI, F11 god mode.
    /// </summary>
    public class AdminTools : MonoBehaviour
    {
        public static AdminTools Instance { get; private set; }

        SpawnCatalog catalog;
        bool open;
        Vector2 scroll;
        readonly Dictionary<Faction, Squad> squadOf = new Dictionary<Faction, Squad>();
        Squad noFactionSquad;
        int factionMode = -1;   // -1: random per spawn; otherwise a faction index
        int squadCounter;
        string toast;
        float toastUntil, lastSpaceTap = -1f;
        GUIStyle style;
        static readonly string[] Callsigns = { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel" };

        public SpawnCatalog Catalog => catalog;
        public CombatAIConfig Config => catalog != null ? catalog.config : null;
        public Faction[] Factions => catalog != null ? catalog.factions : null;
        string FactionLabel => Factions == null || Factions.Length == 0 ? "none" : factionMode >= 0 && factionMode < Factions.Length ? Factions[factionMode].displayName : "Random";

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
            squadOf.Clear(); noFactionSquad = null;
            SetOpen(false);
        }

        void SetOpen(bool on)
        {
            open = on;
            AdminSettings.ConsoleOpen = on;
            AdminSettings.DevMenuOpen = on;
            GameManager.RefreshCursor();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || !GameManager.IsPlaying)
            {
                // Pausing (Esc) closes the console so the pause menu owns the cursor
                if (open && GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) { open = false; AdminSettings.ConsoleOpen = false; AdminSettings.DevMenuOpen = false; }
                return;
            }
            EnsurePlayerTarget();
            if (kb.f1Key.wasPressedThisFrame || kb.f2Key.wasPressedThisFrame) SetOpen(!open);
            bool doubleSpace = false;
            if (kb.spaceKey.wasPressedThisFrame)
            {
                doubleSpace = Time.unscaledTime - lastSpaceTap < 0.3f;
                lastSpaceTap = doubleSpace ? -1f : Time.unscaledTime;
            }
            if (kb.f4Key.wasPressedThisFrame || doubleSpace) ToggleNoClip();
            if (kb.f9Key.wasPressedThisFrame) ClearAll();
            if (kb.f10Key.wasPressedThisFrame) ToggleFreeze();
            if (kb.f11Key.wasPressedThisFrame) ToggleGod();
        }

        void ToggleNoClip()
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) { pc.SetNoClip(!pc.NoClip); Toast(pc.NoClip ? "NO-CLIP ON  (Space up, C down, Shift fast)" : "NO-CLIP OFF"); }
        }
        void ToggleFreeze() { Operative.Frozen = !Operative.Frozen; Toast(Operative.Frozen ? "AI FROZEN" : "AI RESUMED"); }
        void ToggleGod() { AdminSettings.GodMode = !AdminSettings.GodMode; Toast(AdminSettings.GodMode ? "GOD MODE ON" : "GOD MODE OFF"); }

/// <summary>Where the player is looking: a ray from the screen center (through glass), snapped to the nearest
        /// walkable point. A wall hit steps back off the wall; repeat clicks scatter a little so units don't stack.</summary>
        bool AimSpawn(out Vector3 pos)
        {
            pos = default;
            var cam = Camera.main;
            if (cam == null) return false;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            if (!Physics.Raycast(ray, out var hit, 250f, AILayers.World, QueryTriggerInteraction.Ignore)) return false;
            var at = hit.point + hit.normal * 0.6f;
            // Pointed at a wall (or a ceiling): the floor right below, in front of it
            if (hit.normal.y < 0.6f && Physics.Raycast(at + Vector3.up * 0.2f, Vector3.down, out var floor, 80f, AILayers.World, QueryTriggerInteraction.Ignore))
                at = floor.point;
            if (!NavMesh.SamplePosition(at, out var nav, 3f, NavMesh.AllAreas)
                && !NavMesh.SamplePosition(hit.point, out nav, 3f, NavMesh.AllAreas)) return false;
            // Scatter repeat clicks a little, staying connected to the aimed spot (never through a wall)
            var j = Random.insideUnitCircle * 1.2f;
            if (NavMesh.SamplePosition(nav.position + new Vector3(j.x, 0f, j.y), out var spread, 0.6f, NavMesh.AllAreas)
                && !NavMesh.Raycast(nav.position, spread.position, out _, NavMesh.AllAreas)) nav = spread;
            pos = nav.position;
            return true;
        }

        /// <summary>One unit where the player is looking. Unaware unless told otherwise.
        /// Units of the same faction share a squad (so they talk to each other), up to 6.</summary>
        public Operative SpawnSingle(SpawnCatalog.Entry e, bool knowing)
        {
            if (e == null) return null;
            if (!AimSpawn(out var spot)) { Toast("NO WALKABLE GROUND WHERE YOU ARE LOOKING"); return null; }
            var faction = PickFaction();
            Squad squad = faction != null ? (squadOf.TryGetValue(faction, out var sq) ? sq : null) : noFactionSquad;
            if (squad == null || squad.Members.Count >= 6)
            {
                squad = NewSquad();
                if (faction != null) squadOf[faction] = squad; else noFactionSquad = squad;
            }
            var op = SpawnOne(e, spot, squad, null, faction);
            if (op == null) { Toast("NO SPOT FOUND"); return null; }
            if (knowing) op.Perception.ForceCombat();
            Toast($"{(faction != null ? faction.displayName.ToUpper() + " " : "")}{e.archetype.displayName.ToUpper()}  ({squad.callsign}){(knowing ? "  knows where you are" : "")}");
            return op;
        }

void SpawnAmbient(bool drone)
        {
            if (!AimSpawn(out var spot)) { Toast("NO WALKABLE GROUND WHERE YOU ARE LOOKING"); return; }
            var go = new GameObject(drone ? "ScannerDrone" : "InfoBot");
            if (drone) { go.transform.position = spot + Vector3.up * 3f; go.AddComponent<Alcax.Ambient.ScannerDrone>(); }
            else { go.transform.position = spot; go.AddComponent<UnityEngine.AI.NavMeshAgent>(); go.AddComponent<Alcax.Ambient.InfoBot>(); }
            Toast(drone ? "SCANNER DRONE" : "INFO ROBOT");
        }

        void AlertAll()
        {
            int n = 0;
            foreach (var op in Operative.All)
                if (!op.IsDead && op.HostileToPlayer) { op.Perception.ForceCombat(); n++; }
            Toast($"ALERTED {n}");
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
            var sec = StationSecurity.Instance;
            if (sec != null && faction != null && faction == sec.StationFaction) sec.Adopt(op);   // spawned security plays by the station's rules
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
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true };
            float k = DevGUI.Scale;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            float W = Screen.width / k, H = Screen.height / k;
            if (Time.unscaledTime < toastUntil)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(new Rect(W * 0.5f - 240, 84, 480, 30), Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(W * 0.5f - 240, 86, 480, 26), $"<b>{toast}</b>", new GUIStyle(style) { alignment = TextAnchor.MiddleCenter, fontSize = 16 });
            }
            if (open && GameManager.IsPlaying) DrawConsole(W, H);
            else if (GameManager.IsPlaying)
                GUI.Label(new Rect(12, 8, 300, 20), "<size=11><color=#ffffff66>F1 admin</color></size>", style);
            GUI.matrix = saved;
        }

        // ---------- The console ----------

        GUIStyle headStyle, sectionStyle, btnStyle, onStyle, noteStyle;
        const float PanelW = 360f, Pad = 12f, RowH = 28f, Gap = 4f;
        float cy;

        void EnsureConsoleStyles()
        {
            if (headStyle != null) return;
            Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
            headStyle = new GUIStyle(style) { fontSize = 17, fontStyle = FontStyle.Bold };
            sectionStyle = new GUIStyle(style) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.82f, 0.48f) } };
            noteStyle = new GUIStyle(style) { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.65f, 0.8f, 1f) } };
            btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 2, 2), richText = true };
            btnStyle.normal.background = Tex(new Color(1f, 1f, 1f, 0.07f)); btnStyle.normal.textColor = Color.white;
            btnStyle.hover.background = Tex(new Color(1f, 1f, 1f, 0.16f)); btnStyle.hover.textColor = Color.white;
            btnStyle.active.background = Tex(new Color(1f, 0.82f, 0.48f, 0.35f)); btnStyle.active.textColor = Color.white;
            onStyle = new GUIStyle(btnStyle);
            onStyle.normal.background = Tex(new Color(0.45f, 0.85f, 0.5f, 0.22f));
            onStyle.hover.background = Tex(new Color(0.45f, 0.85f, 0.5f, 0.32f));
        }

        // Collapsible sections (like the Inspector foldouts): Spawn starts open, the rest closed. Kept across maps.
        readonly HashSet<string> expanded = new HashSet<string> { "SPAWN" };
        float contentHeight = 600f;

        /// <summary>A clickable section header. Returns true when the section is expanded (draw its rows).</summary>
        bool Section(float x, float w, string title, string summary = null)
        {
            cy += 4f;
            bool isOpen = expanded.Contains(title);
            var r = new Rect(x, cy, w, 24);
            GUI.color = new Color(1f, 0.82f, 0.48f, r.Contains(Event.current.mousePosition) ? 0.16f : 0.08f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 8, cy + 3, w - 16, 18), (isOpen ? "-  " : "+  ") + title, sectionStyle);
            if (summary != null) GUI.Label(new Rect(x + 8, cy + 3, w - 16, 18), $"<size=11><color=#ffffff80>{summary}</color></size>", new GUIStyle(style) { alignment = TextAnchor.MiddleRight });
            if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { if (isOpen) expanded.Remove(title); else expanded.Add(title); isOpen = !isOpen; }
            cy += 28f;
            return isOpen;
        }

        bool Button(float x, float w, string label, string hint = null)
        {
            bool hit = GUI.Button(new Rect(x, cy, w, RowH), label, btnStyle);
            if (hint != null) GUI.Label(new Rect(x, cy, w - 10, RowH), $"<size=11><color=#ffffff70>{hint}</color></size>", new GUIStyle(style) { alignment = TextAnchor.MiddleRight });
            cy += RowH + Gap;
            return hit;
        }

        bool Toggle(float x, float w, string label, bool on, string hint = null)
        {
            bool hit = GUI.Button(new Rect(x, cy, w, RowH), label + (on ? "   <color=#9fe0a0><b>ON</b></color>" : "   <color=#888>off</color>"), on ? onStyle : btnStyle);
            if (hint != null) GUI.Label(new Rect(x, cy, w - 10, RowH), $"<size=11><color=#ffffff70>{hint}</color></size>", new GUIStyle(style) { alignment = TextAnchor.MiddleRight });
            cy += RowH + Gap;
            return hit;
        }

        void DrawConsole(float W, float H)
        {
            EnsureConsoleStyles();
            var pcNow = FindAnyObjectByType<PlayerController>();
            int alive = 0; foreach (var op in Operative.All) if (!op.IsDead) alive++;
            bool shift = Keyboard.current != null && (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

            float panelH = H - 24f;
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.86f);
            GUI.DrawTexture(new Rect(12, 12, PanelW, panelH), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.82f, 0.48f, 0.6f);
            GUI.DrawTexture(new Rect(12, 12, PanelW, 2), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(12 + Pad, 20, PanelW - Pad * 2, 24), "ADMIN", headStyle);
            GUI.Label(new Rect(12 + Pad, 20, PanelW - Pad * 2, 24), $"<size=11><color=#ffffff80>{SceneManager.GetActiveScene().name}   alive {alive}   F1 close</color></size>", new GUIStyle(style) { alignment = TextAnchor.MiddleRight });

            float x = 0f, w = PanelW - Pad * 2 - 14f;
            int units = catalog != null && catalog.roster != null ? catalog.roster.Length : 0;
            var view = new Rect(12 + Pad, 50, PanelW - Pad * 2, panelH - 46);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, w, contentHeight), false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            cy = 0f;

            // --- Spawn ---
            if (Section(x, w, "SPAWN", FactionLabel))
            {
            GUI.Label(new Rect(x, cy, w, 30), "One per click, where the crosshair points, unaware. <b>Shift</b>+click: it knows where you are.", noteStyle);
            cy += 32f;
            if (Factions != null && Factions.Length > 0 && Button(x, w, $"Faction:  <color=#ffd27a>{FactionLabel}</color>", "click to cycle"))
                factionMode = factionMode + 1 >= Factions.Length ? -1 : factionMode + 1;
            if (units > 0)
            {
                if (Button(x, w, "<b>Random unit</b>", shift ? "aware" : null)) SpawnSingle(catalog.roster[Random.Range(0, units)], shift);
                foreach (var e in catalog.roster)
                    if (e != null && e.archetype != null && Button(x, w, e.archetype.displayName, shift ? "aware" : null)) SpawnSingle(e, shift);
            }
            else GUI.Label(new Rect(x, cy, w, 20), "No spawn catalog (build the AI Playground once).", noteStyle);
            if (Button(x, w, "Scanner drone", "ambient")) SpawnAmbient(true);
            if (Button(x, w, "Info robot", "ambient")) SpawnAmbient(false);
            }

            // --- AI ---
            if (Section(x, w, "AI", Operative.Frozen ? "frozen" : null))
            {
            if (Button(x, w, "Alert everyone hostile to you")) AlertAll();
            if (Button(x, w, "Clear spawned units", "F9")) ClearAll();
            if (Toggle(x, w, "Freeze AI", Operative.Frozen, "F10")) ToggleFreeze();
            if (Button(x, w, $"AI overlay:  <color=#ffd27a>{AIDebugOverlay.View}</color>", "F3")) AIDebugOverlay.View = (AIDebugOverlay.Mode)(((int)AIDebugOverlay.View + 1) % 3);
            }

            // --- Player ---
            bool noclip = pcNow != null && pcNow.NoClip;
            if (Section(x, w, "PLAYER", PlayerSummary(noclip)))
            {
            if (Toggle(x, w, "God mode", AdminSettings.GodMode, "F11")) ToggleGod();
            if (Toggle(x, w, "Invisible", AdminSettings.Invisible, "nobody sees or targets you")) { AdminSettings.Invisible = !AdminSettings.Invisible; Toast(AdminSettings.Invisible ? "INVISIBLE" : "VISIBLE"); }
            if (Toggle(x, w, "No-clip", noclip, "F4 / 2x Space")) ToggleNoClip();
            if (Toggle(x, w, "Infinite ammo", AdminSettings.InfiniteAmmo)) AdminSettings.InfiniteAmmo = !AdminSettings.InfiniteAmmo;
            if (Toggle(x, w, "One-hit kills", AdminSettings.OneHitKills)) AdminSettings.OneHitKills = !AdminSettings.OneHitKills;
            if (Toggle(x, w, "All weapons", AdminSettings.AllWeapons)) AdminSettings.SetAllWeapons(!AdminSettings.AllWeapons);
            if (Toggle(x, w, "Infinite stamina", AdminSettings.InfiniteStamina)) AdminSettings.InfiniteStamina = !AdminSettings.InfiniteStamina;
            if (Toggle(x, w, "Needs don't drain", AdminSettings.NoNeeds)) AdminSettings.NoNeeds = !AdminSettings.NoNeeds;
            if (Button(x, w, "Fill needs and stamina")) { Alcax.Player.PlayerVitals.Instance?.Fill(); Toast("FED, WATERED, RESTED"); }
            if (Button(x, w, "Full heal")) { AdminSettings.RequestFullHeal(); Toast("HEALED"); }
            if (Button(x, w, "+50 ammo")) { AdminSettings.RequestAmmo(); Toast("+50 AMMO"); }
            }

            // --- Maps ---
            string current = SceneManager.GetActiveScene().path;
            if (Section(x, w, "MAPS", SceneManager.GetActiveScene().name))
            foreach (var (mapName, path) in GameManager.Maps)
            {
                bool here = path == current;
                if (Button(x, w, here ? mapName + "   <color=#888>(here)</color>" : mapName, here ? "reload" : "load"))
                {
                    if (here) GameManager.Instance?.Restart(); else GameManager.Instance?.LoadMap(path);
                    SetOpen(false);
                }
            }

            // --- World ---
            if (Section(x, w, "WORLD", Alcax.FX.TimeOfDay.Current.ToString()))
            {
                float third = (w - Gap * 2f) / 3f, wy = cy;
                foreach (var (preset, i) in new[] { (Alcax.FX.TimePreset.Day, 0), (Alcax.FX.TimePreset.Dusk, 1), (Alcax.FX.TimePreset.Night, 2) })
                {
                    bool on = Alcax.FX.TimeOfDay.Current == preset;
                    if (GUI.Button(new Rect(x + i * (third + Gap), wy, third, RowH), (on ? "<b>" : "") + preset + (on ? "</b>" : ""), on ? onStyle : btnStyle))
                    { Alcax.FX.TimeOfDay.Set(preset); Toast("TIME: " + preset.ToString().ToUpper()); }
                }
                cy = wy + RowH + Gap;
            }

            // --- View ---
            if (Section(x, w, "VIEW", $"text {DevGUI.UserScale:0.0}x"))
            {
            float half = (w - Gap) * 0.5f, y0 = cy;
            if (GUI.Button(new Rect(x, cy, half, RowH), "Dev text  -", btnStyle)) DevGUI.UserScale = Mathf.Max(0.6f, DevGUI.UserScale - 0.1f);
            if (GUI.Button(new Rect(x + half + Gap, cy, half, RowH), $"Dev text  +   <color=#ffffff80>{DevGUI.UserScale:0.0}x</color>", btnStyle)) DevGUI.UserScale = Mathf.Min(2.5f, DevGUI.UserScale + 0.1f);
            cy = y0 + RowH + Gap;
            }

            contentHeight = cy + 8f;   // measured, so the scroll range always fits what is drawn
            GUI.EndScrollView();
        }

        static string PlayerSummary(bool noclip)
        {
            var on = new List<string>();
            if (AdminSettings.GodMode) on.Add("god");
            if (AdminSettings.Invisible) on.Add("invisible");
            if (noclip) on.Add("no-clip");
            if (AdminSettings.InfiniteAmmo) on.Add("inf ammo");
            if (AdminSettings.OneHitKills) on.Add("one-hit");
            return on.Count > 0 ? string.Join(", ", on) : null;
        }
    }
}
