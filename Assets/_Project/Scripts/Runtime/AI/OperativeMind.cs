using System;
using Alcax.Combat;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Alcax.AI
{
    public enum OpAction { Idle, Patrol, Investigate, Search, TakeCover, HoldCover, PeekShoot, Engage, Advance, Flank, Rush, Retreat, Stalk, PatchUp, Flee, Surrender, ClearRoom }

    public enum Wound { Healthy, Wounded, Critical }

    [Serializable]
    public struct Traits
    {
        public float aggression, caution, discipline, patience, teamwork, skill, nerve, reaction, adaptability;
    }

    [Serializable]
    public struct Emotion
    {
        public float fear, anger, stress;
    }

    /// <summary>What the mind knows this decision tick (built by the Operative).</summary>
    public struct MindContext
    {
        public bool Threat, Seeing, InCover, Hidden, CoverFailedRecently, PlayerDead, PlayerKnowsMe;
        public float SinceSeen, Distance, Health, Ammo, Pressure, Awareness, HeightAdvantage;
        public bool Reloading, BeingAimedAt, ReloadOpportunity, HasNoise, HasRoute, CoverRequested, PlayerHurt;
        public int AlliesAlive, AlliesFiring, PeekFails, SquadFlanking, PatchesLeft;
        public Alertness State;
        public Wound Wound;
        public bool LastStand;
        /// <summary>The target is inside this unit's standoff distance.</summary>
        public bool TooClose;
        /// <summary>Age and confidence of the best information about the player (own sight, squad reports, or noise).</summary>
        public float InfoAge, InfoConf;
        /// <summary>Seconds spent holding cover without new information.</summary>
        public float HoldStale;
        /// <summary>The last look for forward cover found nothing (an advance now means open ground).</summary>
        public bool NoAdvanceCover;
        /// <summary>Shot at or hit in the last few seconds.</summary>
        public bool UnderFire;
        /// <summary>The fight is indoors (me or where he is), and how big the space is (m).</summary>
        public bool Indoor;
        public float FightSpace;
        /// <summary>Layer 2: he's believed to be in the same authored room as me (or both outside).</summary>
        public bool SameRoom;
        /// <summary>Layer 3: he's believed to be in another authored room we can enter through a doorway, not cleared recently.</summary>
        public bool CanClear;
        /// <summary>Combat started moments ago.</summary>
        public bool FreshContact;
        /// <summary>No ammo left at all (ammo budget).</summary>
        public bool Dry;
        /// <summary>Loaded plus spare ammo, as a fraction of a full load.</summary>
        public float AmmoTotal;
    }

    /// <summary>
    /// Utility-based decision making. Every action is scored from:
    ///   the situation (MindContext), personality (traits), emotions, fighting style (primary and secondary),
    ///   wound state (cautious when hurt; when critical, flee and patch up, or make a last stand),
    ///   squad input (orders from the leader, requests for covering fire), and in-fight learning
    ///   (actions that got this unit hurt lose appeal; actions that landed hits gain it).
    /// Each score gets noise that is resampled now and then, and the current action gets a commitment bonus
    /// that fades over time, so they're neither predictable nor dithering.
    /// </summary>
    public class OperativeMind
    {
        public static readonly int ActionCount = Enum.GetValues(typeof(OpAction)).Length;

        readonly Operative op;
        readonly CombatAIConfig cfg;
        public OpAction Current { get; private set; } = OpAction.Idle;
        public float CurrentSince { get; private set; }
        public readonly float[] Scores = new float[ActionCount];
        readonly float[] noise = new float[ActionCount];
        /// <summary>What each action has earned this fight (positive: it worked; negative: it got me hurt).</summary>
        public readonly float[] Outcome = new float[ActionCount];
        float nextNoise, nextDecision, frozenUntil;
        float reloadOpportunityUntil = -100f;
        OpAction orderAction; float orderUntil = -100f, orderBonus;
        public float SuppressedUntil { get; private set; } = -100f;
        public float CoverRequestedUntil { get; set; } = -100f;
        public float LastHitOnPlayer { get; private set; } = -100f;
        public MindContext LastContext;
        public string OrderNote => Time.time < orderUntil ? orderAction.ToString() : "";

        public OperativeMind(Operative op, CombatAIConfig cfg)
        {
            this.op = op;
            this.cfg = cfg;
            nextDecision = Time.time + Random.Range(0.05f, 0.4f);
            ResampleNoise();
        }

        public float TimeInAction => Time.time - CurrentSince;
        public bool ReloadOpportunity => Time.time < reloadOpportunityUntil;

        public void Tick(float dt)
        {
            UpdateEmotions(dt);
            for (int i = 0; i < Outcome.Length; i++) Outcome[i] = Mathf.MoveTowards(Outcome[i], 0f, dt * 0.006f);
            if (Time.time >= nextNoise) ResampleNoise();
            if (Time.time >= nextDecision && Time.time >= frozenUntil) Decide();
        }

        public void Poke(float delay = 0f) => nextDecision = Mathf.Min(nextDecision, Time.time + delay);

        public void Startle(float seconds)
        {
            frozenUntil = Mathf.Max(frozenUntil, Time.time + seconds);
            nextDecision = frozenUntil;
        }

        /// <summary>The squad leader wants this action for a while (obeyed in proportion to discipline and teamwork).</summary>
        public void Order(OpAction a, float seconds, float bonus)
        {
            orderAction = a;
            orderUntil = Time.time + seconds;
            orderBonus = bonus * Mathf.Lerp(0.5f, 1.3f, (op.Traits.discipline + op.Traits.teamwork) * 0.5f);
            Poke(Random.Range(0.3f, 0.8f));
        }

        /// <summary>The ordered action was carried out: stop pushing it.</summary>
        public void CompleteOrder(OpAction a)
        {
            if (a == orderAction) orderUntil = -100f;
        }

        void ResampleNoise()
        {
            nextNoise = Time.time + cfg.noiseResample * Random.Range(0.6f, 1.4f);
            for (int i = 0; i < noise.Length; i++) noise[i] = Random.Range(-cfg.scoreNoise, cfg.scoreNoise);
        }

        void Decide()
        {
            nextDecision = Time.time + cfg.decisionInterval * Random.Range(0.8f, 1.25f);
            if (op.Busy) return;
            // Morale overrides the weighing: a broken man runs, a man who gave up stays down
            if (op.Surrendered || op.Broken)
            {
                var forced = op.Surrendered ? OpAction.Surrender : OpAction.Flee;
                if (Current != forced || op.ActionDone) { Current = forced; CurrentSince = Time.time; op.EnterAction(forced); }
                return;
            }
            var c = op.BuildContext();
            LastContext = c;
            for (int i = 0; i < ActionCount; i++)
            {
                var a = (OpAction)i;
                float s = Score(a, c);
                if (s > -5f)
                {
                    s += StyleBias.For(op.Style, a) + 0.4f * StyleBias.For(op.SecondaryStyle, a);
                    s += ConfidenceBias(a, op.Confidence);
                    if (cfg.useMoveDoctrine) s += DoctrineBias(a, c);
                    if (cfg.useContactDrill) s += ContactBias(a, c);
                    s += Mathf.Clamp(Outcome[i], -0.35f, 0.35f) * op.Traits.adaptability;
                    if (Time.time < orderUntil && a == orderAction) s += orderBonus;
                    s += noise[i];
                }
                Scores[i] = s;
            }

            // Settle: a move that just finished gets fought from before the next move (no ping-pong between destinations)
            if (cfg.useSteadyMoves && op.ActionDone && IsMove(Current)) lastMoveEnd = Time.time;
            if (cfg.useSteadyMoves && Time.time - lastMoveEnd < 2.5f && !(c.UnderFire && !c.InCover) && !c.LastStand)
                for (int i = 0; i < ActionCount; i++) if (IsMove((OpAction)i) && Scores[i] > -5f) Scores[i] -= 0.3f;
            int best = (int)Current;
            float currentScore = Scores[(int)Current] + cfg.commitMargin * Mathf.Max(0f, 1f - TimeInAction / cfg.commitDecaySeconds);
            // A move under way is finished unless something big changes (getting hit, a much better option)
            if (cfg.useFightingCover && !op.ActionDone && (Current == OpAction.TakeCover || Current == OpAction.Advance || Current == OpAction.Flank || Current == OpAction.Retreat))
                currentScore += 0.3f * (1f - Mathf.Clamp01(c.Pressure) * 0.6f) * (TimeInAction < 10f ? 1f : 0.3f);
            if (cfg.useHitCommit && op.Committed && !op.ActionDone) currentScore += 0.4f;
            // A firefight gets a few seconds before second thoughts
            if (cfg.useFightingCover && !op.ActionDone && (Current == OpAction.Engage || Current == OpAction.PeekShoot) && TimeInAction < 3f) currentScore += 0.15f;
            float bestScore = op.ActionDone ? -10f : currentScore;
            for (int i = 0; i < ActionCount; i++)
            {
                if (i == (int)Current && !op.ActionDone) continue;
                if (Scores[i] > bestScore) { bestScore = Scores[i]; best = i; }
            }
            if (best != (int)Current || op.ActionDone)
            {
                Current = (OpAction)best;
                CurrentSince = Time.time;
                op.EnterAction(Current);
            }
        }

        const float No = -10f;
        float lastMoveEnd = -100f;
        static bool IsMove(OpAction a) => a == OpAction.TakeCover || a == OpAction.Advance || a == OpAction.Flank || a == OpAction.ClearRoom;

        /// <summary>
        /// Cover discipline: strict units (special ops, veterans) fight from cover and bound cover to cover under overwatch;
        /// loose units (insurgents, rookies) fight in the open more and walk forward shooting.
        /// </summary>
/// <summary>
        /// Contact drill: the first seconds of a fight, or bullets arriving while in the open. Return fire if he's in view,
        /// otherwise get to cover (the take-cover move may open with a reaction burst toward the shots). Nobody sneaks,
        /// searches, or patches up in the open while being shot at.
        /// </summary>
        float ContactBias(OpAction a, MindContext c)
        {
            if (c.InCover || !(c.UnderFire || c.FreshContact)) return 0f;
            if (cfg.useHitCommit && op.Committed) return 0f;
            var t = op.Traits;
            switch (a)
            {
                case OpAction.Stalk:
                case OpAction.Investigate:
                case OpAction.Search:
                case OpAction.PatchUp:
                    return -20f;
                case OpAction.TakeCover: return c.UnderFire ? 0.45f : 0.25f;
                case OpAction.Engage: return c.Seeing ? 0.25f + t.aggression * 0.2f : 0f;
                case OpAction.HoldCover: return -0.3f;
            }
            return 0f;
        }

        float DoctrineBias(OpAction a, MindContext c)
        {
            float d = op.Doctrine - 0.5f;
            switch (a)
            {
                case OpAction.PeekShoot: return 0.35f * d;
                case OpAction.Engage: return c.InCover ? 0f : -0.35f * d;
                case OpAction.TakeCover:
                    // Strict units prefer cover on principle; loose ones take it when it is obvious (shot at, reloading, hurt)
                    return (c.Seeing ? 0.35f * d : 0f) + (c.Pressure > 0.3f || c.Reloading || c.Health < 0.5f ? 0.35f * (1f - op.Doctrine) : 0f);
                case OpAction.Advance:
                    {
                        float s = 0f;
                        if (c.NoAdvanceCover && c.Seeing) s -= 0.4f * d;                        // loose units don't mind open ground
                        if (op.Doctrine > 0.6f && c.Seeing && c.AlliesAlive > 0 && c.AlliesFiring == 0) s -= 0.15f;   // move while someone covers
                        return s;
                    }
            }
            return 0f;
        }

        /// <summary>
        /// Confidence leans every choice: winning makes them bold (push, rush, fight in the open) and a bit careless;
        /// losing makes them cautious (cover, hold, fall back).
        /// </summary>
        static float ConfidenceBias(OpAction a, float conf)
        {
            float up = Mathf.Max(0f, conf), down = Mathf.Max(0f, -conf);
            return a switch
            {
                OpAction.Advance => 0.25f * up - 0.25f * down,
                OpAction.Rush => 0.3f * up - 0.35f * down,
                OpAction.Flank => 0.15f * up - 0.2f * down,
                OpAction.Engage => 0.22f * up - 0.2f * down,
                OpAction.Stalk => 0.1f * up - 0.1f * down,
                OpAction.Search => 0.1f * up - 0.15f * down,
                OpAction.TakeCover => -0.15f * up + 0.2f * down,
                OpAction.HoldCover => -0.15f * up + 0.25f * down,
                OpAction.Retreat => -0.3f * up + 0.35f * down,
                OpAction.PeekShoot => -0.1f * down,
                OpAction.PatchUp => 0.1f * down,
                _ => 0f,
            };
        }

        float Score(OpAction a, MindContext c)
        {
            var t = op.Traits;
            var e = op.Emotion;
            float lowHp = 1f - c.Health;
            float minR = op.RangeMin, maxR = op.RangeMax;
            // Information, not just eyesight: a squadmate's report or a heard shot keeps the contact live
            bool useInfo = cfg.useInfoEngagement;
            float infoAge = useInfo ? Mathf.Min(c.SinceSeen, c.InfoConf > 0.3f ? c.InfoAge : 999f) : c.SinceSeen;
            bool fresh = infoAge < 6f;
            bool lost = c.Threat && infoAge > cfg.lostAfter * Mathf.Lerp(0.6f, 1.6f, t.patience);
            // Restlessness: holding an angle with nothing new gets old (patient types last longer)
            bool lowRiskPeeks = cfg.useFightingCover && cfg.useCoverRepertoire;
            float restless = useInfo ? Mathf.Clamp01((c.HoldStale - Mathf.Lerp(3f, 10f, t.patience)) / 5f) : 0f;
            float pressure = c.Pressure;
            float perch = Mathf.Clamp01((c.HeightAdvantage - 1.5f) / 4f) * (t.patience * 0.5f + t.skill * 0.5f);
            bool critical = c.Wound == Wound.Critical;
            bool fleeing = critical && !c.LastStand;
            // A last stand burns through fear: they stop caring about getting hurt
            float fear = c.LastStand ? e.fear * 0.2f : e.fear;
            bool opportunist = op.Style == CombatStyle.Opportunist || op.SecondaryStyle == CombatStyle.Opportunist;
            float opening = (c.ReloadOpportunity ? 1f : 0f) + (c.PlayerHurt ? 0.7f : 0f);
            if (opportunist) opening *= 1.8f;
            float coverReq = c.CoverRequested ? t.teamwork : 0f;

            if (c.PlayerDead) return a == OpAction.Idle ? 1f : a == OpAction.Patrol && c.HasRoute ? 0.9f : No;

            if (!c.Threat)
            {
                switch (a)
                {
                    case OpAction.Idle: return 0.3f;
                    case OpAction.Patrol: return c.HasRoute && c.State == Alertness.Unaware ? 0.5f : No;
                    case OpAction.Investigate:
                        return c.HasNoise && c.State >= Alertness.Suspicious ? 0.55f + c.Awareness * 0.3f + t.aggression * 0.1f - fear * 0.3f : No;
                    case OpAction.Search: return c.State == Alertness.Alert ? 0.42f + t.aggression * 0.1f : No;
                    case OpAction.PatchUp: return c.Health < 0.8f && c.PatchesLeft > 0 ? 0.45f + lowHp * 0.8f : No;
                    default: return No;
                }
            }

            if (c.Dry)
            {
                switch (a)
                {
                    case OpAction.TakeCover: return c.InCover ? No : 0.95f;
                    case OpAction.HoldCover: return c.InCover ? 0.7f + t.caution * 0.2f : No;
                    case OpAction.PeekShoot: return c.InCover ? 0.55f + t.teamwork * 0.2f : No;          // spotting peeks only
                    case OpAction.Retreat: return 0.45f + t.caution * 0.3f + e.fear * 0.3f;
                    case OpAction.Rush: return c.Distance < 9f ? 0.2f + t.aggression * 0.6f + e.anger * 0.3f - t.caution * 0.4f : No;   // close enough to swing at him
                    case OpAction.PatchUp: break;
                    case OpAction.Flee:
                    case OpAction.Surrender: return No;
                    default: return No;
                }
            }

            switch (a)
            {
                case OpAction.Idle:
                case OpAction.Patrol:
                case OpAction.Investigate:
                    return No;

                case OpAction.Search:
                    return lost ? 0.6f + t.aggression * 0.2f + restless * 0.3f - fear * 0.2f - (fleeing ? 0.5f : 0f) - (cfg.useCqb && c.CanClear ? 0.3f : 0f) : No;

                case OpAction.TakeCover:
                    if (c.InCover) return No;
                    {
                        float s = 0.45f + t.caution * 0.35f + fear * 0.5f + e.stress * 0.2f + (c.BeingAimedAt ? 0.25f : 0f)
                                  + lowHp * 0.4f + (c.Ammo < 0.25f || c.Reloading ? 0.45f : 0f) + pressure * 0.4f
                                  - t.aggression * 0.25f - e.anger * 0.2f;
                        if (!c.Seeing && c.SinceSeen > 3f) s -= 0.2f;
                        if (c.Wound == Wound.Wounded) s += 0.1f;
                        if (c.LastStand) s -= 0.1f;
                        return c.CoverFailedRecently ? s * 0.3f : s;
                    }

                case OpAction.HoldCover:
                    if (!c.InCover) return No;
                    return 0.35f + t.patience * 0.35f + t.caution * 0.2f + fear * 0.45f + (c.Seeing ? 0f : 0.15f) + perch * 0.3f
                           + (c.Reloading || c.Ammo < 0.3f ? 0.5f : 0f) + pressure * 0.5f - e.anger * 0.2f - coverReq * 0.1f
                           + (c.LastStand ? 0.2f : 0f) - restless * 0.7f - (useInfo && !c.Seeing ? 0.15f : 0f);

                case OpAction.PeekShoot:
                    if (!c.InCover || c.Ammo <= 0f) return No;
                    if (!fresh)
                    {
                        // Stale: a quick re-acquire peek (the cover repertoire makes it head-only) instead of waiting blind
                        if (!useInfo || lost && c.SinceSeen > 25f) return No;
                        return 0.4f + t.aggression * 0.15f + (1f - t.caution) * 0.1f + restless * 0.45f + perch * 0.3f
                               - fear * 0.4f - pressure * 0.4f - c.PeekFails * 0.1f;
                    }
                    return 0.5f + t.discipline * 0.25f + (c.Seeing ? 0.15f : 0f) + opening * 0.3f
                           + (c.SquadFlanking > 0 ? 0.15f * t.teamwork : 0f) + perch * 0.4f + coverReq * 0.25f
                           - (lowRiskPeeks ? fear * 0.2f + pressure * 0.15f : fear * 0.45f + pressure * 0.4f) - c.PeekFails * 0.12f + (c.LastStand ? 0.25f : 0f);

                case OpAction.Engage:
                    if (!c.Seeing || c.InCover) return No;
                    {
                        float s = 0.35f + t.aggression * 0.3f + e.anger * 0.3f - t.caution * 0.25f - fear * 0.3f
                                  + (c.Distance < minR ? 0.1f : 0f) + (c.CoverFailedRecently ? 0.3f : 0f) - pressure * 0.2f + coverReq * 0.1f;
                        // Caught them unaware: open up before they turn around
                        if (!c.PlayerKnowsMe) s += 0.35f;
                        // Too close: fight while making distance (Engage backs off)
                        if (c.TooClose && !op.Archetype.canRush) s += 0.35f;
                        if (fleeing) s -= 0.3f;
                        if (c.LastStand) s += 0.2f;
                        return s;
                    }

                case OpAction.Advance:
                    {
                        if (c.Reloading && c.Ammo < 0.3f) return No;
                        float s = 0.25f + t.aggression * 0.3f - fear * 0.5f - lowHp * 0.3f - pressure * 0.3f + opening * 0.2f;
                        if (c.Distance > maxR * 0.9f) s += 0.3f;
                        if (c.AlliesFiring > 0) s += 0.25f * t.teamwork;
                        if (c.SinceSeen > 3f && fresh) s += 0.25f * t.aggression + 0.1f;
                        s += restless * 0.25f;
                        // Bounding across open ground under his eyes is a last resort, not a habit
                        if (cfg.useFightingCover && c.NoAdvanceCover && c.Seeing && !c.LastStand) s -= 0.4f;
                        if (cfg.useSteadyMoves && c.NoAdvanceCover && c.Seeing && !c.LastStand && !(op.Doctrine < 0.35f && t.aggression > 0.6f)) return No;
                        if (cfg.useSteadyMoves && cfg.useCqb && c.CanClear && !c.Seeing) return No;   // clearing the room is the way in
                        if (c.BeingAimedAt) s -= 0.3f;
                        s += c.PeekFails * 0.1f;
                        if (c.Distance < minR * 0.8f) s -= 0.4f;
                        s -= perch * 0.8f;
                        if (c.Wound == Wound.Wounded) s -= 0.1f;
                        if (critical) s -= 0.5f;
                        return s;
                    }

                case OpAction.Flank:
                    {
                        if (c.AlliesAlive < 1 && t.skill < 0.75f) return No;
                        if (c.Distance < 6f) return No;
                        float s = 0.15f + t.teamwork * 0.25f + t.aggression * 0.15f - fear * 0.4f - pressure * 0.2f;
                        if (c.AlliesAlive >= 1 && c.AlliesFiring > 0) s += 0.3f;
                        s += c.SquadFlanking == 0 ? 0.1f : -0.45f;
                        if (c.SinceSeen > 2f && fresh) s += 0.15f;
                        s += restless * 0.3f;
                        if (cfg.useSteadyMoves && cfg.useCqb && c.CanClear && !c.Seeing) s -= 0.4f;
                        s += c.PeekFails * 0.12f;
                        if (critical) s -= 0.4f;
                        return s;
                    }

                case OpAction.Rush:
                    {
                        if (c.Distance > 20f) return No;
                        float s = 0.05f + t.aggression * 0.4f + e.anger * 0.5f + opening * 0.55f
                                  + (c.Distance < 9f ? 0.2f : 0f) + (op.Archetype.canRush ? 0.25f : 0f) + (c.AlliesAlive >= 3 ? 0.1f : 0f)
                                  - fear * 0.7f - t.caution * 0.3f - lowHp * 0.4f - pressure * 0.3f;
                        if (c.Reloading) s -= 0.3f;
                        if (c.TooClose && !op.Archetype.canRush) s -= 0.4f;
                        if (c.Wound == Wound.Wounded) s -= 0.15f;
                        if (fleeing) s -= 0.6f;
                        return s;
                    }

                case OpAction.Retreat:
                    {
                        float s = fear * 0.9f + lowHp * 0.35f + pressure * 0.3f - t.nerve * 0.35f - t.aggression * 0.2f;
                        if (c.AlliesAlive == 0 && op.Squad != null && op.Squad.Casualties > 0) s += 0.2f;
                        if (c.InCover && fear < 0.6f) s -= 0.3f;
                        if (fleeing) s += 0.55f;
                        if (c.LastStand) s -= 0.8f;
                        return s;
                    }

                case OpAction.Flee:
                case OpAction.Surrender:
                    return No;   // only by morale override

                case OpAction.ClearRoom:
                    {
                        // He's in the next room and out of sight: go in properly (stack, pie, enter) instead of
                        // walking through the door or waiting outside forever. Searching a lost target in a building is this too.
                        if (!cfg.useCqb || !c.CanClear || c.Seeing || c.Dry || c.Reloading) return No;
                        float s = 0.35f + t.aggression * 0.25f + t.teamwork * 0.1f + op.Doctrine * 0.15f + (c.AlliesAlive > 0 ? 0.1f : 0f)
                                  + (lost ? 0.25f : 0f) + restless * 0.3f + (c.Indoor ? 0.1f : 0f)
                                  - fear * 0.5f - lowHp * 0.4f - pressure * 0.4f;
                        if (critical) s -= 0.5f;
                        return s;
                    }

                case OpAction.Stalk:
                    {
                        // Creep up while the player doesn't know where I am
                        if (c.PlayerKnowsMe || c.Seeing || c.SinceSeen > 25f || c.Distance < 8f || c.Distance > 50f || c.Ammo < 0.3f || c.Health < 0.4f) return No;
                        return 0.12f + t.aggression * 0.2f + t.skill * 0.2f + 0.1f - fear * 0.5f - pressure * 0.5f - t.caution * 0.1f;
                    }

                case OpAction.PatchUp:
                    {
                        if (c.Health > 0.65f || c.PatchesLeft <= 0 || c.Seeing || c.SinceSeen < 2.5f || !c.Hidden || pressure > 0.2f) return No;
                        float s = 0.3f + lowHp * 0.9f + t.caution * 0.15f - t.aggression * 0.15f;
                        if (fleeing) s += 0.3f;
                        if (c.LastStand) s -= 0.5f;
                        return s;
                    }
            }
            return No;
        }

        // ---------- Emotions and learning ----------

        void UpdateEmotions(float dt)
        {
            var t = op.Traits;
            ref var e = ref op.Emotion;
            bool inCombat = op.Perception.State == Alertness.Combat;
            bool underFire = Time.time < SuppressedUntil;
            float fearBase = (1f - t.nerve) * 0.1f;
            e.fear = Mathf.MoveTowards(e.fear, fearBase, dt * (op.InCover ? 0.06f : 0.035f));
            e.anger = Mathf.MoveTowards(e.anger, 0f, dt * 0.025f);
            float stressTarget = inCombat ? (op.Perception.CanSee ? 0.55f : 0.35f) + (underFire ? 0.3f : 0f) : 0f;
            e.stress = Mathf.MoveTowards(e.stress, stressTarget * (1.2f - t.discipline * 0.5f), dt * (e.stress < stressTarget ? 0.12f : 0.05f));
            e.fear = Mathf.Clamp01(e.fear); e.anger = Mathf.Clamp01(e.anger); e.stress = Mathf.Clamp01(e.stress);
        }

        public void OnDamaged(DamageInfo info)
        {
            var t = op.Traits;
            ref var e = ref op.Emotion;
            float frac = info.Amount / Mathf.Max(1f, op.Health.Max);
            e.fear = Mathf.Clamp01(e.fear + frac * 1.3f * (1.2f - t.nerve));
            e.anger = Mathf.Clamp01(e.anger + frac * 0.8f * t.aggression);
            e.stress = Mathf.Clamp01(e.stress + 0.15f);
            SuppressedUntil = Time.time + 1.5f;
            // Learn: whatever I was doing got me shot
            bool committed = cfg.useHitCommit && op.Committed;
            Outcome[(int)Current] -= frac * (committed ? 1f : 2.5f);
            if (!committed) Poke(op.Traits.reaction * 0.6f);
        }

        public void OnHitPlayer()
        {
            LastHitOnPlayer = Time.time;
            Outcome[(int)Current] += 0.05f;
        }

        public void OnSuppressed(float amount)
        {
            var t = op.Traits;
            ref var e = ref op.Emotion;
            e.fear = Mathf.Clamp01(e.fear + amount * 0.12f * (1.1f - t.nerve));
            e.stress = Mathf.Clamp01(e.stress + amount * 0.15f);
            SuppressedUntil = Time.time + 1.2f;
            Outcome[(int)Current] -= amount * 0.04f;
            if (amount > 0.5f) Poke(t.reaction);
        }

        public void OnAllyDown(Operative dead)
        {
            var t = op.Traits;
            ref var e = ref op.Emotion;
            float near = Mathf.InverseLerp(30f, 4f, Vector3.Distance(dead.transform.position, op.transform.position));
            e.fear = Mathf.Clamp01(e.fear + (0.12f + 0.25f * near) * (1.1f - t.nerve));
            e.anger = Mathf.Clamp01(e.anger + (0.15f + 0.25f * near) * (0.4f + t.aggression));
            // What they were doing when they died is a lesson too
            Outcome[(int)dead.Mind.Current] -= 0.25f;
            Poke(t.reaction);
        }

        public void OnHeardReload() => reloadOpportunityUntil = Time.time + 2.2f;
        public void OnSawReload() => reloadOpportunityUntil = Time.time + 1.6f;
        public void OnMagazineEmpty() => Poke(0.05f);
    }
}
