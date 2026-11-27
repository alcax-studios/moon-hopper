using System.Collections;
using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.Core;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.AI
{
    /// <summary>
    /// A human combat operative. It ties together:
    ///   Perception (sight, hearing, awareness, belief), OperativeMind (utility decisions, personality, emotions,
    ///   style, learning), OperativeWeapon (player-equivalent gun rules), OperativeAim (human aim), OperativeBody
    ///   (procedural animation), Callouts (spoken information), and ConcealedRoute (paths out of your sight).
    /// This class runs the chosen action each frame: where to move and by which route, how fast, where to face,
    /// when to crouch, when to shoot, and what to say. See docs/design/combat-ai.md.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(OperativeBody))]
    public class Operative : MonoBehaviour, ICombatTarget
    {
        public static readonly List<Operative> All = new();
        /// <summary>Dev toggle (F10): every operative stops thinking and moving.</summary>
        public static bool Frozen;

        static readonly string[] Names =
        {
            "Rami", "Kade", "Moreno", "Ivo", "Sato", "Brandt", "Okafor", "Lenz", "Dace", "Petrov", "Hollis", "Ruiz",
            "Tamsin", "Voss", "Idris", "Marek", "Quill", "Anand", "Becker", "Oyelaran", "Rusk", "Halvor", "Neri", "Cho",
        };

        [SerializeField] EnemyArchetype archetype;
        [SerializeField] CombatAIConfig config;
        [SerializeField] Faction faction;
        public PatrolRoute route;
        public Squad Squad;

        public Traits Traits;
        public Emotion Emotion;
        public CombatStyle Style { get; private set; }
        public CombatStyle SecondaryStyle { get; private set; }
        public string Callsign { get; private set; } = "Op";
        public float VoicePitch { get; private set; } = 1f;
        public Perception Perception { get; private set; }
        public OperativeAttention Attention { get; private set; }
        /// <summary>Where a search is looking (the probable spot being checked).</summary>
        public Vector3? SearchTarget { get; private set; }
        public OperativeMind Mind { get; private set; }
        public OperativeWeapon Weapon { get; private set; }
        public Health Health { get; private set; }
        public EnemyArchetype Archetype => archetype;
        public CombatAIConfig Config => config;
        public Faction Faction => faction;
        public bool IsDead => Health == null || Health.IsDead;
        public Vector3 EyePosition => body.Head.position;
        public Vector3 LookForward => body.Head.forward;
        public Vector3 AimForward => aim != null ? aim.Held : transform.forward;
        public OperativeBody Body => body;
        public Vector3 MuzzlePosition => body.Muzzle != null ? body.Muzzle.position : EyePosition + transform.forward * 0.5f;
        public Vector3 ChestPosition => EyePosition + Vector3.down * 0.35f;
        public Vector3? ClaimedSpot { get; private set; }
        public bool Busy => Time.time < busyUntil;
        public bool ActionDone { get; private set; }
        public bool InCover { get; private set; }
        public CoverKind CoverHere { get; private set; }
        public float Pressure { get; private set; }
        public string Note { get; private set; } = "";
        public CoverSpot Cover => cover;
        public Vector3 MoveTarget => moveTarget;
        public float AimError => aim != null ? aim.ErrorDegrees : 0f;
        /// <summary>Where the weapon is held (without recoil).</summary>
        public Vector3 AimHeld => aim != null ? aim.Held : transform.forward;
        public Wound Wound { get; private set; }
        /// <summary>-1 (shaken) to +1 (confident): the squad's mood, swung by temperament, plus how this unit is doing.</summary>
        public float Confidence { get; private set; }
        /// <summary>Confidence collapsed: running for it (recovers once far away and calmer).</summary>
        public bool Broken { get; private set; }
        /// <summary>Gave up: hands up, weapon dropped, out of the fight for good.</summary>
        public bool Surrendered { get; private set; }
        public bool LastStand { get; private set; }
        public int PatchesLeft { get; private set; } = 1;
        /// <summary>When the player last had this unit in view (in their view cone, with line of sight).</summary>
        public float LastSeenByPlayer { get; private set; } = -100f;
        /// <summary>Does the player probably know where I am? (They saw me, or I just fired or shouted.)</summary>
        public bool PlayerKnowsMe => Time.time - LastSeenByPlayer < 5f || Time.time - Weapon.LastShotTime < 4f || Time.time - lastShotAt < 6f
                                     || (Time.time - LastCalloutTime < 3f && (faction == null || !faction.radio));
        public IReadOnlyList<Vector3> Path => path;
        public int PathIndex => pathIdx;

        // Callouts
        public float NextCalloutTime { get; set; }
        public string LastCallout { get; set; } = "";
        public float LastCalloutTime { get; set; } = -100f;
        readonly List<HeardCallout> inbox = new();
        readonly HashSet<Operative> mourned = new();

        NavMeshAgent agent;
        OperativeBody body;
        OperativeAim aim;
        Rigidbody rb;

        // Action state
        CoverSpot cover;
        float coverFailTime = -100f, busyUntil, phaseUntil, nextRepath, staggerUntil, nextMelee;
        int peekFails, peekCycles, searchCount, patrolIndex;
        enum Phase { Moving, Hidden, Expose, Return, Wait }
        Phase phase;
        Vector3 moveTarget;
        float moveSpeed, crouch;
        bool weaponRaised, faceThreat, scanning, wantFire, suppressing, sawDuringExposure, planted, sneaking;
        float exposeStart, nextStrafe, strafeSide = 1f, plantCrouch, moveSpeedScale = 1f;
        readonly List<Vector3> path = new();
        int pathIdx;
        // Firing state
        int burstLeft, aimPart = 1;
        float burstPauseUntil, meleeStart = -1f, ambushHoldStart = -100f, ambushLastDist;
        bool meleeHit;
        Alertness lastState;
        bool wasSeeing, lostCalled, engaged, attending, facingGaze;
        // Sight sharing
        float lastPositionCallout = -100f;
        Vector3 lastCalledPosition;
        float nextSeenCheck, playerHurtUntil = -100f, lastReloadCallout = -100f;
        float stepDistance, nextDuckCheck, nextConfidence, brokenUntil, lastEngagingCall = -100f;
        bool ducking;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            body = GetComponent<OperativeBody>();
            Health = GetComponent<Health>();
            rb = GetComponent<Rigidbody>();
            if (config == null) config = ScriptableObject.CreateInstance<CombatAIConfig>();
            if (archetype == null) archetype = ScriptableObject.CreateInstance<EnemyArchetype>();
            Health.Damaged += OnDamaged;
            Health.Died += OnDied;
            Setup();

            agent.updateRotation = false;
            agent.acceleration = 14f;
            agent.angularSpeed = 0f;
            agent.stoppingDistance = 0.15f;
            agent.radius = 0.32f;
            agent.height = 1.8f;
            agent.autoBraking = true;
            agent.avoidancePriority = Random.Range(35, 65);
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        }

        void Setup()
        {
            RollTraits();
            Health.SetMax(archetype.maxHealth);
            Perception?.Dispose();
            Perception = new Perception(this, config);
            Mind = new OperativeMind(this, config);
            Weapon = new OperativeWeapon(this, archetype);
            aim = new OperativeAim(transform.forward);
            Attention = new OperativeAttention(this);
            body.Pistol = archetype.weaponName == "Pistol";
            Callsign = PickName();
            VoicePitch = Random.Range(0.86f, 1.14f);
            PatchesLeft = faction == null || Random.value < faction.patchUpChance ? Random.Range(1, 3) : 0;
            ApplyLook();
        }

        static string PickName()
        {
            for (int tries = 0; tries < 40; tries++)
            {
                var n = Names[Random.Range(0, Names.Length)];
                bool taken = false;
                foreach (var o in All) if (o != null && !o.IsDead && o.Callsign == n) { taken = true; break; }
                if (!taken) return n;
            }
            return Names[Random.Range(0, Names.Length)] + "-" + Random.Range(2, 9);
        }

        /// <summary>Faction > role > individual: traits, style, and voice.</summary>
        void RollTraits()
        {
            float js = faction != null ? faction.jitterScale : 1f;
            float J(float v, float off) => Mathf.Clamp01(v + off + Random.Range(-archetype.traitJitter, archetype.traitJitter) * js);
            var f = faction;
            Traits = new Traits
            {
                aggression = J(archetype.aggression, f ? f.aggression : 0f),
                caution = J(archetype.caution, f ? f.caution : 0f),
                discipline = J(archetype.discipline, f ? f.discipline : 0f),
                patience = J(archetype.patience, f ? f.patience : 0f),
                teamwork = J(archetype.teamwork, f ? f.teamwork : 0f),
                skill = J(archetype.skill, f ? f.skill : 0f),
                nerve = J(archetype.nerve, f ? f.nerve : 0f),
                reaction = Random.Range(archetype.reactionTime.x, archetype.reactionTime.y) * (f ? f.reactionScale : 1f),
                adaptability = Mathf.Clamp01((f ? f.adaptability : 0.5f) + Random.Range(-0.15f, 0.15f)),
            };
            Emotion = new Emotion { fear = (1f - Traits.nerve) * 0.1f };
            var w = (f != null ? f.styles : StyleWeights.Even) * archetype.styles;
            Doctrine = Mathf.Clamp01((f != null ? f.coverDiscipline : 0.5f) + archetype.coverDiscipline + (Traits.discipline - 0.5f) * 0.3f + Random.Range(-0.08f, 0.08f));
            Stamina = 1f;
            Style = w.Roll();
            SecondaryStyle = w.Roll(Style);
        }

        public void Init(EnemyArchetype a, CombatAIConfig c, Faction f, Squad squad, PatrolRoute patrol)
        {
            if (a != null) archetype = a;
            if (c != null) config = c;
            faction = f;
            HostileToPlayer = f == null || f.hostileToPlayer;
            talk = GetComponent<Alcax.Dialogue.Npc>();
            Squad = squad;
            route = patrol;
            Setup();
            // What this person carries: searchable once dead (the gun drops separately, as an item)
            var loot = GetComponent<Alcax.Items.LootContainer>();
            if (loot == null) loot = gameObject.AddComponent<Alcax.Items.LootContainer>();
            string table = faction == null ? "insurgents" : faction.displayName == "Station Security" ? "security" : faction.displayName.ToLowerInvariant();
            loot.Configure("Body", table, null, 10, transform.Find("Rig/Body"), Health);
        }

        /// <summary>Drop the gun as a world item (with whatever was left in its magazine).</summary>
        void DropGunAsItem()
        {
            var w = body.DropWeapon();
            if (w == null) return;
            var def = Alcax.Items.LootTables.ForNpcWeapon(archetype.weaponName);
            if (def == null) return;
            var wi = w.gameObject.AddComponent<Alcax.Items.WorldItem>();
            wi.Set(new Alcax.Items.ItemStack(def, 1, def.weapon != null ? Random.Range(0, def.weapon.magazineSize + 1) : -1));
            foreach (var c in w.GetComponentsInChildren<Collider>()) c.gameObject.layer = 0;
        }

        void ApplyLook()
        {
            if (faction == null) return;
            var jacket = faction.jacketVariety != null && faction.jacketVariety.Length > 0
                ? faction.jacketVariety[Random.Range(0, faction.jacketVariety.Length)] : faction.jacket;
            var mpb = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<MeshRenderer>())
            {
                string n = r.gameObject.name;
                Color? c = n is "Torso" or "Upper" or "Fore" ? jacket
                         : n is "Plate" or "Helmet" or "HelmetRim" or "KneePad" or "Cap" or "Brim" ? faction.gear : null;
                if (!c.HasValue) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c.Value);
                r.SetPropertyBlock(mpb);
            }
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            if (Squad == null) Squad = GetComponentInParent<Squad>();
            Squad?.Join(this);
        }

        void OnDestroy()
        {
            Perception?.Dispose();
            Squad?.Leave(this);
        }

        // ---------- Frame ----------

        void Update()
        {
            if (IsDead) return;
            float dt = Time.deltaTime;
            if (Frozen || dt <= 0f)
            {
                if (agent.isOnNavMesh) agent.isStopped = true;
                body.Velocity = Vector3.zero;
                return;
            }
            if (agent.isOnNavMesh) agent.isStopped = false;

            Pressure = Mathf.MoveTowards(Pressure, 0f, dt * 0.5f);
            TrackContact();
            UpdateSpace();
            Perception.Tick(dt);
            var state = Perception.State;
            if (state == Alertness.Combat && lastState != Alertness.Combat)
            {
                // Surprise: a moment to process before deciding (they still turn and raise the weapon)
                Mind.Startle(Traits.reaction * (lastState == Alertness.Unaware ? 1.6f : 1f));
                aim.Acquire();
            }
            lastState = state;
            bool seeing = Perception.CanSee;
            if (seeing && !wasSeeing) aim.Acquire();
            if (seeing) { lostCalled = false; if (recentReports.Count > 0 && Target != null) GradeReports(Target.Position); }
            else if (state == Alertness.Combat && !lostCalled && Perception.TimeSinceSeen > config.lostAfter * 0.8f && Perception.TimeSinceSeen < 60f)
            {
                lostCalled = true;
                if (Squad != null && Squad.Alive > 1) Callouts.Say(this, Callout.LostHim);
            }
            wasSeeing = seeing;
            UpdateContacts();
            UpdateCooldown();
            var player = Target;
            if (seeing && player != null && player.IsReloading)
            {
                Mind.OnSawReload();
                if (Time.time - lastReloadCallout > 5f && Squad != null && Squad.Alive > 1) { lastReloadCallout = Time.time; Callouts.Say(this, Callout.HesReloading, player.Position); }
            }
            if (Time.time >= nextSeenCheck) { nextSeenCheck = Time.time + 0.2f; CheckSeenByPlayer(player); }
            ProcessInbox();
            if (Time.time >= nextConfidence) { nextConfidence = Time.time + 0.25f; UpdateConfidence(player); }

            Mind.Tick(dt);
            AdvancePath();
            TickAction(dt);
            UpdateCombat(dt);
            UpdateMovement(dt);
            Weapon.Tick(dt, agent.velocity.sqrMagnitude > 0.5f);
            Footsteps(dt);
        }

        void UpdateConfidence(ICombatTarget player)
        {
            if (Surrendered) { Confidence = -1f; return; }
            // How much the squad's mood carries this unit: faction temperament, discipline, nerve
            float vol = (faction != null ? faction.volatility : 1f) * Mathf.Lerp(1.15f, 0.7f, Traits.discipline);
            float squad = Squad != null ? Squad.Confidence : 0f;
            float personal = (Health.Normalized - 0.7f) * 0.6f + (Time.time - Mind.LastHitOnPlayer < 4f ? 0.15f : 0f) - (Weapon.Dry ? 0.35f : 0f)
                             - Emotion.fear * 0.5f + (Traits.nerve - 0.5f) * 0.3f;
            // Panic spreads: watching nearby allies run or give up
            float contagion = 0f;
            foreach (var o in All)
            {
                if (o == this || o.IsDead || o.faction != faction) continue;
                if (!o.Broken && !o.Surrendered) continue;
                if ((o.transform.position - transform.position).sqrMagnitude > 18f * 18f) continue;
                contagion -= o.Surrendered ? 0.1f : 0.18f;
            }
            contagion = Mathf.Max(contagion, -0.5f) * vol;
            // Soft curve rather than a hard clamp: only extreme situations come near +-1
            float raw = squad * vol + personal * 0.6f + contagion;
            float e2 = Mathf.Exp(2f * Mathf.Clamp(raw, -4f, 4f));
            Confidence = (e2 - 1f) / (e2 + 1f);

            // Breaking point: deeper for the disciplined, the brave, and well-trained factions
            float breakAt = -0.55f - Traits.discipline * 0.35f - Traits.nerve * 0.2f - (faction != null ? faction.breakResistance : 0f);
            if (!Broken)
            {
                if (Confidence < breakAt && Emotion.fear > 0.45f && !LastStand && Perception.State == Alertness.Combat)
                {
                    Broken = true;
                    brokenUntil = Time.time + Random.Range(14f, 25f);
                    Callouts.Say(this, Callout.Breaking, force: true);
                    Mind.Poke();
                }
            }
            else
            {
                // Cornered: low discipline and he's right there looking at me
                bool cornered = player != null && !player.IsDead && Perception.CanSee
                                && Vector3.Distance(player.Position, transform.position) < 10f
                                && Vector3.Angle(player.Forward, ChestPosition - player.HeadPosition) < 20f;
                if (cornered && (faction == null || faction.canSurrender) && Traits.discipline < 0.6f && Random.value < 0.5f)
                {
                    Surrendered = true;
                    Broken = false;
                    Callouts.Say(this, Callout.Surrender, force: true);
                    DropGunAsItem();
                    Mind.Poke();
                }
                else if (Time.time > brokenUntil)
                {
                    // Rally once far enough and calmer, otherwise keep running a while longer
                    float d = player != null ? Vector3.Distance(player.Position, transform.position) : 999f;
                    if (Confidence > breakAt + 0.25f || d > 45f) { Broken = false; Emotion.fear *= 0.6f; Mind.Poke(); }
                    else brokenUntil = Time.time + 8f;
                }
            }
        }

void CheckSeenByPlayer(ICombatTarget p)
        {
            if (p == null || p.IsDead) return;
            if (p is Operative o)
            {
                if (o.Target == (ICombatTarget)this && o.Perception.CanSee) LastSeenByPlayer = Time.time;
                return;
            }
            var to = ChestPosition - p.HeadPosition;
            if (to.magnitude > 90f || Vector3.Angle(p.Forward, to) > 52f) return;
            if (!CoverFinder.Blocked(p.HeadPosition, ChestPosition) || !CoverFinder.Blocked(p.HeadPosition, EyePosition))
                LastSeenByPlayer = Time.time;
        }

        void Footsteps(float dt)
        {
            float speed = agent.velocity.magnitude;
            if (speed < 0.4f) return;
            stepDistance += speed * dt;
            float stride = 0.7f + speed * 0.12f;
            if (stepDistance < stride) return;
            stepDistance = 0f;
            float vol = sneaking ? 0.05f : speed > 4.6f ? 0.5f : speed > 2.6f ? 0.34f : 0.2f;
            SoundManager.PlayAt(Sfx.Footstep, transform.position, vol, 0.15f);
            if (!sneaking) Hearing.Emit(transform.position, speed > 4.6f ? 14f : 6f, NoiseKind.Footstep, gameObject);
        }

        // ---------- Sharing what they see ----------

        /// <summary>Called by Perception on every sighting. Radio factions stream it; shouters call it out.</summary>
        public void ShareSighting(Belief b)
        {
            bool radio = faction == null || faction.radio;
            if (radio) Squad?.Report(this, b);
            // Someone just called it out: confirm instead of repeating "contact"
            if (Perception.TimeSinceSeen > 3f && Squad != null && Callouts.LastContactCall.TryGetValue(Squad, out var called) && Time.time - called < 6f)
            {
                if (Callouts.Say(this, Callout.ContactConfirm, b.Position)) { lastPositionCallout = Time.time; lastCalledPosition = b.Position; }
                return;
            }
            bool fresh = Perception.TimeSinceSeen > 3f || Time.time - lastPositionCallout > 20f;
            bool moved = Vector3.Distance(lastCalledPosition, b.Position) > 5f && Time.time - lastPositionCallout > 4f;
            if (!fresh && !moved) return;
            bool anyoneToTell = false;
            foreach (var o in All) if (o != this && !o.IsDead && (o.Squad == Squad || !radio)) { anyoneToTell = true; break; }
            if (!anyoneToTell) return;
            if (Callouts.Say(this, fresh ? Callout.Contact : Callout.ThereHeIs, b.Position))
            {
                lastPositionCallout = Time.time;
                lastCalledPosition = b.Position;
            }
        }

        public void QueueCallout(HeardCallout h) => inbox.Add(h);

        /// <summary>How much is on this unit's mind (stress, a target in view, being shot at).</summary>
        public float Load => Emotion.stress + (Perception.CanSee ? 0.3f : 0f) + (Time.time < Mind.SuppressedUntil ? 0.3f : 0f);
        /// <summary>How much it can handle (skill and discipline).</summary>
        public float Capacity => Mathf.Lerp(0.6f, 1.25f, Traits.skill * 0.5f + Traits.discipline * 0.5f);
        public bool Overloaded => config.useCognitiveFilters && Load > Capacity;
        public static int CalloutsMissed;

        static bool Critical(HeardCallout h, Operative me) => h.Kind is Callout.ManDown or Callout.LeaderDown or Callout.FallBack or Callout.Push or Callout.Contact
                                                            || (h.Kind == Callout.FlankOrder && h.Addressee == me);

        void ProcessInbox()
        {
            for (int i = inbox.Count - 1; i >= 0; i--)
            {
                var h = inbox[i];
                if (Time.time < h.At) continue;
                inbox.RemoveAt(i);
                if (h.From == null) continue;
                // Overloaded: low-priority chatter goes unheard (critical calls always get through)
                if (Overloaded && !Critical(h, this) && Random.value < Mathf.Clamp01((Load - Capacity) * 1.5f + 0.3f)) { CalloutsMissed++; continue; }
                OnCallout(h);
            }
        }

        void OnCallout(HeardCallout h)
        {
            bool friendly = h.From.faction == faction;
            if (!friendly) return;
            switch (h.Kind)
            {
                case Callout.Contact:
                case Callout.ThereHeIs:
                    if (h.Belief.Has)
                    {
                        var b = h.Belief;
                        float err = 1f + Vector3.Distance(h.From.transform.position, transform.position) * 0.04f;
                        b.Position += Random.insideUnitSphere.Flat() * err;
                        b.BaseError += err;
                        Perception.OnSquadReport(b, h.From);
                    }
                    break;
                case Callout.Reloading:
                case Callout.CoverMe:
                case Callout.Moving:
                case Callout.Flanking:
                case Callout.ImHit:
                case Callout.NeedHelp:
                    Mind.CoverRequestedUntil = Time.time + 3.5f;
                    if (h.Kind == Callout.NeedHelp) Emotion.anger = Mathf.Clamp01(Emotion.anger + 0.1f * Traits.teamwork);
                    Mind.Poke(Traits.reaction);
                    break;
                case Callout.HesReloading:
                    Mind.OnHeardReload();
                    Mind.Poke(Traits.reaction);
                    break;
                case Callout.HesHit:
                    playerHurtUntil = Time.time + 3f;
                    break;
                case Callout.LastStand:
                    Emotion.anger = Mathf.Clamp01(Emotion.anger + 0.1f);
                    break;
                case Callout.ManDown:
                    if (h.Addressee != null) NoteAllyDown(h.Addressee);
                    break;
            }
        }

        // ---------- Trust ----------
        // How much this unit believes each squadmate's reports (0.2 to 1). The starting value comes from who is talking
        // (skill, discipline, same squad or not) and who is listening (rookies believe everything, skilled independent
        // types discount others). Then it is learned: when this unit sees the player itself, recent reports are checked
        // against reality, and speakers who were right gain trust while those who were way off lose it.
        readonly Dictionary<Operative, float> trust = new();
        readonly List<(Operative from, Vector3 pos, float err, float t)> recentReports = new();

        public float TrustIn(Operative speaker)
        {
            if (speaker == null) return 0.7f;
            if (trust.TryGetValue(speaker, out var v)) return v;
            float credibility = 0.45f + 0.25f * speaker.Traits.skill + 0.2f * speaker.Traits.discipline;
            if (speaker.Squad != Squad) credibility *= 0.8f;
            // Rookies (low skill) take reports at face value; skilled, independent types lean on their own eyes
            float gullible = Mathf.Lerp(1.15f, 0.8f, Traits.skill * (1f - Traits.teamwork * 0.5f));
            v = Mathf.Clamp(credibility * gullible, 0.2f, 1f);
            trust[speaker] = v;
            return v;
        }

        /// <summary>Remember a report that was used, so it can be checked against what this unit sees later.</summary>
        public void NoteReport(Operative from, Vector3 pos, float err)
        {
            if (from == null) return;
            recentReports.Add((from, pos, err, Time.time));
            if (recentReports.Count > 12) recentReports.RemoveAt(0);
        }

        /// <summary>Own eyes on the player: grade the reports received in the last few seconds.</summary>
        void GradeReports(Vector3 actual)
        {
            for (int i = recentReports.Count - 1; i >= 0; i--)
            {
                var r = recentReports[i];
                if (Time.time - r.t > 4f) { recentReports.RemoveAt(i); continue; }
                float off = Vector3.Distance(r.pos.Flat(), actual.Flat());
                float t = TrustIn(r.from);
                if (off < r.err + 1.5f) t += 0.06f;              // it checked out
                else if (off > r.err * 2f + 4f) t -= 0.12f;      // way off
                trust[r.from] = Mathf.Clamp(t, 0.2f, 1f);
                recentReports.RemoveAt(i);
            }
        }

        /// <summary>Grief and anger for a fallen squadmate, once per death however we learned it.</summary>
        public void NoteAllyDown(Operative dead)
        {
            if (!mourned.Add(dead)) return;
            Mind.OnAllyDown(dead);
        }

        // ---------- Context for the mind ----------

        public Vector3 ThreatEye
        {
            get
            {
                var p = Target;
                if (Perception.CanSee && p != null) return p.HeadPosition;
                return Perception.Belief.Predicted + Vector3.up * 1.5f;
            }
        }

        public MindContext BuildContext()
        {
            var p = Target;
            var b = Perception.Belief;
            bool threat = Perception.State == Alertness.Combat && b.Has && p != null;
            var c = new MindContext
            {
                Threat = threat,
                PlayerDead = p != null ? p.IsDead : (PlayerTarget.Instance == null || PlayerTarget.Instance.IsDead),   // no target is not the same as a dead player
                Seeing = Perception.CanSee,
                SinceSeen = Perception.TimeSinceSeen,
                Distance = b.Has ? Vector3.Distance(transform.position, b.Predicted) : 999f,
                Health = Health.Normalized,
                Ammo = Weapon.Ammo01,
                Dry = Weapon.Dry,
                AmmoTotal = Weapon.Total01,
                InfoAge = b.Has ? b.Age : 999f,
                NoAdvanceCover = Time.time - noAdvanceCoverTime < 8f,
                UnderFire = UnderFire,
                Indoor = SpaceHere.Indoor || SpaceThere.Indoor,
                FightSpace = FightSpace,
                SameRoom = MyRoom == ThreatRoom,
                CanClear = CanClearThreatRoom(),
                FreshContact = FreshContact,
                InfoConf = b.Confidence(config.uncertaintyGrowth),
                HoldStale = Mind.Current == OpAction.HoldCover ? Mathf.Min(Mind.TimeInAction, b.Has ? b.Age : 999f) : 0f,
                Reloading = Weapon.Reloading,
                Pressure = Mathf.Clamp01(Pressure),
                Awareness = Perception.Awareness,
                State = Perception.State,
                HasNoise = Time.time - Perception.NoiseTime < 25f,
                HasRoute = route != null && route.Count > 0,
                ReloadOpportunity = Mind.ReloadOpportunity,
                CoverFailedRecently = Time.time - coverFailTime < 4f,
                PeekFails = peekFails,
                HeightAdvantage = b.Has ? transform.position.y - b.Position.y : 0f,
                PlayerKnowsMe = PlayerKnowsMe,
                CoverRequested = Time.time < Mind.CoverRequestedUntil,
                PlayerHurt = Time.time < playerHurtUntil,
                PatchesLeft = PatchesLeft,
                Wound = Wound,
                LastStand = LastStand,
                TooClose = config.useStandoff && Perception.CanSee && b.Has && Vector3.Distance(transform.position, b.Predicted) < Standoff * 0.8f,
            };
            if (threat)
            {
                var eye = ThreatEye;
                CoverHere = CoverFinder.Classify(transform.position, eye, out _);
                InCover = CoverHere != CoverKind.None;
                // Behind something you can't shoot from is hiding, not fighting cover (unless you have nothing to shoot)
                c.InCover = config.useFightingCover && !Weapon.Dry ? CoverHere == CoverKind.Low || CoverHere == CoverKind.Corner : InCover;
                c.Hidden = CoverFinder.Hidden(transform.position, eye, true);
            }
            else { InCover = false; CoverHere = CoverKind.None; c.Hidden = true; }
            if (p != null && Perception.VisibleFraction > 0f)
                c.BeingAimedAt = Vector3.Angle(p.Forward, ChestPosition - p.HeadPosition) < 7f;
            if (Squad != null)
            {
                c.AlliesAlive = Mathf.Max(0, Squad.Fighting - 1);
                c.AlliesFiring = Squad.Firing(this);
                c.SquadFlanking = Squad.CountDoing(OpAction.Flank, this);
            }
            return c;
        }

        bool UsesConcealedRoutes => Confidence < 0.5f && (faction != null ? faction.concealedRoutes || Traits.skill > 0.7f : Traits.skill > 0.6f);

        // ---------- Actions ----------

        public void EnterAction(OpAction a)
        {
            ActionDone = false;
            phase = Phase.Moving;
            wantFire = false;
            suppressing = false;
            planted = false;
            scanning = false;
            sneaking = false;
            sideLean = 0f; blindFire = 0f;
            if (a != OpAction.ClearRoom) { cqbAim = null; CqbDoor = null; }
            if (a != OpAction.Patrol) { EndDuty(); if (talk != null) { if (talk.InConversation && Alcax.Dialogue.DialogueUI.Instance != null) Alcax.Dialogue.DialogueUI.Instance.Close(); talk.CanTalk = false; } }
            if (a != OpAction.TakeCover && a != OpAction.HoldCover && a != OpAction.PeekShoot) ClaimedSpot = null;
            if (a != OpAction.PeekShoot && a != OpAction.HoldCover) peekCycles = 0;
            var b = Perception.Belief;
            Note = "";
            bool talkative = Squad != null && Squad.Alive > 1 && Traits.teamwork > 0.35f;

            switch (a)
            {
                case OpAction.Idle:
                    Stop();
                    phaseUntil = Time.time + Random.Range(2f, 5f);
                    break;

                case OpAction.Patrol:
                    if (OnDuty) { StartDuty(); break; }
                    if (route != null && route.Count > 0) MoveTo(route.Point(patrolIndex), archetype.walkSpeed);
                    break;

                case OpAction.Investigate:
                    {
                        float speed = Perception.State == Alertness.Alert ? archetype.runSpeed * 0.55f : archetype.walkSpeed;
                        var noise = Perception.NoisePosition;
                        // Observe the spot from a standoff distance, don't walk onto it
                        var v = config.useStandoff ? CoverFinder.FindVantage(this, noise, Standoff, Standoff + 7f) : null;
                        if (v.HasValue) { MoveTo(v.Value, speed); Note = "vantage on noise"; }
                        else MoveTo(noise, speed);
                    }
                    break;

                case OpAction.Search:
                    searchCount = 0;
                    PickSearchPoint();
                    break;

                case OpAction.ClearRoom:
                    if (!StartClear()) { ActionDone = true; Mind.Poke(); }
                    break;

                case OpAction.TakeCover:
                    ChooseMoveMode(a);
                    if (config.useContactDrill && UnderFire && !config.useSteadyMoves) Mode = MoveMode.Dash;
                    MaybeReactionFire();
                    FindCoverAndGo(Emotion.fear > 0.6f ? CoverIntent.Retreat : CoverIntent.Hold, CoverRadius,
                        Pressure > 0.3f || Perception.CanSee ? archetype.sprintSpeed : archetype.runSpeed);
                    break;

                case OpAction.HoldCover:
                    Stop();
                    float hold = Mathf.Lerp(2f, 7f, Traits.patience) * Random.Range(0.7f, 1.3f) * (1f + Emotion.fear);
                    if (Style == CombatStyle.Ambusher) hold *= 1.7f;
                    phaseUntil = Time.time + hold;
                    if (Weapon.Ammo01 < 0.6f) Weapon.StartReload();
                    break;

                case OpAction.PeekShoot:
                    Stop();
                    if (!cover.Valid || (cover.Position - transform.position).sqrMagnitude > 2f)
                        cover = new CoverSpot { Valid = true, Position = transform.position, Kind = CoverHere, PeekPosition = transform.position };
                    if (cover.Kind == CoverKind.Corner)
                        CoverFinder.Classify(cover.Position, ThreatEye, out cover.PeekPosition);
                    phase = Phase.Hidden;
                    phaseUntil = Time.time + Random.Range(0.25f, 0.9f) * Mathf.Lerp(0.6f, 1.6f, Traits.patience);
                    break;

                case OpAction.Engage:
                    nextStrafe = 0f;
                    break;

                case OpAction.Advance:
                    ChooseMoveMode(a);
                    if (FindCoverAndGo(CoverIntent.Advance, CoverRadius, archetype.runSpeed, failQuietly: true))
                    {
                        if (Traits.caution > 0.55f && UsesConcealedRoutes) RouteTo(cover.Position, archetype.runSpeed, false);
                    }
                    else
                    {
                        if (config.useSteadyMoves && Perception.CanSee && !LastStand && !(Doctrine < 0.35f && Traits.aggression > 0.6f))
                        {
                            noAdvanceCoverTime = Time.time;
                            Note = "no forward cover";
                            ActionDone = true; Mind.Poke(0.1f);
                            break;
                        }
                        var dir = (b.Predicted - transform.position).Flat().normalized;
                        float step = Mathf.Min(8f, Vector3.Distance(transform.position, b.Predicted) - RangeMin);
                        if (config.useSteadyMoves && agent.isOnNavMesh && UnityEngine.AI.NavMesh.SamplePosition(b.Predicted, out var bh, 3f, UnityEngine.AI.NavMesh.AllAreas))
                        {
                            // Step along the walking route toward him, not a straight line through a wall (which U-turns around it)
                            routePath ??= new UnityEngine.AI.NavMeshPath();
                            if (UnityEngine.AI.NavMesh.CalculatePath(transform.position, bh.position, UnityEngine.AI.NavMesh.AllAreas, routePath) && routePath.corners.Length > 1)
                            {
                                var cs = routePath.corners; float left = Mathf.Max(2f, step); var pt = cs[0];
                                for (int i = 1; i < cs.Length && left > 0f; i++) { float seg = Vector3.Distance(cs[i - 1], cs[i]); pt = seg <= left ? cs[i] : Vector3.Lerp(cs[i - 1], cs[i], left / seg); left -= seg; }
                                MoveTo(pt, archetype.runSpeed);
                                Note = "advance (no cover)";
                                noAdvanceCoverTime = Time.time;
                                if (talkative) Callouts.Say(this, Callout.Moving);
                                break;
                            }
                        }
                        MoveTo(transform.position + dir * Mathf.Max(2f, step), archetype.runSpeed);
                        Note = "advance (no cover)";
                        noAdvanceCoverTime = Time.time;
                    }
                    if (talkative) Callouts.Say(this, Callout.Moving);
                    break;

                case OpAction.Flank:
                    {
                        ChooseMoveMode(a);
                        float side = Squad != null && Squad.Flanker == this ? Squad.FlankSide : 0f;
                        // A real flank first (a wide angle on him from the squad's line), else any cover off his line of sight
                        cover = CoverFinder.FindFlank(this, ThreatEye, side);
                        bool found = cover.Valid;
                        if (found) { ClaimedSpot = cover.Position; Note = "flank " + cover.Kind; }
                        else found = FindCoverAndGo(CoverIntent.Flank, 22f, archetype.runSpeed, failQuietly: true, sideSign: side);
                        Vector3 goal;
                        if (found) goal = cover.Position;
                        else
                        {
                            var toMe = (transform.position - b.Predicted).Flat().normalized;
                            if (side == 0f) side = Random.value < 0.5f ? 1f : -1f;
                            var lateral = Vector3.Cross(Vector3.up, toMe) * side;
                            goal = b.Predicted + (toMe * 0.6f + lateral).normalized * Mathf.Clamp(Vector3.Distance(transform.position, b.Predicted), 8f, 16f);
                            Note = "flank (open)";
                        }
                        // Plan at crouch height: they duck-walk behind low cover where standing would show them
                        if (UsesConcealedRoutes) RouteTo(goal, archetype.runSpeed, true);
                        else MoveTo(goal, archetype.runSpeed);
                        if (talkative && (faction == null || faction.radio || Traits.discipline < 0.5f))
                            Callouts.Say(this, Callout.Flanking, goal, SideWord(goal));
                    }
                    break;

                case OpAction.Rush:
                    MoveTo(RushPoint(b.Predicted), archetype.sprintSpeed);
                    nextRepath = Time.time + 0.25f;
                    break;

                case OpAction.Retreat:
                    {
                        bool found = FindCoverAndGo(CoverIntent.Retreat, 25f, archetype.sprintSpeed, failQuietly: true);
                        var goal = found ? cover.Position : transform.position + (transform.position - b.Predicted).Flat().normalized * 12f;
                        if (!found) Note = "retreat (open)";
                        if (UsesConcealedRoutes) RouteTo(goal, archetype.sprintSpeed, false);
                        else MoveTo(goal, archetype.sprintSpeed);
                    }
                    break;

                case OpAction.Stalk:
                    {
                        // Get behind or beside where he's looking, through ground he can't see
                        var player = Target;
                        var center = b.Predicted;
                        Vector3 behind = player != null && Perception.TimeSinceSeen < 10f ? -player.Forward.Flat().normalized
                                                                                         : (transform.position - center).Flat().normalized;
                        var side = Vector3.Cross(Vector3.up, behind) * (Random.value < 0.5f ? 1f : -1f);
                        var goal = center + (behind * 0.75f + side * 0.65f).normalized * Random.Range(6f, 10f);
                        sneaking = true;
                        if (!RouteTo(goal, 1.9f, true)) MoveTo(goal, 1.9f);
                        Note = "stalk";
                    }
                    break;

                case OpAction.Flee:
                    {
                        var away = (transform.position - b.Predicted).Flat();
                        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
                        var goal = transform.position + Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * away.normalized * Random.Range(25f, 40f);
                        MoveTo(goal, archetype.sprintSpeed);
                        Note = "fleeing";
                    }
                    break;

                case OpAction.Surrender:
                    Stop();
                    Note = "surrendered";
                    break;

                case OpAction.PatchUp:
                    Stop();
                    phaseUntil = Time.time + Random.Range(3.8f, 5.5f) * Mathf.Lerp(1.2f, 0.85f, Traits.skill);
                    Note = "patching up";
                    break;
            }
        }

        /// <summary>Rushing gunmen close to shooting distance, not to arm's length (shotgun rushers go in).</summary>
        Vector3 RushPoint(Vector3 threat)
        {
            if (!config.useStandoff || archetype.canRush) return threat;
            float keep = Mathf.Max(config.useSpaceAwareness ? 2.5f : 4f, Standoff * 0.6f);
            var away = (transform.position - threat).Flat();
            if (away.sqrMagnitude < 0.01f) return transform.position;
            return threat + away.normalized * keep;
        }

        string SideWord(Vector3 goal)
        {
            var b = Perception.Belief;
            var toThreat = (b.Predicted - transform.position).Flat();
            float s = Vector3.SignedAngle(toThreat, (goal - transform.position).Flat(), Vector3.up);
            return s < 0f ? "left" : "right";
        }

        bool FindCoverAndGo(CoverIntent intent, float radius, float speed, bool failQuietly = false, float sideSign = 0f)
        {
            cover = CoverFinder.Find(this, ThreatEye, intent, radius, AIDebugOverlay.Enabled, sideSign);
            if (!cover.Valid)
            {
                coverFailTime = Time.time;
                if (!failQuietly) { ActionDone = true; Mind.Poke(); }
                return false;
            }
            ClaimedSpot = cover.Position;
            MoveTo(cover.Position, speed);
            Note = intent + " " + cover.Kind;
            return true;
        }

        /// <summary>Walk a concealed route to a goal (waypoints out of the threat's sight). Falls back to a direct move.</summary>
        bool RouteTo(Vector3 goal, float speed, bool crouched)
        {
            if (!Perception.Belief.Has) { MoveTo(goal, speed); return false; }
            var planned = new List<Vector3>();
            if (!ConcealedRoute.Plan(this, goal, ThreatEye, crouched, planned) || planned.Count == 0) { MoveTo(goal, speed); return false; }
            MoveTo(planned[0], speed);
            path.Clear();
            path.AddRange(planned);
            pathIdx = 0;
            Note += " (hidden route " + planned.Count + ")";
            return true;
        }

        void AdvancePath()
        {
            if (path.Count == 0 || pathIdx >= path.Count - 1 || !agent.isOnNavMesh || agent.pathPending) return;
            if (agent.remainingDistance < 1.3f)
            {
                pathIdx++;
                moveTarget = path[pathIdx];
                agent.SetDestination(moveTarget);
            }
        }

        void TickAction(float dt)
        {
            var b = Perception.Belief;
            bool seeing = Perception.CanSee;
            float dist = b.Has ? Vector3.Distance(transform.position, b.Predicted) : 999f;
            crouch = 0f;
            wantFire = false;
            moveSpeedScale = 1f;
            dashing = false;
            faceThreat = b.Has && Perception.State == Alertness.Combat;
            weaponRaised = Perception.State >= Alertness.Alert;
            scanning = false;

            switch (Mind.Current)
            {
                case OpAction.Idle:
                    scanning = true;
                    faceThreat = false;
                    if (Time.time > phaseUntil) ActionDone = true;
                    break;

                case OpAction.Patrol:
                    scanning = true;
                    faceThreat = false;
                    if (OnDuty) { TickDuty(); break; }
                    if (route == null || route.Count == 0) { ActionDone = true; break; }
                    if (phase == Phase.Moving && Arrived())
                    {
                        phase = Phase.Wait;
                        phaseUntil = Time.time + Random.Range(1.5f, 5f);
                        Stop();
                    }
                    else if (phase == Phase.Wait && Time.time > phaseUntil)
                    {
                        patrolIndex++;
                        phase = Phase.Moving;
                        MoveTo(route.Point(patrolIndex), archetype.walkSpeed);
                    }
                    break;

                case OpAction.Investigate:
                    weaponRaised = true;
                    faceThreat = false;
                    if (!config.useAttention) body.LookPoint = Perception.NoisePosition + Vector3.up * 1.2f;
                    if (phase == Phase.Moving && Arrived())
                    {
                        phase = Phase.Wait;
                        phaseUntil = Time.time + Mathf.Lerp(3f, 8f, Traits.patience);
                        Stop();
                    }
                    if (phase == Phase.Wait)
                    {
                        scanning = true;
                        body.LookPoint = null;
                        if (Time.time > phaseUntil) { Perception.NoiseTime = -100f; ActionDone = true; }
                    }
                    break;

                case OpAction.Search:
                    weaponRaised = true;
                    faceThreat = false;
                    if (phase == Phase.Moving && Arrived())
                    {
                        phase = Phase.Wait;
                        phaseUntil = Time.time + Random.Range(1.2f, 3f);
                        Stop();
                    }
                    if (phase == Phase.Wait)
                    {
                        scanning = true;
                        if (Time.time > phaseUntil)
                        {
                            if (++searchCount >= Random.Range(4, 7)) ActionDone = true;
                            else PickSearchPoint();
                        }
                    }
                    break;

                case OpAction.TakeCover:
                case OpAction.Advance:
                case OpAction.Flank:
                case OpAction.Retreat:
                    {
                        bool retreating = Mind.Current == OpAction.Retreat;
                        bool flanking = Mind.Current == OpAction.Flank;
                        bool canShoot = seeing && !retreating && (!flanking || dist < 12f || (config.useContactDrill && PlayerKnowsMe)) && dist < (config.useSteadyMoves ? archetype.range * 0.6f : 30f);
                        bool shootOnMove = config.useMoveDoctrine && !retreating ? TickMoveFire(canShoot, dist, seeing)
                                                                                 : canShoot && (Traits.aggression > 0.35f || dist < 10f);
                        wantFire = shootOnMove || (movePlanted && seeing);
                        dashing = config.useMoveDoctrine && !shootOnMove && !movePlanted && (Mode == MoveMode.Dash || retreating);
                        if (config.useSteadyMoves && dashing && !retreating && (seeing || UnderFire)) dashing = false;
                        faceThreat = seeing && (dist < 18f || Traits.skill > 0.6f) && !retreating && !dashing;
                        moveSpeedScale = movePlanted ? 0f : shootOnMove ? 0.6f : 1f;
                        if (Mind.Current == OpAction.TakeCover && Time.time < reactionFireUntil)
                        {
                            // A burst toward him (or where the shots came from) before running
                            dashing = false;
                            moveSpeedScale = 0f;
                            wantFire = true;
                            suppressing = !seeing;
                            faceThreat = true;
                            weaponRaised = true;
                        }
                        else if (suppressing && Mind.Current == OpAction.TakeCover && reactionFireUntil > 0f) { suppressing = false; reactionFireUntil = -100f; }
                        if (dashing) weaponRaised = false;   // low ready, running
                        // On a hidden route, go low where only low cover hides them
                        if (path.Count > 0 && !shootOnMove && b.Has && !(config.useContactDrill && (dashing || UnderFire)))
                        {
                            if (Time.time > nextDuckCheck)
                            {
                                nextDuckCheck = Time.time + 0.25f;
                                var eyeT = ThreatEye;
                                ducking = !CoverFinder.Blocked(eyeT, transform.position + Vector3.up * 1.55f) && CoverFinder.Blocked(eyeT, transform.position + Vector3.up * 1.0f);
                            }
                            if (ducking) { crouch = 0.55f; moveSpeedScale = 0.7f; }
                        }
                        else ducking = false;
                        if (Time.time > nextRepath && cover.Valid && ClaimedSpot.HasValue)
                        {
                            nextRepath = Time.time + 0.6f;
                            if (b.Has && CoverFinder.Classify(cover.Position, ThreatEye, out _) == CoverKind.None && Mind.Current != OpAction.Advance && !flanking)
                            { ActionDone = true; Mind.Poke(); }
                        }
                        if (Arrived()) { ActionDone = true; Mind.CompleteOrder(Mind.Current); Mind.Poke(); Stop(); }
                        if (flanking && seeing && dist < 12f) { ActionDone = true; Mind.Poke(); }
                    }
                    break;

                case OpAction.HoldCover:
                    crouch = CoverHere == CoverKind.Low || CoverHere == CoverKind.Full ? 1f : 0.35f;
                    weaponRaised = true;
                    wantFire = seeing;
                    if (seeing) crouch = CoverHere == CoverKind.Low ? 0f : crouch;
                    if (Weapon.Ammo01 < 0.6f && !seeing) Weapon.StartReload();
                    if (Time.time > phaseUntil && !Weapon.Reloading) ActionDone = true;
                    break;

                case OpAction.PeekShoot:
                    TickPeek(seeing);
                    break;

                case OpAction.ClearRoom:
                    TickClear(seeing);
                    break;

                case OpAction.Engage:
                    TickEngage(dist, seeing);
                    break;

                case OpAction.Rush:
                    weaponRaised = true;
                    wantFire = config.useContactDrill ? seeing && !Weapon.Dry : seeing && !archetype.canRush || (seeing && dist > 6f);
                    moveSpeedScale = wantFire && dist > 6f ? 0.75f : 1f;
                    if (Time.time > nextRepath)
                    {
                        nextRepath = Time.time + 0.25f;
                        var p = Target;
                        MoveTo(RushPoint(seeing && p != null ? p.Position : b.Predicted), archetype.sprintSpeed);
                    }
                    if (!seeing && Arrived()) { ActionDone = true; Mind.CompleteOrder(OpAction.Rush); Mind.Poke(); }
                    // A gunman's rush is over once at shooting distance: fight from there
                    else if (config.useStandoff && !archetype.canRush && seeing && dist < Mathf.Max(config.useSpaceAwareness ? 3f : 4.5f, Standoff * 0.7f)) { ActionDone = true; Mind.CompleteOrder(OpAction.Rush); Mind.Poke(); }
                    break;

                case OpAction.Stalk:
                    {
                        sneaking = true;
                        crouch = 0.6f;
                        weaponRaised = true;
                        faceThreat = dist < 18f;
                        var p = Target;
                        // Open fire only with the advantage: close, and he's facing away
                        if (seeing && p != null)
                        {
                            float facingAway = Vector3.Angle(p.Forward.Flat(), (transform.position - p.Position).Flat());
                            if (dist < 16f && facingAway > 60f) { wantFire = true; Note = "ambush!"; }
                        }
                        if (PlayerKnowsMe && !wantFire) { ActionDone = true; Mind.Poke(); }
                        if (Arrived()) { ActionDone = true; Mind.Poke(); }
                    }
                    break;

                case OpAction.Flee:
                    weaponRaised = false;
                    faceThreat = false;
                    if (Arrived()) { ActionDone = true; Mind.Poke(); }
                    break;

                case OpAction.Surrender:
                    weaponRaised = false;
                    faceThreat = false;
                    {
                        var p = (ICombatTarget)PlayerTarget.Instance ?? Target;
                        if (p != null) body.LookPoint = p.HeadPosition;
                    }
                    break;

                case OpAction.PatchUp:
                    crouch = 1f;
                    weaponRaised = false;
                    faceThreat = false;
                    if (seeing || Pressure > 0.3f) { ActionDone = true; Mind.Poke(); break; }
                    if (Time.time > phaseUntil)
                    {
                        float heal = Mathf.Min(35f, Health.Max * 0.8f - Health.Current);
                        if (heal > 0f) Health.Heal(heal);
                        PatchesLeft--;
                        UpdateWound();
                        ActionDone = true;
                        Mind.Poke();
                    }
                    break;
            }
            if ((Mind.Current != OpAction.Investigate || config.useAttention) && Mind.Current != OpAction.Surrender) body.LookPoint = null;
        }

        // ---------- Cover repertoire ----------
        // Coming out of cover is a choice between four exposures, each trading risk for information and accuracy:
        //   Info  - head only, briefly, no shooting: refresh where he is.
        //   Blind - only the gun over or around the cover: suppression, physically very inaccurate.
        //   Lean  - head, shoulder, and gun: real aimed shots at limited risk.
        //   Full  - step or stand out: the best shot, the most exposure.
        // The choice weighs how stale the information is, who needs covering, fear, confidence, style, and ammo.
        // The peek spot also varies (shift along low cover, or come out low around a corner) when the last peek drew fire.
        public enum PeekType { Info, Blind, Lean, Full }
        public static readonly int[] PeekCounts = new int[4];
        PeekType peekType;
        float peekSide, sideLean, blindFire, blindSide;
        bool lastPeekHurt;
        int peeksHere;

        PeekType ChoosePeek(float dist)
        {
            var b = Perception.Belief;
            if (!config.useCoverRepertoire) return PeekType.Full;
            if (Weapon.Dry) return PeekType.Info;
            float ammo = Weapon.Total01;
            float need = Mathf.Clamp01(b.Age / 6f) + Mathf.Clamp01(b.Uncertainty(config.uncertaintyGrowth) / 8f) * 0.5f;
            bool covering = Time.time < Mind.CoverRequestedUntil
                            || (Squad != null && (Squad.CountDoing(OpAction.Advance, this) + Squad.CountDoing(OpAction.Flank, this)) > 0);
            bool suppressor = Style == CombatStyle.Suppressor || SecondaryStyle == CombatStyle.Suppressor;
            float info = 0.25f + need * 0.8f + Traits.caution * 0.3f + Traits.patience * 0.2f - (Perception.CanSee ? 0.6f : 0f);
            float blind = b.Age < 4f
                ? 0.05f + (covering ? 0.5f * Traits.teamwork : 0f) + (suppressor ? 0.35f : 0f) + Emotion.fear * 0.45f + Pressure * 0.4f
                  - Traits.discipline * 0.3f + (ammo > 0.5f ? 0.1f : -0.6f) + (lastPeekHurt ? 0.25f : 0f)
                : -1f;
            float lean = 0.45f + Traits.skill * 0.2f + Traits.discipline * 0.2f - Emotion.fear * 0.2f - need * 0.3f + (ammo < 0.3f ? 0.1f : 0f);
            float full = 0.12f + Traits.aggression * 0.4f + Mathf.Max(0f, Confidence) * 0.4f + (dist < 10f ? 0.2f : 0f)
                         + (Mind.ReloadOpportunity ? 0.4f : 0f) - Emotion.fear * 0.4f - Traits.caution * 0.3f - (lastPeekHurt ? 0.3f : 0f) - (ammo < 0.3f ? 0.1f : 0f);
            float[] sc = { info, blind, lean, full };
            int best = 0;
            for (int i = 0; i < 4; i++) { sc[i] += Random.Range(-0.1f, 0.1f); if (sc[i] > sc[best]) best = i; }
            return (PeekType)best;
        }

        /// <summary>Which way to lean around a corner: toward the peek point.</summary>
        float CornerSide()
        {
            var off = cover.PeekPosition - cover.Position;
            float d = Vector3.Dot(transform.right, off);
            return Mathf.Abs(d) < 0.05f ? (Random.value < 0.5f ? -1f : 1f) : Mathf.Sign(d);
        }

        /// <summary>Don't pop up where he's aiming: shift along low cover after a peek that drew fire.</summary>
        void MaybeShiftPeekSpot()
        {
            if (cover.Kind != CoverKind.Low) return;
            if (!(lastPeekHurt || (peeksHere >= 2 && Random.value < Traits.skill * 0.6f))) return;
            var toThreat = (ThreatEye - cover.Position).Flat().normalized;
            var side = Vector3.Cross(Vector3.up, toThreat) * (Random.value < 0.5f ? -1f : 1f);
            foreach (float d in new[] { 1.1f, -1.1f, 0.7f, -0.7f })
            {
                var p = cover.Position + side * d;
                if (!UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 0.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (CoverFinder.Classify(h.position, ThreatEye, out _) != CoverKind.Low) continue;
                cover.Position = h.position; cover.PeekPosition = h.position;
                MoveTo(h.position, 2.2f);
                peeksHere = 0;
                Note = "shifting peek spot";
                return;
            }
        }

        void TickPeek(bool seeing)
        {
            weaponRaised = true;
            bool low = cover.Kind == CoverKind.Low;
            switch (phase)
            {
                case Phase.Hidden:
                    crouch = low || cover.Kind == CoverKind.Full ? 1f : 0.3f;
                    if (Weapon.Ammo01 < 0.4f) Weapon.StartReload();
                    if (seeing) wantFire = true;
                    if (Time.time > phaseUntil && !Weapon.Reloading && Arrived())
                    {
                        if (cover.Kind == CoverKind.Full) { peekFails++; ActionDone = true; Mind.Poke(); break; }
                        phase = Phase.Expose;
                        exposeStart = Time.time;
                        sawDuringExposure = false;
                        float distNow = Perception.Belief.Has ? Vector3.Distance(transform.position, Perception.Belief.Predicted) : 20f;
                        peekType = ChoosePeek(distNow);
                        PeekCounts[(int)peekType]++;
                        peeksHere++;
                        lastPeekHurt = false;
                        peekSide = cover.Kind == CoverKind.Corner ? CornerSide() : 0f;
                        float baseDur = Mathf.Lerp(2.8f, 1.0f, Traits.caution) * Random.Range(0.75f, 1.3f) * (1f - Emotion.fear * 0.5f)
                                        * (1f + Mathf.Max(0f, Confidence) * 0.6f) * (1f - Mathf.Max(0f, -Confidence) * 0.35f);
                        switch (peekType)
                        {
                            case PeekType.Info: phaseUntil = Time.time + Random.Range(0.45f, 0.85f); Note = "info peek"; break;
                            case PeekType.Blind:
                                phaseUntil = Time.time + Random.Range(1.0f, 2.2f); Note = "blind fire";
                                if (!suppressing) Callouts.Say(this, Callout.Suppressing);
                                break;
                            case PeekType.Lean: phaseUntil = Time.time + baseDur * 0.75f; Note = "lean"; break;
                            default:
                                phaseUntil = Time.time + baseDur; Note = "step out";
                                if (cover.Kind == CoverKind.Corner) MoveTo(cover.PeekPosition, 2.6f);
                                break;
                        }
                    }
                    break;

                case Phase.Expose:
                    if (peekType == PeekType.Info)
                    {
                        // Head only: over low cover (half rise), or a quick low lean around a corner (below where he aims)
                        crouch = low ? 0.4f : 0.75f;
                        sideLean = low ? 0f : peekSide * 0.85f;
                        float d0 = Perception.Belief.Has ? Vector3.Distance(transform.position, Perception.Belief.Predicted) : 99f;
                        wantFire = seeing && d0 < 8f && !Weapon.Dry;
                        // Saw him and he isn't looking this way: take the shot from here
                        if (seeing && !Weapon.Dry && !PlayerKnowsMe && config.useCoverRepertoire) { peekType = PeekType.Lean; phaseUntil = Time.time + 1.2f; Note = "lean (spotted him)"; }
                        if (Time.time > phaseUntil) EndPeek();
                        break;
                    }
                    if (peekType == PeekType.Blind)
                    {
                        // Body stays tucked; the gun goes up over the cover or out around the corner
                        crouch = low ? 1f : 0.3f;
                        blindFire = 1f;
                        blindSide = low ? 0f : peekSide;
                        sideLean = low ? 0f : peekSide * 0.25f;
                        suppressing = true;
                        wantFire = true;
                        if (Time.time > phaseUntil || Weapon.Magazine <= 0) EndPeek();
                        break;
                    }
                    if (peekType == PeekType.Lean)
                    {
                        crouch = low ? 0.25f : 0.3f;
                        sideLean = low ? 0f : peekSide;
                    }
                    else crouch = 0f;
                    wantFire = seeing;
                    sawDuringExposure |= seeing;
                    if (!sawDuringExposure && Time.time - exposeStart > 1.1f)
                    {
                        // Nothing in sight: lay down fire on the last known spot if that helps the team, else duck back
                        var b = Perception.Belief;
                        bool covering = Time.time < Mind.CoverRequestedUntil
                                        || (Squad != null && (Squad.CountDoing(OpAction.Advance, this) + Squad.CountDoing(OpAction.Flank, this)) > 0);
                        bool suppressor = Style == CombatStyle.Suppressor || SecondaryStyle == CombatStyle.Suppressor;
                        bool affordable = !Weapon.Budgeted || Weapon.Total01 > 0.35f || covering;
                        if (b.Age < 3.5f && affordable && (covering || suppressor || Traits.aggression > 0.6f || Traits.discipline < 0.35f))
                        {
                            if (!suppressing && covering) Callouts.Say(this, Callout.Suppressing);
                            suppressing = true; wantFire = true;
                        }
                        else { peekFails++; EndPeek(); }
                    }
                    if (seeing) peekFails = 0;
                    if (Time.time > phaseUntil) EndPeek();
                    break;

                case Phase.Return:
                    crouch = low ? 1f : 0.3f;
                    sideLean = 0f; blindFire = 0f;
                    if (cover.Kind != CoverKind.Corner || Arrived())
                    {
                        phase = Phase.Hidden;
                        phaseUntil = Time.time + Mathf.Lerp(0.6f, 2.4f, Traits.patience) * Random.Range(0.7f, 1.4f) * (1f + Emotion.fear);
                        if (++peekCycles >= Random.Range(2, 5)) { ActionDone = true; Mind.Poke(); }
                        else MaybeShiftPeekSpot();
                    }
                    break;
            }
        }

        void EndPeek()
        {
            phase = Phase.Return;
            suppressing = false;
            sideLean = 0f;
            blindFire = 0f;
            if (cover.Kind == CoverKind.Corner) MoveTo(cover.Position, 2.8f);
        }

        void TickEngage(float dist, bool seeing)
        {
            weaponRaised = true;
            wantFire = seeing;
            faceThreat = true;
            if (planted) crouch = plantCrouch;
            if (Time.time > nextStrafe)
            {
                nextStrafe = Time.time + Random.Range(0.7f, 2.2f);
                planted = Random.value < Mathf.Lerp(0.15f, 0.4f, Traits.discipline);
                plantCrouch = planted && Random.value < Traits.discipline ? 1f : 0f;
                if (planted) Stop();
                else
                {
                    if (Random.value < 0.4f) strafeSide = -strafeSide;
                    var b = Perception.Belief;
                    var toThreat = (b.Predicted - transform.position).Flat().normalized;
                    var side = Vector3.Cross(Vector3.up, toThreat) * strafeSide;
                    float keep = config.useStandoff ? Mathf.Max(RangeMin, Standoff) : RangeMin;
                    float rangeFix = dist < keep ? (dist < Standoff * 0.8f ? -2f : -1f) : dist > RangeMax ? 1f : 0f;
                    var target = transform.position + side * Random.Range(1.5f, 4f) + toThreat * rangeFix * Random.Range(1f, 3f);
                    MoveTo(target, Mathf.Lerp(2f, 3.2f, Traits.aggression));
                }
            }
            if (!seeing && Perception.TimeSinceSeen > 1.5f) { ActionDone = true; Mind.Poke(); }
        }

        void PickSearchPoint()
        {
            var map = Squad != null ? Squad.Map : null;
            if (config.useOccupancySearch && map != null && map.HasMass && OccupancySearch(map)) return;
            LegacySearchPoint();
        }

        static readonly List<int> searchNodes = new();

        /// <summary>
        /// Pick the most probable unclaimed cluster on the squad's map (minus travel), then a vantage point that sees it
        /// from a standoff distance, reached by a route hidden from that spot.
        /// </summary>
        bool OccupancySearch(OccupancyMap map)
        {
            NavGraph.Query(transform.position, 55f, searchNodes);
            int bestNode = -1; float bestS = 0f;
            int checks = 0;
            // Top candidates by raw probability, then score by local mass and travel
            searchNodes.Sort((a, c) => map.P[c].CompareTo(map.P[a]));
            foreach (int k in searchNodes)
            {
                if (map.P[k] <= 1e-5f || ++checks > 30) break;
                var pos = NavGraph.Pos[k];
                float m = map.MassNear(pos, 3f);
                float s = m - Vector3.Distance(pos, transform.position) * 0.002f;
                if (Squad != null)
                    foreach (var o in Squad.Members)
                        if (o != null && o != this && !o.IsDead && o.SearchTarget.HasValue && (o.SearchTarget.Value - pos).sqrMagnitude < 36f) s -= m * 0.7f;
                if (s > bestS) { bestS = s; bestNode = k; }
            }
            if (bestNode < 0) return false;
            var target = NavGraph.Pos[bestNode];
            SearchTarget = target;
            Vector3 goal = target;
            if (config.useStandoff)
            {
                var v = CoverFinder.FindVantage(this, target, Standoff * 0.8f, Standoff + 6f);
                if (v.HasValue) goal = v.Value;
            }
            ClaimedSpot = goal;
            phase = Phase.Moving;
            bool stalky = Style == CombatStyle.Stalker;
            sneaking = stalky;
            float speed = stalky ? 1.9f : Traits.aggression > 0.5f ? archetype.runSpeed * 0.65f : archetype.walkSpeed * 1.2f;
            var planned = new List<Vector3>();
            if (UsesConcealedRoutes && ConcealedRoute.Plan(this, goal, target + Vector3.up * 1.5f, stalky, planned) && planned.Count > 0)
            {
                MoveTo(planned[0], speed);
                path.Clear(); path.AddRange(planned); pathIdx = 0;
            }
            else MoveTo(goal, speed);
            Note = "search: probable spot";
            return true;
        }

        void LegacySearchPoint()
        {
            SearchTarget = null;
            var b = Perception.Belief;
            var center = b.Has ? b.Predicted : transform.position;
            float radius = Mathf.Clamp(b.Uncertainty(config.uncertaintyGrowth), 3f, 16f);
            Vector3 best = center; float bestScore = float.MinValue;
            for (int i = 0; i < 14; i++)
            {
                var probe = center + Random.insideUnitSphere.Flat() * radius;
                if (!NavMesh.SamplePosition(probe, out var hit, 2f, NavMesh.AllAreas)) continue;
                var p = hit.position;
                float s = 0f;
                if (CoverFinder.Blocked(EyePosition, p + Vector3.up * 1.2f)) s += 1f;
                s -= Vector3.Distance(p, transform.position) * 0.04f;
                if (Squad != null && Squad.Crowded(p, this, 5f)) s -= 1.2f;
                s += Random.Range(-0.2f, 0.2f);
                if (s > bestScore) { bestScore = s; best = p; }
            }
            ClaimedSpot = best;
            phase = Phase.Moving;
            bool stalky = Style == CombatStyle.Stalker;
            sneaking = stalky;
            MoveTo(best, stalky ? 1.9f : Traits.aggression > 0.5f ? archetype.runSpeed * 0.65f : archetype.walkSpeed * 1.2f);
        }

        // ---------- Movement ----------

        void MoveTo(Vector3 p, float speed)
        {
            path.Clear();
            pathIdx = 0;
            moveTarget = p;
            moveSpeed = speed;
            moveSpeedScale = 1f;
            if (!agent.isOnNavMesh) return;
            // A target inside an obstacle (a noise behind a crate) resolves to the nearest reachable ground
            if (NavMesh.SamplePosition(p, out var hit, 2.5f, NavMesh.AllAreas) || NavMesh.SamplePosition(p, out hit, 7f, NavMesh.AllAreas)) moveTarget = hit.position;
            agent.SetDestination(moveTarget);
        }

        void Stop()
        {
            moveSpeed = 0f;
            path.Clear();
            if (agent.isOnNavMesh) agent.ResetPath();
        }

        bool Arrived()
        {
            if (!agent.isOnNavMesh) return true;
            if (agent.pathPending) return false;
            if (path.Count > 0 && pathIdx < path.Count - 1) return false;
            return !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.25f;
        }

        void UpdateMovement(float dt)
        {
            Vector3 face = transform.forward;
            var v = agent.velocity.Flat();
            if (dashing && v.sqrMagnitude > 0.2f) face = v;
            else if (engaged && weaponRaised) face = aim.Held.Flat();
            else if (attending)
            {
                var g = gunGaze.Flat();
                float off = v.sqrMagnitude > 0.2f ? Vector3.Angle(g, v) : 180f;
                if (!facingGaze && off > 105f) facingGaze = true;
                else if (facingGaze && off < 75f) facingGaze = false;
                face = facingGaze || v.sqrMagnitude <= 0.2f ? g : v;
            }
            else if (v.sqrMagnitude > 0.2f) face = v;
            else if (faceThreat) face = (Perception.Belief.Predicted - transform.position).Flat();
            if (dutyFace.sqrMagnitude > 0.01f && v.sqrMagnitude < 0.2f && !Perception.CanSee) face = dutyFace;
            // Walking somewhere without eyes on him: face the way I walk (head and gun can still turn about 50 deg either side)
            if (config.useSteadyMoves && v.magnitude > 1f && !Perception.CanSee && !dashing && Vector3.Angle(face, v) > 90f) face = v;
            if (face.sqrMagnitude > 0.001f)
            {
                float turn = Mathf.Lerp(260f, 520f, Traits.skill);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(face.normalized), turn * dt);
            }

            float forwardness = v.sqrMagnitude > 0.01f ? Vector3.Dot(v.normalized, transform.forward) : 1f;
            float dirFactor = Mathf.Lerp(0.6f, 1f, (forwardness + 1f) * 0.5f);
            float stagger = Time.time < staggerUntil ? 0.35f : 1f;
            float limp = Wound == Wound.Critical ? 0.7f : Wound == Wound.Wounded ? 0.86f : 1f;
            float crouchSlow = crouch > 0.5f && !sneaking && !ducking ? 0.45f : 1f;
            float baseSpeed = moveSpeed;
            if (config.useMoveDoctrine)
            {
                // Pace the dash: sprint across exposed ground or while fresh, otherwise a run with the gun low
                if (dashing && (Stamina > 0.45f || Perception.CanSee || Pressure > 0.3f)) baseSpeed = Mathf.Max(baseSpeed, archetype.sprintSpeed);
                if (Winded) baseSpeed = Mathf.Min(baseSpeed, archetype.runSpeed * 0.8f);   // blown: no sprinting until recovered
            }
            if (duty == Duty.Confront) baseSpeed = moveSpeed;   // catching up to a thief: no stamina limits
            agent.speed = baseSpeed * moveSpeedScale * dirFactor * stagger * crouchSlow * limp;
            TickStamina(dt, v.magnitude);
            TrackReversal(v);

            body.Velocity = agent.velocity;
            if (prefireActive && prefireStand) crouch = Mathf.Min(crouch, 0.15f);   // up over the cover for the prefire
            body.Crouch = crouch;
            body.WeaponRaised = weaponRaised || meleeStart >= 0f;
            body.AimDirection = aim.Direction(Weapon.Kick);
            body.Scanning = scanning;
            body.HandsUp = Surrendered;
            // Neutral and calm (a guard on patrol): gun holstered. Drawn as soon as there's a threat.
            bool holster = !HostileToPlayer && Perception.State < Alertness.Alert && !Surrendered;
            if (body.Holstered && !holster) SoundManager.PlayAt(Sfx.Equip, transform.position + Vector3.up * 1.1f, 0.5f, 0.08f);
            body.Holstered = holster;
            if (holster) body.WeaponRaised = false;
            body.SideLean = sideLean;
            body.BlindFire = blindFire;
            body.BlindSide = blindSide;
            body.ReloadProgress = Weapon.Reloading ? Weapon.ReloadProgress
                                : Mind.Current == OpAction.PatchUp ? (Time.time * 0.7f) % 1f : -1f;
            body.MeleeT = meleeStart >= 0f ? (Time.time - meleeStart) / 0.45f : -1f;
        }

        // ---------- Aim, fire, melee ----------

        void UpdateCombat(float dt)
        {
            var p = Target;
            var b = Perception.Belief;
            bool seeing = Perception.CanSee && p != null;
            var origin = MuzzlePosition;
            float dist = p != null ? Vector3.Distance(transform.position, p.Position) : 999f;

            prefireActive = DecidePrefire(seeing, out var prefireSpot);
            DecideAim(seeing, b);
            if (prefireActive && weaponRaised) { aimSource = AimSource.ThreatAxis; }
            Vector3 target;
            switch (aimSource)
            {
                case AimSource.Target:
                    if (seeing)
                    {
                        // Visual latency only (the aim model feeds the motion forward itself)
                        float lead = Mathf.Lerp(-0.1f, -0.02f, Traits.skill);
                        target = p.AimPoint(aimPart) + p.Velocity * lead;
                    }
                    else if (suppressing && b.Has) target = b.Position + Vector3.up * 1.1f;
                    else target = b.Has ? b.Predicted + Vector3.up * 1.45f : EyePosition + transform.forward * 10f;
                    break;
                case AimSource.ThreatAxis:
                    target = prefireActive ? prefireSpot : cqbAim.HasValue && Mind.Current == OpAction.ClearRoom ? cqbAim.Value : PreAimPoint();
                    if (config.useSteadyMoves && !seeing && !prefireActive)
                    {
                        var vel = agent.velocity.Flat();
                        if (vel.magnitude > 1f && Vector3.Angle(vel, (target - transform.position).Flat()) > 100f)
                        {
                            // Don't walk backwards: look where I'm going, at the next turn of the path
                            var ahead = agent.steeringTarget;
                            if ((ahead - transform.position).Flat().magnitude < 2f) ahead = transform.position + vel.normalized * 6f;
                            target = ahead + Vector3.up * 1.4f;
                            lookingAhead = true;
                        }
                        else lookingAhead = false;
                    }
                    else lookingAhead = false;
                    break;
                default:
                    target = b.Has ? b.Predicted + Vector3.up * 1.45f : EyePosition + LookForward * 10f;
                    break;
            }
            engaged = aimSource == AimSource.Target || aimSource == AimSource.ThreatAxis;
            attending = aimSource == AimSource.Gaze;
            if (attending)
            {
                var v = agent.velocity.Flat();
                Attention.Tick(v.sqrMagnitude > 0.2f ? v.normalized : transform.forward, v.sqrMagnitude > 0.2f);
                if (body.LookPoint == null) body.LookPoint = Attention.GazePoint;
                // Eyes lead, the gun follows: only a deliberate look (held a moment, planned long enough) moves the muzzle
                var g = Attention.GazeDir;
                bool deliberate = Time.time - Attention.FixStart > 0.3f && Attention.FixLength > 0.55f;
                if (deliberate && Time.time - gunGazeSince > 1.2f && Vector3.Angle(g, gunGaze) > 20f) { gunGaze = g; gunGazeSince = Time.time; }
            }

            if (weaponRaised || meleeStart >= 0f)
            {
                float selfSpeed = agent.velocity.magnitude;
                float lateral = 0f;
                if (seeing)
                {
                    var toP = (p.Position - transform.position).Flat();
                    var latV = Vector3.ProjectOnPlane(p.Velocity.Flat(), toP.normalized);
                    lateral = latV.magnitude / Mathf.Max(2f, dist);
                }
                float woundAim = Wound == Wound.Critical ? 1.35f : Wound == Wound.Wounded ? 1.15f : 1f;
                float err = Mathf.Lerp(2.4f, 0.45f, Traits.skill)
                            * (1f + Emotion.stress * 1.3f + (LastStand ? 0f : Emotion.fear * 0.5f))
                            * (1f + selfSpeed / 3f)
                            * (1f + Mathf.Min(lateral, 1.2f) * 1.5f)
                            * (suppressing ? 2.5f : 1f)
                            * (blindFire > 0.5f ? 2.6f : 1f)
                            * (config.useMoveDoctrine && Stamina < 0.3f ? 1f + (0.3f - Stamina) * 1.5f : 1f)
                            * (crouch > 0.5f && selfSpeed < 0.3f ? 0.8f : 1f)
                            * woundAim;
                if (engaged) aim.Track(origin, target, err, Traits.skill, dt);
                else if (attending) aim.Relax(gunGaze, dt, 3.5f);
                else aim.Relax(scanning ? LookForward : transform.forward, dt, 3f);
            }
            else aim.Relax(transform.forward + Vector3.down * 0.3f, dt, 4f);

            HoldFire = "";
            if (Broken || Surrendered) { wantFire = false; HoldFire = "broken"; return; }
            if (Weapon.Magazine <= 0 && !Weapon.Reloading && meleeStart < 0f) Weapon.StartReload();

            if (meleeStart >= 0f) TickMelee(p);
            else if (seeing && dist < archetype.meleeRange && Time.time > nextMelee && (archetype.canRush || Weapon.Reloading || Weapon.Magazine == 0 || dist < 1.2f))
            {
                meleeStart = Time.time;
                meleeHit = false;
                busyUntil = Time.time + 0.55f;
                Weapon.CancelReload();
                SoundManager.PlayAt(Sfx.Swing, transform.position + Vector3.up * 1.3f, 0.7f);
            }

            bool denyPeek = config.useCounterPeek && !seeing && Perception.PeekStreak >= 2 && Perception.TimeSinceSeen > 0.3f && Perception.TimeSinceSeen < 2.5f
                            && (Mind.Current == OpAction.Engage || Mind.Current == OpAction.HoldCover || Mind.Current == OpAction.PeekShoot)
                            && (Traits.aggression > 0.4f || Traits.discipline > 0.6f || Style == CombatStyle.Suppressor)
                            && (!Weapon.Budgeted || Weapon.Total01 > 0.3f) && aimSource == AimSource.ThreatAxis
                            && Vector3.Distance(target, Perception.LastSeenHead) < 1.5f;
            if (prefireActive) { denyPeek = true; Note = "prefire"; }
            if (prefireActive) { HoldFire = ""; }
            if (denyPeek) { wantFire = true; if (burstLeft > 3) burstLeft = 3; }
            if (!wantFire) { HoldFire = "no wantFire"; return; }
            if (meleeStart >= 0f) { HoldFire = "melee"; return; }
            if (!Weapon.CanFire) { HoldFire = Weapon.Reloading ? "reloading" : Weapon.Magazine <= 0 ? "empty" : "cycling"; return; }
            if (seeing && Time.time < Perception.SightStartTime + ReactionFor(p)) { HoldFire = "reacting"; return; }
            if (!seeing && !suppressing && !denyPeek) { HoldFire = "not seeing"; return; }
            if (Vector3.Distance(origin, target) > archetype.range) { HoldFire = "out of range"; return; }
            if (seeing && AmbushHold(p, dist)) { HoldFire = "ambush hold"; return; }

            if (Physics.Linecast(origin, target, out var blockHit, AILayers.World, QueryTriggerInteraction.Ignore))
            {
                HoldFire = "muzzle blocked";
                return;
            }
if (ArmoredGlassBetween(origin, target))
            {
                HoldFire = "armored glass";
                // Shooting it is pointless: get another angle (counts like a failed peek, which pushes flanking or advancing)
                if (Time.time > nextGlassNudge) { nextGlassNudge = Time.time + 2.5f; peekFails++; Mind.Poke(0.2f); }
                return;
            }
            if (AllyInLine(origin, target))
            {
                HoldFire = "ally in line";
                if (config.useContactDrill && Mind.Current == OpAction.Engage && Time.time > nextSidestep)
                {
                    nextSidestep = Time.time + Random.Range(1.2f, 2f);
                    var fwd = (target - origin).Flat().normalized;
                    var right = Vector3.Cross(Vector3.up, fwd);
                    float sgn = Random.value < 0.5f ? 1f : -1f;
                    for (int k = 0; k < 2; k++, sgn = -sgn)
                    {
                        var step = transform.position + right * sgn * Random.Range(1.6f, 2.6f);
                        if (UnityEngine.AI.NavMesh.SamplePosition(step, out var sh, 0.8f, UnityEngine.AI.NavMesh.AllAreas) && !AllyInLine(sh.position + Vector3.up * 1.5f, target))
                        { MoveTo(sh.position, archetype.runSpeed); Note = "clearing the line"; break; }
                    }
                }
                return;
            }

            float onTarget = Vector3.Angle(aim.Held, target - origin);
            float tolerance = Mathf.Max(1.2f, Mathf.Atan2(0.4f, Mathf.Max(1f, dist)) * Mathf.Rad2Deg) + aim.ErrorDegrees * 1.2f;
            if (Weapon.Budgeted && Weapon.Total01 < 0.3f && dist > 8f && !suppressing) tolerance *= 0.6f;   // make the rounds count
            if (onTarget > tolerance) { HoldFire = "aim off " + onTarget.ToString("0") + ">" + tolerance.ToString("0.0"); return; }

            if (burstLeft <= 0)
            {
                if (Time.time < burstPauseUntil) { HoldFire = "burst pause"; return; }
                float rangeK = Mathf.InverseLerp(40f, 8f, dist);
                int lo = archetype.burst.x, hi = archetype.burst.y;
                burstLeft = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(lo, hi, rangeK * (1f - Traits.discipline * 0.4f)) * Random.Range(0.7f, 1.3f)));
                if (suppressing) burstLeft = Mathf.Max(2, burstLeft / 2);
                if (Weapon.Budgeted && Weapon.Total01 < 0.3f && dist > 8f) burstLeft = Mathf.Min(burstLeft, 2);
                if (LastStand) burstLeft += 2;
                if (seeing) aimPart = PickAimPart(p, dist);
            }
            float movementSpread = agent.velocity.magnitude * 0.6f;
            // Opening fire on a fresh sighting: "engaging" (not every burst)
            if (seeing && Time.time - Perception.SightStartTime < 3f && Time.time - lastEngagingCall > 10f && Squad != null && Squad.Alive > 1 && Random.value < 0.6f)
            {
                lastEngagingCall = Time.time;
                Callouts.Say(this, Callout.Engaging, p.Position);
            }
            Weapon.Fire(aim.Direction(Weapon.Kick), movementSpread);
            ambushHoldStart = -100f;
            if (--burstLeft <= 0)
                burstPauseUntil = Time.time + Mathf.Lerp(0.6f, 0.16f, Traits.aggression) * Mathf.Clamp(dist / 20f, 0.5f, 2f) * Random.Range(0.7f, 1.4f);
        }

        /// <summary>
        /// Ambushers holding a spot don't fire the moment they see you: while you don't know they're there and
        /// you're still closing in, they let you come (up to a few seconds) for a surer first burst.
        /// </summary>
        bool AmbushHold(ICombatTarget p, float dist)
        {
            bool ambusher = Style == CombatStyle.Ambusher || (SecondaryStyle == CombatStyle.Ambusher && Traits.patience > 0.6f);
            bool holding = Mind.Current == OpAction.HoldCover || (Mind.Current == OpAction.PeekShoot && phase == Phase.Hidden);
            if (!ambusher || !holding || PlayerKnowsMe || dist < 11f) { ambushHoldStart = -100f; return false; }
            if (ambushHoldStart < 0f) { ambushHoldStart = Time.time; ambushLastDist = dist; Note = "waiting for him"; return true; }
            bool closing = dist < ambushLastDist - 0.05f;
            ambushLastDist = dist;
            if (Time.time - ambushHoldStart > Mathf.Lerp(2f, 6f, Traits.patience)) return false;
            if (!closing && Time.time - ambushHoldStart > 1.5f) return false;
            return true;
        }

        // ---------- Aim intention ----------
        // One committed decision about where the weapon points, so it never flits between sources:
        //   Target     - the player in sight (or suppressing, or melee); wins immediately.
        //   ThreatAxis - combat with a recent fix: hold where he most likely is (pre-aim), moving or hidden.
        //   Gaze       - searching or alert: the muzzle follows deliberate looks only.
        //   Rest       - low ready.
        // Apart from Target, a new source must wait for the current one to have held 0.6 s, and the threat axis
        // switches on under 15 s since last sight but only off after 20 s (hysteresis).
        enum AimSource { Rest, Gaze, ThreatAxis, Target }
        AimSource aimSource;
        float aimSourceSince;
        bool threatAxisOn;
        Vector3 gunGaze = Vector3.forward;
        float gunGazeSince, preAimLockedUntil;
        public string AimSourceName => aimSource.ToString();
        /// <summary>Debug: counts of aim source transitions (from>to, action).</summary>
        public static readonly Dictionary<string, int> AimTransitions = new();

        void DecideAim(bool seeing, Belief b)
        {
            bool combat = b.Has && Perception.State == Alertness.Combat && !Broken && !Surrendered;
            // The threat axis follows the action, which already has its own commitment: hold the line on him through
            // the fight; hand the muzzle to the eyes only when actually searching or investigating.
            bool searching = Mind.Current == OpAction.Search || Mind.Current == OpAction.Investigate || Mind.Current == OpAction.Patrol || Mind.Current == OpAction.Idle;
            if (!threatAxisOn && combat && !searching && Perception.TimeSinceSeen < 45f) threatAxisOn = true;
            else if (threatAxisOn && (!combat || searching || Perception.TimeSinceSeen > 60f)) threatAxisOn = false;
            bool holdingAngle = combat && (Mind.Current == OpAction.HoldCover || Mind.Current == OpAction.PeekShoot);

            AimSource want;
            if (!weaponRaised && meleeStart < 0f) want = AimSource.Rest;
            else if (seeing || suppressing || meleeStart >= 0f) want = AimSource.Target;
            else if (cqbAim.HasValue && Mind.Current == OpAction.ClearRoom) want = AimSource.ThreatAxis;   // clearing: the muzzle follows the procedure
            else if (config.usePreAim && (threatAxisOn || holdingAngle)) want = AimSource.ThreatAxis;
            else if (!config.usePreAim && combat && Perception.TimeSinceSeen < 8f) want = AimSource.ThreatAxis;
            else if (config.useAttention) want = AimSource.Gaze;
            else want = AimSource.Rest;
            if (Broken || Surrendered) want = AimSource.Rest;

            if (want == aimSource) return;
            bool preempt = want == AimSource.Target || Broken || Surrendered;
            if (!preempt && Time.time - aimSourceSince < 0.6f) return;
            if (want == AimSource.Gaze) gunGaze = aim.Held;
            var key = aimSource + ">" + want + (seeing ? "" : " (unseen)") + " in " + Mind.Current;
            AimTransitions[key] = AimTransitions.TryGetValue(key, out var n) ? n + 1 : 1;
            aimSource = want;
            aimSourceSince = Time.time;
        }

        Vector3 preAim, preAimPending;
        int preAimPendingHits;
        float nextPreAim;
        bool lookReasonDoor;
        public bool HoldingDoor => lookReasonDoor && !Perception.CanSee;
        static readonly List<int> preAimNodes = new();

        /// <summary>
        /// Pre-aim (holding an angle): from the spot I'll shoot from (the peek position, standing), the place he most
        /// likely is that I would actually see. That's the highest-probability ground in view of the peek eye, weighted
        /// toward closer. The muzzle waits there while hidden, so coming out of cover I'm already on him instead of
        /// swinging around to find him. Without a map it falls back to the predicted last-known position.
        /// </summary>
        Vector3 PreAimPoint()
        {
            if (Time.time < nextPreAim) return preAim;
            nextPreAim = Time.time + 0.4f;
            var b = Perception.Belief;
            var fallback = b.Predicted + Vector3.up * 1.45f;
            nextPreAim = Time.time + (agent.velocity.sqrMagnitude > 0.5f ? 0.25f : 0.4f);
            var from = (Mind.Current == OpAction.PeekShoot && cover.Valid ? cover.PeekPosition : transform.position) + Vector3.up * 1.6f;
            // Good information first: a fresh sighting, or a target that was standing still when last seen, is
            // still the best estimate, so hold right on it. Only guess from the map when that has gone stale.
            bool fresh = b.Uncertainty(config.uncertaintyGrowth) < 5f || (b.Velocity.magnitude < 0.8f && b.Age < 12f);
            if (config.useCounterPeek && Perception.TimeSinceSeen < 6f && !CoverFinder.Blocked(from, Perception.LastSeenHead))
            { preAim = Perception.LastSeenHead; return preAim; }   // he ducked: he comes back out where he went in
            // He's in another room and out of sight: hold the doorway he'd have to come through, not his spot through the wall
            if (config.useRoomGraph && !Perception.CanSee && DoorToward != null && MyRoom != ThreatRoom)
            {
                var door = DoorToward.Position + Vector3.up * 1.3f;
                if (Vector3.Distance(from, door) < 20f && !CoverFinder.Blocked(from, door)) { lookReasonDoor = true; return Commit(door, from); }
            }
            lookReasonDoor = false;
            if (fresh)
            {
                var onHim = b.Predicted + Vector3.up * 1.45f;
                if (config.useSpaceAim && CoverFinder.Blocked(from, onHim)) return Commit(RouteAim(b.Predicted), from);   // he's behind something: hold where he'd appear
                preAim = onHim; return preAim;   // dead reckoning stops at 1.5 s, so this is continuous
            }
            var map = Squad != null ? Squad.Map : null;
            if (map == null || !map.HasMass) { preAim = config.useSpaceAim && CoverFinder.Blocked(from, fallback) ? RouteAim(b.Predicted) : fallback; return preAim; }
            NavGraph.Query(from, 45f, preAimNodes);
            preAimNodes.Sort((x, y) => map.P[y].CompareTo(map.P[x]));
            bool peeking = Mind.Current == OpAction.PeekShoot || Mind.Current == OpAction.HoldCover;
            if (!peeking)
            {
                // On the move: hold the bearing to where he most likely is (through walls), so the muzzle is already
                // on him when a gap opens. The centroid of the top spots, weighted by probability.
                Vector3 sum = Vector3.zero; float w = 0f; int taken = 0;
                foreach (int k in preAimNodes)
                {
                    if (map.P[k] <= 1e-5f || ++taken > 12) break;
                    sum += NavGraph.Pos[k] * map.P[k]; w += map.P[k];
                }
                var centroid = w > 0f ? sum / w + Vector3.up * 1.4f : fallback;
                if (config.useSpaceAim && CoverFinder.Blocked(from, centroid)) return Commit(RouteAim(centroid - Vector3.up * 1.4f), from);
                return Commit(centroid, from);
            }
            float bestS = 0f; Vector3 best = fallback;
            int checks = 0;
            foreach (int k in preAimNodes)
            {
                if (map.P[k] <= 1e-5f || ++checks > 30) break;
                var t = NavGraph.Pos[k] + Vector3.up * 1.4f;
                if (CoverFinder.Blocked(from, t)) continue;
                // Likely, close to me, and close to where he was last seen
                float s = map.P[k] / (1f + Vector3.Distance(from, t) / 25f) / (1f + Vector3.Distance(NavGraph.Pos[k], b.Predicted) / 8f);
                if (s > bestS) { bestS = s; best = t; }
            }
            return Commit(best, from);
        }

        /// <summary>Adopt a new pre-aim spot only if it is meaningfully different and has stayed the best for two checks.</summary>
        Vector3 Commit(Vector3 candidate, Vector3 from)
        {
            if (preAim == Vector3.zero) { preAim = candidate; return preAim; }
            float change = Vector3.Angle(preAim - from, candidate - from);
            if (change < 10f) { preAimPendingHits = 0; return preAim; }
            if (change <= 70f && Time.time < preAimLockedUntil) return preAim;   // a recent decision stands
            if ((candidate - preAimPending).sqrMagnitude < 4f) preAimPendingHits++;
            else { preAimPending = candidate; preAimPendingHits = 1; }
            if (preAimPendingHits >= 2 || change > 70f) { preAim = candidate; preAimPendingHits = 0; preAimLockedUntil = Time.time + 2.5f; }
            return preAim;
        }

        static readonly RaycastHit[] allyHits = new RaycastHit[16];

        bool AllyInLine(Vector3 origin, Vector3 target)
        {
            var d = target - origin;
            float len = d.magnitude;
            if (len < 0.5f) return false;
            var pl = PlayerTarget.Instance;
            if (pl != null && !IsHostile(pl) && pl.Raycast(origin, d / len, len, out _, out _)) return true;   // never through a friendly player
            int n = Physics.SphereCastNonAlloc(origin, 0.4f, d / len, allyHits, len, 1 << AILayers.Actor, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = allyHits[i].collider.GetComponentInParent<Operative>();
                if (h != null && h != this && !h.IsDead && !IsHostile(h)) return true;
            }
            return false;
        }

        float ReactionFor(ICombatTarget p)
        {
            var b = Perception.Belief;
            bool holding = Mind.Current == OpAction.HoldCover || Mind.Current == OpAction.PeekShoot;
            bool expected = holding && ((b.Has && Vector3.Distance(b.Predicted, p.Position) < 2.5f) || Vector3.Distance(preAim, p.HeadPosition) < 3f);
            if (config.useCounterPeek && Perception.Repeek && Vector3.Angle(aim.Held, p.HeadPosition - MuzzlePosition) < 12f)
                return Traits.reaction * 0.35f * (1f + Emotion.stress * 0.2f);   // he came back where I'm aiming
            return Traits.reaction * (expected ? 0.6f : 1f) * (1f + Emotion.stress * 0.3f);
        }

        int PickAimPart(ICombatTarget p, float dist)
        {
            if (dist < 15f && p.Velocity.magnitude < 1f && Random.value < Traits.skill * 0.35f) return 2;
            return Random.value < 0.78f ? 1 : 0;
        }

        void TickMelee(ICombatTarget p)
        {
            float t = Time.time - meleeStart;
            if (!meleeHit && t > 0.22f && p != null)
            {
                meleeHit = true;
                var to = p.Position - transform.position;
                if (to.magnitude < archetype.meleeRange + 0.4f && Vector3.Angle(transform.forward, to.Flat()) < 70f)
                {
                    p.Health.TakeDamage(new DamageInfo
                    {
                        Amount = archetype.meleeDamage, Point = p.HeadPosition, Direction = to.normalized, Knockback = 6f, Source = gameObject,
                    });
                    SoundManager.PlayAt(Sfx.BluntHit, p.HeadPosition, 0.9f);
                    OnHitPlayer();
                }
            }
            if (t > 0.45f) { meleeStart = -1f; nextMelee = Time.time + Random.Range(0.9f, 1.5f); }
        }

        // ---------- Events ----------

        // ---------- Movement doctrine ----------
        // How a unit moves under fire: dash (sprint, gun low, no shooting), stop and shoot (run, plant for a burst, run on),
        // or fire on the move. Cover discipline (faction + role + traits) sets the habit; stamina limits sprinting.
        public enum MoveMode { Dash, StopAndShoot, FireOnMove }
        /// <summary>0 to 1: loose (walk and shoot) to strict (cover to cover under overwatch).</summary>
        public float Doctrine { get; private set; } = 0.5f;
        public float Stamina { get; private set; } = 1f;
        public MoveMode Mode { get; private set; } = MoveMode.FireOnMove;
        public bool Winded { get; private set; }
        /// <summary>0 to 1: exhaustion that builds over repeated sprints and fades slowly; it lowers and slows stamina recovery.</summary>
        public float Fatigue { get; private set; }
        bool movePlanted, dashing;
        float plantUntil, nextPlant;

        void ChooseMoveMode(OpAction a)
        {
            movePlanted = false;
            if (!config.useMoveDoctrine) { Mode = MoveMode.FireOnMove; return; }
            var b = Perception.Belief;
            float dist = b.Has ? Vector3.Distance(transform.position, b.Predicted) : 30f;
            float D = Doctrine;
            float fire = 0.15f + (1f - D) * 0.5f + Traits.aggression * 0.35f + (dist < 10f ? 0.35f : 0f) - D * 0.25f;
            float stop = 0.35f + Traits.skill * 0.2f + Traits.discipline * 0.1f + D * 0.15f;   // trained shooters stop to shoot
            float dash = 0.2f + D * 0.35f + Traits.caution * 0.25f + Mathf.Clamp01(Pressure) * 0.4f
                         + (a == OpAction.TakeCover ? 0.3f : 0f) + (a == OpAction.Flank ? 0.25f : 0f) - (Winded ? 0.5f : 0f);
            fire += Random.Range(-0.12f, 0.12f); stop += Random.Range(-0.12f, 0.12f); dash += Random.Range(-0.12f, 0.12f);
            Mode = dash >= stop && dash >= fire ? MoveMode.Dash : stop >= fire ? MoveMode.StopAndShoot : MoveMode.FireOnMove;
        }

        /// <summary>Per frame while moving: returns whether to shoot on the move; handles planting for a burst.</summary>
        bool TickMoveFire(bool canShoot, float dist, bool seeing)
        {
            if (seeing && dist < 6f) { movePlanted = false; return true; }   // point blank: shoot whatever the habit
            if (config.useSteadyMoves && Mode == MoveMode.Dash && seeing)
                Mode = Doctrine > 0.5f || dist > 20f ? MoveMode.StopAndShoot : MoveMode.FireOnMove;   // in view: fight while moving, never turn your back
            else if (config.useContactDrill && Mode == MoveMode.Dash && canShoot && !UnderFire && Time.time - Perception.SightStartTime > 1.2f)
                Mode = MoveMode.StopAndShoot;   // he's just standing there in view: stop and shoot him
            switch (Mode)
            {
                case MoveMode.FireOnMove: return canShoot && (config.useSteadyMoves || Traits.aggression > 0.3f || dist < 10f);
                case MoveMode.StopAndShoot:
                    if (movePlanted)
                    {
                        if (Time.time > plantUntil || !seeing) { movePlanted = false; nextPlant = Time.time + (config.useSteadyMoves ? Random.Range(1.0f, 2.0f) : Random.Range(2.2f, 4f)); }
                        return false;
                    }
                    if (canShoot && Time.time > nextPlant && Time.time - Perception.SightStartTime > 0.3f)
                    {
                        movePlanted = true;
                        plantUntil = Time.time + (config.useSteadyMoves ? Random.Range(0.9f, 1.6f) : Random.Range(0.7f, 1.4f));
                        Note = "stop and shoot";
                    }
                    return false;
                default: return false;
            }
        }

        void TickStamina(float dt, float speed)
        {
            if (!config.useMoveDoctrine) { Stamina = 1f; Winded = false; return; }
            // Breathing follows effort, wherever you are: standing recovers fastest, walking slower, running drains a little
            float fit = Mathf.Max(0.5f, archetype.fitness);
            float drain = 0f;
            if (speed > archetype.runSpeed * 1.05f) drain = 0.085f * dt;               // about 11 s of flat-out sprinting
            else if (speed > archetype.walkSpeed * 1.3f) drain = 0.012f * dt;
            if (drain > 0f)
            {
                Stamina -= drain;
                Fatigue = Mathf.Min(1f, Fatigue + drain * 0.22f / fit);                // every sprint leaves a little behind
            }
            else
            {
                // Recovery: standing fastest, walking slower; both slowed by accumulated fatigue
                float rate = (speed > 0.4f ? 0.07f : 0.14f) * (1f - 0.6f * Fatigue);
                Stamina += rate * dt;
                Fatigue = Mathf.Max(0f, Fatigue - (speed > 0.4f ? 0.0015f : 0.004f) * fit * dt);   // fades over minutes
            }
            Stamina = Mathf.Clamp(Stamina, 0f, 1f - 0.5f * Fatigue);                    // tired legs don't refill all the way
            if (Stamina < 0.1f) Winded = true;
            else if (Stamina > 0.4f) Winded = false;
        }

        /// <summary>Why the last fire check did not shoot (diagnostics; empty when it fired or nothing held it).</summary>
        public string HoldFire { get; private set; } = "";
        // ---------- Contact drill ----------
        float lastShotAt = -100f, combatSince = -100f, reactionFireUntil = -100f, nextSidestep;
        Alertness prevAlert;
        public bool UnderFire => Time.time - lastShotAt < 4f;
        public bool FreshContact => Time.time - combatSince < 3.5f;

        void TrackContact()
        {
            var st = Perception.State;
            if (st == Alertness.Combat && prevAlert != Alertness.Combat) combatSince = Time.time;
            prevAlert = st;
        }

        /// <summary>On entering a move to cover in the first moments of a fight: maybe open with a burst toward him or the shots.</summary>
        void MaybeReactionFire()
        {
            if (!config.useContactDrill || !(UnderFire || FreshContact) || Weapon.Dry || Weapon.Magazine <= 0) return;
            var b = Perception.Belief;
            if (!b.Has) return;
            float dist = Vector3.Distance(transform.position, b.Predicted);
            if (dist > 35f) return;
            float want = Traits.aggression * 0.6f + Traits.discipline * 0.3f + Mathf.Max(0f, Confidence) * 0.3f - Emotion.fear * 0.5f + Random.Range(-0.2f, 0.2f);
            if (want < 0.3f) return;
            reactionFireUntil = Time.time + Random.Range(0.7f, 1.4f);
            Note = Perception.CanSee ? "return fire" : "reaction fire";
        }

// ---------- Hit response ----------
        // Getting hit while fighting is a choice, not a reflex: push through (commit for a few seconds) or duck.
        // Close range, nerve, confidence, anger, winning the exchange, and having already ducked recently push toward committing;
        // fear, big hits, and low health toward ducking. Without this, peek - get hit - duck - peek becomes a farmable loop.
        float committedUntil = -100f;
        readonly List<float> hitDucks = new List<float>();
        public bool Committed => Time.time < committedUntil;
        public int RecentDucks => hitDucks.Count;
        public static readonly int[] HitCounts = new int[2];

        bool DecideHitResponse(DamageInfo info)
        {
            hitDucks.RemoveAll(t0 => Time.time - t0 > 20f);
            var a = Mind.Current;
            if (a != OpAction.Engage && a != OpAction.Rush && a != OpAction.PeekShoot && a != OpAction.Advance && a != OpAction.Flank) return false;
            if (!config.useHitCommit) { hitDucks.Add(Time.time); HitCounts[1]++; return false; }   // the old reflex: always duck
            var b = Perception.Belief;
            float dist = b.Has ? Vector3.Distance(transform.position, b.Predicted) : 20f;
            float frac = info.Amount / Mathf.Max(1f, Health.Max);
            float s = -0.25f + Traits.aggression * 0.4f + Traits.nerve * 0.35f + Mathf.Max(0f, Confidence) * 0.3f + Emotion.anger * 0.3f
                      + (dist < 10f ? 0.3f : dist < 18f ? 0f : -0.3f)   // up close, turning away is worse; at range, ducking is cheap
                      + hitDucks.Count * 0.22f
                      + (Time.time - Mind.LastHitOnPlayer < 3f ? 0.25f : 0f)
                      + (LastStand ? 0.6f : 0f) + (a == OpAction.Rush ? 0.3f : 0f)
                      - Emotion.fear * 0.4f - frac * 1.2f - (1f - Health.Normalized) * 0.4f
                      - (Wound == Wound.Critical && !LastStand ? 0.5f : 0f);
            s += Random.Range(-0.2f, 0.2f);
            if (s > 0.35f) { HitCounts[0]++; return true; }
            hitDucks.Add(Time.time);
            HitCounts[1]++;
            return false;
        }

// ---------- Space awareness ----------
        // Fixed distances (standoff, fighting range, cover and flank search radii) don't fit a 6 m room. The fight's space is
        // the smaller of my space and his (believed) space; indoors, the distances shrink to fit it.
        public SpaceInfo SpaceHere { get; private set; } = SpaceInfo.Open;
        public SpaceInfo SpaceThere { get; private set; } = SpaceInfo.Open;
        /// <summary>Size of the space the fight happens in (m).</summary>
        public float FightSpace { get; private set; } = 99f;
        public float Standoff { get; private set; } = 6f;
        public float RangeMin { get; private set; } = 10f;
        public float RangeMax { get; private set; } = 25f;
        public float CoverRadius { get; private set; } = 14f;
        float nextSpaceCheck;
        /// <summary>Layer 2: the authored room I'm in and the room he's believed to be in (null = outside or unmapped).</summary>
        public CombatRoom MyRoom { get; private set; }
        public CombatRoom ThreatRoom { get; private set; }
        /// <summary>The door of my room on the way to his room (what to hold when he's in another room).</summary>
        public CombatDoor DoorToward { get; private set; }

        void UpdateSpace()
        {
            if (Time.time < nextSpaceCheck) return;
            nextSpaceCheck = Time.time + 0.4f;
            float std = archetype.standoff, rmin = archetype.preferredRangeMin, rmax = archetype.preferredRangeMax, cr = config.coverSearchRadius;
            if (!config.useSpaceAwareness) { Standoff = std; RangeMin = rmin; RangeMax = rmax; CoverRadius = cr; FightSpace = 99f; return; }
            SpaceHere = NavGraph.SpaceAt(transform.position);
            var b = Perception.Belief;
            SpaceThere = b.Has ? NavGraph.SpaceAt(b.Predicted) : SpaceHere;
            FightSpace = Mathf.Min(SpaceHere.Size, SpaceThere.Size);
            if (config.useRoomGraph && RoomGraph.HasRooms)
            {
                MyRoom = RoomGraph.RoomAt(transform.position);
                ThreatRoom = b.Has ? RoomGraph.RoomAt(b.Predicted) : MyRoom;
                DoorToward = MyRoom != ThreatRoom ? RoomGraph.FirstDoorToward(MyRoom, ThreatRoom, transform.position, b.Predicted) : null;
            }
            else { MyRoom = ThreatRoom = null; DoorToward = null; }
            float room = FightSpace;
            Standoff = Mathf.Min(std, Mathf.Max(2.5f, room * 0.45f));
            RangeMin = Mathf.Min(rmin, Mathf.Max(3f, room * 0.6f));
            RangeMax = Mathf.Min(rmax, Mathf.Max(RangeMin + 4f, room * 1.6f));
            CoverRadius = Mathf.Min(cr, Mathf.Max(6f, room * 1.3f));
        }

// ---------- CQB: clearing a room (layer 3) ----------
        // Approach a stack point beside the door (off the line of fire through it), stack (wait a moment, and for a
        // partner if one is coming), pie the corner (ease toward the opening while the muzzle sweeps the room from the
        // far side to the near side), enter to a point along the wall (a second man takes the other side), and dominate
        // (sweep the corners), then call it clear and wipe the room from the squad's map. Contact at any point is a fight;
        // the entry itself is committed.
        enum CqbPhase { Approach, Stack, Pie, Entry, Dominate }
        CqbPhase cqbPhase;
        CombatRoom cqbRoom;
        Vector3 cqbStack, cqbThreshold, cqbEntry, cqbInto, cqbAlong;
        float cqbSide, cqbPhaseStart, cqbPhaseUntil;
        bool cqbSecond;
        Vector3? cqbAim;
        public CombatDoor CqbDoor { get; private set; }
        public float CqbSince { get; private set; }
        public string CqbState => Mind.Current == OpAction.ClearRoom && CqbDoor != null ? cqbPhase + " " + CqbDoor.name.Replace("Door_", "") + (cqbSecond ? " (2nd)" : "") : "";

        /// <summary>Can I clear his believed room: a walkable, non-stair door into it, and not just cleared by the squad.</summary>
readonly List<(Vector3 pt, float dwell)> cqbScan = new List<(Vector3, float)>();

        /// <summary>What to cover once inside: other doors of the room (where people come from), visible corners; a corridor's two ends.</summary>
        void BuildScanList()
        {
            cqbScan.Clear();
            if (cqbRoom == null) return;
            var eye = EyePosition;
            var eyeH = Vector3.up * 1.35f;
            var c = cqbRoom.Center; c.y = cqbEntry.y;
            var hs = cqbRoom.size * 0.5f;
            if (Mathf.Min(cqbRoom.size.x, cqbRoom.size.z) < 3f)
            {
                // Corridor: only its two ends
                var axis = cqbRoom.size.x > cqbRoom.size.z ? Vector3.right : Vector3.forward;
                float half = Mathf.Max(hs.x, hs.z) - 0.4f;
                foreach (float sgn in new[] { 1f, -1f }) cqbScan.Add((c + axis * sgn * half + eyeH, 1.0f));
                return;
            }
            foreach (var d in RoomGraph.Doors)
            {
                if (d == null || d == CqbDoor || !d.Connects(cqbRoom)) continue;
                var pt = d.Position + eyeH;
                if (!CoverFinder.Blocked(eye, pt)) cqbScan.Add((pt, 0.9f));
            }
            foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                var pt = new Vector3(c.x + sx * (hs.x - 0.5f), c.y, c.z + sz * (hs.z - 0.5f)) + eyeH;
                if ((pt - eye).Flat().magnitude < 1.5f) continue;                       // the corner I'm standing in
                if (!CoverFinder.Blocked(eye, pt)) cqbScan.Add((pt, 0.45f));
            }
        }

        bool CanClearThreatRoom()
        {
            if (!config.useCqb || ThreatRoom == null || ThreatRoom == MyRoom) return false;
            if (Squad != null && Squad.RecentlyCleared(ThreatRoom)) return false;
            foreach (var d in RoomGraph.Doors) if (d != null && d.Connects(ThreatRoom)) return true;
            return false;
        }

UnityEngine.AI.NavMeshPath cqbPath;

        /// <summary>Is there a walk to this door's near side that doesn't pass through the room I want to clear?</summary>
        bool ApproachAvoidsRoom(CombatDoor d, CombatRoom room, out float length)
        {
            length = Vector3.Distance(transform.position, d.Position);
            if (!agent.isOnNavMesh) return false;
            cqbPath ??= new UnityEngine.AI.NavMeshPath();
            var into = d.stairs ? StairInto(d) : SnapAxis(room.Center - d.Position);
            var near = d.stairs ? d.Position : d.Position - into * 0.9f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(near, out var nh, 1.0f, UnityEngine.AI.NavMesh.AllAreas)) return false;
            if (!UnityEngine.AI.NavMesh.CalculatePath(transform.position, nh.position, UnityEngine.AI.NavMesh.AllAreas, cqbPath) || cqbPath.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) return false;
            var cs = cqbPath.corners;
            length = 0f;
            for (int i = 1; i < cs.Length; i++) length += Vector3.Distance(cs[i - 1], cs[i]);
            // Any corner before the last inside the room: the way to this door goes through the room
            for (int i = 1; i < cs.Length - 1; i++) if (room.Contains(cs[i] + Vector3.up * 0.5f)) return false;
            return true;
        }

        /// <summary>For a stair door (at the top): the flat direction you face climbing, from the foot to the top.</summary>
        Vector3 StairInto(CombatDoor d)
        {
            // The foot is the nearest walkable point well below the top; climbing faces from it toward the room's center
            var c = cqbRoom != null ? cqbRoom.Center : d.Position;
            var v = (c - d.Position).Flat();
            return v.sqrMagnitude > 0.01f ? SnapAxis(v) : Vector3.forward;
        }

        static Vector3 SnapAxis(Vector3 v)
        {
            v.y = 0f;
            return Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        bool StartClear()
        {
            cqbRoom = ThreatRoom;
            CqbDoor = null; cqbAim = null;
            if (cqbRoom == null) return false;
            // Pick the door: nearest walkable one, but a third man (or a flanker, once someone has a door) takes another door
            bool flanker = Style == CombatStyle.Flanker || SecondaryStyle == CombatStyle.Flanker;
            CombatDoor best = null; float bestS = float.MaxValue;
            foreach (var d in RoomGraph.Doors)
            {
                if (d == null || !d.Connects(cqbRoom)) continue;
                if (!ApproachAvoidsRoom(d, cqbRoom, out float pathLen)) continue;   // e.g. the balcony door, reachable only from inside
                float cost = pathLen;
                int others = Squad != null ? Squad.Claimants(d, this, out _) : 0;
                bool anyClaimed = false;
                if (Squad != null) foreach (var d2 in RoomGraph.Doors) if (d2 != null && d2 != d && d2.Connects(cqbRoom) && Squad.Claimants(d2, this, out _) > 0) anyClaimed = true;
                if (others >= 2) cost += 12f;                          // two in the stack is enough
                if (others >= 1 && flanker) cost += 8f;                 // flankers go in another way
                if (others == 0 && anyClaimed && flanker) cost -= 4f;
                if (!UnityEngine.AI.NavMesh.SamplePosition(d.Position, out _, 1f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (cost < bestS) { bestS = cost; best = d; }
            }
            if (best == null) return false;
            CqbDoor = best;
            CqbSince = Time.time;
            if (best.stairs)
            {
                // Stairs: the foot is where the walk to the top starts climbing; then up, muzzle on the landing
                cqbInto = StairInto(best);
                cqbAlong = Vector3.Cross(Vector3.up, cqbInto);
                cqbSecond = Squad != null && Squad.Claimants(best, this, out _) > 0;
                cqbSide = 1f;
                cqbStack = transform.position;
                if (agent.isOnNavMesh && UnityEngine.AI.NavMesh.SamplePosition(best.Position, out var th, 1f, UnityEngine.AI.NavMesh.AllAreas))
                {
                    cqbPath ??= new UnityEngine.AI.NavMeshPath();
                    if (UnityEngine.AI.NavMesh.CalculatePath(transform.position, th.position, UnityEngine.AI.NavMesh.AllAreas, cqbPath))
                        foreach (var cn in cqbPath.corners) if (cn.y < best.Position.y - 1.5f) cqbStack = cn;   // the last corner still downstairs
                    cqbThreshold = th.position;
                }
                else cqbThreshold = best.Position;
                var landing = best.Position + cqbInto * 1.6f + cqbAlong * (cqbSecond ? 1.2f : -1.2f);
                cqbEntry = UnityEngine.AI.NavMesh.SamplePosition(landing, out var lh, 1.2f, UnityEngine.AI.NavMesh.AllAreas) && Mathf.Abs(lh.position.y - best.Position.y) < 1f ? lh.position : cqbThreshold;
                cqbPhase = CqbPhase.Approach;
                MoveTo(cqbStack, archetype.runSpeed);
                Note = "to the stairs";
                return true;
            }
            cqbInto = SnapAxis(cqbRoom.Center - best.Position);
            cqbAlong = Vector3.Cross(Vector3.up, cqbInto);
            Operative first = null;
            int ahead = Squad != null ? Squad.Claimants(best, this, out first) : 0;
            cqbSecond = ahead > 0;
            // Stack on one side of the opening; a second man stacks on the other side
            float side = cqbSecond && first != null ? -first.cqbSide : (Random.value < 0.5f ? 1f : -1f);
            Vector3 Sample(Vector3 p, Vector3 fallback) => UnityEngine.AI.NavMesh.SamplePosition(p, out var h, 0.8f, UnityEngine.AI.NavMesh.AllAreas) ? h.position : fallback;
            var stackA = best.Position - cqbInto * 0.9f + cqbAlong * side * 0.95f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(stackA, out var sh, 0.5f, UnityEngine.AI.NavMesh.AllAreas)) { side = -side; stackA = best.Position - cqbInto * 0.9f + cqbAlong * side * 0.95f; }
            cqbSide = side;
            cqbStack = Sample(stackA, best.Position - cqbInto * 1.2f);
            cqbThreshold = Sample(best.Position - cqbInto * 0.35f + cqbAlong * side * 0.3f, best.Position);
            // Entry: the first man crosses to the far side of the room's wall, the second hooks to his own side
            float entrySide = cqbSecond ? side : -side;
            float halfAlong = Mathf.Abs(Vector3.Dot(cqbAlong, cqbRoom.size)) * 0.5f;
            float lateral = Mathf.Min(1.8f, Mathf.Max(0.6f, halfAlong - 0.7f));
            var entry = best.Position + cqbInto * 1.5f + cqbAlong * entrySide * lateral;
            if (!cqbRoom.Contains(entry + Vector3.up * 0.5f)) entry = best.Position + cqbInto * 1.2f;
            cqbEntry = Sample(entry, best.Position + cqbInto * 1.0f);
            cqbPhase = CqbPhase.Approach;
            MoveTo(cqbStack, archetype.runSpeed);
            Note = (cqbSecond ? "stacking 2nd on " : "stacking on ") + best.name.Replace("Door_", "");
            return true;
        }

        void TickClear(bool seeing)
        {
            weaponRaised = true;
            faceThreat = false;
            if (CqbDoor == null || cqbRoom == null) { ActionDone = true; return; }
            if (seeing) wantFire = true;
            // Contact before the entry: it's a fight now, let the mind choose; in the entry, push through
            if (seeing && cqbPhase < CqbPhase.Entry) { ActionDone = true; Mind.Poke(); return; }
            if (Time.time - CqbSince > 30f) { ActionDone = true; Mind.Poke(); return; }
            var eyeH = Vector3.up * 1.35f;
            switch (cqbPhase)
            {
                case CqbPhase.Approach:
                    // On the way: hold where the route to the door leaves view, then the opening itself
                    cqbAim = config.useSpaceAim ? (CoverFinder.Blocked(EyePosition, CqbDoor.Position + eyeH) ? RouteAim(CqbDoor.Position) : CqbDoor.Position + eyeH) : CqbDoor.Position + cqbInto * 2.5f + eyeH;
                    if (Arrived())
                    {
                        cqbPhase = CqbPhase.Stack;
                        cqbPhaseStart = Time.time;
                        cqbPhaseUntil = Time.time + Random.Range(0.6f, 1.3f) * Mathf.Lerp(1.3f, 0.7f, Traits.aggression);
                        Stop();
                    }
                    break;

                case CqbPhase.Stack:
                    crouch = 0.25f;
                    {
                        // Stacked beside the door: the muzzle on the opening (the room beyond is out of view from here)
                        var opening = CqbDoor.Position + cqbInto * 0.4f + eyeH;
                        cqbAim = !config.useSpaceAim ? CqbDoor.Position + cqbInto * 3f + eyeH : CoverFinder.Blocked(EyePosition, opening) ? CqbDoor.Position + eyeH : opening;
                    }
                    {
                        // The first man waits (up to 3 s) for a partner who is on his way
                        bool waitPartner = false;
                        if (!cqbSecond && Squad != null)
                            foreach (var m in Squad.Members)
                                if (m != null && m != this && !m.IsDead && m.Mind.Current == OpAction.ClearRoom && m.CqbDoor == CqbDoor && m.cqbPhase == CqbPhase.Approach
                                    && Vector3.Distance(m.transform.position, transform.position) < 10f) waitPartner = true;
                        if (CqbDoor.stairs) { waitPartner = false; cqbPhaseUntil = Mathf.Min(cqbPhaseUntil, cqbPhaseStart + 0.4f); }
                        if (Time.time > cqbPhaseUntil && (!waitPartner || Time.time - cqbPhaseStart > 3f))
                        {
                            cqbPhase = CqbPhase.Pie;
                            cqbPhaseStart = Time.time;
                            cqbPhaseUntil = Time.time + Mathf.Lerp(1.8f, 1.1f, Traits.aggression) * Random.Range(0.85f, 1.15f);
                            MoveTo(cqbThreshold, archetype.walkSpeed * 0.8f);
                            Note = "pie " + CqbDoor.name.Replace("Door_", "");
                        }
                    }
                    break;

                case CqbPhase.Pie:
                    if (CqbDoor.stairs)
                    {
                        cqbAim = CqbDoor.Position + cqbInto * 2.5f + eyeH;
                        crouch = 0.1f;
                        if (Arrived() || Time.time - cqbPhaseStart > 9f)
                        {
                            cqbPhase = CqbPhase.Entry;
                            cqbPhaseStart = Time.time;
                            MoveTo(cqbEntry, archetype.runSpeed);
                            committedUntil = Time.time + 2.5f;
                            if (!cqbSecond) Callouts.Say(this, Callout.Breaching);
                            Note = "up into " + RoomGraph.Name(cqbRoom);
                        }
                        break;
                    }
                    {
                        // The muzzle sweeps from the far side of the room (seen first from this side) to the near side
                        float k = Mathf.Clamp01((Time.time - cqbPhaseStart) / Mathf.Max(0.1f, cqbPhaseUntil - cqbPhaseStart));
                        float ang = Mathf.Lerp(65f, -35f, k) * Mathf.Deg2Rad;
                        var dir = cqbInto * Mathf.Cos(ang) + cqbAlong * -cqbSide * Mathf.Sin(ang);
                        var pieAim = CqbDoor.Position + dir * 4f + eyeH;
                        if (!config.useSpaceAim || !Physics.Raycast(CqbDoor.Position + eyeH, dir, 1.5f, AILayers.World, QueryTriggerInteraction.Ignore)) cqbAim = pieAim;
                        crouch = 0.15f;
                        if (k >= 1f)
                        {
                            cqbPhase = CqbPhase.Entry;
                            cqbPhaseStart = Time.time;
                            MoveTo(cqbEntry, archetype.runSpeed);
                            committedUntil = Time.time + 2.5f;
                            if (!cqbSecond) Callouts.Say(this, Callout.Breaching);
                            Note = "entering " + RoomGraph.Name(cqbRoom);
                        }
                    }
                    break;

                case CqbPhase.Entry:
                    {
                        // Eyes on the deep corner across from where I'm heading
                        var c = cqbRoom.Center; c.y = cqbEntry.y;
                        var deep = c + cqbInto * Mathf.Abs(Vector3.Dot(cqbInto, cqbRoom.size)) * 0.3f - (cqbEntry - c).Flat() * 0.6f;
                        cqbAim = config.useSpaceAim && CoverFinder.Blocked(EyePosition, deep + eyeH) ? new Vector3(cqbRoom.Center.x, cqbEntry.y, cqbRoom.Center.z) + eyeH : deep + eyeH;
                        if (Arrived() || Time.time - cqbPhaseStart > 3f)
                        {
                            cqbPhase = CqbPhase.Dominate;
                            cqbPhaseStart = Time.time;
                            cqbPhaseUntil = Time.time + Random.Range(1.4f, 2.4f);
                            Stop();
                            BuildScanList();
                        }
                    }
                    break;

                case CqbPhase.Dominate:
                    {
                        crouch = 0.1f;
                        if (config.useSpaceAim && cqbScan.Count > 0)
                        {
                            // Cycle what matters: the room's other doors (longer dwell) and the corners I can actually see
                            float tt = Time.time - cqbPhaseStart, acc = 0f;
                            float total = 0f; foreach (var (_, dwell) in cqbScan) total += dwell;
                            tt %= Mathf.Max(0.1f, total);
                            foreach (var (pt, dwell) in cqbScan) { acc += dwell; if (tt <= acc) { cqbAim = pt; break; } }
                        }
                        else
                        {
                            // Sweep the room's four corners (pulled half a meter in)
                            var c = cqbRoom.Center; var hs = cqbRoom.size * 0.5f;
                            int idx = Mathf.FloorToInt((Time.time - cqbPhaseStart) / 0.55f) % 4;
                            var corner = new Vector3(c.x + (idx == 0 || idx == 3 ? -1f : 1f) * (hs.x - 0.5f), cqbEntry.y, c.z + (idx < 2 ? -1f : 1f) * (hs.z - 0.5f));
                            cqbAim = corner + eyeH;
                        }
                        if (Time.time > cqbPhaseUntil)
                        {
                            Squad?.Map?.ClearRoom(cqbRoom);
                            Squad?.MarkCleared(cqbRoom);
                            if (!cqbSecond || Random.value < 0.4f) Callouts.Say(this, Callout.RoomClear);
                            Note = RoomGraph.Name(cqbRoom) + " clear";
                            ActionDone = true;
                            Mind.Poke();
                        }
                    }
                    break;
            }
        }

// ---------- Aim from the space ----------
        // Where would he appear? Not "the bearing to his last position" (that points at walls, ceilings, floors), but where
        // the route toward him leaves my view: the corner he'd come around, the far end of the hall, the doorway, the top
        // of the stairs. The walking path is the AI's model of the space between us.
        UnityEngine.AI.NavMeshPath routePath;   // created on first use (Unity forbids it in field initializers)
        float nextRouteAim, routeHintTime = -100f;
        Vector3 routeAimTarget, routeAimPoint;
        /// <summary>The current route-aim point (where the way to him leaves view), if computed recently.</summary>
        public Vector3? RouteHint => config.useSpaceAim && Time.time - routeHintTime < 2f ? routeAimPoint : (Vector3?)null;

        Vector3 RouteAim(Vector3 target)
        {
            if (Time.time < nextRouteAim && (target - routeAimTarget).sqrMagnitude < 4f) return routeAimPoint;
            nextRouteAim = Time.time + 0.5f;
            routeAimTarget = target;
            var eye = EyePosition;
            var up = Vector3.up * 1.4f;
            routeAimPoint = target + up;
            if (!agent.isOnNavMesh || !UnityEngine.AI.NavMesh.SamplePosition(target, out var th, 3f, UnityEngine.AI.NavMesh.AllAreas)) return routeAimPoint;
            routePath ??= new UnityEngine.AI.NavMeshPath();
            if (!UnityEngine.AI.NavMesh.CalculatePath(transform.position, th.position, UnityEngine.AI.NavMesh.AllAreas, routePath) || routePath.corners.Length < 2) return routeAimPoint;
            var cs = routePath.corners;
            routeHintTime = Time.time;
            for (int i = 1; i < cs.Length; i++)
            {
                if ((cs[i] - eye).Flat().magnitude < 2.5f && i < cs.Length - 1) continue;   // a doorframe at my elbow is not where he appears
                if (!CoverFinder.Blocked(eye, cs[i] + up)) continue;
                if (i == 1)
                {
                    // The first leg is already out of view: look level along it
                    var d0 = (cs[1] - eye).Flat().normalized;
                    routeAimPoint = new Vector3(eye.x + d0.x * 4f, eye.y - 0.15f, eye.z + d0.z * 4f);
                    return routeAimPoint;
                }
                // The route turns out of view at the previous corner: hold that corner, a little toward where it goes
                var prev = cs[i - 1];
                var dir = (cs[i] - prev).Flat();
                float len = dir.magnitude;
                // Close corners: look past them along where the route goes (around the corner), not at the frame
                float reach = (prev - eye).Flat().magnitude < 3f ? 3f : 1.5f;
                routeAimPoint = prev + (len > 0.01f ? dir / len * Mathf.Min(reach, len) : Vector3.zero) + up;
                if (CoverFinder.Blocked(eye, routeAimPoint))
                {
                    // Past the corner is out of view: hold the corner itself, but at least 3 m out along my line to it
                    var toPrev = (prev - eye).Flat();
                    float dl = toPrev.magnitude;
                    routeAimPoint = dl < 3f && dl > 0.01f ? new Vector3(eye.x, prev.y + 1.4f, eye.z) + toPrev / dl * 3f : prev + up;
                }
                return routeAimPoint;
            }
            routeAimPoint = cs[cs.Length - 1] + up;   // the whole way to him is in view
            return routeAimPoint;
        }

// ---------- Diagnostics: mid-walk reversals ----------
        public static readonly Dictionary<string, int> Reversals = new Dictionary<string, int>();
        public static int MoveTicks, ReversalCount, BackTicks;
        bool lookingAhead;
        public bool LookingAhead => lookingAhead;
        readonly Vector3[] velRing = new Vector3[3];
        readonly string[] actRing = new string[3];
        int velIdx; float nextVelTick;

        void TrackReversal(Vector3 v)
        {
            if (Time.time < nextVelTick) return;
            nextVelTick = Time.time + 0.5f;
            var old = velRing[(velIdx + 1) % 3];   // 1 s ago
            string oldAct = actRing[(velIdx + 1) % 3];
            string now = Mind.Current.ToString();
            if (v.magnitude > 1.2f) { MoveTicks++; if (!Perception.CanSee && Vector3.Angle(transform.forward, v) > 110f) BackTicks++; }
            if (v.magnitude > 1.2f && old.magnitude > 1.2f && Vector3.Angle(v, old) > 135f)
            {
                ReversalCount++;
                var key = (oldAct == now ? now + " (same action)" : oldAct + " > " + now) + " | " + (Note.Length > 28 ? Note.Substring(0, 28) : Note);
                Reversals[key] = Reversals.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            velIdx = (velIdx + 1) % 3;
            velRing[velIdx] = v; actRing[velIdx] = now;
        }

public static readonly Dictionary<string, int> PrefireGates = new Dictionary<string, int>();
        public static readonly List<string> BlockSamples = new List<string>();
        bool prefireActive, prefireStand;
        float prefireStandUntil = -100f;

        /// <summary>
        /// The squad has seen him pop out of the same spot twice or more: whoever holds it fires on the spot from just before
        /// his learned hide time until a second after (Suppressors may add slow fire). Aimed at the spot's chest height.
        /// </summary>
        bool DecidePrefire(bool seeing, out Vector3 spot)
        {
            spot = default;
            prefireStand = false;
            if (!config.usePeekPrefire || !config.useCounterPeek || seeing || Squad == null) return false;
            if (Squad.PeekStreak < 2 || Squad.PeekGapCount < 1) return false;
            var a = Mind.Current;
            if (a == OpAction.Retreat || a == OpAction.Flee || a == OpAction.Surrender || a == OpAction.PatchUp || a == OpAction.ClearRoom || (a == OpAction.Rush && archetype.canRush)) return false;
            if (Weapon.Dry || Weapon.Reloading || (Weapon.Budgeted && Weapon.Total01 <= 0.3f) || Broken || Surrendered) return false;
            float since = Squad.SinceSeenBySquad, gap = Squad.PeekGap;
            var head = Squad.PeekSpot;
            if (Vector3.Distance(transform.position, head) > 40f) return false;
            bool holder = Squad.PeekHolder(head) == this;
            bool suppressor = Style == CombatStyle.Suppressor || SecondaryStyle == CombatStyle.Suppressor;
            bool onBeat = since > gap - 0.4f && since < gap + 1.0f;
            bool lineNow = !CoverFinder.Blocked(MuzzlePosition, head) || !CoverFinder.Blocked(MuzzlePosition, head - Vector3.up * 0.3f);
            bool active = (holder && onBeat) || (suppressor && lineNow && since > 0.3f && since < gap + 1.0f && Mathf.Repeat(Time.time * 0.7f + Traits.reaction, 1f) < 0.35f);
            if (!active) return false;
            // Aim where the muzzle has a line: chest, else head; if neither, rise up from behind low cover for it
            var mz = MuzzlePosition;
            var chest = head - Vector3.up * 0.3f;
            if (!CoverFinder.Blocked(mz, chest)) spot = chest;
            else if (!CoverFinder.Blocked(mz, head)) spot = head;
            else { spot = chest; prefireStandUntil = Time.time + 1.3f; }
            prefireStand = Time.time < prefireStandUntil;   // latched: rising clears the line, which must not drop him back down
            return true;
        }
        float lastHitTime = -100f;
        int volleyHits;
// ---------- Neutral (station security) ----------
        /// <summary>False for guards on a peaceful station: they ignore the player until StationSecurity calls a crime.</summary>
        public bool HostileToPlayer { get; private set; } = true;

        public void SetHostile(bool on, Vector3? lastKnown = null, float error = 3f)
        {
HostileToPlayer = on;
            var p = PlayerTarget.Instance;
            if (on)
            {
                if (p != null && lastKnown.HasValue)
                {
                    var c = ContactOf(p);
                    c.lastShotAtMe = Time.time; c.lastHeard = Time.time;
                    c.belief = new Belief { Has = true, Position = lastKnown.Value, Time = Time.time, Source = BeliefSource.Squad, BaseError = error };
                    if (Target == null || !HasNpcThreat) { FocusOn(p); Perception.ForceCombatAt(lastKnown.Value, error); }
                }
            }
            else
            {
                if (p != null) contacts.Remove(p);
                if (Target is PlayerTarget) FocusOn(null);
                if (!HasNpcThreat) calmAt = Time.time + Random.Range(2f, 8f);   // stand down on my own clock, not in lockstep
            }
            Mind.Poke(0.1f);
        }

// ---------- As a combat target (for other operatives) ----------
        Vector3 ICombatTarget.Position => transform.position;
        Vector3 ICombatTarget.Velocity => agent != null ? agent.velocity : Vector3.zero;
        Vector3 ICombatTarget.HeadPosition => EyePosition;
        Vector3 ICombatTarget.Forward => LookForward;
        bool ICombatTarget.NoClipping => false;
        bool ICombatTarget.IsCrouching => crouch > 0.5f;
        bool ICombatTarget.IsReloading => Weapon != null && Weapon.Reloading;
        bool ICombatTarget.FlashlightOn => false;
        float ICombatTarget.LastShotTime => Weapon != null ? Weapon.LastShotTime : -100f;
        public string DisplayName => Callsign;
        void ICombatTarget.GetBodyPoints(Vector3[] points)
        {
            points[0] = EyePosition;
            points[1] = ChestPosition;
            points[2] = transform.position + Vector3.up * Mathf.Lerp(0.95f, 0.6f, Mathf.Clamp01(crouch));
        }
        Vector3 ICombatTarget.AimPoint(int part) => part switch
        {
            2 => EyePosition + Vector3.down * 0.05f,
            1 => ChestPosition,
            _ => transform.position + Vector3.up * Mathf.Lerp(0.95f, 0.6f, Mathf.Clamp01(crouch)),
        };

        // ---------- Sides, contacts, and focus ----------
        // Who is hostile: the player per HostileToPlayer; other operatives by faction (Faction.hostileTo), never squadmates,
        // and always anyone who shot at me or whom I shot at (a grudge). Each unit keeps a short list of contacts (seen,
        // heard, shot at me) and focuses on one, re-picked every 0.4 s with a bonus for the current target, so it switches
        // when a more pressing threat appears but doesn't flicker.
        class Contact { public Belief belief; public float lastSeen = -100f, lastHeard = -100f, lastShotAtMe = -100f; public int sightings; }
        readonly Dictionary<ICombatTarget, Contact> contacts = new Dictionary<ICombatTarget, Contact>();
        readonly HashSet<Operative> grudges = new HashSet<Operative>();
        float nextScan, nextFocus, focusSince = -100f;
        public static int TargetSwitches;
        int scanCursor;
        static readonly List<ICombatTarget> candidates = new List<ICombatTarget>();
        static readonly Vector3[] scanPts = new Vector3[3];

        public ICombatTarget Target => Perception?.Target;
        public int ContactCount => contacts.Count;
        /// <summary>My squad's occupancy map is about the target I'm on.</summary>
        public bool MapIsMine => Squad == null || Squad.MapTarget == null || Squad.MapTarget == Target;
        /// <summary>An operative (not the player) I'm hostile to, seen or heard in the last 30 s.</summary>
        public bool HasNpcThreat
        {
            get
            {
                foreach (var kv in contacts)
                    if (kv.Key is Operative o && !o.IsDead && IsHostile(o) && Time.time - Mathf.Max(kv.Value.lastSeen, kv.Value.lastHeard, kv.Value.lastShotAtMe) < 30f) return true;
                return false;
            }
        }

        public bool IsHostile(ICombatTarget t)
        {
            if (t == null || t.IsDead || ReferenceEquals(t, this)) return false;
            if (t is PlayerTarget) return HostileToPlayer;
            if (t is Operative o)
            {
                if (Squad != null && o.Squad == Squad) return false;
                if (grudges.Contains(o) || o.grudges.Contains(this)) return true;
                return Faction.Hostile(faction, o.faction);
            }
            return false;
        }

        Contact ContactOf(ICombatTarget t)
        {
            if (!contacts.TryGetValue(t, out var c)) { c = new Contact(); contacts[t] = c; }
            return c;
        }

        public void NoteHeard(ICombatTarget t, Vector3 at, float err)
        {
            if (t == null) return;
            var c = ContactOf(t);
            c.lastHeard = Time.time;
            if (!c.belief.Has || c.belief.Age > 1f) c.belief = new Belief { Has = true, Position = at, Time = Time.time, Source = BeliefSource.Heard, BaseError = err };
            if (Target == null && IsHostile(t)) FocusOn(t);
        }

        public void NoteShotBy(ICombatTarget t, Vector3 from, float err)
        {
            if (t == null) return;
            var c = ContactOf(t);
            c.lastShotAtMe = Time.time;
            c.belief = new Belief { Has = true, Position = from, Time = Time.time, Source = BeliefSource.Shot, BaseError = err };
            if (Target == null || !IsHostile(Target)) FocusOn(t);
            else nextFocus = Mathf.Min(nextFocus, Time.time + Traits.reaction);   // reconsider soon
        }

        public void NoteReported(ICombatTarget t, Belief b)
        {
            if (t == null) return;
            var c = ContactOf(t);
            if (!c.belief.Has || c.belief.Time < b.Time) { c.belief = b; c.belief.Source = BeliefSource.Squad; }
            c.lastHeard = Mathf.Max(c.lastHeard, b.Time);
        }

        /// <summary>Switch focus to this target, keeping what I knew about the old one.</summary>
        public void FocusOn(ICombatTarget next)
        {
            if (next == Target || Perception == null) return;
            var old = Target;
            if (old != null)
            {
                var oc = ContactOf(old);
                oc.belief = Perception.Belief;
                oc.lastSeen = Mathf.Max(oc.lastSeen, Perception.LastSeenTime);
            }
            Contact nc = next != null ? ContactOf(next) : null;
            var known = nc != null ? nc.belief : default;
            if (old != null && next != null) TargetSwitches++;
            focusSince = Time.time;
            Perception.Retarget(next, known, nc != null ? nc.lastSeen : -100f);
            aim?.Acquire();
            if (Mind != null) Mind.Poke(0.15f);
            if (next != null && Squad != null && Squad.Leader == this) Squad.MapTarget = next;
        }

        /// <summary>Cheap sight checks on other hostile candidates (a few per tick), and the focus choice.</summary>
        void UpdateContacts()
        {
            // Default focus: the player, when I'm hostile to him and there's nothing else
            if (Target == null) { var pl = PlayerTarget.Instance; if (pl != null && IsHostile(pl) && !HasNpcThreat) FocusOn(pl); }
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.25f;
                candidates.Clear();
                var p = PlayerTarget.Instance;
                if (p != null && !p.IsDead && !p.NoClipping && IsHostile(p)) candidates.Add(p);
                foreach (var o in All) if (o != this && !o.IsDead && IsHostile(o)) candidates.Add(o);
                int n = Mathf.Min(3, candidates.Count);
                var eye = EyePosition; var fwd = LookForward;
                for (int k = 0; k < n; k++)
                {
                    var t = candidates[(scanCursor + k) % candidates.Count];
                    if (t == Target) continue;   // Perception watches the current target itself
                    t.GetBodyPoints(scanPts);
                    bool seen = false;
                    for (int i = 0; i < 2 && !seen; i++)
                    {
                        var to = scanPts[i] - eye;
                        float d = to.magnitude;
                        if (d > config.visionRange) continue;
                        if (Vector3.Angle(fwd, to) > Perception.EffectivePeripheral * 0.5f && d > 3f) continue;
                        if (!Physics.Linecast(eye, scanPts[i], AILayers.World, QueryTriggerInteraction.Ignore)) seen = true;
                    }
                    if (!seen) continue;
                    var c = ContactOf(t);
                    c.lastSeen = Time.time; c.sightings++;
                    c.belief = new Belief { Has = true, Position = t.Position, Velocity = t.Velocity, Time = Time.time, Source = BeliefSource.Seen };
                    if (c.sightings >= 2 && Perception.State < Alertness.Combat) Perception.ForceCombatAt(t.Position, 0.5f);
                    if (Target == null) FocusOn(t);
                }
                scanCursor = candidates.Count > 0 ? (scanCursor + n) % candidates.Count : 0;
            }
            if (Time.time < nextFocus) return;
            nextFocus = Time.time + 0.4f;
            // Forget the dead and the long gone
            List<ICombatTarget> drop = null;
            foreach (var kv in contacts)
                if (kv.Key == null || kv.Key.IsDead || (Time.time - Mathf.Max(kv.Value.lastSeen, kv.Value.lastHeard, kv.Value.lastShotAtMe) > 90f && kv.Key != Target))
                    (drop ??= new List<ICombatTarget>()).Add(kv.Key);
            if (drop != null) foreach (var k in drop) contacts.Remove(k);
            if (Target != null && (Target.IsDead || !IsHostile(Target))) FocusOn(null);
            // Pick the most pressing hostile contact
            ICombatTarget best = null; float bestS = float.MinValue;
            foreach (var kv in contacts)
            {
                var t = kv.Key; var c = kv.Value;
                if (t == null || t.IsDead || !IsHostile(t)) continue;
                // Only real evidence makes a target: seen, heard, or shot at me in the last minute (not just "exists")
                float evidence = Mathf.Max(c.lastSeen, c.lastHeard, c.lastShotAtMe, t == Target ? Perception.LastSeenTime : -100f);
                if (t != Target && Time.time - evidence > 60f) continue;
                float sinceSeen = t == Target ? Perception.TimeSinceSeen : Time.time - c.lastSeen;
                float s = (sinceSeen < 1.5f ? 2f : sinceSeen < 8f ? 1f : Time.time - c.lastHeard < 6f ? 0.6f : 0f)
                          + (Time.time - c.lastShotAtMe < 4f ? 1.6f : 0f)
                          + (Time.time - t.LastShotTime < 2f ? 0.4f : 0f)
                          - Vector3.Distance(transform.position, t.Position) * 0.03f
                          + (t == Target ? 0.7f : 0f);
                if (s > bestS) { bestS = s; best = t; }
            }
            // Hold a fresh focus for 2 s unless it's gone or someone else is hitting me right now
            bool hitByOther = best != null && contacts.TryGetValue(best, out var bc) && Time.time - bc.lastShotAtMe < 1f;
            bool canSwitch = Target == null || Time.time - focusSince > 2f || hitByOther;
            if (best != null && best != Target && bestS > 0.3f && canSwitch) FocusOn(best);
        }

// ---------- Cooling down (security after a fight) ----------
        // A unit that isn't hostile to the player by nature (a guard) relaxes on its own clock once the threat is gone:
        // a random 5 to 14 s after the last hostile (2 to 8 s after a stand-down), then it holsters and goes back to work.
        float calmAt = -1f;

        void UpdateCooldown()
        {
            if (HostileToPlayer || faction == null || faction.hostileToPlayer) { calmAt = -1f; return; }
            bool clear = !HasNpcThreat && (Target == null || Target.IsDead);
            if (!clear || Perception.State < Alertness.Alert) { calmAt = -1f; return; }
            if (calmAt < 0f) calmAt = Time.time + Random.Range(5f, 14f);
            else if (Time.time >= calmAt)
            {
                calmAt = -1f;
                Perception.Calm();
                ActionDone = true;
                Mind.Poke(0.1f);
            }
        }

// ---------- On duty (calm security life) ----------
        // A unit that isn't hostile to the player by nature (a guard), calm and unaware, goes about the job like the
        // residents do: walks its route, wanders, stops at a window, the railing, or a console, and chats with another
        // guard (meeting halfway, facing each other, taking turns). No sitting. Any threat ends it at once.
        public bool OnDuty => !HostileToPlayer && faction != null && !faction.hostileToPlayer;
enum Duty { Choose, Walk, Look, Chat, Confront }
        Transform confrontTarget;
        System.Action<Operative> confrontArrived;
        bool confrontReached;

        /// <summary>Calm duty only: walk briskly up to someone and stand facing them (security collecting a fine).
        /// Calls back once on arrival; holds until EndConfront.</summary>
        public bool Confront(Transform target, System.Action<Operative> arrived)
        {
            if (!OnDuty || IsDead || Mind.Current != OpAction.Patrol || target == null) return false;
            EndDuty();
            confrontTarget = target; confrontArrived = arrived; confrontReached = false;
            duty = Duty.Confront; dutyUntil = Time.time + 90f;
            Note = "confronting";
            return true;
        }

        public void EndConfront()
        {
            if (duty != Duty.Confront) return;
            confrontTarget = null; confrontArrived = null;
            duty = Duty.Choose; dutyUntil = Time.time + Random.Range(1f, 3f);
            dutyFace = Vector3.zero; body.LookPoint = null;
        }

        public bool Confronting => duty == Duty.Confront;
        Duty duty;
        float dutyUntil;
        Vector3 dutyFace;
        Alcax.Dialogue.StationPoint dutySpot;
        Operative chatMate;
        bool chatLeader, myTurn;
        float nextTurn;
        static readonly HashSet<Alcax.Dialogue.StationPoint> claimedSpots = new HashSet<Alcax.Dialogue.StationPoint>();

        void StartDuty() { EndDuty(); duty = Duty.Choose; dutyUntil = Time.time + Random.Range(0.5f, 2f); }

        void EndDuty()
        {
            if (dutySpot != null) { claimedSpots.Remove(dutySpot); dutySpot = null; }
            if (chatMate != null) { var m = chatMate; chatMate = null; if (m.chatMate == this) { m.chatMate = null; m.duty = Duty.Choose; m.dutyUntil = Time.time + 1f; m.dutyFace = Vector3.zero; } }
            dutyFace = Vector3.zero;
            body.LookPoint = null;
        }

        bool FreeToChat => Mind.Current == OpAction.Patrol && OnDuty && chatMate == null && (duty == Duty.Walk || duty == Duty.Choose || duty == Duty.Look);

        void TickDuty()
        {
            weaponRaised = false;
            if (UpdateTalk()) return;
            switch (duty)
            {
                case Duty.Choose:
                    if (Time.time < dutyUntil) break;
                    {
                        float r = Random.value;
                        if (r < 0.3f && route != null && route.Count > 0) { patrolIndex++; MoveTo(route.Point(patrolIndex), archetype.walkSpeed); duty = Duty.Walk; dutyUntil = Time.time + 40f; Note = "patrol"; }
                        else if (r < 0.52f) { if (!WanderStep()) dutyUntil = Time.time + 1f; }
                        else if (r < 0.82f) { if (!GoToSpot()) { if (!WanderStep()) dutyUntil = Time.time + 1f; } }
                        else if (!InviteChat()) { if (!WanderStep()) dutyUntil = Time.time + 1f; }
                    }
                    break;
                case Duty.Walk:
                    if (Arrived() || Time.time > dutyUntil)
                    {
                        Stop();
                        if (dutySpot != null)
                        {
                            duty = Duty.Look; dutyUntil = Time.time + Random.Range(8f, 20f);
                            dutyFace = dutySpot.transform.forward.Flat();
                            Note = dutySpot.kind == Alcax.Dialogue.PointKind.Console ? "at a console" : "looking out";
                        }
                        else { duty = Duty.Choose; dutyUntil = Time.time + Random.Range(2f, 6f); dutyFace = Vector3.zero; }
                    }
                    break;
                case Duty.Look:
                    scanning = dutySpot == null || dutySpot.kind != Alcax.Dialogue.PointKind.Console;
                    if (Time.time > dutyUntil) { if (dutySpot != null) { claimedSpots.Remove(dutySpot); dutySpot = null; } duty = Duty.Choose; dutyUntil = Time.time + Random.Range(1f, 3f); dutyFace = Vector3.zero; }
                    break;
case Duty.Confront:
                    scanning = false;
                    if (confrontTarget == null) { EndConfront(); break; }
                    {
                        var to = (confrontTarget.position - transform.position).Flat();
                        float d = to.magnitude;
                        if (d > 2.2f)
                        {
                            // Faster than you: 1.2x your sprint speed, no stamina limit; easing to a walk over the last few meters
                            var pc = PlayerTarget.Instance != null ? PlayerTarget.Instance.GetComponent<Alcax.Player.PlayerController>() : null;
                            float top = (pc != null ? pc.SprintSpeed : 7f) * 1.2f;
                            float chase = d > 5f ? top : Mathf.Lerp(archetype.walkSpeed * 1.3f, top, (d - 2.2f) / 2.8f);
                            MoveTo(confrontTarget.position, chase);
                        }
                        else
                        {
                            Stop();
                            if (to.sqrMagnitude > 0.01f) dutyFace = to.normalized;
                            body.LookPoint = confrontTarget.position + Vector3.up * 1.6f;
                            if (!confrontReached) { confrontReached = true; var cb = confrontArrived; cb?.Invoke(this); }
                        }
                    }
                    break;
                case Duty.Chat:
                    scanning = false;
                    if (chatMate == null || chatMate.IsDead || chatMate.chatMate != this || Time.time > dutyUntil) { EndDuty(); duty = Duty.Choose; dutyUntil = Time.time + Random.Range(2f, 5f); break; }
                    if (!Arrived()) break;
                    {
                        var to = (chatMate.transform.position - transform.position).Flat();
                        if (to.sqrMagnitude > 0.01f) dutyFace = to.normalized;
                        body.LookPoint = chatMate.EyePosition;
                        bool both = chatMate.Arrived();
                        if (both && chatLeader && Time.time > nextTurn)
                        {
                            nextTurn = Time.time + Random.Range(1.8f, 4f);
                            myTurn = !myTurn; chatMate.myTurn = !myTurn;
                        }
                        if (both && myTurn && Random.value < Time.deltaTime * 3f)
                            SoundManager.PlayAt(Sfx.TalkBlip, EyePosition, 0.1f, 0.25f);
                    }
                    break;
            }
        }

        bool WanderStep()
        {
            var path = new UnityEngine.AI.NavMeshPath();
            for (int i = 0; i < 8; i++)
            {
                var guess = transform.position + new Vector3(Random.Range(-12f, 12f), 0f, Random.Range(-12f, 12f));
                if (!UnityEngine.AI.NavMesh.SamplePosition(guess, out var h, 1.5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (Mathf.Abs(h.position.y - transform.position.y) > 2f) continue;
                if (!agent.isOnNavMesh || !agent.CalculatePath(h.position, path) || path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete) continue;
                MoveTo(h.position, archetype.walkSpeed);
                duty = Duty.Walk; dutyUntil = Time.time + 30f; Note = "wandering";
                return true;
            }
            return false;
        }

        bool GoToSpot()
        {
            Alcax.Dialogue.StationPoint best = null; float bd = float.MaxValue;
            foreach (var p in Alcax.Dialogue.StationPoint.All)
            {
                if (p == null || p.kind == Alcax.Dialogue.PointKind.Seat || p.Occupant != null || claimedSpots.Contains(p)) continue;
                float d = Vector3.Distance(p.transform.position, transform.position) + Random.Range(0f, 15f);
                if (d > 45f || Mathf.Abs(p.transform.position.y - transform.position.y) > 7f) continue;
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) return false;
            claimedSpots.Add(best);
            dutySpot = best;
            MoveTo(best.transform.position, archetype.walkSpeed);
            duty = Duty.Walk; dutyUntil = Time.time + 40f; Note = "going to " + best.kind.ToString().ToLower();
            return true;
        }

        bool InviteChat()
        {
            Operative mate = null; float bd = 30f;
            foreach (var o in All)
            {
                if (o == this || o.IsDead || !o.FreeToChat) continue;
                float d = Vector3.Distance(o.transform.position, transform.position);
                if (d < bd && Mathf.Abs(o.transform.position.y - transform.position.y) < 2f) { bd = d; mate = o; }
            }
            if (mate == null) return false;
            var mid = (transform.position + mate.transform.position) * 0.5f;
            if (!UnityEngine.AI.NavMesh.SamplePosition(mid, out var h, 3f, UnityEngine.AI.NavMesh.AllAreas)) return false;
            var axis = (mate.transform.position - transform.position).Flat();
            axis = axis.sqrMagnitude > 0.01f ? axis.normalized : transform.forward;
            if (mate.dutySpot != null) { claimedSpots.Remove(mate.dutySpot); mate.dutySpot = null; }
            float length = Random.Range(15f, 30f);
            chatMate = mate; mate.chatMate = this;
            chatLeader = true; mate.chatLeader = false;
            myTurn = true; mate.myTurn = false;
            duty = Duty.Chat; mate.duty = Duty.Chat;
            dutyUntil = Time.time + length; mate.dutyUntil = dutyUntil;
            MoveTo(h.position - axis * 0.65f, archetype.walkSpeed);
            mate.MoveTo(h.position + axis * 0.65f, mate.archetype.walkSpeed);
            Note = "chatting"; mate.Note = "chatting";
            return true;
        }

// ---------- Talking (guards on duty) ----------
        Alcax.Dialogue.Npc talk;
        bool wasTalking;

        /// <summary>Calm and on duty: the player can talk to me. Returns true while a conversation holds me in place.</summary>
        bool UpdateTalk()
        {
            if (talk == null) return false;
            if (talk.displayName == "Officer" || string.IsNullOrEmpty(talk.displayName)) talk.displayName = "Officer " + Callsign;
            talk.CanTalk = OnDuty && !IsDead && Perception.State < Alertness.Alert && Mind.Current == OpAction.Patrol;
            if (!talk.InConversation)
            {
                if (wasTalking) { wasTalking = false; body.LookPoint = null; dutyFace = Vector3.zero; if (duty != Duty.Confront) { duty = Duty.Choose; dutyUntil = Time.time + Random.Range(1f, 3f); } }
                return false;
            }
            wasTalking = true;
            var p = PlayerTarget.Instance;
            Stop();
            if (p != null)
            {
                var to = (p.Position - transform.position).Flat();
                if (to.sqrMagnitude > 0.01f) dutyFace = to.normalized;
                body.LookPoint = p.HeadPosition;
            }
            return true;
        }

// ---------- Armored glass ----------
        // Visibly cracked glass (2+ hits) or glass my own round bounced off: don't waste ammo on it, move instead.
        readonly HashSet<GlassPane> knownArmored = new HashSet<GlassPane>();
        float nextGlassNudge;
        public void NoteArmoredGlass(GlassPane p) { if (p != null) knownArmored.Add(p); }

        bool ArmoredGlassBetween(Vector3 from, Vector3 to)
        {
            if (!Physics.Linecast(from, to, out var hit, GlassPane.Mask, QueryTriggerInteraction.Ignore)) return false;
            var pane = hit.collider.GetComponent<GlassPane>();
            return pane != null && pane.Armored && !pane.Broken && (knownArmored.Contains(pane) || pane.Hits >= 2);
        }

        bool calledDry;
        float noAdvanceCoverTime = -100f;
        public void OnOutOfAmmo()
        {
            if (calledDry) return;
            calledDry = true;
            Callouts.Say(this, Callout.OutOfAmmo, force: true);
            Mind.Poke(0.1f);
        }

        public void OnReloadStarted()
        {
            if (Perception.State == Alertness.Combat && Squad != null && Squad.Alive > 1 && Traits.teamwork > 0.3f)
                Callouts.Say(this, Callout.Reloading);
        }

        public void OnHitPlayer()
        {
            Mind.OnHitPlayer();
            Squad?.OnMemberHitPlayer();
            playerHurtUntil = Time.time + 3f;
            if (Random.value < 0.3f && Squad != null && Squad.Alive > 1) Callouts.Say(this, Callout.HesHit);
        }

        public void OnNearMiss(float closeness, Vector3 shooter, ICombatTarget who = null)
        {
            if (IsDead) return;
            if (who != null && !IsHostile(who)) return;   // a friend's rounds going past: no suppression from friends
            Pressure = Mathf.Clamp01(Pressure + closeness * 0.5f);
            lastShotAt = Time.time;
            Mind.OnSuppressed(closeness);
            Perception.OnShot(shooter, who);
        }

        void UpdateWound()
        {
            float h = Health.Normalized;
            Wound = h < 0.3f ? Wound.Critical : h < 0.6f ? Wound.Wounded : Wound.Healthy;
            if (Wound != Wound.Critical) LastStand = false;
        }

        void OnDamaged(DamageInfo info)
        {
            if (IsDead) return;
            var attacker = Targets.Of(info.Source);
            var from = attacker != null ? attacker.HeadPosition : info.Point - info.Direction * 10f;
            if (attacker is Operative ao && ao != this && (Squad == null || ao.Squad != Squad)) grudges.Add(ao);
            if (attacker is PlayerTarget pt && !HostileToPlayer && StationSecurity.Instance == null) SetHostile(true, pt.Position, 1f);
            lastHitDir = info.Direction.sqrMagnitude > 0.01f ? info.Direction.normalized : -transform.forward;
            lastHitPoint = info.Point;
            bool commit = Health.Current > 0f && DecideHitResponse(info);
            if (commit) { committedUntil = Time.time + Random.Range(2.5f, 4f); Note = "pushing through"; }
            Perception.OnShot(from, attacker);
            Mind.OnDamaged(info);
            Squad?.AddDanger(transform.position, info.Amount / 30f);
            // Killing blows count as a loss (Squad.ReportDeath), not as an injury
            if (Health.Current > 0f) Squad?.OnMemberHurt(this, Mathf.Clamp01(info.Amount / Mathf.Max(1f, Health.Max)));
            Pressure = Mathf.Clamp01(Pressure + (commit ? 0.25f : 0.6f));
            lastShotAt = Time.time;
            body.Flinch(info.Direction, Mathf.Clamp(info.Amount / 30f, 0.3f, 1.2f));
            staggerUntil = Time.time + Mathf.Lerp(0.15f, 0.45f, info.Amount / 40f) * (config.useHitStunLimit && Time.time - lastHitTime < 0.8f ? 0.4f : 1f);
            // Hit-stun: the first hit of a volley breaks the burst; follow-ups within 0.8 s only add a shrinking flinch
            bool volley = config.useHitStunLimit && Time.time - lastHitTime < 0.8f;
            volleyHits = volley ? volleyHits + 1 : 0;
            lastHitTime = Time.time;
            float kickScale = volley ? 1f / (1f + volleyHits) : 1f;
            Weapon.Kick += new Vector2(Random.Range(1f, 5f), Random.Range(-4f, 4f)) * kickScale;
            if (!volley && !(config.useHitStunLimit && commit))
            {
                burstLeft = 0;
                burstPauseUntil = Time.time + Random.Range(0.15f, 0.4f);
            }
            if (Mind.Current == OpAction.PeekShoot && phase == Phase.Expose)
            {
                if (commit) phaseUntil = Mathf.Max(phaseUntil, Time.time + Random.Range(0.6f, 1.2f));   // stay up and finish him
                else
                {
                    EndPeek(); lastPeekHurt = true;
                    if (config.useHitCommit && RecentDucks >= 2) peekFails++;   // this spot keeps getting me shot: go somewhere else
                }
            }
            if (Mind.Current == OpAction.PatchUp) ActionDone = true;
            if (Random.value < 0.55f) SoundManager.PlayAt(Sfx.NpcGrunt, EyePosition, 0.7f, 0.1f);
            SoundManager.PlayAt(Sfx.BodyHit, info.Point, 0.6f);

            // Wound states: hurt makes them careful; critical splits them into fleeing or fighting to the end
            var before = Wound;
            if (Health.Current <= 0f) return;   // this hit killed; the death handler takes over
            float hAfter = (Health.Current) / Health.Max;
            Wound = hAfter < 0.3f ? Wound.Critical : hAfter < 0.6f ? Wound.Wounded : Wound.Healthy;
            if (Wound > before)
            {
                if (Wound == Wound.Wounded) Callouts.Say(this, Callout.ImHit, force: Random.value < 0.6f);
                else
                {
                    bool alone = Squad == null || Squad.Alive <= 1;
                    float styleGrit = Style == CombatStyle.Brawler ? 0.12f : Style == CombatStyle.Survivor ? -0.25f : 0f;
                    float grit = Traits.aggression * 0.55f + Traits.nerve * 0.45f + styleGrit + (alone ? 0.12f : 0f) + Confidence * 0.2f + Random.Range(-0.15f, 0.15f);
                    LastStand = grit > 0.78f;
                    if (LastStand) { Emotion.anger = 1f; Callouts.Say(this, Callout.LastStand, force: true); }
                    else Callouts.Say(this, Callout.NeedHelp, force: true);
                }
                Mind.Poke(Traits.reaction);
            }
        }

        void OnDied()
        {
            if (talk != null) { if (talk.InConversation && Alcax.Dialogue.DialogueUI.Instance != null) Alcax.Dialogue.DialogueUI.Instance.Close(); talk.enabled = false; }
            ClaimedSpot = null;
            Squad?.ReportDeath(this);
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            StartCoroutine(Collapse(lastHitDir, lastHitPoint));
        }

        Vector3 lastHitDir, lastHitPoint;

        IEnumerator Collapse(Vector3 dir, Vector3 point)
        {
            body.Velocity = Vector3.zero;
            body.Crouch = 1f;
            body.WeaponRaised = false;
            yield return new WaitForSeconds(0.08f);
            body.Freeze();
            DropGunAsItem();
            // Loose limbs: a ragdoll that settles, then stays put (and never blocks the player)
            Alcax.Combat.RagdollCorpse.Go(gameObject, dir + Vector3.up * 0.15f, point, 90f);
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || Perception == null) return;
            var b = Perception.Belief;
            if (b.Has)
            {
                Gizmos.color = new Color(1f, 0.4f, 0.2f);
                Gizmos.DrawWireSphere(b.Predicted, Mathf.Max(0.3f, b.Uncertainty(config.uncertaintyGrowth)));
                Gizmos.DrawLine(EyePosition, b.Predicted + Vector3.up * 1.5f);
            }
            if (cover.Valid)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(cover.Position + Vector3.up * 0.5f, new Vector3(0.6f, 1f, 0.6f));
                Gizmos.DrawLine(cover.Position, cover.PeekPosition);
            }
            Gizmos.color = Color.cyan;
            for (int i = 1; i < path.Count; i++) Gizmos.DrawLine(path[i - 1] + Vector3.up * 0.2f, path[i] + Vector3.up * 0.2f);
        }
    }
}
