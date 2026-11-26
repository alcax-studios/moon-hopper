using System.Collections;
using System.Collections.Generic;
using Alcax.Combat;
using Alcax.Dialogue;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.AI
{
    /// <summary>
    /// Station security for a peaceful area (the Atrium). Spawns armed guards who patrol and ignore the player, and
    /// decides when he becomes a threat: firing a gun outside the range, or hurting a civilian or a guard. Then every
    /// guard turns hostile (witnesses at once, the rest over the radio) and fights with the full combat AI. When nobody
    /// has seen him and nothing new has happened for a while, security stands down and replaces lost guards.
    /// </summary>
    public class StationSecurity : MonoBehaviour
    {
        public static StationSecurity Instance { get; private set; }

        [SerializeField] GameObject guardPrefab;
        [SerializeField] EnemyArchetype guardArchetype;
        [SerializeField] CombatAIConfig config;
        [SerializeField] Faction faction;
        [SerializeField] PatrolRoute[] routes;
        [Tooltip("Firing here is allowed (the range). Min and max corners, pairs.")]
        [SerializeField] Vector3[] freeFireMin, freeFireMax;
[Tooltip("Staff who arm themselves when security is alerted (the quartermaster): swapped for an armed operative in their clothes, swapped back on stand-down.")]
        [SerializeField] Civilian[] armedStaff;
        [SerializeField] Color[] armedStaffJacket;
        [SerializeField] float standDownAfter = 40f;
        [SerializeField] float replaceAfter = 30f;

        readonly List<Operative> guards = new List<Operative>();
readonly Dictionary<Civilian, Operative> armed = new Dictionary<Civilian, Operative>();
        readonly List<Operative> extras = new List<Operative>();

        /// <summary>A security unit spawned some other way (the admin menu): it follows the station's crime rules too.</summary>
        public void Adopt(Operative op)
        {
            if (op == null || extras.Contains(op)) return;
            extras.Add(op);
            op.SetHostile(Wanted, Wanted && PlayerTarget.Instance != null ? PlayerTarget.Instance.Position : (Vector3?)null, 3f);
            op.Health.Damaged += info => OnGuardHurt(op, info);
        }

        IEnumerable<Operative> AllUnits()
        {
            foreach (var g in guards) if (g != null && !g.IsDead) yield return g;
            foreach (var x in extras) if (x != null && !x.IsDead) yield return x;
            foreach (var a in armed.Values) if (a != null && !a.IsDead) yield return a;
        }
        Squad squad;
        float lastCrime = -100f, lastSeen = -100f, stoodDownAt = -100f;
        string toast; float toastUntil;

public bool Wanted { get; private set; }
        /// <summary>A guard (or armed staff) is fighting, or recently saw or heard, a hostile operative.</summary>
        public bool ThreatActive { get; private set; }
        public Faction StationFaction => faction;
        const float GraceSeconds = 12f;
        float lastThreat = -100f;

        /// <summary>An operative that is a danger to the station: its faction is hostile to security, or it attacked a guard.</summary>
        public static bool IsStationThreat(Operative o)
        {
            if (o == null || o.IsDead || Instance == null) return false;
            if (o.Squad != null && Instance.squad != null && o.Squad == Instance.squad) return false;
            if (Faction.Hostile(o.Faction, Instance.faction)) return true;
            foreach (var g in Instance.guards) if (g != null && !g.IsDead && g.IsHostile(o)) return true;
            return false;
        }
        public IReadOnlyList<Operative> Guards => guards;

        void Awake()
        {
            Instance = this;
        }

        void OnEnable()
        {
            Hearing.Listen(OnHear);
            Civilian.Hurt += OnCivilianHurt;
        }

        void OnDisable()
        {
            Hearing.Stop(OnHear);
            Civilian.Hurt -= OnCivilianHurt;
            if (Instance == this) Instance = null;
        }

        IEnumerator Start()
        {
            yield return null;   // let the NavMesh and the player settle
            var go = new GameObject("Squad Security");
            squad = go.AddComponent<Squad>();
            squad.callsign = "Security";
            if (routes != null) for (int i = 0; i < routes.Length; i++) SpawnGuard(i);
        }

        void SpawnGuard(int i)
        {
            if (guardPrefab == null || routes == null || routes.Length == 0) return;
            var route = routes[i % routes.Length];
            var at = route != null && route.Count > 0 ? route.Point(0) : transform.position;
            if (!NavMesh.SamplePosition(at, out var hit, 4f, NavMesh.AllAreas)) return;
            var obj = Instantiate(guardPrefab, hit.position, Quaternion.identity, squad.transform);
            obj.name = "Guard " + (i + 1);
            var op = obj.GetComponent<Operative>();
            op.Init(guardArchetype, config, faction, squad, route);
            op.SetHostile(false);
            op.Health.Damaged += info => OnGuardHurt(op, info);
            while (guards.Count <= i) guards.Add(null);
            guards[i] = op;
        }

        // ---------- Crimes ----------

        void OnHear(NoiseEvent e)
        {
            var p = PlayerTarget.Instance;
            if (p == null || e.Source != p.gameObject || e.Kind != NoiseKind.Gunshot) return;
            if (IsRangeFire(e.Position)) return;
            if (ThreatActive) return;   // helping fight hostiles on the station is fine
            Crime(e.Position, "SHOTS FIRED");
        }

        void OnCivilianHurt(Civilian c, DamageInfo info)
        {
            var p = PlayerTarget.Instance;
            if (p != null && info.Source == p.gameObject) Crime(p.Position, c.Dead ? "CIVILIAN DOWN" : "ASSAULT");
        }

        void OnGuardHurt(Operative g, DamageInfo info)
        {
            var p = PlayerTarget.Instance;
            if (p != null && info.Source == p.gameObject) Crime(p.Position, "OFFICER UNDER FIRE");
        }

        void Crime(Vector3 where, string reason)
        {
            lastCrime = Time.time;
            if (!Wanted) { Wanted = true; Toast("SECURITY ALERTED: " + reason); }
            var p = PlayerTarget.Instance;
            ArmAllStaff();
            foreach (var a in armed.Values) if (a != null && !a.IsDead && p != null) a.SetHostile(true, p.Position, 2f);
            foreach (var g in AllUnits())
            {
                if (g.HostileToPlayer) continue;
                // Witnesses (close, or in sight) react at once; the rest get it over the radio
                bool sees = p != null && !CoverFinder.Blocked(g.EyePosition, p.HeadPosition);
                float dist = Vector3.Distance(g.transform.position, where);
                bool witness = sees || dist < 25f;
                float delay = witness ? g.Traits.reaction : 1.2f + Random.Range(0f, 0.8f);
                float err = sees ? 0.5f : Mathf.Clamp(dist * 0.08f, 1f, 6f);
                StartCoroutine(AlertLater(g, sees ? p.Position : where, err, delay));
            }
        }

IEnumerator ArmStaff(Civilian c, int index)
        {
            armed[c] = null;   // claimed
            yield return new WaitForSeconds(Random.Range(0.6f, 1.1f));   // duck under the desk, come up armed
            if (c == null || c.Dead || !(Wanted || ThreatActive) || guardPrefab == null) { armed.Remove(c); yield break; }
            var obj = Instantiate(guardPrefab, c.transform.position, c.transform.rotation, squad.transform);
            obj.name = c.DisplayName + " (armed)";
            var op = obj.GetComponent<Operative>();
            op.Init(guardArchetype, config, faction, squad, null);
            op.Health.SetMax(Mathf.Max(1f, c.Health.Current));
            // His own clothes, not the security uniform
            if (armedStaffJacket != null && index < armedStaffJacket.Length)
            {
                var mpb = new MaterialPropertyBlock();
                foreach (var r in obj.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.gameObject.name is not ("Torso" or "Upper" or "Fore")) continue;
                    r.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", armedStaffJacket[index]); r.SetPropertyBlock(mpb);
                }
            }
            op.Health.Damaged += info => OnGuardHurt(op, info);
            var p = PlayerTarget.Instance;
            if (p != null && Wanted) op.SetHostile(true, p.Position, 1.5f);
            else op.SetHostile(false);
            c.gameObject.SetActive(false);
            armed[c] = op;
        }

void ArmAllStaff()
        {
            if (armedStaff == null) return;
            for (int i = 0; i < armedStaff.Length; i++)
            {
                var c = armedStaff[i];
                if (c != null && !c.Dead && c.gameObject.activeSelf && !armed.ContainsKey(c)) StartCoroutine(ArmStaff(c, i));
            }
        }

        void DisarmStaff()
        {
            foreach (var kv in new List<KeyValuePair<Civilian, Operative>>(armed))
            {
                var c = kv.Key; var op = kv.Value;
                if (c == null) continue;
                if (op != null && !op.IsDead)
                {
                    c.gameObject.SetActive(true);
                    c.Health.SetMax(c.Health.Max, false);
                    Destroy(op.gameObject);
                    armed.Remove(c);
                }
                else if (op == null) armed.Remove(c);
            }
        }

        IEnumerator AlertLater(Operative g, Vector3 where, float err, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (g == null || g.IsDead || !Wanted) yield break;
            g.SetHostile(true, where, err);
        }

void Update()
        {
            bool threat = false;
            foreach (var g in AllUnits()) if (g.HasNpcThreat) { threat = true; break; }
            if (threat) { lastThreat = Time.time; if (!ThreatActive) Toast("SECURITY ALERT: HOSTILES ON THE STATION"); }
            ThreatActive = threat || Time.time - lastThreat < GraceSeconds;   // shots right after the last hostile falls aren't crimes
            if (ThreatActive) ArmAllStaff();
            else if (!Wanted && armed.Count > 0 && Time.time - lastThreat > 15f) DisarmStaff();
            if (!Wanted)
            {
                // Replace lost guards some time after things calmed down
                if (Time.time - stoodDownAt > replaceAfter && stoodDownAt > 0f)
                {
                    for (int i = 0; i < guards.Count; i++) if (guards[i] == null || guards[i].IsDead) SpawnGuard(i);
                    stoodDownAt = -100f;
                }
                return;
            }
            foreach (var g in AllUnits()) if (g.Target is PlayerTarget && g.Perception.CanSee) lastSeen = Time.time;
            bool anyAlive = false;
            foreach (var g in AllUnits()) { anyAlive = true; break; }
            if (!anyAlive || (Time.time - lastCrime > standDownAfter && Time.time - lastSeen > standDownAfter)) StandDown();
        }

        void StandDown()
        {
            Wanted = false;
            stoodDownAt = Time.time;
            foreach (var g in AllUnits()) g.SetHostile(false);
            if (!ThreatActive) DisarmStaff();
            Toast(ThreatActive ? "SECURITY: YOU'RE CLEAR, HOSTILES REMAIN" : "SECURITY STOOD DOWN");
        }

        // ---------- Free-fire zones (the range) ----------

        public static bool IsRangeFire(Vector3 pos) => Instance != null && Instance.InFreeFire(pos);
        public static bool InRange(Vector3 pos) => Instance != null && Instance.InFreeFire(pos);

        bool InFreeFire(Vector3 pos)
        {
            if (freeFireMin == null || freeFireMax == null) return false;
            for (int i = 0; i < freeFireMin.Length && i < freeFireMax.Length; i++)
            {
                var a = freeFireMin[i]; var b = freeFireMax[i];
                if (pos.x >= a.x && pos.x <= b.x && pos.y >= a.y && pos.y <= b.y && pos.z >= a.z && pos.z <= b.z) return true;
            }
            return false;
        }

        // ---------- HUD ----------

        void Toast(string s) { toast = s; toastUntil = Time.unscaledTime + 3f; }

        void OnGUI()
        {
            if (!Wanted && !ThreatActive && Time.unscaledTime > toastUntil) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, richText = true };
            float w = Screen.width;
            if (Wanted)
            {
                int alive = 0; foreach (var g in guards) if (g != null && !g.IsDead) alive++;
                float calm = Mathf.Max(0f, standDownAfter - Mathf.Min(Time.time - lastCrime, Time.time - lastSeen));
                GUI.Label(new Rect(0f, 18f, w, 26f), $"<color=#ff6a4a>WANTED</color>  <size=13><color=#e0d6c8>station security ({alive})   lose them: {calm:0}s</color></size>", style);
            }
            else if (ThreatActive) GUI.Label(new Rect(0f, 18f, w, 26f), "<color=#ffb347>SECURITY ALERT</color>  <size=13><color=#e0d6c8>hostiles on the station</color></size>", style);
            if (Time.unscaledTime < toastUntil)
                GUI.Label(new Rect(0f, 46f, w, 24f), $"<size=14><color=#ffd27a>{toast}</color></size>", style);
        }
    }
}
