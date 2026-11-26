using System.Collections.Generic;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// A group of operatives. It holds:
    ///  - the radio net (radio factions share sightings squad-wide after a delay; shouting factions only share by voice),
    ///  - who is firing (covering fire makes moving safer), claimed cover spots, and casualties,
    ///  - a shared danger map: spots where members got shot, which cover and routes then avoid ("he's watching that"),
    ///  - a leader (the most skilled and team-minded alive) who reads the fight and gives orders: push, fall back, flank.
    /// </summary>
    public class Squad : MonoBehaviour
    {
        public static readonly List<Squad> All = new();

        public string callsign = "Alpha";
        [SerializeField] CombatAIConfig config;

        public readonly List<Operative> Members = new();
        /// <summary>Where the player probably is, from this squad's senses only.</summary>
        public OccupancyMap Map { get; private set; }

// ---------- Peek rhythm (squad level) ----------
        public Vector3 PeekSpot { get; private set; }
        public int PeekStreak { get; private set; }
        public float PeekGap { get; private set; } = 1.5f;
        public int PeekGapCount { get; private set; }
        float peekLastSeen = -100f;
        /// <summary>Seconds since anyone in the squad saw him.</summary>
        public float SinceSeenBySquad => Time.time - peekLastSeen;

        /// <summary>A member sees him: a fresh appearance near the last spot moments after he vanished extends the peek streak.</summary>
        public void NoteSighting(Vector3 head)
        {
            float gap = Time.time - peekLastSeen;
            if (gap > 0.4f)
            {
                bool same = peekLastSeen > 0f && gap < 6f && Vector3.Distance(head, PeekSpot) < 2f;
                if (same)
                {
                    PeekStreak++;
                    PeekGap = PeekGapCount == 0 ? gap : Mathf.Lerp(PeekGap, gap, 0.4f);
                    PeekGapCount++;
                }
                else { PeekStreak = 0; PeekGapCount = 0; }
            }
            peekLastSeen = Time.time;
            PeekSpot = head;
        }

        // ---------- Peek holder ----------
        Operative peekHolder; Vector3 peekSpot; float peekHolderUntil;

        /// <summary>The squadmate who prefires a peek spot: the one whose muzzle is closest to it with a clear line (rechosen every 0.4 s).</summary>
        public Operative PeekHolder(Vector3 spot)
        {
            // Keep the holder while he still has a line to the spot; otherwise hand it to whoever does
            bool LineTo(Operative m) => !CoverFinder.Blocked(m.MuzzlePosition, spot) || !CoverFinder.Blocked(m.MuzzlePosition, spot - Vector3.up * 0.3f)
                                        || (m.CoverHere == CoverKind.Low && !CoverFinder.Blocked(m.EyePosition + Vector3.up * 0.2f, spot));
            if (peekHolder != null && !peekHolder.IsDead && (spot - peekSpot).sqrMagnitude < 4f && !peekHolder.Weapon.Reloading && !peekHolder.Weapon.Dry
                && (Time.time < peekHolderUntil || LineTo(peekHolder)) && LineTo(peekHolder)) return peekHolder;
            peekHolder = null; float best = float.MaxValue;
            foreach (var m in Members)
            {
                if (m == null || m.IsDead || m.Weapon.Dry || m.Weapon.Reloading) continue;
                var mz = m.MuzzlePosition;
                if (!LineTo(m)) continue;
                float ang = Vector3.Angle(m.AimForward, spot - mz);
                if (ang < best) { best = ang; peekHolder = m; }
            }
            peekSpot = spot; peekHolderUntil = Time.time + 0.4f;
            return peekHolder;
        }

        // ---------- CQB bookkeeping ----------
        readonly Dictionary<CombatRoom, float> cleared = new Dictionary<CombatRoom, float>();
        public void MarkCleared(CombatRoom r) { if (r != null) cleared[r] = Time.time; }
        public bool RecentlyCleared(CombatRoom r) => r != null && cleared.TryGetValue(r, out var t) && Time.time - t < 20f;

        /// <summary>Squadmates (other than me) currently clearing through this door.</summary>
        public int Claimants(CombatDoor d, Operative except, out Operative first)
        {
            first = null; int n = 0;
            foreach (var m in Members)
            {
                if (m == null || m == except || m.IsDead || m.Mind.Current != OpAction.ClearRoom || m.CqbDoor != d) continue;
                if (first == null || m.CqbSince < first.CqbSince) first = m;
                n++;
            }
            return n;
        }
        float nextMapTick, lastObserveStamp = -1f;
        readonly List<(float at, Operative from, Belief b, ICombatTarget about)> pending = new();
        /// <summary>Who the squad's occupancy map is about (the leader's focus, else the first member's).</summary>
        public ICombatTarget MapTarget { get; set; }
        readonly List<(Vector3 p, float t, float w)> danger = new();
        float lastReportTime = -100f, nextOrder;

        public float LastCasualtyTime { get; private set; } = -100f;
        public int Casualties { get; private set; }
        public int StartSize { get; private set; }
        public Operative Leader { get; private set; }
        public string LastOrder { get; private set; } = "";
        /// <summary>Who has been told to flank (only one at a time).</summary>
        public Operative Flanker { get; set; }
        public float FlankSide { get; set; }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Join(Operative op)
        {
            if (Members.Contains(op)) return;
            Members.Add(op);
            StartSize = Mathf.Max(StartSize, Members.Count);
            if (config == null) config = op.Config;
            if (Map == null && UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length > 0) Map = new OccupancyMap();
        }

        /// <summary>Fold an estimate (noise, hit, report) into the map once, however many members perceived it.</summary>
        public void MapObserve(Vector3 pos, float sigma, float stamp)
        {
            if (Map == null || Mathf.Approximately(stamp, lastObserveStamp)) return;
            lastObserveStamp = stamp;
            Map.Observe(pos, sigma);
        }

        void UpdateMap()
        {
            if (Map == null) return;
            // The map is about the squad's focus: the leader's target, else whoever a member is fighting
            if (Leader != null && !Leader.IsDead && Leader.Target != null) MapTarget = Leader.Target;
            else if (MapTarget == null || MapTarget.IsDead) { MapTarget = null; foreach (var m in Members) if (m != null && !m.IsDead && m.Target != null) { MapTarget = m.Target; break; } }
            var p = MapTarget;
            bool seen = false;
            foreach (var m in Members) if (m != null && !m.IsDead && m.Target == p && m.Perception.CanSee) { seen = true; break; }
            if (seen && p != null) Map.Collapse(p.Position);
            else Map.Diffuse(0.2f, config != null ? config.occupancySpread : 3f);
            foreach (var m in Members)
                if (m != null && !m.IsDead && !m.Surrendered && m.Perception.State >= Alertness.Alert) Map.ObserveFrom(m, m.Overloaded ? Mathf.RoundToInt(40f * Mathf.Clamp(m.Capacity / Mathf.Max(0.01f, m.Load), 0.35f, 1f)) : 40);
            Map.Renormalize();
        }

        public void Leave(Operative op) => Members.Remove(op);

        /// <summary>Members still in the fight (alive, not broken, not surrendered).</summary>
        public int Fighting
        {
            get { int n = 0; foreach (var m in Members) if (m != null && !m.IsDead && !m.Broken && !m.Surrendered) n++; return n; }
        }

        public int Alive
        {
            get { int n = 0; foreach (var m in Members) if (m != null && !m.IsDead) n++; return n; }
        }

        public int Firing(Operative except)
        {
            int n = 0;
            foreach (var m in Members)
                if (m != null && m != except && !m.IsDead && Time.time - m.Weapon.LastShotTime < 0.8f) n++;
            return n;
        }

        public int CountDoing(OpAction action, Operative except)
        {
            int n = 0;
            foreach (var m in Members)
                if (m != null && m != except && !m.IsDead && m.Mind.Current == action) n++;
            return n;
        }

        /// <summary>A sighting to share over the radio (radio factions only; shouting goes through Callouts).</summary>
        public void Report(Operative from, Belief b)
        {
            if (Time.time - lastReportTime < 0.5f) return;
            lastReportTime = Time.time;
            float delay = from.Faction != null ? from.Faction.radioDelay : config != null ? config.shareDelay : 0.7f;
            pending.Add((Time.time + delay * Random.Range(0.7f, 1.4f), from, b, from.Perception.Target));
        }

        // ---------- Confidence ----------
        // A squad's mood: a baseline from how the fight stands (numbers left, a leader, the target hurt or reloading)
        // plus momentum from events, which fades back toward the baseline over about half a minute.
        // Losses that come fast hurt far more than the same losses spread out.

        float momentum;
        readonly List<float> lossTimes = new();
        public float Confidence { get; private set; }
        public float Baseline { get; private set; }
        public float Momentum => momentum;
        float nextRally;

        public void AddMomentum(float v) => momentum = Mathf.Clamp(momentum + v, -1.6f, 1f);

        public void OnMemberHurt(Operative op, float frac) => AddMomentum(-0.04f - frac * 0.3f);

        public void OnMemberHitPlayer() => AddMomentum(0.05f);

        void UpdateConfidence(float dt)
        {
            int fighting = Fighting;
            float frac = StartSize > 0 ? fighting / (float)StartSize : 1f;
            bool playerHurt = false, opening = false;
            foreach (var m in Members)
            {
                if (m == null || m.IsDead) continue;
                if (Time.time - m.Mind.LastHitOnPlayer < 4f) playerHurt = true;
                if (m.Mind.ReloadOpportunity) opening = true;
            }
            var lead = Leader;
            float leadBonus = lead != null && !lead.IsDead && !lead.Broken ? 0.04f + lead.Traits.teamwork * 0.08f : -0.05f;
            Baseline = (frac - 0.6f) * 0.55f + Mathf.Clamp(fighting - 1, 0, 7) * 0.035f + leadBonus
                       + (playerHurt ? 0.15f : 0f) + (opening ? 0.1f : 0f);
            momentum = Mathf.MoveTowards(momentum, 0f, dt * 0.028f);
            Confidence = Mathf.Lerp(Confidence, Mathf.Clamp(Baseline + momentum, -1f, 1f), 1f - Mathf.Exp(-1.6f * dt));
        }

        public void ReportDeath(Operative dead)
        {
            Casualties++;
            LastCasualtyTime = Time.time;
            AddDanger(dead.transform.position, 2f);
            lossTimes.Add(Time.time);
            int recent = 0;
            foreach (var t in lossTimes) if (Time.time - t < 15f) recent++;
            bool wasLeader = dead == Leader;
            if (dead.Surrendered) AddMomentum(0.05f);    // killing a man who gave up enrages more than it scares
            else AddMomentum(-(0.15f + 0.08f * (recent - 1)) * (wasLeader ? 1.4f : 1f));
            // Those close enough to see it react now; the rest learn from the "man down" call
            Operative witness = null;
            foreach (var m in Members)
            {
                if (m == null || m == dead || m.IsDead) continue;
                bool sees = Vector3.Distance(m.transform.position, dead.transform.position) < 30f
                            && !CoverFinder.Blocked(m.EyePosition, dead.ChestPosition);
                if (sees || (dead.Faction != null && dead.Faction.radio)) m.NoteAllyDown(dead);
                if (sees && dead.Surrendered) m.Emotion.anger = Mathf.Clamp01(m.Emotion.anger + 0.5f);
                if (sees && witness == null) witness = m;
            }
            if (witness != null) Callouts.Say(witness, wasLeader ? Callout.LeaderDown : Callout.ManDown, null, dead.Callsign, null, dead, force: true);
        }

        /// <summary>True if a squadmate already claimed (or stands at) a spot within the spacing distance.</summary>
        public bool Crowded(Vector3 spot, Operative except, float spacing)
        {
            foreach (var m in Members)
            {
                if (m == null || m == except || m.IsDead) continue;
                if ((m.transform.position - spot).sqrMagnitude < spacing * spacing) return true;
                if (m.ClaimedSpot.HasValue && (m.ClaimedSpot.Value - spot).sqrMagnitude < spacing * spacing) return true;
            }
            return false;
        }

        // ---------- Danger map ----------

        public void AddDanger(Vector3 p, float weight)
        {
            for (int i = 0; i < danger.Count; i++)
                if ((danger[i].p - p).sqrMagnitude < 4f) { danger[i] = (danger[i].p, Time.time, Mathf.Min(3f, danger[i].w + weight)); return; }
            danger.Add((p, Time.time, weight));
        }

        /// <summary>How dangerous a spot is believed to be (0 = fine), fading over about a minute.</summary>
        public float DangerAt(Vector3 p)
        {
            float d = 0f;
            foreach (var (dp, t, w) in danger)
            {
                float age = Time.time - t;
                if (age > 70f) continue;
                float dist = Vector3.Distance(dp, p);
                if (dist > 4f) continue;
                d += w * (1f - dist / 4f) * (1f - age / 70f);
            }
            return d;
        }

        public IEnumerable<(Vector3 p, float t, float w)> DangerSpots => danger;

        // ---------- Leadership ----------

        void PickLeader()
        {
            Operative best = null; float bestScore = -1f;
            foreach (var m in Members)
            {
                if (m == null || m.IsDead) continue;
                float s = m.Traits.skill + m.Traits.teamwork + m.Traits.discipline * 0.5f;
                if (s > bestScore) { bestScore = s; best = m; }
            }
            Leader = best;
        }

        void LeaderThink()
        {
            nextOrder = Time.time + Random.Range(3f, 5f);
            PickLeader();
            var lead = Leader;
            if (lead == null || Alive < 2) return;
            if (lead.Faction != null && !lead.Faction.leaderOrders) return;
            if (lead.Perception.State != Alertness.Combat || !lead.Perception.Belief.Has) return;
            var b = lead.Perception.Belief;

            // Rally a shaken squad (a steady leader is worth a lot)
            if (Confidence < -0.25f && Time.time > nextRally && !lead.Broken && lead.Traits.discipline > 0.45f)
            {
                nextRally = Time.time + 15f;
                Callouts.Say(lead, Callout.Rally, force: true);
                AddMomentum(0.12f + 0.15f * lead.Traits.discipline);
                LastOrder = "Rally";
                return;
            }
            if (Confidence > 0.55f && Random.value < 0.35f) Callouts.Say(lead, Callout.Taunt);

            float fear = 0f; int alive = 0, hurt = 0;
            foreach (var m in Members) if (m != null && !m.IsDead) { fear += m.Emotion.fear; alive++; if (m.Health.Normalized < 0.5f) hurt++; }
            fear /= Mathf.Max(1, alive);
            float losses = StartSize > 0 ? Casualties / (float)StartSize : 0f;

            // Fall back: heavy losses and a scared, hurt squad
            if (losses >= 0.5f && (fear > 0.35f || hurt >= alive / 2f) && lead.Traits.caution > 0.3f)
            {
                Order(lead, Callout.FallBack, m => m.Mind.Order(OpAction.Retreat, 6f, 0.45f));
                return;
            }
            // Push: the target is reloading or hurt, and we're still strong
            bool opening = lead.Mind.ReloadOpportunity || Time.time - lead.Mind.LastHitOnPlayer < 3f || AnyRecentHit(3f);
            if (opening && losses < 0.5f && alive >= 2)
            {
                Order(lead, Callout.Push, m => m.Mind.Order(m.Archetype.canRush ? OpAction.Rush : OpAction.Advance, 5f, 0.35f));
                return;
            }
            // Stalemate: nobody has eyes but we know roughly where he is, so send someone around
            bool nobodySees = true;
            foreach (var m in Members) if (m != null && !m.IsDead && m.Perception.CanSee) { nobodySees = false; break; }
            if (nobodySees && b.Age > 3f && b.Age < 20f && (Flanker == null || Flanker.IsDead || Flanker.Mind.Current != OpAction.Flank))
            {
                Operative pick = null; float bestS = -1f;
                foreach (var m in Members)
                {
                    if (m == null || m.IsDead || m == lead && Alive > 2) continue;
                    float s = m.Traits.aggression + m.Traits.skill + (m.Style == CombatStyle.Flanker || m.Style == CombatStyle.Stalker ? 0.6f : 0f) - m.Emotion.fear;
                    if (s > bestS) { bestS = s; pick = m; }
                }
                if (pick != null)
                {
                    Flanker = pick;
                    FlankSide = Random.value < 0.5f ? -1f : 1f;
                    string side = FlankSide < 0f ? "left" : "right";
                    if (Callouts.Say(lead, Callout.FlankOrder, null, pick.Callsign, side, pick, force: lead.Faction == null || lead.Faction.radio))
                        LastOrder = "flank: " + pick.Callsign;
                    pick.Mind.Order(OpAction.Flank, 8f, 0.5f);
                    StartCoroutine(SayLater(pick, Callout.Copy, Random.Range(0.6f, 1.1f)));
                }
            }
        }

        System.Collections.IEnumerator SayLater(Operative who, Callout c, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (who != null && !who.IsDead) Callouts.Say(who, c, force: true);
        }

        bool AnyRecentHit(float window)
        {
            foreach (var m in Members) if (m != null && !m.IsDead && Time.time - m.Mind.LastHitOnPlayer < window) return true;
            return false;
        }

        void Order(Operative lead, Callout c, System.Action<Operative> apply)
        {
            Callouts.Say(lead, c, null, force: true);
            LastOrder = c.ToString();
            // Radioed orders reach everyone; shouted ones only reach those within earshot of the leader
            bool radio = lead.Faction != null && lead.Faction.radio;
            float reach = lead.Faction != null ? lead.Faction.shoutRadius : 40f;
            Operative ack = null;
            foreach (var m in Members)
                if (m != null && !m.IsDead && m != lead && (radio || Vector3.Distance(m.transform.position, lead.transform.position) < reach))
                {
                    apply(m);
                    if (ack == null || Vector3.Distance(m.transform.position, lead.transform.position) < Vector3.Distance(ack.transform.position, lead.transform.position)) ack = m;
                }
            if (ack != null && (c == Callout.Push || c == Callout.FallBack)) StartCoroutine(SayLater(ack, Callout.Copy, Random.Range(0.6f, 1.1f)));
        }

        void Update()
        {
            float err = config != null ? config.shareError : 1.5f;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (Time.time < pending[i].at) continue;
                var (_, from, b, about) = pending[i];
                pending.RemoveAt(i);
                b.Position += Random.insideUnitSphere.Flat() * err;
                b.BaseError += err;
                foreach (var m in Members)
                    if (m != null && m != from && !m.IsDead) m.Perception.OnSquadReport(b, from, about);
            }
            if (!Operative.Frozen) UpdateConfidence(Time.deltaTime);
            if (!Operative.Frozen && Time.time >= nextMapTick) { nextMapTick = Time.time + 0.2f; UpdateMap(); }
            if (Time.time >= nextOrder && !Operative.Frozen) LeaderThink();
            Members.RemoveAll(m => m == null);
            if (Members.Count == 0 && StartSize > 0) Destroy(gameObject, 0.5f);
        }
    }
}
