using System.Collections.Generic;
using UnityEngine;

namespace Alcax.AI
{
    public enum Alertness { Unaware, Suspicious, Alert, Combat }

    public enum BeliefSource { None, Seen, Heard, Shot, Squad }

    /// <summary>
    /// What one operative believes about the player. It is never the truth while unseen: the position
    /// is a last-known point plus a velocity guess, and the uncertainty radius grows with time.
    /// </summary>
    public struct Belief
    {
        public bool Has;
        public Vector3 Position;
        public Vector3 Velocity;
        public float Time;          // when this was true
        public BeliefSource Source;
        public float BaseError;     // error at the moment of the report (heard noises are vague)

        public float Age => UnityEngine.Time.time - Time;

        public float Uncertainty(float growth) => BaseError + Mathf.Min(Age, 12f) * growth;

        /// <summary>0 to 1: how much this belief can be trusted right now (1 = seen this instant, about 0.5 at 3 m of uncertainty).</summary>
        public float Confidence(float growth) => Has ? 1f / (1f + Uncertainty(growth) / 3f) : 0f;

        /// <summary>Where the player probably is now: dead reckoning that trusts the velocity for about 1.5 s.</summary>
        public Vector3 Predicted => Position + Velocity * Mathf.Min(Age, 1.5f);
    }

    /// <summary>
    /// Senses for one operative: a two-zone vision cone (sharp focal plus soft peripheral), line-of-sight
    /// to three body points, and hearing through walls. Sight and sound fill an awareness meter (0 to 1)
    /// that sets the alertness state. Ticks a few times per second, staggered per unit.
    /// </summary>
    public class Perception
    {
        readonly Operative self;
        readonly CombatAIConfig cfg;
        readonly Vector3[] points = new Vector3[3];
        float nextTick;

        public float Awareness;
        public Alertness State;
        public Belief Belief;
        /// <summary>Who this unit is focused on (see Operative's contacts and target selection).</summary>
        public ICombatTarget Target { get; private set; }
        /// <summary>Fraction (0 to 1) of the player's body in line of sight and inside the cone right now.</summary>
        public float VisibleFraction { get; private set; }
        public bool CanSee => VisibleFraction > 0f && State == Alertness.Combat;
        public float LastSeenTime { get; private set; } = -100f;
        /// <summary>Where his head was the last time I saw him (the spot to hold when he ducks).</summary>
        public Vector3 LastSeenHead { get; private set; }
        /// <summary>Consecutive re-appearances near the same spot within a few seconds (a peek rhythm).</summary>
        public int PeekStreak { get; private set; }
        /// <summary>The current sighting began near where he was last seen, moments after he vanished.</summary>
        public bool Repeek { get; private set; }
        /// <summary>Learned time he stays hidden between peeks from the same spot (s), and how many gaps it's based on.</summary>
        public float PeekGap { get; private set; } = 1.5f;
        public int PeekGapCount { get; private set; }
        public float TimeSinceSeen => Time.time - LastSeenTime;
        /// <summary>Set when a fresh sighting begins after the player was out of sight; reaction time counts from here.</summary>
        public float SightStartTime { get; private set; } = -100f;
        /// <summary>The last noise worth investigating.</summary>
        // ---------- Cognitive filters ----------
        // Habituation: a stimulus of the same kind from the same direction (30-degree sector) loses its pull each time it
        // repeats, and recovers with quiet (exponential, time constant 4 s). Novel stimuli get full weight and redirect attention.
        readonly Dictionary<int, float> habit = new();
        public static int Interrupts, Habituated;   // debug counters

        float HabituationFactor(NoiseKind kind, Vector3 pos)
        {
            if (!cfg.useCognitiveFilters) return 1f;
            var v = (pos - self.transform.position).Flat();
            int sector = Mathf.RoundToInt(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg / 30f);
            int key = (int)kind * 100 + ((sector % 12) + 12) % 12;
            float level = habit.TryGetValue(key, out var h) ? h : 0f;
            // Recover since the last time (stored as level at last time; decay applied lazily via timestamps below)
            float last = habitTime.TryGetValue(key, out var t) ? t : -100f;
            level *= Mathf.Exp(-(Time.time - last) / 4f);   // quiet lets it fade back (most of the way in about 8 s)
            habit[key] = level + 1f;
            habitTime[key] = Time.time;
            return 1f / (1f + level * 0.7f);
        }
        readonly Dictionary<int, float> habitTime = new();

        /// <summary>Stress narrows the visual field (tunnel vision); discipline resists it.</summary>
        public float EffectivePeripheral
        {
            get
            {
                if (!cfg.useCognitiveFilters) return cfg.peripheralAngle;
                float tunnel = Mathf.Pow(self.Emotion.stress, 1.5f) * (1f - self.Traits.discipline * 0.5f);
                return Mathf.Lerp(cfg.peripheralAngle, Mathf.Max(cfg.focalAngle * 1.8f, 70f), tunnel);
            }
        }

        /// <summary>Auditory exclusion under stress: quiet sounds get missed.</summary>
        float HearingScale => cfg.useCognitiveFilters ? 1f - 0.5f * self.Emotion.stress * (1f - self.Traits.discipline * 0.5f) : 1f;

        public Vector3 NoisePosition;
        public float NoiseTime = -100f;

        public Perception(Operative self, CombatAIConfig cfg)
        {
            this.self = self;
            this.cfg = cfg;
            nextTick = Time.time + Random.value * 0.15f;
            Hearing.Listen(OnNoise);
        }

        public void Dispose() => Hearing.Stop(OnNoise);

        public void Tick(float dt)
        {
            var player = Target;
            if (player == null || player.IsDead || player.NoClipping || !self.IsHostile(player)) { VisibleFraction = 0f; Decay(dt); return; }

            if (Time.time >= nextTick)
            {
                nextTick = Time.time + 0.1f;
                Look(player);
            }

            if (VisibleFraction > 0f)
            {
                float gain = SightGain(player);
                Awareness = Mathf.Clamp01(Awareness + gain * dt);
                if (Awareness >= cfg.combatAt) State = Alertness.Combat;
                if (State == Alertness.Combat)
                {
                    if (TimeSinceSeen > 0.4f)
                    {
                        bool same = LastSeenTime > 0f && TimeSinceSeen < 6f && Vector3.Distance(player.HeadPosition, LastSeenHead) < 2f;
                        Repeek = same;
                        PeekStreak = same ? PeekStreak + 1 : 0;
                        if (same)
                        {
                            PeekGap = PeekGapCount == 0 ? TimeSinceSeen : Mathf.Lerp(PeekGap, TimeSinceSeen, 0.4f);
                            PeekGapCount++;
                        }
                        else PeekGapCount = 0;
                        SightStartTime = Time.time;
                    }
                    LastSeenTime = Time.time;
                    LastSeenHead = player.HeadPosition;
                    if (player is PlayerTarget) self.Squad?.NoteSighting(player.HeadPosition);
                    SetBelief(player.Position, player.Velocity, BeliefSource.Seen, 0f);
                    self.ShareSighting(Belief);
                }
                else if (Awareness >= cfg.suspiciousAt)
                {
                    // Something is there: a vague fix on the movement in the corner of the eye
                    NoisePosition = player.Position + Random.insideUnitSphere.Flat() * 2f;
                    NoiseTime = Time.time;
                }
            }
            else Decay(dt);

            UpdateState();
        }

        void Decay(float dt)
        {
            // Alerted people stay alert (a minute or so); a vague glimpse fades fast
            float rate = State == Alertness.Combat ? cfg.combatAwarenessDecay : State == Alertness.Alert ? cfg.alertAwarenessDecay : cfg.awarenessDecay;
            Awareness = Mathf.Max(0f, Awareness - rate * dt);
        }

        void UpdateState()
        {
            if (State == Alertness.Combat)
            {
                // Stay in combat until they have looked for a long time and found nothing
                if (Awareness < cfg.alertAt * 0.5f && TimeSinceSeen > 40f) State = Alertness.Alert;
                return;
            }
            State = Awareness >= cfg.alertAt ? Alertness.Alert : Awareness >= cfg.suspiciousAt ? Alertness.Suspicious : Alertness.Unaware;
        }

        void Look(ICombatTarget player)
        {
            var eye = self.EyePosition;
            var fwd = self.LookForward;
            player.GetBodyPoints(points);
            int visible = 0;
            float bestAngle = 180f;
            for (int i = 0; i < points.Length; i++)
            {
                var to = points[i] - eye;
                float d = to.magnitude;
                if (d > cfg.visionRange) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang > EffectivePeripheral * 0.5f && d > 2.5f) continue;   // up close, they sense you behind them
                if (Physics.Linecast(eye, points[i], AILayers.World, QueryTriggerInteraction.Ignore)) continue;
                visible++;
                bestAngle = Mathf.Min(bestAngle, ang);
            }
            VisibleFraction = visible / (float)points.Length;
            lastBestAngle = bestAngle;
        }

        float lastBestAngle;

        float SightGain(ICombatTarget player)
        {
            float d = Vector3.Distance(self.EyePosition, player.HeadPosition);
            float half = cfg.focalAngle * 0.5f;
            float angular = lastBestAngle <= half
                ? cfg.focalGain
                : Mathf.Lerp(cfg.focalGain * 0.6f, cfg.peripheralGain * 0.3f, Mathf.InverseLerp(half, EffectivePeripheral * 0.5f, lastBestAngle)) * (cfg.peripheralGain / 0.55f);
            float distance = 1f / (1f + (d / 14f) * (d / 14f));
            float mods = 1f;
            if (player.IsCrouching) mods *= cfg.crouchVisibility;
            if (player.Velocity.magnitude < 0.3f) mods *= cfg.stillVisibility;
            if (Time.time - player.LastShotTime < 1f) mods *= cfg.firingVisibility;
            if (player.FlashlightOn && Vector3.Angle(player.Forward, self.EyePosition - player.HeadPosition) < 30f) mods *= cfg.flashlightVisibility;
            float alertBoost = State >= Alertness.Alert ? 2.2f : State == Alertness.Suspicious ? 1.4f : 1f;
            return angular * distance * mods * alertBoost * Mathf.Lerp(0.6f, 1f, VisibleFraction) * Mathf.Lerp(0.85f, 1.15f, self.Traits.skill);
        }

        void OnNoise(NoiseEvent e)
        {
            if (self == null || self.IsDead) return;
            if (e.Source == null || e.Source == self.gameObject) return;
            var src = Targets.Of(e.Source);
            if (src == null) return;                                       // civilians and props
            bool hostile = self.IsHostile(src);
            bool fromPlayer = hostile;                                     // (the "threat" branch below; named for history)
            bool fromAlly = !hostile && src is Operative;
            if (!fromPlayer && !fromAlly) return;                          // a neutral party (the player, to a calm guard)

            float loud = Hearing.Loudness(e, self.EyePosition) * HearingScale;
            if (loud <= 0.02f) return;
            float novelty = HabituationFactor(e.Kind, e.Position);
            if (novelty < 0.5f) Habituated++;

            if (fromPlayer)
            {
                float weight = e.Kind switch
                {
                    NoiseKind.Gunshot => 3f,
                    NoiseKind.Melee => 1.2f,
                    NoiseKind.Reload => 0.8f,
                    NoiseKind.Footstep => 0.5f,
                    _ => 0.6f,
                };
                Awareness = Mathf.Clamp01(Awareness + loud * weight * novelty);
                // Sound gives direction and rough distance, never a precise fix
                float err = Mathf.Lerp(0.5f, 8f, 1f - loud) * (e.Kind == NoiseKind.Gunshot ? 0.6f : 1f);
                var guess = e.Position + Random.insideUnitSphere.Flat() * err;
                self.NoteHeard(src, guess, err);
                bool aboutTarget = Target == null || Target == src;
                NoisePosition = guess;
                NoiseTime = Time.time;
                if (e.Kind == NoiseKind.Reload && State == Alertness.Combat) self.Mind.OnHeardReload();
                if (aboutTarget && (State >= Alertness.Suspicious || loud > 0.3f) && self.MapIsMine) self.Squad?.MapObserve(guess, err + 1f, e.Time + (int)e.Kind * 0.001f);
                if (novelty > 0.5f) { self.Attention?.Interrupt(); Interrupts++; }
                if (Awareness >= cfg.combatAt || (e.Kind == NoiseKind.Gunshot && loud > 0.25f))
                {
                    State = Alertness.Combat;
                    Awareness = Mathf.Max(Awareness, cfg.combatAt);
                    if (aboutTarget && (!Belief.Has || Belief.Age > 0.5f)) SetBelief(guess, Vector3.zero, BeliefSource.Heard, err);
                }
            }
            else if (e.Kind == NoiseKind.Gunshot || e.Kind == NoiseKind.Voice)
            {
                // Allies firing: something is happening over there
                Awareness = Mathf.Clamp01(Mathf.Max(Awareness, cfg.alertAt) + loud * 0.3f * novelty);
                if (State < Alertness.Combat) { NoisePosition = e.Position; NoiseTime = Time.time; }
            }
        }

        /// <summary>Hit by a bullet: instant combat, with a rough idea of where it came from.</summary>
public void OnShot(Vector3 from, ICombatTarget shooter = null)
        {
            Awareness = 1f;
            State = Alertness.Combat;
            float err = Mathf.Clamp(Vector3.Distance(from, self.transform.position) * 0.12f, 0.5f, 6f);
            if (shooter != null && shooter != Target) { self.NoteShotBy(shooter, from, err); return; }
            if (!Belief.Has || Belief.Age > 0.3f || Belief.Source != BeliefSource.Seen)
                SetBelief(from + Random.insideUnitSphere.Flat() * err, Vector3.zero, BeliefSource.Shot, err);
            if (self.MapIsMine) self.Squad?.MapObserve(Belief.Position, err + 1f, Time.time);
        }

        /// <summary>Change focus: keep the combat state, load what's known about the new target.</summary>
        public void Retarget(ICombatTarget next, Belief known, float lastSeen)
        {
            Target = next;
            Belief = known;
            LastSeenTime = lastSeen;
            SightStartTime = -100f;
            VisibleFraction = 0f;
            PeekStreak = 0; PeekGapCount = 0; Repeek = false;
            if (known.Has) LastSeenHead = known.Position + Vector3.up * 1.6f;
        }

        /// <summary>Radio report from a squadmate (already delayed and degraded by the squad).</summary>
        /// <summary>
        /// A report from a squadmate. With fusion on, it is merged into this unit's belief by confidence
        /// (inverse-variance weighting, as in sensor fusion and Kalman filtering): both estimates are projected to now,
        /// each weighted by 1 / uncertainty squared, so a precise fresh report pulls hard, a vague old one barely moves it,
        /// and a much worse one is ignored. Own eyes always win. The result is still one belief, so decisions and aim
        /// never have to choose between competing versions of where he is.
        /// </summary>
        public void OnSquadReport(Belief b, Operative from = null, ICombatTarget about = null)
        {
            if (about != null && about != Target) { self.NoteReported(about, b); if (Target != null || !self.IsHostile(about)) return; self.FocusOn(about); }
            if (VisibleFraction > 0f && State == Alertness.Combat) return;   // own eyes win
            if (!cfg.fuseReports)
            {
                if (Belief.Has && Belief.Time >= b.Time) return;
                Belief = b;
                Belief.Source = BeliefSource.Squad;
            }
            else if (!Belief.Has) { Belief = b; Belief.Source = BeliefSource.Squad; self.NoteReport(from, b.Predicted, b.Uncertainty(cfg.uncertaintyGrowth)); }
            else
            {
                float g = cfg.uncertaintyGrowth;
                // A distrusted source counts as vaguer than it claims
                float trustIn = cfg.useTrust ? self.TrustIn(from) : 1f;
                float s1 = Mathf.Max(0.3f, Belief.Uncertainty(g)), s2 = Mathf.Max(0.3f, b.Uncertainty(g)) / Mathf.Max(0.2f, trustIn);
                if (s2 > s1 * 2.5f) return;                           // much worse information: ignore it
                float w1 = 1f / (s1 * s1), w2 = 1f / (s2 * s2);
                var fused = (Belief.Predicted * w1 + b.Predicted * w2) / (w1 + w2);
                float sFused = Mathf.Sqrt(1f / (w1 + w2));
                Belief.Velocity = s2 < s1 ? b.Velocity : Belief.Velocity;
                Belief.Position = fused;
                Belief.Time = Time.time;
                Belief.BaseError = Mathf.Max(0.4f, sFused);           // never more certain than a good radio call
                if (w2 > w1) Belief.Source = BeliefSource.Squad;
                self.NoteReport(from, b.Predicted, b.Uncertainty(g));
            }
            if (self.MapIsMine) self.Squad?.MapObserve(b.Position, b.BaseError + 1f, b.Time);
            if (State < Alertness.Combat) { State = Alertness.Combat; Awareness = Mathf.Max(Awareness, cfg.combatAt); }
        }

        void SetBelief(Vector3 pos, Vector3 vel, BeliefSource src, float err)
        {
            Belief.Has = true;
            Belief.Position = pos;
            Belief.Velocity = vel;
            Belief.Time = Time.time;
            Belief.Source = src;
            Belief.BaseError = err;
        }

/// <summary>Instant combat with a believed position (a report or a witnessed crime) and its error radius.</summary>
        public void ForceCombatAt(Vector3 pos, float error)
        {
            Awareness = 1f;
            State = Alertness.Combat;
            SetBelief(pos, Vector3.zero, BeliefSource.Squad, Mathf.Max(0.5f, error));
            self.Squad?.MapObserve(pos, error + 1f, Time.time);
        }

        /// <summary>Back to calm: no threat, no belief (a guard standing down).</summary>
        public void Calm()
        {
            Awareness = 0f;
            State = Alertness.Unaware;
            Belief.Has = false;
            NoiseTime = -100f;
        }

        public void ForceCombat()
        {
            var p = Target ?? (self.IsHostile(PlayerTarget.Instance) ? PlayerTarget.Instance : null);
            if (p != null && p != Target) self.FocusOn(p);
            Awareness = 1f;
            State = Alertness.Combat;
            if (p != null) SetBelief(p.Position, p.Velocity, BeliefSource.Squad, 3f);
        }
    }

    public static class VectorExt
    {
        public static Vector3 Flat(this Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
