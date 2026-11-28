using System;
using System.Collections;
using System.Collections.Generic;
using Alcax.AI;
using Alcax.Audio;
using Alcax.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.Dialogue
{
    /// <summary>
    /// A person on the station who can get hurt: hit colliders on the shared rig (HitZone multipliers apply), panic on
    /// nearby gunfire or when hit (an AmbientNpc runs and hides; a standing NPC like a clerk ducks in place), and a
    /// physics collapse on death. Hits and deaths are announced so StationSecurity can respond.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Civilian : MonoBehaviour
    {
        public static readonly List<Civilian> All = new();
        /// <summary>Someone hurt a civilian (StationSecurity treats the player as the culprit when he's the source).</summary>
        public static event Action<Civilian, DamageInfo> Hurt;
        public static event Action<Civilian> Killed;

        [SerializeField] float maxHealth = 70f;
[SerializeField] float panicSeconds = 25f;
        [Tooltip("0 to 1 (negative = roll at start). Above 0.8, this person fights back with fists when attacked up close.")]
        [SerializeField] float bravery = -1f;

        Health health;
        Npc npc;
        AmbientNpc brain;
        NavMeshAgent agent;
        Rigidbody rb;
        float panicUntil = -100f;
        CornerLatch corner;
        Vector3 threatPos, postPos;
        Quaternion postRot;
        Coroutine staffRun, fight;
        int hitsTaken;
        ICombatTarget lastAttacker;
        float nextLook;
        float nextFightThought, fightCooldownUntil;
        public bool Fighting => fight != null;
        public bool Brave => bravery > 0.8f;
        Vector3 lastHitDir, lastHitPoint;

        public Health Health => health;
        public bool Dead => health != null && health.IsDead;
        public bool Panicking => Time.time < panicUntil;
        public string DisplayName => npc != null ? npc.displayName : name;

        void Awake()
        {
            health = GetComponent<Health>();
            health.SetMax(maxHealth);
            npc = GetComponent<Npc>();
            brain = GetComponent<AmbientNpc>();
            agent = GetComponent<NavMeshAgent>();
            rb = GetComponent<Rigidbody>();
            health.Damaged += OnDamaged;
            health.Died += OnDied;
            // Pockets: searchable once dead
            var loot = GetComponent<Alcax.Items.LootContainer>();
            if (loot == null) loot = gameObject.AddComponent<Alcax.Items.LootContainer>();
            var torso = transform.Find("Rig/Body");
            if (torso == null && TryGetComponent<Animator>(out var an) && an.isHuman) torso = an.GetBoneTransform(HumanBodyBones.Chest);   // (a human model)
            loot.Configure("Body", "civilian", null, 8, torso, health);
            if (bravery < 0f) bravery = UnityEngine.Random.value;
            postPos = transform.position;
            postRot = transform.rotation;
            if (brain == null && agent != null) agent.enabled = false;   // a post (desk, counter): the agent only runs while fleeing
        }

        void OnEnable() { All.Add(this); Hearing.Listen(OnHear); }
        void OnDisable() { All.Remove(this); Hearing.Stop(OnHear); }

        void Update()
        {
            if (Dead) return;
            bool panicking = Panicking || (brain != null && brain.Panicking);
if (npc != null) npc.CanTalk = !panicking;
            if (Time.time >= nextLook)
            {
                nextLook = Time.time + 0.5f + UnityEngine.Random.value * 0.2f;
                var eye = transform.position + Vector3.up * 1.6f;
                var danger = DangerInSight(eye);
                if (danger != null) Scare(danger.Position);
            }
            // Brave people in a panic watch for an opening: he's reloading, looking away, or they're cornered or angry
            if (panicking && fight == null && brain != null && agent != null && bravery > 0.6f && Time.time >= nextFightThought && Time.time > fightCooldownUntil)
            {
                nextFightThought = Time.time + 0.5f;
var p = Foe();
                if (p != null && health.Normalized > 0.45f)
                {
                    float d = Vector3.Distance(transform.position, p.Position);
                    bool facingAway = Vector3.Angle(p.Forward.Flat(), (transform.position - p.Position).Flat()) > 110f;
                    float score = bravery + (p.IsReloading ? 0.5f : 0f) + (facingAway ? 0.4f : 0f) + (Cornered(transform, p.Position) ? 0.25f : 0f)
                                  + (hitsTaken > 0 ? 0.2f : 0f) - d * 0.05f - (1f - health.Normalized) * 0.6f;
                    if (d < 10f && score > 0.95f) fight = StartCoroutine(Fight());
                }
            }

        }

        void OnHear(NoiseEvent e)
        {
            if (Dead || e.Kind != NoiseKind.Gunshot || e.Source == gameObject) return;
            float loud = Hearing.Loudness(e, transform.position + Vector3.up * 1.6f);
            if (loud < 0.12f) return;
            // On the range, gunfire is normal: people in the hall and the armory don't panic at it
            if (StationSecurity.IsRangeFire(e.Position) && !StationSecurity.InRange(transform.position) && loud < 0.6f) return;
            Scare(e.Position);
        }

        void OnDamaged(DamageInfo info)
        {
            if (Dead) return;
            lastHitDir = info.Direction.sqrMagnitude > 0.01f ? info.Direction.normalized : -transform.forward;
            lastHitPoint = info.Point;
            if (health.Current > 0f && UnityEngine.Random.value < 0.7f) SoundManager.PlayAt(Sfx.NpcGrunt, transform.position + Vector3.up * 1.6f, 0.75f, 0.12f);
            SoundManager.PlayAt(Sfx.BodyHit, info.Point, 0.6f);
            var from = info.Source != null ? info.Source.transform.position : info.Point - lastHitDir * 6f;
            if (npc != null && npc.InConversation && DialogueUI.Instance != null) DialogueUI.Instance.Close();
            hitsTaken++;
            var attacker = Targets.Of(info.Source);
            if (attacker != null) lastAttacker = attacker;
            if (fight == null && attacker != null && Brave && brain != null && agent != null && health.Normalized > 0.45f && Vector3.Distance(transform.position, attacker.Position) < 9f)
                fight = StartCoroutine(Fight());
            else if (fight == null) Scare(from);
            Hurt?.Invoke(this, info);
        }

        void Scare(Vector3 from)
        {
            // Squared up and fighting: stay in the fight (re-panicking the crowd routine ran its flee and huddle
            // alongside, so up close they swung with their hands still up). The fight hands back to it when it ends.
            if (fight != null) { threatPos = from; return; }
            panicUntil = Time.time + panicSeconds;
            if (npc != null && npc.InConversation && DialogueUI.Instance != null) DialogueUI.Instance.Close();
            threatPos = from;
            if (brain != null && brain.enabled) brain.Panic(from, panicSeconds);
            else if (brain == null && staffRun == null) staffRun = StartCoroutine(StaffPanic());
        }

const int SightMask = ~((1 << 2) | (1 << 8));

        /// <summary>
        /// The threat is close and has a clear line to me: no point running. The line goes to my chest height and
        /// ignores my own body (its hit colliders blocked it standing and cleared it crouched: hands up, down, up).
        /// </summary>
        public static bool Cornered(Transform self, Vector3 threat) => Seen(self, threat, 10f);

        /// <summary>He's within range and has a clear line to my chest (my own body doesn't count as cover).</summary>
        public static bool Seen(Transform self, Vector3 threat, float range)
        {
            var me = self.position;
            if (Vector3.Distance(me, threat) >= range) return false;
            var from = threat + Vector3.up * 1.5f;
            var to = me + Vector3.up * 1.0f;
            var d = to - from; float len = d.magnitude;
            if (len < 0.01f) return true;
            int n = Physics.RaycastNonAlloc(from, d / len, sightHits, len, SightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!sightHits[i].transform.IsChildOf(self)) return false;
            return true;
        }
        static readonly RaycastHit[] sightHits = new RaycastHit[16];

        /// <summary>A cornered-or-not decision that, once made, holds a while (no twitching between hands up and ducking).</summary>
        public struct CornerLatch
        {
            bool on; float until;
            public bool Update(bool raw)
            {
                if (raw != on && Time.time >= until) { on = raw; until = Time.time + (raw ? 3f : 2f); }
                return on;
            }
        }

/// <summary>
        /// Where to run. Borrowed from the combat AI: the candidate spots are NavGraph nodes (the whole walkable map,
        /// with the hiding prior's enclosure score), and cover is judged with CoverFinder.Classify against his eye line.
        /// Far from him is best, behind something is better (people crouch behind low cover), routes that pass close
        /// to him are rejected, and spots other fleeing people already picked are avoided so a crowd spreads out.
        /// </summary>
        public static Vector3 FleeSpot(NavMeshAgent agent, Vector3 pos, Vector3 threat)
        {
            var eye = threat + Vector3.up * 1.5f;
            float dNow = Vector3.Distance(pos, threat);
            var cands = new List<(Vector3 p, float h)>();
            NavGraph.EnsureBuilt();
            if (NavGraph.Count > 0)
            {
                NavGraph.Query(pos, 42f, near);
                foreach (int k in near)
                {
                    var c = NavGraph.Pos[k];
                    if (Mathf.Abs(c.y - pos.y) > 4.5f) continue;                  // stay on (or near) my floor
                    float dThreat = Vector3.Distance(c, threat);
                    float away = dThreat - dNow;
                    if (away < 4f) continue;
                    float h = away * 1.4f + dThreat * 0.5f - Vector3.Distance(pos, c) * 0.04f + NavGraph.Enclosure[k] * 3f + UnityEngine.Random.Range(0f, 4f);   // as far as he can get
                    foreach (var (cp, ct) in claims) if (Time.time - ct < 12f && (cp - c).sqrMagnitude < 2.5f) h -= 5f;
                    cands.Add((c, h));
                }
            }
            else
            {
                foreach (var p in StationPoint.All) cands.Add((p.transform.position, Vector3.Distance(p.transform.position, threat)));
                for (int i = 0; i < 14; i++) { var c = pos + Quaternion.Euler(0f, i * (360f / 14f), 0f) * Vector3.forward * UnityEngine.Random.Range(10f, 24f); cands.Add((c, Vector3.Distance(c, threat))); }
            }
            cands.Sort((a, b) => b.h.CompareTo(a.h));
            var best = pos; float bestS = float.MinValue;
            var path = new NavMeshPath();
            int checks = 0;
            foreach (var (c0, h) in cands)
            {
                if (++checks > 30) break;
                if (!NavMesh.SamplePosition(c0, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                var c = hit.position;
                var kind = CoverFinder.Classify(c, eye, out _);
                bool hidden = kind != CoverKind.None || CoverFinder.Blocked(eye, c + Vector3.up * 1.2f);
                float sc = h + (kind == CoverKind.Low ? 9f : kind != CoverKind.None ? 10f : hidden ? 6f : 0f);
                if (sc <= bestS) continue;
                if (agent == null || !agent.isOnNavMesh || !agent.CalculatePath(c, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                // Don't run past him, and don't run forever
                bool pastHim = false; float len = 0f;
                var cs = path.corners;
                for (int i = 1; i < cs.Length; i++)
                {
                    len += Vector3.Distance(cs[i - 1], cs[i]);
                    var closest = ClosestOnSegment(cs[i - 1], cs[i], threat);
                    if (Vector3.Distance(closest, threat) < Mathf.Min(5f, dNow * 0.8f)) pastHim = true;
                }
                if (pastHim) continue;
                sc -= len * 0.03f;
                if (sc > bestS) { bestS = sc; best = c; }
            }
            claims.Add((best, Time.time));
            if (claims.Count > 40) claims.RemoveAt(0);
            return best;
        }

        static readonly List<int> near = new List<int>();
        static readonly List<(Vector3 p, float t)> claims = new List<(Vector3, float)>();

        static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            if (l < 1e-4f) return a;
            return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / l);
        }

        void Face(Vector3 dir, float rate = 6f)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-rate * Time.deltaTime));
        }

        /// <summary>
        /// Staff at a post. Cornered (he's close and sees me): crouch, hands up, facing him. Otherwise, if I can walk,
        /// run somewhere out of his sight and huddle there; if not, duck behind my counter. Once it's quiet, back to the post.
        /// </summary>
        IEnumerator StaffPanic()
        {
            bool fled = false;
            while (Panicking && !Dead)
            {
                bool cornered = corner.Update(Cornered(transform, threatPos));
                if (!cornered && !fled && agent != null)
                {
                    var start = transform.position;
                    if (NavMesh.SamplePosition(start, out var onMesh, 2.5f, NavMesh.AllAreas)) start = onMesh.position;
                    agent.enabled = true;
                    agent.Warp(start);
                    agent.speed = UnityEngine.Random.Range(5.8f, 7.2f);   // (flat out)
                    agent.acceleration = 12f;
                    agent.isStopped = false;
                    npc.HandsUp = 0f;
                    if (agent.isOnNavMesh && agent.SetDestination(FleeSpot(agent, transform.position, threatPos)))
                    {
                        float give = Time.time + 12f;
                        while (agent.pathPending) yield return null;
                        while (!Dead && Time.time < give && agent.remainingDistance > agent.stoppingDistance + 0.3f)
                        {
                            npc.SitBlend = Mathf.MoveTowards(npc.SitBlend, 0f, Time.deltaTime * 3f);
                            npc.MoveSpeed = agent.velocity.magnitude;
                            if (Cornered(transform, threatPos) && Vector3.Distance(transform.position, threatPos) < 5f) break;   // ran into him
                            yield return null;
                        }
                        if (Dead) yield break;
                        agent.isStopped = true;
                        npc.MoveSpeed = 0f;
                    }
                    fled = true;
                }
                // Cower: crouched; hands up and facing him when cornered, else turned away
                npc.SitBlend = Mathf.MoveTowards(npc.SitBlend, 0.6f, Time.deltaTime * 2f);
                npc.HandsUp = cornered ? 1f : 0f;
                Face(cornered ? threatPos - transform.position : transform.position - threatPos);
                yield return null;
            }
            if (Dead) yield break;
            npc.HandsUp = 0f;
            while (npc.SitBlend > 0f) { npc.SitBlend = Mathf.MoveTowards(npc.SitBlend, 0f, Time.deltaTime * 1.5f); yield return null; }
            if (fled && agent != null && agent.enabled && agent.isOnNavMesh)
            {
                // Walk back to the post
                agent.isStopped = false;
                agent.speed = 1.3f;
                agent.SetDestination(postPos);
                float give = Time.time + 40f;
                while (agent.pathPending) yield return null;
                while (!Dead && Time.time < give && agent.remainingDistance > agent.stoppingDistance + 0.15f && !Panicking)
                {
                    npc.MoveSpeed = agent.velocity.magnitude;
                    yield return null;
                }
                npc.MoveSpeed = 0f;
                if (Panicking) { staffRun = StartCoroutine(StaffPanic()); yield break; }
                agent.enabled = false;
                float t = 0f;
                var p0 = transform.position; var r0 = transform.rotation;
                while (t < 1f) { t += Time.deltaTime / 0.6f; transform.SetPositionAndRotation(Vector3.Lerp(p0, new Vector3(postPos.x, p0.y, postPos.z), t), Quaternion.Slerp(r0, postRot, t)); yield return null; }
            }
            else
            {
                float t = 0f; var r0 = transform.rotation;
                while (t < 1f) { t += Time.deltaTime / 0.6f; transform.rotation = Quaternion.Slerp(r0, postRot, t); yield return null; }
            }
            staffRun = null;
        }

/// <summary>
        /// A brave civilian rushes the player and throws punches. Breaks off and runs after taking two more hits, when badly
        /// hurt, when he gets away, or after a few punches.
        /// </summary>
        IEnumerator Fight()
        {
            var p = Foe();
            brain.Suspend();
            npc.SitBlend = 0f; npc.HandsUp = 0f;   // (up off a huddle or a seat to fight: no punching from a crouch)
            panicUntil = -100f;
            int startHits = hitsTaken, punches = 0;
            float end = Time.time + 14f, nextPunch = 0f;
            agent.isStopped = false;
            agent.speed = UnityEngine.Random.Range(3.6f, 4.6f);
            SoundManager.PlayAt(Sfx.NpcGrunt, transform.position + Vector3.up * 1.6f, 0.9f, 0.1f);
            while (!Dead && p != null && Time.time < end)
            {
                float d = Vector3.Distance(transform.position, p.Position);
                if (hitsTaken - startHits >= 2 || health.Normalized < 0.35f || d > 14f || punches >= 4) break;
                if (d > 1.25f)
                {
                    if (agent.isOnNavMesh) agent.SetDestination(p.Position);
                    npc.MoveSpeed = agent.velocity.magnitude;
                }
                else
                {
                    if (agent.isOnNavMesh) agent.ResetPath();
                    npc.MoveSpeed = 0f;
                    Face(p.Position - transform.position, 12f);
                    if (Time.time >= nextPunch)
                    {
                        nextPunch = Time.time + UnityEngine.Random.Range(0.7f, 1.1f);
                        punches++;
                        float t = 0f; bool landed = false;
                        while (t < 1f && !Dead)
                        {
                            t += Time.deltaTime / 0.42f;
                            npc.PunchT = t;
                            if (!landed && t > 0.45f)
                            {
                                landed = true;
                                SoundManager.PlayAt(Sfx.Swing, transform.position + Vector3.up * 1.4f, 0.6f);
                                if (Vector3.Distance(transform.position, p.Position) < 1.7f)
                                {
                                    var dir = (p.Position - transform.position).normalized;
                                    if (p.Health != null) p.Health.TakeDamage(new DamageInfo { Amount = UnityEngine.Random.Range(7f, 12f), Point = p.HeadPosition - dir * 0.2f, Direction = dir, Knockback = 2f, Source = gameObject });
                                    SoundManager.PlayAt(Sfx.Punch, p.HeadPosition, 0.8f);
                                }
                            }
                            yield return null;
                        }
                        npc.PunchT = -1f;
                    }
                }
                yield return null;
            }
            npc.PunchT = -1f;
            npc.MoveSpeed = 0f;
            fight = null;
            fightCooldownUntil = Time.time + 8f;
            if (Dead) yield break;
            // Done fighting: run
            panicUntil = Time.time + panicSeconds;
            brain.Resume(true, p != null ? p.Position : transform.position);
        }

/// <summary>Who's dangerous to me: my last attacker if he's still around, else a wanted player, else the nearest station threat.</summary>
        ICombatTarget Foe()
        {
            if (lastAttacker != null && !lastAttacker.IsDead && Vector3.Distance(transform.position, lastAttacker.Position) < 14f) return lastAttacker;
            var p = PlayerTarget.Instance;
            var sec = StationSecurity.Instance;
            if (p != null && !p.IsDead && (sec == null || sec.Wanted) && Vector3.Distance(transform.position, p.Position) < 12f) return p;
            ICombatTarget best = null; float bd = 12f;
            foreach (var o in Operative.All)
            {
                if (!StationSecurity.IsStationThreat(o)) continue;
                float d = Vector3.Distance(transform.position, o.transform.position);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        /// <summary>An armed hostile (or a wanted player) I can see nearby.</summary>
        ICombatTarget DangerInSight(Vector3 eye)
        {
            var sec = StationSecurity.Instance;
            var p = PlayerTarget.Instance;
            if (p != null && !p.IsDead && sec != null && sec.Wanted && Vector3.Distance(eye, p.HeadPosition) < 14f && !Physics.Linecast(eye, p.HeadPosition, SightMask, QueryTriggerInteraction.Ignore)) return p;
            foreach (var o in Operative.All)
            {
                if (!StationSecurity.IsStationThreat(o)) continue;
                var head = o.EyePosition;
                if (Vector3.Distance(eye, head) > 20f) continue;
                if (!Physics.Linecast(eye, head, SightMask, QueryTriggerInteraction.Ignore)) return o;
            }
            return null;
        }

        void OnDied()
        {
            if (fight != null) { StopCoroutine(fight); fight = null; }
            if (npc != null) npc.PunchT = -1f;
            if (staffRun != null) { StopCoroutine(staffRun); staffRun = null; }
            if (npc != null) npc.HandsUp = 0f;
            if (brain != null) brain.Die();
            if (npc != null)
            {
                if (npc.InConversation && DialogueUI.Instance != null) DialogueUI.Instance.Close();
                npc.CanTalk = false;
                npc.enabled = false;
            }
            if (agent != null) { if (agent.enabled && agent.isOnNavMesh) agent.ResetPath(); agent.enabled = false; }
            Killed?.Invoke(this);
            StartCoroutine(Collapse());
        }

        IEnumerator Collapse()
        {
            yield return new WaitForSeconds(0.05f);
            RagdollCorpse.Go(gameObject, lastHitDir + Vector3.up * 0.1f, lastHitPoint, 70f);
        }
    }
}
