using System.Collections;
using System.Collections.Generic;
using System.Text;
using Alcax.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.AI
{
    /// <summary>
    /// Dev-only A/B experiments for the combat AI, run from Play mode (start with AIExperiment.Run).
    /// Each trial runs twice, with the spatial-reasoning switches off (the earlier behavior) and on, from the same setup.
    ///   Search:   the player hides in a room of the two-floor house; an alerted squad of 4 that heard a noise near the
    ///             house approaches from outside. Metrics: time to first sighting, useful-gaze fraction (open view of 4 m or
    ///             more along the gaze), wall-stare fraction (under 1.5 m), closest approach before the sighting.
    ///   Distance: the player stands in the yard and a squad knows where they are. Metrics: fraction of time any
    ///             operative is within 3 m or 5 m, and the median operative distance.
    /// Results go to the console and to <see cref="Report"/>.
    /// </summary>
    public class AIExperiment : MonoBehaviour
    {
        public static string Report = "";
        public static bool Running;

        /// <summary>
        /// Run an A/B experiment. Capped by real time: it stops starting new trial pairs once maxMinutes of wall-clock
        /// time is used (default 3), and reports what it has. Trials are short and run at timeScale (default 4).
        /// </summary>
        public static void Run(string kind, int trials, float timeScale = 4f, float maxMinutes = 3f)
        {
            Application.runInBackground = true;   // keep ticking while the Editor is unfocused
            var go = new GameObject("AIExperiment");
            var x = go.AddComponent<AIExperiment>();
            x.budgetEnd = Time.realtimeSinceStartup + maxMinutes * 60f;
            x.StartCoroutine(x.Runner(kind, trials, timeScale));
        }

        float budgetEnd;
        public static int TrialsDone;

        /// <summary>Stop a running experiment now (also F9 or Esc while it runs): restore everything and leave Play mode.</summary>
        public static void Abort()
        {
            foreach (var x in FindObjectsByType<AIExperiment>())
            {
                x.StopAllCoroutines();
                Report = "aborted after " + TrialsDone + " trial pairs";
                Debug.Log("[AIExperiment] " + Report);
                x.Finish();
            }
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.f9Key.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) Abort();
        }

        /// <summary>Restore the switches and time scale, clear the field, and exit Play mode (Editor).</summary>
        void Finish()
        {
            Clear();
            Time.timeScale = 1f;
            if (cfgRef != null) cfgRef.useCounterPeek = cfgRef.useSteadyMoves = cfgRef.useSpaceAim = cfgRef.useCqb = cfgRef.useRoomGraph = cfgRef.useSpaceAwareness = cfgRef.useHitCommit = cfgRef.useContactDrill = cfgRef.useMoveDoctrine = cfgRef.useFightingCover = cfgRef.useInfoEngagement = cfgRef.useCoverRepertoire = cfgRef.useAmmoBudget = cfgRef.useCognitiveFilters = cfgRef.useTrust = cfgRef.fuseReports = cfgRef.useAttention = cfgRef.useStandoff = cfgRef.useOccupancySearch = cfgRef.usePreAim = true;
            Running = false;
            Destroy(gameObject);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        void OnDestroy()
        {
            // Never leave the switches in the "before" state or the game fast-forwarded
            Time.timeScale = 1f;
            if (cfgRef != null) cfgRef.useCounterPeek = cfgRef.useSteadyMoves = cfgRef.useSpaceAim = cfgRef.useCqb = cfgRef.useRoomGraph = cfgRef.useSpaceAwareness = cfgRef.useHitCommit = cfgRef.useContactDrill = cfgRef.useMoveDoctrine = cfgRef.useFightingCover = cfgRef.useInfoEngagement = cfgRef.useCoverRepertoire = cfgRef.useAmmoBudget = cfgRef.useCognitiveFilters = cfgRef.useTrust = cfgRef.fuseReports = cfgRef.useAttention = cfgRef.useStandoff = cfgRef.useOccupancySearch = cfgRef.usePreAim = true;
            Running = false;
        }

        CombatAIConfig cfgRef;

        struct Arm { public string name; public bool on; public List<float> detect, useful, wall, closest, near3, near5, median, acquire, gun3, acquireCover, switches, reversals, beliefJump, beliefErr, exposed, hits, freshness, shots, dry, peekExposed, peekHits, passive, active, resight, sneaky, firstShot, inCover6, exposedC, duckShare, loops, shotsCqb, hitsCqb, duckCqb, duckFar, inDist, inFace, inLeft, inFiring, cqFind, cqEntered, cqDoors, cqProper, cqWall, cqPitch, cqUpPitch, rgFirst, rgFiring, rgAway, rgLowered, rgRev, rgBack, pkLatency, pkHits, pkAnswered, pkDeny, pkHidden, pkSeenOut, pkFocus, pkEarly; public int[] peeks; }

        IEnumerator Runner(string kind, int trials, float timeScale)
        {
            Running = true;
            Report = "running";
            // Wait for the scene to be ready (started right after entering Play mode)
            float wait = Time.realtimeSinceStartup + 15f;
            while ((GameManager.Instance == null || FindAnyObjectByType<PlaygroundDirector>() == null || PlayerTarget.Instance == null) && Time.realtimeSinceStartup < wait)
                yield return null;
            if (GameManager.Instance == null || FindAnyObjectByType<PlaygroundDirector>() == null)
            {
                Report = "failed: the AI Playground scene is not running";
                Debug.LogWarning("[AIExperiment] " + Report);
                Finish();
                yield break;
            }
            var gm = GameManager.Instance;
            gm.SetState(GameState.Playing, true);
            AdminSettings.SetGodModeForSession(true);   // (an automated run: not saved into the tester's settings)
            var dir = FindAnyObjectByType<PlaygroundDirector>();
            var admin = AdminTools.Instance;
            var cfg = admin != null ? admin.Config : null;
            if (cfg == null && admin != null && admin.Catalog != null && admin.Catalog.roster.Length > 0)
                cfg = admin.Catalog.roster[0].prefab.GetComponent<Operative>().Config;   // the operative prefabs carry the config asset
            var arms = new[]
            {
                new Arm { name = "BEFORE (switches off)", on = false, detect = new(), useful = new(), wall = new(), closest = new(), near3 = new(), near5 = new(), median = new(), acquire = new(), gun3 = new(), acquireCover = new(), switches = new(), reversals = new(), beliefJump = new(), beliefErr = new(), exposed = new(), hits = new(), freshness = new(), shots = new(), dry = new(), peekExposed = new(), peekHits = new(), passive = new(), active = new(), resight = new(), sneaky = new(), firstShot = new(), inCover6 = new(), exposedC = new(), duckShare = new(), loops = new(), shotsCqb = new(), hitsCqb = new(), duckCqb = new(), duckFar = new(), inDist = new(), inFace = new(), inLeft = new(), inFiring = new(), cqFind = new(), cqEntered = new(), cqDoors = new(), cqProper = new(), cqWall = new(), cqPitch = new(), cqUpPitch = new(), rgFirst = new(), rgFiring = new(), rgAway = new(), rgLowered = new(), rgRev = new(), rgBack = new(), pkLatency = new(), pkHits = new(), pkAnswered = new(), pkDeny = new(), pkHidden = new(), pkSeenOut = new(), pkFocus = new(), pkEarly = new(), peeks = new int[4] },
                new Arm { name = "AFTER (switches on)", on = true, detect = new(), useful = new(), wall = new(), closest = new(), near3 = new(), near5 = new(), median = new(), acquire = new(), gun3 = new(), acquireCover = new(), switches = new(), reversals = new(), beliefJump = new(), beliefErr = new(), exposed = new(), hits = new(), freshness = new(), shots = new(), dry = new(), peekExposed = new(), peekHits = new(), passive = new(), active = new(), resight = new(), sneaky = new(), firstShot = new(), inCover6 = new(), exposedC = new(), duckShare = new(), loops = new(), shotsCqb = new(), hitsCqb = new(), duckCqb = new(), duckFar = new(), inDist = new(), inFace = new(), inLeft = new(), inFiring = new(), cqFind = new(), cqEntered = new(), cqDoors = new(), cqProper = new(), cqWall = new(), cqPitch = new(), cqUpPitch = new(), rgFirst = new(), rgFiring = new(), rgAway = new(), rgLowered = new(), rgRev = new(), rgBack = new(), pkLatency = new(), pkHits = new(), pkAnswered = new(), pkDeny = new(), pkHidden = new(), pkSeenOut = new(), pkFocus = new(), pkEarly = new(), peeks = new int[4] },
            };
            cfgRef = cfg;
            TrialsDone = 0;
            float oldScale = Time.timeScale;
            Time.timeScale = timeScale;
            bool cut = false;
            for (int t = 0; t < trials; t++)
            {
                if (Time.realtimeSinceStartup > budgetEnd) { cut = true; break; }
                int seed = 1000 + t * 17;
                for (int a = 0; a < arms.Length; a++)
                {
                    if (kind == "prefire")
                    {
                        cfg.useSteadyMoves = cfg.useSpaceAim = cfg.useCqb = cfg.useContactDrill = cfg.useSpaceAwareness = cfg.useRoomGraph = cfg.useHitCommit = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = cfg.useCounterPeek = cfg.useHitStunLimit = true;
                        cfg.usePeekPrefire = arms[a].on;
                    }
                    else if (kind == "peekrapid")
                    {
                        cfg.useSteadyMoves = cfg.useSpaceAim = cfg.useCqb = cfg.useContactDrill = cfg.useSpaceAwareness = cfg.useRoomGraph = cfg.useHitCommit = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = cfg.useCounterPeek = true;
                        cfg.useHitStunLimit = arms[a].on;
                    }
                    else if (kind == "doorpeek")
                    {
                        cfg.useSteadyMoves = cfg.useSpaceAim = cfg.useCqb = cfg.useContactDrill = cfg.useSpaceAwareness = cfg.useRoomGraph = cfg.useHitCommit = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useCounterPeek = arms[a].on;
                    }
                    else if (kind == "peek")
                    {
                        cfg.useSteadyMoves = cfg.useSpaceAim = cfg.useCqb = cfg.useContactDrill = cfg.useSpaceAwareness = cfg.useRoomGraph = cfg.useHitCommit = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useCounterPeek = arms[a].on;
                    }
                    else if (kind == "range")
                    {
                        cfg.useSpaceAim = cfg.useCqb = cfg.useSpaceAwareness = cfg.useRoomGraph = cfg.useHitCommit = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useContactDrill = true;
                        cfg.useSteadyMoves = arms[a].on;
                    }
                    else if (kind == "hall")
                    {
                        cfg.useSpaceAim = cfg.useCqb = cfg.useContactDrill = cfg.useSpaceAwareness = cfg.useRoomGraph = cfg.useHitCommit = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useSteadyMoves = arms[a].on;
                    }
                    else if (kind == "cqb")
                    {
                        cfg.useSpaceAwareness = cfg.useRoomGraph = cfg.useHitCommit = cfg.useContactDrill = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useCqb = cfg.useSpaceAim = arms[a].on;
                    }
                    else if (kind == "indoor")
                    {
                        cfg.useHitCommit = cfg.useContactDrill = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useSpaceAwareness = arms[a].on;
                    }
                    else if (kind == "hits")
                    {
                        cfg.useContactDrill = cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useHitCommit = arms[a].on;
                    }
                    else if (kind == "contact")
                    {
                        cfg.useInfoEngagement = cfg.useFightingCover = cfg.useMoveDoctrine = cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useContactDrill = arms[a].on;
                    }
                    else if (kind == "hide")
                    {
                        cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useInfoEngagement = arms[a].on;
                    }
                    else if (kind == "cover")
                    {
                        cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
                        cfg.useCoverRepertoire = cfg.useAmmoBudget = arms[a].on;
                    }
                    else cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = arms[a].on;
                    Clear();
                    yield return null;
                    Random.InitState(seed);
                    if (kind == "search") yield return SearchTrial(dir, arms[a], t);
                    else if (kind == "cover") yield return CoverTrial(dir, arms[a], t);
                    else if (kind == "hide") yield return HideTrial(dir, arms[a], t);
                    else if (kind == "contact") yield return ContactTrial(dir, arms[a], t);
                    else if (kind == "hits") yield return HitTrial(dir, arms[a], t);
                    else if (kind == "indoor") yield return IndoorTrial(dir, arms[a], t);
                    else if (kind == "cqb" || kind == "hall") yield return CqbTrial(dir, arms[a], t);
                    else if (kind == "range") yield return RangeTrial(dir, arms[a], t);
                    else if (kind == "peek") yield return PeekSpamTrial(dir, arms[a], t);
                    else if (kind == "doorpeek") yield return PeekSpamTrial(dir, arms[a], t, true);
                    else if (kind == "peekrapid") yield return PeekSpamTrial(dir, arms[a], t, false, true);
                    else if (kind == "prefire") yield return PeekSpamTrial(dir, arms[a], t, t % 2 == 1, true);
                    else yield return DistanceTrial(dir, arms[a], t);
                }
                TrialsDone = t + 1;
            }
            cfg.useCoverRepertoire = cfg.useAmmoBudget = cfg.useCognitiveFilters = cfg.useTrust = cfg.fuseReports = cfg.usePreAim = cfg.useAttention = cfg.useStandoff = cfg.useOccupancySearch = true;
            Clear();
            Time.timeScale = oldScale;

            var sb = new StringBuilder();
            sb.AppendLine($"=== AI experiment: {kind}, {TrialsDone} of {trials} trial pairs{(cut ? " (stopped at the time cap)" : "")} ===");
            foreach (var arm in arms)
            {
                sb.AppendLine(arm.name);
                if (kind == "peek" || kind == "doorpeek" || kind == "peekrapid" || kind == "prefire")
                {
                    if (kind == "prefire") sb.AppendLine($"  rounds already arriving when he steps out (first 0.15 s): {Stat(arm.pkEarly)} per peek");
                    if (kind == "peekrapid" || kind == "prefire") sb.AppendLine($"  the enemy being hit fired back during the peek: {Stat(arm.pkFocus, 100f)} % of peeks");
                    if (kind == "doorpeek")
                    {
                        sb.AppendLine($"  geometry check: hide spot hidden from {Stat(arm.pkHidden, 100f)} % of them, peek spot seen by {Stat(arm.pkSeenOut, 100f)} %");
                        var l = new List<KeyValuePair<string, int>>(DoorPeekActs); l.Sort((x, y) => y.Value.CompareTo(x.Value));
                        sb.Append("  what they do:"); int shown = 0; foreach (var kv in l) { if (!kv.Key.StartsWith(arm.on ? "on " : "off ")) continue; sb.Append("\n    " + kv.Value + "  " + kv.Key); if (++shown >= 10) break; } sb.AppendLine();
                    }
                    sb.AppendLine($"  peeks answered with fire while he's out: {Stat(arm.pkAnswered, 100f)} %");
                    sb.AppendLine($"  first enemy shot after he steps out: {Stat(arm.pkLatency)} s (1.0 = none while out)");
                    sb.AppendLine($"  hits on him per peek: {Stat(arm.pkHits)}");
                    sb.AppendLine($"  rounds on his peek spot while he's hidden, per peek: {Stat(arm.pkDeny)}");
                }
                else if (kind == "range")
                {
                    sb.AppendLine($"  first shot back: {Stat(arm.rgFirst)} s (20 = never)");
                    sb.AppendLine($"  firing while he's in their line of sight: {Stat(arm.rgFiring, 100f)} %");
                    sb.AppendLine($"  gun lowered (dashing) while he's in their line of sight: {Stat(arm.rgLowered, 100f)} %");
                    sb.AppendLine($"  moved away from him: {Stat(arm.rgAway)} m (mean change in distance)");
                    sb.AppendLine($"  mid-walk reversals (135+ deg within 1 s): {Stat(arm.rgRev, 100f)} % of moving half-seconds");
                    sb.AppendLine($"  walking backwards while not seeing him: {Stat(arm.rgBack, 100f)} % of moving half-seconds");
                    var l = new List<KeyValuePair<string, int>>(RangeWhy); l.Sort((x, y) => y.Value.CompareTo(x.Value));
                    sb.Append("  in sight but not firing:"); int shown = 0; foreach (var kv in l) { if (!kv.Key.StartsWith(arm.on ? "on " : "off ")) continue; sb.Append("\n    " + kv.Value + "  " + kv.Key); if (++shown >= 10) break; } sb.AppendLine();
                }
                else if (kind == "hall")
                {
                    sb.AppendLine($"  indoors: walking backwards while not seeing him: {Stat(arm.rgBack, 100f)} % of moving half-seconds");
                    sb.AppendLine($"  indoors: mid-walk reversals: {Stat(arm.rgRev, 100f)} % of moving half-seconds");
                    sb.AppendLine($"  time until someone sees him: {Stat(arm.cqFind)} s (30 = never)");
                }
                else if (kind == "cqb")
                {
                    sb.AppendLine($"  time until someone sees him: {Stat(arm.cqFind)} s (30 = never)");
                    sb.AppendLine($"  operatives that entered his room: {Stat(arm.cqEntered, 100f)} %");
                    sb.AppendLine($"  distinct doors used to enter: {Stat(arm.cqDoors)}");
                    sb.AppendLine($"  entries through the clearing procedure: {Stat(arm.cqProper, 100f)} %");
                    sb.AppendLine($"  unseen aim into a wall within 1.5 m (out of cover): {Stat(arm.cqWall, 100f)} %, into a nearby ceiling or floor (over 25 deg, surface within 2.5 m): {Stat(arm.cqPitch, 100f)} %");
                    sb.AppendLine($"  player upstairs: unseen aim into a nearby ceiling or floor: {Stat(arm.cqUpPitch, 100f)} %");
                    if (arm.on) sb.AppendLine($"  hidden spot: seen from {string.Join(",", HiddenVis)} of the outside probes");
                    if (arm.on) { var l = new List<KeyValuePair<string, int>>(CqbPhases); l.Sort((x, y) => y.Value.CompareTo(x.Value)); sb.Append("  phases seen (samples):"); foreach (var kv in l) sb.Append(" " + kv.Key + " " + kv.Value); sb.AppendLine(); }
                }
                else if (kind == "indoor")
                {
                    sb.AppendLine($"  distance to the player: {Stat(arm.inDist)} m");
                    sb.AppendLine($"  face to face (under 2 m): {Stat(arm.inFace, 100f)} % of op-time");
                    sb.AppendLine($"  left the building (outdoor node): {Stat(arm.inLeft, 100f)} % of op-time");
                    sb.AppendLine($"  firing with eyes on him: {Stat(arm.inFiring, 100f)} % of in-sight op-time");
                }
                else if (kind == "hits")
                {
                    sb.AppendLine($"  ducked on a hit (close, 9 m): {Stat(arm.duckCqb, 100f)} % of hits while fighting or peeking");
                    sb.AppendLine($"  ducked on a hit (mid, 22 m): {Stat(arm.duckFar, 100f)} % of hits while fighting or peeking");
                    sb.AppendLine($"  duck loops (3+ ducks within 20 s): {Stat(arm.loops, 100f)} % of operatives");
                    sb.AppendLine($"  close range: shots fired {Stat(arm.shotsCqb)} per op-minute, hits taken {Stat(arm.hitsCqb)} per op-minute");
                }
                else if (kind == "contact")
                {
                    sb.AppendLine($"  stalking / investigating / searching under fire: {Stat(arm.sneaky, 100f)} % of op-time");
                    sb.AppendLine($"  first shot back: {Stat(arm.firstShot)} s after the first shot at them (10 = never)");
                    sb.AppendLine($"  in cover 6 s after contact: {Stat(arm.inCover6, 100f)} % of operatives");
                    sb.AppendLine($"  visible to the player: {Stat(arm.exposedC, 100f)} % of op-time");
                }
                else if (kind == "hide")
                {
                    sb.AppendLine($"  passive (holding cover on stale info): {Stat(arm.passive, 100f)} % of op-time");
                    sb.AppendLine($"  active (peeking, flanking, advancing, searching, stalking): {Stat(arm.active, 100f)} % of op-time");
                    sb.AppendLine($"  time until someone sees him again: {Stat(arm.resight)} s (25 = never)");
                }
                else if (kind == "cover")
                {
                    sb.AppendLine($"  visible to the player: {Stat(arm.exposed, 100f)} % of the time");
                    sb.AppendLine($"  hits taken: {Stat(arm.hits)} per op-minute");
                    sb.AppendLine($"  while peeking: visible {Stat(arm.peekExposed, 100f)} % of the time, hit {Stat(arm.peekHits)} per peeking-minute");
                    sb.AppendLine($"  belief age while in cover (freshness, lower is better): {Stat(arm.freshness)} s");
                    sb.AppendLine($"  shots fired: {Stat(arm.shots)} per op-minute");
                    sb.AppendLine($"  ran dry: {Stat(arm.dry, 100f)} % of operatives");
                    sb.AppendLine($"  cover moves chosen: info {arm.peeks[0]}, blind {arm.peeks[1]}, lean {arm.peeks[2]}, full {arm.peeks[3]}");
                }
                else if (kind == "search")
                {
                    sb.AppendLine($"  time to spot player: {Stat(arm.detect)} s   (30 = not found)");
                    sb.AppendLine($"  useful gaze (4 m+ open view): {Stat(arm.useful, 100f)} %");
                    sb.AppendLine($"  staring at a wall (<1.5 m): {Stat(arm.wall, 100f)} %");
                    sb.AppendLine($"  closest approach before spotting: {Stat(arm.closest)} m");
                }
                else
                {
                    sb.AppendLine($"  time any op within 3 m: {Stat(arm.near3, 100f)} %");
                    sb.AppendLine($"  time any op within 5 m: {Stat(arm.near5, 100f)} %");
                    sb.AppendLine($"  time a gunman (not the shotgun rusher) within 3 m: {Stat(arm.gun3, 100f)} %");
                    sb.AppendLine($"  median op distance: {Stat(arm.median)} m");
                    sb.AppendLine($"  aim off target when re-acquiring sight: {Stat(arm.acquire)} deg (per sighting)");
                    sb.AppendLine($"    of which coming out of cover (peek/hold): {Stat(arm.acquireCover)} deg");
                    sb.AppendLine($"  out of sight: aim source switches per op-minute: {Stat(arm.switches)}");
                    sb.AppendLine($"  out of sight: aim reversals (swing back and forth) per op-minute: {Stat(arm.reversals)}");
                    sb.AppendLine($"  while a squadmate sees him: belief movement {Stat(arm.beliefJump)} m/s (the player stands still, so ideally 0)");
                    sb.AppendLine($"  while a squadmate sees him: belief error {Stat(arm.beliefErr)} m");
                }
            }
            Report = sb.ToString();
            Debug.Log(Report);
            Finish();
        }

        static string Stat(List<float> v, float scale = 1f)
        {
            if (v.Count == 0) return "n/a";
            float mean = 0f; foreach (var x in v) mean += x; mean /= v.Count;
            float var = 0f; foreach (var x in v) var += (x - mean) * (x - mean);
            float sd = v.Count > 1 ? Mathf.Sqrt(var / (v.Count - 1)) : 0f;
            var sorted = new List<float>(v); sorted.Sort();
            return $"mean {mean * scale:0.0} ± {sd * scale:0.0} (median {sorted[sorted.Count / 2] * scale:0.0}, n={v.Count})";
        }

        static void Clear()
        {
            foreach (var s in Squad.All.ToArray()) if (s != null) Destroy(s.gameObject);
            foreach (var o in Operative.All.ToArray()) if (o != null) Destroy(o.gameObject);
        }

        static void PlacePlayer(Vector3 pos, float yaw)
        {
            var p = PlayerTarget.Instance;
            var cc = p.GetComponent<CharacterController>();
            cc.enabled = false;
            p.transform.position = pos;
            cc.enabled = true;
            var pc = p.GetComponent<Alcax.Player.PlayerController>();
            var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            typeof(Alcax.Player.PlayerController).GetField("yaw", f)?.SetValue(pc, yaw);
        }

        static List<Operative> SpawnAt(PlaygroundDirector dir, string[] names, Vector3 at, int factionIndex)
        {
            var admin = AdminTools.Instance;
            var squad = admin.NewSquad();
            var factions = admin.Factions;
            var list = new List<Operative>();
            for (int i = 0; i < names.Length; i++)
            {
                var op = admin.SpawnOne(admin.Find(names[i]), at + new Vector3((i % 2) * 1.5f, 0f, (i / 2) * 1.5f), squad, null, factions[factionIndex]);
                if (op != null) list.Add(op);
            }
            return list;
        }

        // Hiding spots inside the house (room corners and the upper floor)
        static readonly Vector3[] Hides =
        {
            new(-38.6f, 0.1f, -4.8f), new(-38.6f, 0.1f, 0.2f), new(-25.2f, 0.1f, -4.8f), new(-38.5f, 3.7f, -4.7f), new(-25.5f, 3.7f, 4.6f), new(-34.5f, 3.7f, 2.2f),
        };

        IEnumerator SearchTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            var hide = Hides[trial % Hides.Length];
            PlacePlayer(hide, Random.Range(0f, 360f));
            yield return null;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Rookie", "Veteran" }, new Vector3(-16f, 0f, -12f), trial % 2);
            yield return null;
            var noise = new Vector3(-32f, 0f, 0f) + Random.insideUnitSphere.Flat() * 4f;
            foreach (var op in ops)
            {
                op.Perception.Awareness = op.Config.alertAt + 0.05f;
                op.Perception.State = Alertness.Alert;
                op.Perception.NoisePosition = noise;
                op.Perception.NoiseTime = Time.time;
                op.Squad?.MapObserve(noise, 5f, Time.time);
            }
            float start = Time.time, detect = 30f, closest = 999f;
            int samples = 0, useful = 0, wall = 0;
            var p = PlayerTarget.Instance;
            while (Time.time - start < 30f)
            {
                bool seen = false;
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    if (op.Perception.CanSee) seen = true;
                    closest = Mathf.Min(closest, Vector3.Distance(op.transform.position, p.Position));
                    if (op.Perception.State < Alertness.Alert) continue;
                    samples++;
                    float free = Physics.Raycast(op.EyePosition, op.LookForward, out var hit, 30f, AILayers.World, QueryTriggerInteraction.Ignore) ? hit.distance : 30f;
                    if (free >= 4f) useful++;
                    if (free < 1.5f) wall++;
                }
                if (seen) { detect = Time.time - start; break; }
                yield return new WaitForSeconds(0.2f);
            }
            arm.detect.Add(detect);
            arm.closest.Add(closest);
            if (samples > 0) { arm.useful.Add(useful / (float)samples); arm.wall.Add(wall / (float)samples); }
        }

        /// <summary>
        /// Cover use under fire: the player stands in the yard and a simulated shooter hits whatever part of an operative
        /// it can see (every 0.4 s: 40% chance if only a head shows, 60% otherwise; head hits do more). Measures exposure,
        /// hits taken, information freshness in cover, shots fired, and how many run dry.
        /// </summary>
IEnumerator HideTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            // He was just seen ducking into a hiding spot (believed position near it, 1 s old). Do they stay on him?
            var hide = Hides[trial % Hides.Length];
            PlacePlayer(hide, Random.Range(0f, 360f));
            yield return null;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Rookie", "Veteran" }, new Vector3(-16f, 0f, -12f), trial % 2);
            yield return null;
            foreach (var op in ops)
            {
                op.Perception.ForceCombat();
                op.Perception.Belief.Has = true;
                op.Perception.Belief.Position = hide + Random.insideUnitSphere.Flat() * 2f;
                op.Perception.Belief.Velocity = Vector3.zero;
                op.Perception.Belief.Time = Time.time - 1f;
            }
            var p = PlayerTarget.Instance;
            float start = Time.time, resight = 25f; int n = 0, passive = 0, active = 0;
            while (Time.time - start < 25f)
            {
                bool seen = false;
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    if (op.Perception.CanSee) seen = true;
                    if (Time.time - start < 3f) continue;
                    n++;
                    var a = op.Mind.Current;
                    if (a == OpAction.HoldCover && op.Perception.Belief.Age > 6f) passive++;
                    if (a == OpAction.PeekShoot || a == OpAction.Flank || a == OpAction.Advance || a == OpAction.Search || a == OpAction.Stalk) active++;
                }
                if (seen) { resight = Time.time - start; break; }
                yield return new WaitForSeconds(0.2f);
            }
            arm.resight.Add(resight);
            if (n > 0) { arm.passive.Add(passive / (float)n); arm.active.Add(active / (float)n); }
        }

/// <summary>
        /// Relaxed squad in the open gets shot at: the player opens up from 22 m with near misses on whoever he can see
        /// (every 0.6 s). Do they return fire or get to cover, or wander and sneak around in the open?
        /// </summary>
/// <summary>
        /// Trading hits: a simulated shooter hits whatever part of an operative it can see (every 0.5 s, 50%), with the squad
        /// spawned 9 m away (even trials) or 22 m (odd). Counts whether hits while fighting or peeking make them duck, and duck loops.
        /// </summary>
/// <summary>
        /// A fight inside the building: player on an indoor node, squad spawned on another indoor node 5 to 9 m away.
        /// Measures the distance they keep, face-to-face time, leaving the building to make room, and firing while in sight.
        /// </summary>
        public static readonly Dictionary<string, int> OutdoorWhy = new Dictionary<string, int>();
        public static readonly Dictionary<string, int> CqbPhases = new Dictionary<string, int>();
        public static readonly List<int> HiddenVis = new List<int>();
        public static readonly Dictionary<string, int> RangeWhy = new Dictionary<string, int>();

        /// <summary>
        /// Ranged contact: an unaware squad 25, 35, or 45 m away in the yard; the player stands in view and fires at
        /// whoever he can see (near misses every 0.5 s, a hit every third). Do they fight back at range, or run and hide?
        /// </summary>
/// <summary>
        /// Peek spam: the player hides behind a tall crate and steps out to the side for 1.0 s every 2.4 s, hitting a
        /// visible operative every 0.4 s while out. Measures how fast and how often they answer each peek.
        /// </summary>
public static readonly Dictionary<string, int> DoorPeekActs = new Dictionary<string, int>();

        static List<Operative> SpawnLine(PlaygroundDirector dir, string[] names, Vector3[] at, int factionIndex)
        {
            var admin = AdminTools.Instance;
            var squad = admin.NewSquad();
            var factions = admin.Factions;
            var list = new List<Operative>();
            for (int i = 0; i < names.Length && i < at.Length; i++)
            {
                var op = admin.SpawnOne(admin.Find(names[i]), at[i], squad, null, factions[factionIndex]);
                if (op != null) list.Add(op);
            }
            return list;
        }

        IEnumerator PeekSpamTrial(PlaygroundDirector dir, Arm arm, int trial, bool door = false, bool rapid = false)
        {
            Vector3 hide, peek; List<Operative> ops;
            if (door)
            {
                // The house's east door (x -24, opening z -0.7..0.7). Even trials: he's inside peeking out at a squad to the
                // south-east. Odd trials: he's outside beside the door peeking in at a squad in the corridor.
                bool inside = trial % 2 == 0;
                hide = inside ? new Vector3(-24.6f, 0.15f, -0.8f) : new Vector3(-23.3f, 0.1f, 1.0f);
                peek = inside ? new Vector3(-24.6f, 0.15f, 0f) : new Vector3(-23.3f, 0.1f, 0.2f);
                PlacePlayer(hide, inside ? 90f : -90f);
                yield return null;
                var spots = inside
                    ? new[] { new Vector3(-12f, 0f, -6f), new Vector3(-13.5f, 0f, -7.5f), new Vector3(-11f, 0f, -8f), new Vector3(-14f, 0f, -5f) }
                    : new[] { new Vector3(-34f, 0.1f, 0f), new Vector3(-36f, 0.1f, -0.3f), new Vector3(-38f, 0.1f, 0.3f), new Vector3(-35f, 0.1f, 0.4f) };
                for (int i = 0; i < spots.Length; i++) if (NavMesh.SamplePosition(spots[i], out var sh, 1.5f, NavMesh.AllAreas)) spots[i] = sh.position;
                ops = SpawnLine(dir, new[] { "Rifleman", "Rifleman", "Veteran", "Rookie" }, spots, (trial / 2) % 2);
            }
            else
            {
                var crates = new[] { new Vector3(-4f, 0f, -31f), new Vector3(8f, 0f, -32f), new Vector3(-11f, 0f, -38f) };
                var c = crates[trial % crates.Length];
                float side = trial % 2 == 0 ? 1f : -1f;
                hide = c + new Vector3(0f, 0.1f, -1.6f);
                peek = hide + new Vector3(side * 1.75f, 0f, 0f);
                PlacePlayer(hide, 0f);
                yield return null;
                var from = c + new Vector3(Random.Range(-3f, 3f), 0f, 17f);
                if (NavMesh.SamplePosition(from, out var h, 5f, NavMesh.AllAreas)) from = h.position;
                ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Veteran", "Rookie" }, from, trial % 2);
            }
            yield return null;
            foreach (var op in ops) op.Perception.ForceCombat();
            yield return new WaitForSeconds(3f);   // they settle in
            var p = PlayerTarget.Instance;
            var lastHit = new Dictionary<Operative, float>(); var lastShots = new Dictionary<Operative, int>();
            foreach (var op in ops) { lastHit[op] = op.Mind.LastHitOnPlayer; lastShots[op] = op.Weapon.ShotsFired; }
            int peeks = 0, answered = 0, hits = 0, denyRounds = 0, focusAnswered = 0, early = 0; float latSum = 0f;
            foreach (var op in ops) { if (op != null && op.Squad != null) { op.Squad.NoteSighting(new Vector3(9999f, 0f, 9999f)); break; } }   // reset the rhythm
            if (door)
            {
                int hid = 0, seen = 0, n0 = 0;
                foreach (var op in ops)
                {
                    if (op == null) continue; n0++;
                    if (Physics.Linecast(op.EyePosition, hide + Vector3.up * 1.5f, AILayers.World, QueryTriggerInteraction.Ignore)) hid++;
                    if (!Physics.Linecast(op.EyePosition, peek + Vector3.up * 1.5f, AILayers.World, QueryTriggerInteraction.Ignore)) seen++;
                }
                arm.pkHidden.Add(hid / (float)Mathf.Max(1, n0)); arm.pkSeenOut.Add(seen / (float)Mathf.Max(1, n0));
            }
            for (int cycle = 0; cycle < 10; cycle++)
            {
                PlacePlayer(peek, 0f);
                peeks++;
                float t0 = Time.time, first = 1f, nextShot = Time.time + 0.2f;
                Operative focus = null; int focusShots0 = 0;
                while (Time.time - t0 < 1.0f)
                {
                    var eye = p.HeadPosition;
                    foreach (var op in ops)
                    {
                        if (op == null || op.IsDead) continue;
                        if (op.Weapon.ShotsFired > lastShots[op] && Time.time - t0 < 0.15f) early += op.Weapon.ShotsFired - lastShots[op];
                        if (op.Weapon.ShotsFired > lastShots[op] && op.Perception.CanSee && first >= 1f) first = Time.time - t0;
                        if (door) { var k = (arm.on ? "on " : "off ") + "out: " + op.Mind.Current + (op.Perception.CanSee ? " SEES" : "") + (op.HoldFire != "" && op.Perception.CanSee ? " hold " + (op.HoldFire.StartsWith("aim off") ? "aim off" : op.HoldFire) : ""); DoorPeekActs[k] = (DoorPeekActs.TryGetValue(k, out var c2) ? c2 : 0) + 1; }
                        lastShots[op] = op.Weapon.ShotsFired;
                        if (op.Mind.LastHitOnPlayer > lastHit[op]) { hits++; lastHit[op] = op.Mind.LastHitOnPlayer; }
                    }
                    if (Time.time >= nextShot)
                    {
                        nextShot = Time.time + (rapid ? 0.12f : 0.4f);
                        Hearing.Emit(eye, 90f, NoiseKind.Gunshot, p.gameObject);
                        if (rapid && (focus == null || focus.IsDead || Physics.Linecast(eye, focus.ChestPosition, AILayers.World, QueryTriggerInteraction.Ignore))) { focus = null; foreach (var op in ops) if (op != null && !op.IsDead && !Physics.Linecast(eye, op.ChestPosition, AILayers.World, QueryTriggerInteraction.Ignore)) { focus = op; focusShots0 = op.Weapon.ShotsFired; break; } }
                        if (rapid) { if (focus != null) focus.Health.TakeDamage(new Alcax.Combat.DamageInfo { Amount = 3f, Point = focus.ChestPosition, Direction = (focus.ChestPosition - eye).normalized, Source = p.gameObject }); }
                        else foreach (var op in ops)
                            if (op != null && !op.IsDead && !Physics.Linecast(eye, op.ChestPosition, AILayers.World, QueryTriggerInteraction.Ignore))
                            { op.Health.TakeDamage(new Alcax.Combat.DamageInfo { Amount = 6f, Point = op.ChestPosition, Direction = (op.ChestPosition - eye).normalized, Source = p.gameObject }); break; }
                    }
                    yield return new WaitForSeconds(0.05f);
                }
                if (first < 1f) answered++;
                if (rapid && focus != null && focus.Weapon.ShotsFired > focusShots0) focusAnswered++;
                latSum += first;
                PlacePlayer(hide, 0f);
                float t1 = Time.time;
                float hideFor = Random.Range(1.1f, 1.7f);
                while (Time.time - t1 < hideFor)
                {
                    foreach (var op in ops)
                    {
                        if (op == null || op.IsDead) continue;
                        if (op.Weapon.ShotsFired > lastShots[op] && !op.Perception.CanSee) denyRounds += op.Weapon.ShotsFired - lastShots[op];
                        if (door) { var k = (arm.on ? "on " : "off ") + "hidden: " + op.Mind.Current + (op.CqbState != "" ? " (" + op.CqbState.Split(' ')[0] + ")" : ""); DoorPeekActs[k] = (DoorPeekActs.TryGetValue(k, out var c1) ? c1 : 0) + 1; }
                        lastShots[op] = op.Weapon.ShotsFired;
                        if (op.Mind.LastHitOnPlayer > lastHit[op]) lastHit[op] = op.Mind.LastHitOnPlayer;
                    }
                    yield return new WaitForSeconds(0.05f);
                }
            }
            arm.pkAnswered.Add(answered / (float)peeks);
            arm.pkEarly.Add(early / (float)peeks);
            if (rapid) arm.pkFocus.Add(focusAnswered / (float)peeks);
            arm.pkLatency.Add(latSum / peeks);
            arm.pkHits.Add(hits / (float)peeks);
            arm.pkDeny.Add(denyRounds / (float)peeks);
        }

        IEnumerator RangeTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            float range = new[] { 25f, 35f, 45f }[trial % 3];
            var at = new Vector3(trial % 2 == 0 ? -4f : 4f, 0.1f, -44f);
            PlacePlayer(at, 0f);
            yield return null;
            var from = at + new Vector3(Random.Range(-6f, 6f), 0f, range);
            if (NavMesh.SamplePosition(from, out var h, 6f, NavMesh.AllAreas)) from = h.position;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Rookie", "Veteran" }, from, trial % 2);
            yield return new WaitForSeconds(0.5f);
            var p = PlayerTarget.Instance;
            int rev0 = Operative.ReversalCount, mv0 = Operative.MoveTicks, bk0 = Operative.BackTicks;
            var d0 = new Dictionary<Operative, float>(); var last = new Dictionary<Operative, int>();
            foreach (var op in ops) { d0[op] = Vector3.Distance(op.transform.position, at); last[op] = op.Weapon.ShotsFired; }
            float start = Time.time, first = 20f, next = Time.time; int n = 0, firing = 0, lowered = 0, k = 0;
            while (Time.time - start < 20f)
            {
                var eye = p.HeadPosition;
                bool shoot = Time.time >= next;
                if (shoot) { next = Time.time + 0.5f; k++; Hearing.Emit(eye, 90f, NoiseKind.Gunshot, p.gameObject); }
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    bool los = !Physics.Linecast(eye, op.EyePosition, AILayers.World, QueryTriggerInteraction.Ignore);
                    if (shoot && los)
                    {
                        if (k % 3 == 0) op.Health.TakeDamage(new Alcax.Combat.DamageInfo { Amount = 7f, Point = op.ChestPosition, Direction = (op.ChestPosition - eye).normalized, Source = p.gameObject });
                        else op.OnNearMiss(0.6f, eye);
                    }
                    bool fired = op.Weapon.ShotsFired > last[op];
                    last[op] = op.Weapon.ShotsFired;
                    if (fired && first >= 20f) first = Time.time - start;
                    if (!los) continue;
                    n++;
                    if (fired || op.Weapon.Reloading) firing++;
                    else
                    {
                        bool low = !op.Body.WeaponRaised;
                        if (low) lowered++;
                        var key = (arm.on ? "on " : "off ") + op.Mind.Current + (low ? " (gun low)" : "") + (op.Perception.CanSee ? "" : " NOT SEEING") + " : " + (op.HoldFire == "" ? "-" : op.HoldFire.StartsWith("aim off") ? "aim off" : op.HoldFire);
                        RangeWhy[key] = (RangeWhy.TryGetValue(key, out var c0) ? c0 : 0) + 1;
                    }
                }
                yield return new WaitForSeconds(0.1f);
            }
            float away = 0f; int m = 0;
            foreach (var op in ops) if (op != null && !op.IsDead) { away += Vector3.Distance(op.transform.position, at) - d0[op]; m++; }
            arm.rgFirst.Add(first);
            int mv = Operative.MoveTicks - mv0;
            if (mv > 0) { arm.rgRev.Add((Operative.ReversalCount - rev0) / (float)mv); arm.rgBack.Add((Operative.BackTicks - bk0) / (float)mv); }
            if (n > 0) { arm.rgFiring.Add(firing / (float)n); arm.rgLowered.Add(lowered / (float)n); }
            if (m > 0) arm.rgAway.Add(away / m);
        }
        public static readonly Dictionary<string, int> WallAim = new Dictionary<string, int>();

        /// <summary>
        /// Room clearing: the player stands in one of the house's ground-floor rooms; a squad outside knows which room.
        /// Measures time to find him, how many go in and through how many doors, and how many entries used the procedure.
        /// </summary>
        IEnumerator CqbTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            var rooms = new List<CombatRoom>();
            foreach (var r in RoomGraph.Rooms) if (r != null && !r.roomName.StartsWith("Upper") && !r.roomName.StartsWith("Corridor")) rooms.Add(r);
            if (rooms.Count == 0) yield break;
            rooms.Sort((x, y) => string.CompareOrdinal(x.roomName, y.roomName));
            var room = rooms[trial % rooms.Count];
            bool upstairs = trial % 2 == 1;
            if (upstairs) foreach (var r in RoomGraph.Rooms) if (r != null && r.roomName.StartsWith("Upper")) room = r;
            // The occupant tucks into the spot least visible from outside the house and from beyond the room's doorways
            var at = room.Center; at.y = 0.15f;
            var probes = new List<Vector3>();
            var houseC = new Vector3(-32f, 1.6f, 0f);
            for (int k = 0; k < 20; k++) probes.Add(houseC + Quaternion.Euler(0f, k * 18f, 0f) * Vector3.forward * 14f);
            foreach (var d in RoomGraph.Doors)
                if (d != null && d.Connects(room)) { var into = (room.Center - d.Position).Flat().normalized; probes.Add(d.Position - into * 2.5f + Vector3.up * 1.6f); }
            int bestVis = int.MaxValue;
            var hsz = room.size * 0.5f;
            for (float gx = -hsz.x + 0.6f; gx <= hsz.x - 0.6f; gx += 0.6f)
                for (float gz = -hsz.z + 0.6f; gz <= hsz.z - 0.6f; gz += 0.6f)
                {
                    var q = new Vector3(room.Center.x + gx, 0.1f, room.Center.z + gz);
                    if (!NavMesh.SamplePosition(q, out var qh, 0.3f, NavMesh.AllAreas)) continue;
                    int vis = 0;
                    foreach (var pr in probes) if (!Physics.Linecast(pr, qh.position + Vector3.up * 1.5f, AILayers.World, QueryTriggerInteraction.Ignore)) vis++;
                    if (vis < bestVis) { bestVis = vis; at = qh.position + Vector3.up * 0.1f; }
                }
            HiddenVis.Add(bestVis);
            if (upstairs && NavMesh.SamplePosition(new Vector3(-28f, 3.7f, -3f), out var uh, 1.5f, NavMesh.AllAreas)) at = uh.position + Vector3.up * 0.1f;
            PlacePlayer(at, Random.Range(0f, 360f));
            yield return null;
            var starts = new[] { new Vector3(-18f, 0f, 0f), new Vector3(-30f, 0f, -11f), new Vector3(-27f, 0f, 10f) };
            var from = starts[(trial / rooms.Count + trial) % starts.Length];
            if (NavMesh.SamplePosition(from, out var fh, 3f, NavMesh.AllAreas)) from = fh.position;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Veteran", "Rookie" }, from, 1);
            yield return null;
            foreach (var op in ops)
            {
                op.Perception.ForceCombat();
                op.Perception.Belief.Position = room.Center + Random.insideUnitSphere.Flat() * 1f;
                op.Perception.Belief.Position = new Vector3(op.Perception.Belief.Position.x, at.y - 0.1f, op.Perception.Belief.Position.z);
                op.Perception.Belief.Velocity = Vector3.zero;
                op.Perception.Belief.Time = Time.time - 2f;
            }
            var p = PlayerTarget.Instance;
            float start = Time.time, find = 30f;
            var entered = new HashSet<Operative>(); var doors = new HashSet<string>(); int proper = 0, entries = 0;
            int rev0 = Operative.ReversalCount, mv0 = Operative.MoveTicks, bk0 = Operative.BackTicks;
            var wasIn = new Dictionary<Operative, bool>();
            int aimN = 0, aimWall = 0, aimPitch = 0;
            foreach (var op in ops) wasIn[op] = false;
            while (Time.time - start < 30f)
            {
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    if (find >= 30f && op.Perception.CanSee) find = Time.time - start;
                    if (arm.on && op.CqbState != "") { var ph2 = op.CqbState.Split(' ')[0]; CqbPhases[ph2] = (CqbPhases.TryGetValue(ph2, out var c0) ? c0 : 0) + 1; }
                    if (!op.Perception.CanSee && op.Perception.State == Alertness.Combat && !op.InCover)   // behind cover the gun points at the cover by design
                    {
                        aimN++;
                        var ad = op.AimForward;
                        if (Physics.Raycast(op.EyePosition, ad, 1.5f, AILayers.World, QueryTriggerInteraction.Ignore)) { aimWall++; var wk = (arm.on ? "on " : "off ") + op.AimSourceName + " " + op.Mind.Current; WallAim[wk] = (WallAim.TryGetValue(wk, out var w0) ? w0 : 0) + 1; }
                        if (Mathf.Abs(Mathf.Asin(Mathf.Clamp(ad.normalized.y, -1f, 1f)) * Mathf.Rad2Deg) > 25f && Physics.Raycast(op.EyePosition, ad, 2.5f, AILayers.World, QueryTriggerInteraction.Ignore)) aimPitch++;   // steep AND into a nearby surface
                    }
                    bool inside = room.Contains(op.transform.position + Vector3.up * 0.5f);
                    if (inside && !wasIn[op])
                    {
                        entries++;
                        entered.Add(op);
                        if (op.Mind.Current == OpAction.ClearRoom && op.CqbDoor != null) { proper++; doors.Add(op.CqbDoor.name); }
                        else
                        {
                            // Which door did he come through? The nearest one of this room
                            float bd = 99f; string bn = "?";
                            foreach (var d in RoomGraph.Doors) if (d != null && d.Connects(room)) { float dd = Vector3.Distance(d.Position, op.transform.position); if (dd < bd) { bd = dd; bn = d.name; } }
                            doors.Add(bn);
                        }
                    }
                    wasIn[op] = inside;
                }
                yield return new WaitForSeconds(0.2f);
            }
            arm.cqFind.Add(find);
            { int mvC = Operative.MoveTicks - mv0; if (mvC > 0) { arm.rgRev.Add((Operative.ReversalCount - rev0) / (float)mvC); arm.rgBack.Add((Operative.BackTicks - bk0) / (float)mvC); } }
            if (upstairs && aimN > 0) arm.cqUpPitch.Add(aimPitch / (float)aimN);
            if (aimN > 0) { arm.cqWall.Add(aimWall / (float)aimN); arm.cqPitch.Add(aimPitch / (float)aimN); }
            arm.cqEntered.Add(entered.Count / (float)Mathf.Max(1, ops.Count));
            arm.cqDoors.Add(doors.Count);
            arm.cqProper.Add(entries > 0 ? proper / (float)entries : 0f);
        }
        IEnumerator IndoorTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            NavGraph.EnsureBuilt();
            var indoor = new List<int>();
            for (int i = 0; i < NavGraph.Count; i++) if (NavGraph.Space[i].Indoor) indoor.Add(i);
            if (indoor.Count < 10) yield break;
            var rng = new System.Random(trial * 7919 + 13);
            Vector3 at = NavGraph.Pos[indoor[rng.Next(indoor.Count)]], from = at;
            for (int k = 0; k < 60; k++)
            {
                var c = NavGraph.Pos[indoor[rng.Next(indoor.Count)]];
                float d = Vector3.Distance(c, at);
                if (d > 5f && d < 9f && Mathf.Abs(c.y - at.y) < 1f) { from = c; break; }
            }
            PlacePlayer(at + Vector3.up * 0.1f, Random.Range(0f, 360f));
            yield return null;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Rookie", "Veteran" }, from, trial % 2);
            yield return null;
            foreach (var op in ops) op.Perception.ForceCombat();
            var p = PlayerTarget.Instance;
            var startShots = new Dictionary<Operative, int>();
            float start = Time.time, distSum = 0f; int n = 0, face = 0, left = 0, seenN = 0, firingN = 0;
            var lastShots = new Dictionary<Operative, int>();
            foreach (var op in ops) lastShots[op] = op.Weapon.ShotsFired;
            while (Time.time - start < 20f)
            {
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    float d = Vector3.Distance(op.transform.position, p.Position);
                    distSum += d; n++;
                    if (d < 2f) face++;
                    if (!NavGraph.SpaceAt(op.transform.position).Indoor) { left++; var k = (arm.on ? "on " : "off ") + op.Mind.Current + " " + op.Note; OutdoorWhy[k] = (OutdoorWhy.TryGetValue(k, out var c0) ? c0 : 0) + 1; }
                    if (op.Perception.CanSee)
                    {
                        seenN++;
                        if (op.Weapon.ShotsFired > lastShots[op] || op.Weapon.Reloading) firingN++;
                    }
                    lastShots[op] = op.Weapon.ShotsFired;
                }
                yield return new WaitForSeconds(0.5f);
            }
            if (n > 0) { arm.inDist.Add(distSum / n); arm.inFace.Add(face / (float)n); arm.inLeft.Add(left / (float)n); }
            if (seenN > 0) arm.inFiring.Add(firingN / (float)seenN);
        }

        IEnumerator HitTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            bool cqb = trial % 2 == 0;
            var spots = new[] { new Vector3(0f, 0.1f, -24f), new Vector3(-6f, 0.1f, -30f), new Vector3(6f, 0.1f, -20f) };
            var at = spots[(trial / 2) % spots.Length];
            PlacePlayer(at, 0f);
            yield return null;
            var from = at + new Vector3(Random.Range(-3f, 3f), 0f, cqb ? 9f : 22f);
            if (NavMesh.SamplePosition(from, out var h, 6f, NavMesh.AllAreas)) from = h.position;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Rookie", "Veteran" }, from, (trial / 2) % 2);
            yield return null;
            foreach (var op in ops) op.Perception.ForceCombat();
            Operative.HitCounts[0] = Operative.HitCounts[1] = 0;
            var p = PlayerTarget.Instance;
            var startShots = new Dictionary<Operative, int>();
            foreach (var op in ops) startShots[op] = op.Weapon.ShotsFired;
            var looped = new HashSet<Operative>();
            float start = Time.time, opSeconds = 0f, nextShot = Time.time + 0.5f; int hits = 0;
            while (Time.time - start < 20f)
            {
                var eye = p.HeadPosition;
                bool shoot = Time.time >= nextShot;
                if (shoot) nextShot = Time.time + 0.5f;
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    opSeconds += 0.1f;
                    if (op.RecentDucks >= 3) looped.Add(op);
                    bool headVis = !Physics.Linecast(eye, op.EyePosition, AILayers.World, QueryTriggerInteraction.Ignore);
                    bool chestVis = !Physics.Linecast(eye, op.ChestPosition, AILayers.World, QueryTriggerInteraction.Ignore);
                    if (shoot && (headVis || chestVis) && Random.value < 0.5f)
                    {
                        hits++;
                        var point = chestVis ? op.ChestPosition : op.EyePosition;
                        op.Health.TakeDamage(new Alcax.Combat.DamageInfo { Amount = 9f, Point = point, Direction = (point - eye).normalized, Source = p.gameObject });
                        Hearing.Emit(eye, 90f, NoiseKind.Gunshot, p.gameObject);
                    }
                }
                yield return new WaitForSeconds(0.1f);
            }
            int dec = Operative.HitCounts[0] + Operative.HitCounts[1];
            float duck = dec > 0 ? Operative.HitCounts[1] / (float)dec : 0f;
            if (dec > 0) { if (cqb) arm.duckCqb.Add(duck); else arm.duckFar.Add(duck); }
            arm.loops.Add(looped.Count / (float)Mathf.Max(1, ops.Count));
            if (cqb)
            {
                int fired = 0;
                foreach (var op in ops) if (op != null) fired += op.Weapon.ShotsFired - startShots[op];
                float minutes = Mathf.Max(0.01f, opSeconds / 60f);
                arm.shotsCqb.Add(fired / minutes);
                arm.hitsCqb.Add(hits / minutes);
            }
        }

        IEnumerator ContactTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            var spots = new[] { new Vector3(0f, 0.1f, -24f), new Vector3(-6f, 0.1f, -30f), new Vector3(6f, 0.1f, -20f) };
            var at = spots[trial % spots.Length];
            PlacePlayer(at, 0f);
            yield return null;
            var from = at + new Vector3(Random.Range(-5f, 5f), 0f, 22f);
            if (NavMesh.SamplePosition(from, out var h, 8f, NavMesh.AllAreas)) from = h.position;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Rookie", "Veteran" }, from, trial % 2);
            yield return new WaitForSeconds(1f);
            var p = PlayerTarget.Instance;
            var startShots = new Dictionary<Operative, int>();
            foreach (var op in ops) startShots[op] = op.Weapon.ShotsFired;
            float start = Time.time, firstShot = 10f, opT = 0f, sneakyT = 0f, expT = 0f, nextShot = Time.time;
            bool covered6 = false; int inCover = 0;
            while (Time.time - start < 10f)
            {
                var eye = p.HeadPosition;
                if (Time.time >= nextShot)
                {
                    nextShot = Time.time + 0.6f;
                    Hearing.Emit(eye, 90f, NoiseKind.Gunshot, p.gameObject);
                    foreach (var op in ops)
                        if (op != null && !op.IsDead && !Physics.Linecast(eye, op.ChestPosition, AILayers.World, QueryTriggerInteraction.Ignore) && Random.value < 0.5f)
                            op.OnNearMiss(0.7f, eye);
                }
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    opT += 0.1f;
                    var a = op.Mind.Current;
                    if (a == OpAction.Stalk || a == OpAction.Investigate || a == OpAction.Search) sneakyT += 0.1f;
                    if (!Physics.Linecast(eye, op.ChestPosition, AILayers.World, QueryTriggerInteraction.Ignore)) expT += 0.1f;
                    if (firstShot >= 10f && op.Weapon.ShotsFired > startShots[op]) firstShot = Time.time - start;
                }
                if (!covered6 && Time.time - start >= 6f)
                {
                    covered6 = true;
                    foreach (var op in ops) if (op != null && !op.IsDead && op.InCover) inCover++;
                }
                yield return new WaitForSeconds(0.1f);
            }
            arm.sneaky.Add(opT > 0f ? sneakyT / opT : 0f);
            arm.firstShot.Add(firstShot);
            arm.inCover6.Add(inCover / (float)Mathf.Max(1, ops.Count));
            arm.exposedC.Add(opT > 0f ? expT / opT : 0f);
        }

        IEnumerator CoverTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            var spots = new[] { new Vector3(0f, 0.1f, -24f), new Vector3(-6f, 0.1f, -30f), new Vector3(6f, 0.1f, -20f) };
            var at = spots[trial % spots.Length];
            PlacePlayer(at, 0f);
            yield return null;
            var from = at + new Vector3(Random.Range(-6f, 6f), 0f, 22f);
            if (NavMesh.SamplePosition(from, out var h, 8f, NavMesh.AllAreas)) from = h.position;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Rifleman", "Rookie", "Veteran" }, from, trial % 2);
            yield return null;
            foreach (var op in ops) op.Perception.ForceCombat();
            for (int i = 0; i < 4; i++) Operative.PeekCounts[i] = 0;
            var p = PlayerTarget.Instance;
            float start = Time.time, opSeconds = 0f, exposedSeconds = 0f, freshSum = 0f; int freshN = 0, hits = 0, peekHitsN = 0; float peekSec = 0f, peekExp = 0f;
            float nextShot = Time.time + 0.4f;
            var startShots = new Dictionary<Operative, int>();
            foreach (var op in ops) startShots[op] = op.Weapon.ShotsFired;
            while (Time.time - start < 24f)
            {
                var eye = p.HeadPosition;
                bool shoot = Time.time >= nextShot;
                if (shoot) nextShot = Time.time + 0.4f;
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    if (Time.time - start < 4f) continue;   // the approach
                    opSeconds += 0.1f;
                    bool headVis = !Physics.Linecast(eye, op.EyePosition, AILayers.World, QueryTriggerInteraction.Ignore);
                    bool chestVis = !Physics.Linecast(eye, op.ChestPosition, AILayers.World, QueryTriggerInteraction.Ignore);
                    bool inRange = Vector3.Distance(eye, op.EyePosition) < 60f;
                    bool peeking = op.Mind.Current == OpAction.PeekShoot;
                    if (peeking) peekSec += 0.1f;
                    if ((headVis || chestVis) && inRange) { exposedSeconds += 0.1f; if (peeking) peekExp += 0.1f; }
                    if (op.Mind.Current == OpAction.PeekShoot || op.Mind.Current == OpAction.HoldCover) { freshSum += op.Perception.Belief.Age; freshN++; }
                    if (shoot && inRange && (headVis || chestVis) && Random.value < (chestVis ? 0.6f : 0.4f))
                    {
                        hits++; if (peeking) peekHitsN++;
                        var point = chestVis ? op.ChestPosition : op.EyePosition;
                        op.Health.TakeDamage(new Alcax.Combat.DamageInfo { Amount = chestVis ? 14f : 30f, Point = point, Direction = (point - eye).normalized, Source = p.gameObject });
                        Hearing.Emit(eye, 90f, NoiseKind.Gunshot, p.gameObject);
                    }
                }
                yield return new WaitForSeconds(0.1f);
            }
            int fired = 0, dryN = 0;
            foreach (var op in ops) { if (op == null) continue; fired += op.Weapon.ShotsFired - (startShots.TryGetValue(op, out var s0) ? s0 : 0); if (!op.IsDead && op.Weapon.Dry) dryN++; }
            float minutes = Mathf.Max(0.01f, opSeconds / 60f);
            arm.exposed.Add(opSeconds > 0f ? exposedSeconds / opSeconds : 0f);
            arm.hits.Add(hits / minutes);
            if (peekSec > 1f) { arm.peekExposed.Add(peekExp / peekSec); arm.peekHits.Add(peekHitsN / (peekSec / 60f)); }
            arm.freshness.Add(freshN > 0 ? freshSum / freshN : 0f);
            arm.shots.Add(fired / minutes);
            arm.dry.Add(dryN / (float)Mathf.Max(1, ops.Count));
            for (int i = 0; i < 4; i++) arm.peeks[i] += Operative.PeekCounts[i];
        }

        IEnumerator DistanceTrial(PlaygroundDirector dir, Arm arm, int trial)
        {
            var spots = new[] { new Vector3(0f, 0.1f, -24f), new Vector3(-30f, 0.1f, 2f), new Vector3(24f, 0.1f, -30f), new Vector3(-38f, 0.1f, -30f) };
            PlacePlayer(spots[trial % spots.Length], Random.Range(0f, 360f));
            yield return null;
            var from = spots[trial % spots.Length] + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * 30f;
            if (NavMesh.SamplePosition(from, out var h, 10f, NavMesh.AllAreas)) from = h.position;
            var ops = SpawnAt(dir, new[] { "Rifleman", "Pointman", "Rifleman", "Rookie" }, from, trial % 2);
            yield return null;
            foreach (var op in ops) op.Perception.ForceCombat();
            var p = PlayerTarget.Instance;
            float start = Time.time;
            int samples = 0, n3 = 0, n5 = 0, g3 = 0;
            var dists = new List<float>();
            var saw = new Dictionary<Operative, bool>();
            var lastSrc = new Dictionary<Operative, string>();
            var lastYaw = new Dictionary<Operative, float>();
            var lastRate = new Dictionary<Operative, float>();
            int switches = 0, reversals = 0; float opSeconds = 0f;
            var lastBelief = new Dictionary<Operative, Vector3>();
            float jumpSum = 0f, errSum = 0f; int jumpN = 0;
            while (Time.time - start < 22f)
            {
                float minD = 999f, minGun = 999f;
                foreach (var op in ops)
                {
                    if (op == null || op.IsDead) continue;
                    // Re-acquisition: how far the weapon is from him at the moment sight returns
                    bool sees = op.Perception.CanSee;
                    if (sees && saw.TryGetValue(op, out var before) && !before && Time.time - start > 5f)
                    {
                        float ang = Vector3.Angle(op.AimHeld, p.AimPoint(1) - op.MuzzlePosition);
                        arm.acquire.Add(ang);
                        if (op.Mind.Current == OpAction.PeekShoot || op.Mind.Current == OpAction.HoldCover) arm.acquireCover.Add(ang);
                    }
                    // Belief stability while a squadmate (not this unit) sees the player: how much the estimate jumps, and how wrong it is
                    bool allySees = false;
                    foreach (var o2 in ops) if (o2 != null && o2 != op && !o2.IsDead && o2.Perception.CanSee) { allySees = true; break; }
                    var bp = op.Perception.Belief.Predicted;
                    if (!sees && allySees && op.Perception.Belief.Has && Time.time - start > 5f)
                    {
                        if (lastBelief.TryGetValue(op, out var lb)) { jumpSum += Vector3.Distance(lb.Flat(), bp.Flat()); jumpN++; errSum += Vector3.Distance(bp.Flat(), p.Position.Flat()); }
                        lastBelief[op] = bp;
                    }
                    else lastBelief.Remove(op);
                    saw[op] = sees;
                    // Aim stability while the target is out of sight
                    if (!sees && Time.time - start > 5f)
                    {
                        opSeconds += 0.1f;
                        string src = op.AimSourceName;
                        if (lastSrc.TryGetValue(op, out var ls) && ls != src) switches++;
                        lastSrc[op] = src;
                        float yaw = Mathf.Atan2(op.AimHeld.x, op.AimHeld.z) * Mathf.Rad2Deg;
                        if (lastYaw.TryGetValue(op, out var ly))
                        {
                            float rate = Mathf.DeltaAngle(ly, yaw) / 0.1f;
                            if (lastRate.TryGetValue(op, out var lr) && Mathf.Abs(rate) > 25f && Mathf.Abs(lr) > 25f && Mathf.Sign(rate) != Mathf.Sign(lr)) reversals++;
                            lastRate[op] = rate;
                        }
                        lastYaw[op] = yaw;
                    }
                    else { lastYaw.Remove(op); lastRate.Remove(op); }
                    float d = Vector3.Distance(op.transform.position, p.Position);
                    minD = Mathf.Min(minD, d);
                    if (!op.Archetype.canRush) minGun = Mathf.Min(minGun, d);
                    dists.Add(d);
                }
                if (Time.time - start > 6f)   // after the approach
                {
                    samples++;
                    if (minD < 3f) n3++;
                    if (minD < 5f) n5++;
                    if (minGun < 3f) g3++;
                }
                yield return new WaitForSeconds(0.1f);
            }
            dists.Sort();
            arm.near3.Add(samples > 0 ? n3 / (float)samples : 0f);
            arm.near5.Add(samples > 0 ? n5 / (float)samples : 0f);
            arm.gun3.Add(samples > 0 ? g3 / (float)samples : 0f);
            if (opSeconds > 1f) { arm.switches.Add(switches / opSeconds * 60f); arm.reversals.Add(reversals / opSeconds * 60f); }
            if (jumpN > 5) { arm.beliefJump.Add(jumpSum / jumpN / 0.1f); arm.beliefErr.Add(errSum / jumpN); }
            arm.median.Add(dists.Count > 0 ? dists[dists.Count / 2] : 0f);
        }
    }
}
