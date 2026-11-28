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
        [Tooltip("Different-looking guards (same mind): each guard spawns as one of these, in turn. Empty: guardPrefab.")]
        [SerializeField] GameObject[] guardVariants;
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
            DialogueUI.ActionPicked += OnDialogueAction;
        }

        void OnDisable()
        {
            Hearing.Stop(OnHear);
            Civilian.Hurt -= OnCivilianHurt;
            DialogueUI.ActionPicked -= OnDialogueAction;
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
            var prefab = guardVariants != null && guardVariants.Length > 0 && guardVariants[i % guardVariants.Length] != null ? guardVariants[i % guardVariants.Length] : guardPrefab;
            var obj = Instantiate(prefab, hit.position, Quaternion.identity, squad.transform);
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

/// <summary>A crime by the player (fencing stolen goods, refusing a fine): every guard turns on him.</summary>
        public void ReportCrime(Vector3 where, string reason)
        {
            CancelFine();
            Crime(where, reason);
        }

        // ---------- Theft and fines ----------
        // Seen stealing: the nearest calm guard walks up and asks for a fine (what you took, plus a fee). Pay it (the
        // goods go back), hand everything over if you can't, or refuse and fight. Walking off or stalling is refusing.
        const int FineFee = 50;
        int fineValue;
        Operative fineGuard;
        float fineSince, fineAskedAt = -10f;
        bool fineAsked;
        DialogueTree savedGuardTree;
        bool searching;   // the current visit is a random stop, not a reported theft

        public bool FinePending => fineGuard != null;
        public int FineAmount => fineValue + FineFee;

        public void ReportTheft(int value, string witness)
        {
            if (Wanted) return;
            var p = PlayerTarget.Instance;
            if (p == null) return;
            fineValue += value;
if (fineGuard != null && !fineGuard.IsDead) { Toast($"FINE: {FineAmount} CR"); return; }
            Toast("SEEN STEALING");
            bool anyGuard = false;
            foreach (var g in AllUnits()) if (!g.HostileToPlayer && g.OnDuty) { anyGuard = true; break; }
            if (!anyGuard) { Crime(p.Position, "THEFT"); fineValue = 0; return; }
            // Somebody who can actually get to you; if nobody can right now, the fine waits until you're reachable
            SendFineGuard();
        }

        float nextFineTry;
        static UnityEngine.AI.NavMeshPath finePath;

        bool SendFineGuard()
        {
            var p = PlayerTarget.Instance;
            if (p == null) return false;
            if (!NavMesh.SamplePosition(p.Position, out var at, 2f, NavMesh.AllAreas)) return false;
            finePath ??= new NavMeshPath();
            Operative best = null; float bd = float.MaxValue;
            foreach (var g in AllUnits())
            {
                if (g.HostileToPlayer || !g.OnDuty || g.GetComponent<Npc>() == null) continue;
                if (!NavMesh.CalculatePath(g.transform.position, at.position, NavMesh.AllAreas, finePath) || finePath.status != NavMeshPathStatus.PathComplete) continue;
                float d = Vector3.Distance(g.transform.position, p.Position);
                if (d < bd) { bd = d; best = g; }
            }
            if (best == null || !best.Confront(p.transform, OnFineGuardArrived)) return false;
            fineGuard = best;
            fineSince = Time.time;
            fineAsked = false;
            return true;
        }

void OnFineGuardArrived(Operative g)
        {
            if (searching)
            {
                var inv = Alcax.Items.PlayerInventory.Instance;
                int stolen = inv != null ? inv.StolenValue() : 0;
                if (stolen > 0) { fineValue = stolen; AskFine("Random check. Hold still... What's this? That isn't yours."); }
                else AskClear();
                return;
            }
            AskFine();
        }

        void AskClear()
        {
            var npc = fineGuard != null ? fineGuard.GetComponent<Npc>() : null;
            if (npc == null || DialogueUI.Instance == null || DialogueUI.Instance.IsOpen) return;
            var t = ScriptableObject.CreateInstance<DialogueTree>();
            t.startNode = "start";
            t.Add("start", "Random check. Hold still... You're clear. Carry on.", ("Thanks.", "@search_clear"));
            if (savedGuardTree == null) savedGuardTree = npc.dialogue;
            npc.dialogue = t;
            fineAsked = true;
            fineAskedAt = Time.time;
            DialogueUI.Instance.Open(npc);
        }

        void AskFine(string opening = null)
        {
            var npc = fineGuard != null ? fineGuard.GetComponent<Npc>() : null;
            var inv = Alcax.Items.PlayerInventory.Instance;
            if (npc == null || inv == null || DialogueUI.Instance == null || DialogueUI.Instance.IsOpen) return;
            int fine = FineAmount;
            var t = ScriptableObject.CreateInstance<DialogueTree>();
            t.startNode = "start";
            bool can = inv.Credits >= fine;
            string line = (opening ?? "Hold it right there. You were seen taking things that aren't yours.") + $" The fine is {fine} credits: what it's worth, plus {FineFee}. The goods come back to us either way.";
            if (can) t.Add("start", line, ($"Pay {fine} credits.", "@fine_pay"), ("I'm not paying. (Fight)", "@fine_refuse"));
            else t.Add("start", line + $" You've got {inv.Credits}? Then you hand over all of it.", ($"Hand over the goods and {inv.Credits} credits.", "@fine_pay"), ("I'm not paying. (Fight)", "@fine_refuse"));
            if (savedGuardTree == null) savedGuardTree = npc.dialogue;
            npc.dialogue = t;
            npc.CanTalk = true;
            fineAsked = true;
            fineAskedAt = Time.time;
            DialogueUI.Instance.Open(npc);
        }

        void OnDialogueAction(string action, Npc npc)
        {
            if (fineGuard == null || npc == null || npc.gameObject != fineGuard.gameObject) return;
            var inv = Alcax.Items.PlayerInventory.Instance;
            if (action == "@search_clear") { CancelFine(); return; }
            if (action == "@fine_pay" && inv != null)
            {
                int fine = FineAmount;
                int paid = Mathf.Min(fine, inv.Credits);
                inv.Spend(paid);
                inv.ConfiscateStolen();
                Toast($"FINE PAID: {paid} CR");
                CancelFine();
            }
            else if (action == "@fine_refuse")
            {
                var p = PlayerTarget.Instance;
                ReportCrime(p != null ? p.Position : fineGuard.transform.position, "RESISTING A FINE");
            }
        }

        void CancelFine()
        {
            if (fineGuard != null)
            {
                var npc = fineGuard.GetComponent<Npc>();
                if (npc != null && savedGuardTree != null) npc.dialogue = savedGuardTree;
                fineGuard.EndConfront();
            }
            savedGuardTree = null;
            fineGuard = null;
            fineValue = 0;
            fineAsked = false;
            searching = false;
        }

// ---------- Stolen goods in plain sight, and random stops ----------
        readonly HashSet<Alcax.Items.ItemStack> recognized = new HashSet<Alcax.Items.ItemStack>();
        float exposedFor, nextSearchCheck, searchCooldownUntil;
        const float RecognizeRange = 8f, RecognizeSeconds = 1.5f;

        /// <summary>Wearing something stolen, or holding it in your hand, where someone close gets a good look: reported.
        /// What's in the backpack (and what you've eaten) can't be seen.</summary>
        void UpdateStolenInSight(float dt)
        {
            var inv = Alcax.Items.PlayerInventory.Instance;
            if (inv == null || Wanted) { exposedFor = 0f; return; }
            int value = 0;
            var shown = new List<Alcax.Items.ItemStack>();
            foreach (var slot in Alcax.Items.PlayerInventory.BodySlots)
            {
                var w = inv.Worn(slot);
                if (w != null && w.stolen && !recognized.Contains(w)) { shown.Add(w); value += w.Value; }
            }
            var held = inv.SelectedStack;
            if (held != null && held.stolen && !held.def.Consumable && !recognized.Contains(held)) { shown.Add(held); value += held.Value; }
            if (shown.Count == 0 || !Alcax.Items.Theft.Watched(RecognizeRange, out var who)) { exposedFor = 0f; return; }
            exposedFor += dt;
            if (exposedFor < RecognizeSeconds) return;
            exposedFor = 0f;
            foreach (var s in shown) recognized.Add(s);
            ReportTheft(value, who);
        }

        /// <summary>Now and then a calm guard who can see you comes over for a random check (much likelier when you
        /// carry stolen goods). Clean: he waves you on. Not clean: the fine.</summary>
        void UpdateSearches()
        {
            if (Wanted || fineGuard != null || fineValue > 0 || Time.time < searchCooldownUntil || Time.time < nextSearchCheck) return;
            nextSearchCheck = Time.time + 20f;
            var p = PlayerTarget.Instance;
            var inv = Alcax.Items.PlayerInventory.Instance;
            if (p == null || inv == null || DialogueUI.Instance == null || DialogueUI.Instance.IsOpen) return;
            float chance = inv.StolenValue() > 0 ? 0.35f : 0.06f;
            if (Random.value > chance) return;
            Operative nearest = null; float bd = 18f;
            foreach (var g in AllUnits())
            {
                if (g.HostileToPlayer || !g.OnDuty || g.GetComponent<Npc>() == null) continue;
                float d = Vector3.Distance(g.transform.position, p.Position);
                if (d < bd && !CoverFinder.Blocked(g.EyePosition, p.HeadPosition)) { bd = d; nearest = g; }
            }
            if (nearest == null || !nearest.Confront(p.transform, OnFineGuardArrived)) return;
            fineGuard = nearest;
            fineSince = Time.time;
            fineAsked = false;
            searching = true;
            searchCooldownUntil = Time.time + 180f;
        }

        /// <summary>Test hook: start a random stop now.</summary>
        public void ForceSearch() { searchCooldownUntil = 0f; nextSearchCheck = 0f; var keep = Random.state; Random.InitState(1); UpdateSearchesForced(); Random.state = keep; }

        void UpdateSearchesForced()
        {
            var p = PlayerTarget.Instance;
            if (p == null || fineGuard != null) return;
            Operative nearest = null; float bd = float.MaxValue;
            foreach (var g in AllUnits())
            {
                if (g.HostileToPlayer || !g.OnDuty || g.GetComponent<Npc>() == null) continue;
                float d = Vector3.Distance(g.transform.position, p.Position);
                if (d < bd) { bd = d; nearest = g; }
            }
            if (nearest == null || !nearest.Confront(p.transform, OnFineGuardArrived)) return;
            fineGuard = nearest; fineSince = Time.time; fineAsked = false; searching = true; searchCooldownUntil = Time.time + 180f;
        }

        void UpdateFine()
        {
            UpdateStolenInSight(Time.deltaTime);
            UpdateSearches();
            // A fine that's waiting for you to be reachable
            if (fineGuard == null && fineValue > 0 && !Wanted && Time.time > nextFineTry) { nextFineTry = Time.time + 2f; SendFineGuard(); }
            if (fineGuard == null) return;
            var p = PlayerTarget.Instance;
            if (fineGuard.IsDead || p == null || Wanted) { CancelFine(); return; }
            float d = Vector3.Distance(fineGuard.transform.position, p.Position);
            bool talking = DialogueUI.Instance != null && DialogueUI.Instance.IsOpen;
            // Never reached you in time, or you walked off after being asked: you're refusing
            bool clean = searching && fineValue <= 0;
            if (clean && fineAsked && !talking) { CancelFine(); return; }
            if (fineAsked && !talking && d > 12f) { ReportCrime(p.Position, "EVADING A FINE"); return; }
            if (!fineAsked && Time.time - fineSince > 45f) { int keep = fineValue; CancelFine(); fineValue = keep; return; }   // couldn't reach you: try again later
            // Closed the conversation without answering: he asks again
            if (fineAsked && !talking && d < 3.5f && Time.time - fineAskedAt > 4f) AskFine();
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
            UpdateFine();
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
