using System.Collections;
using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Player;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.Dialogue
{
    /// <summary>
    /// Lobby life for a station resident. On a loop it picks an activity: sit on a bench, look out the window,
    /// work at a console, lean on the mezzanine railing, wander, or chat with another resident.
    /// It walks there on the NavMesh, does it for a while, and repeats. When the player talks to it,
    /// it stops, turns to the player, and resumes afterwards. It drives an Npc rig (walk, sit, type, look, speak).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Npc))]
    public class AmbientNpc : MonoBehaviour
    {
        public static readonly List<AmbientNpc> All = new();

        public enum Temperament { Settler, Worker, Social }

        [Tooltip("Settlers mostly sit (and start seated), workers stay at consoles and windows, socials move around and chat.")]
        [SerializeField] Temperament temperament = Temperament.Worker;

[SerializeField] float walkSpeed = 1.25f;
        [Tooltip("Optional home area (min, max corners): most activities are picked inside it (a food court regular).")]
        [SerializeField] bool hasHome;
        [SerializeField] Vector3 homeMin, homeMax;
        [Range(0f, 1f)] [SerializeField] float homeBias = 0.85f;

        enum Activity { Deciding, Walking, Sitting, Window, Working, Railing, Chatting, Wandering, Talking, Fleeing, Cowering }

        NavMeshAgent agent;
        Npc npc;
        Transform player;
        Activity activity = Activity.Deciding;
        StationPoint point;
        AmbientNpc chatPartner;
        Vector3 chatSpot;
        bool chatInvited;
        Coroutine routine;
        float facingYaw;
        bool hasFacing;

        public bool Available => !panicking && (activity == Activity.Window || activity == Activity.Wandering || activity == Activity.Railing || activity == Activity.Deciding)
                                 && chatPartner == null && !npc.InConversation;

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            npc = GetComponent<Npc>();
            walkSpeed *= Random.Range(0.92f, 1f);   // (an easy stroll, each their own pace)
            agent.speed = walkSpeed;
            agent.acceleration = 5f;
            agent.angularSpeed = 300f;
            agent.stoppingDistance = 0.08f;
            agent.autoBraking = true;
            agent.radius = 0.32f;
            agent.height = 1.8f;
            agent.updateRotation = true;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.Head;
            routine = StartCoroutine(Life());
        }

        void Update()
        {
            npc.MoveSpeed = agent.enabled && agent.isOnNavMesh ? agent.velocity.magnitude : 0f;

            // Turn to face the activity direction once stopped
            if (hasFacing && agent.enabled && agent.velocity.sqrMagnitude < 0.01f && !npc.InConversation)
            {
                var rot = Quaternion.Euler(0f, facingYaw, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }

            // Player conversation: pause and face the player (if not seated)
            if (npc.InConversation && player != null)
            {
                if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                if (npc.SitBlend < 0.5f)
                {
                    var to = player.position - transform.position;
                    to.y = 0f;
                    if (to.sqrMagnitude > 0.01f)
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 1f - Mathf.Exp(-6f * Time.deltaTime));
                }
            }
            else if (agent.enabled && agent.isOnNavMesh && agent.isStopped && activity == Activity.Walking) agent.isStopped = false;
        }

        // ---------- Life loop ----------

        IEnumerator Life()
        {
            if (temperament == Temperament.Settler) yield return StartSeated();
            yield return new WaitForSeconds(Random.Range(0.2f, 3f));
            while (true)
            {
                while (npc.InConversation) yield return null;
                if (chatInvited) { yield return DoChat(); continue; }

                activity = Activity.Deciding;
                float r = Random.value;
                switch (temperament)
                {
                    case Temperament.Settler:
                        if (r < 0.85f) yield return DoPoint(PointKind.Seat, 60f, 180f);
                        else yield return DoPoint(PointKind.Window, 20f, 45f);
                        break;
                    case Temperament.Worker:
                        if (r < 0.45f) yield return DoPoint(PointKind.Console, 40f, 90f);
                        else if (r < 0.72f) yield return DoPoint(PointKind.Window, 25f, 55f);
                        else if (r < 0.82f) yield return DoPoint(PointKind.Railing, 20f, 40f);
                        else if (r < 0.92f) yield return DoPoint(PointKind.Seat, 40f, 90f);
                        else yield return StartChat();
                        break;
                    default:
                        if (r < 0.3f) yield return StartChat();
                        else if (r < 0.55f) yield return DoPoint(PointKind.Window, 20f, 45f);
                        else if (r < 0.7f) yield return DoPoint(PointKind.Railing, 20f, 40f);
                        else if (r < 0.85f) yield return DoPoint(PointKind.Seat, 30f, 70f);
                        else yield return DoWander();
                        break;
                }
                // Stand a moment before deciding again (no constant to-and-fro)
                activity = Activity.Deciding;
                yield return Wait(Random.Range(3f, 8f), () => chatInvited);
            }
        }

        IEnumerator WalkTo(Vector3 target)
        {
            if (!agent.enabled) yield break;
            activity = Activity.Walking;
            hasFacing = false;
            if (!agent.isOnNavMesh) yield break;
            agent.isStopped = false;
            if (!agent.SetDestination(target)) yield break;
            float timeout = Time.time + 40f;
            while (agent.pathPending) yield return null;
            while (Time.time < timeout)
            {
                while (npc.InConversation) yield return null;
                if (chatInvited && chatPartner != null && activity == Activity.Walking && target != chatSpot) yield break;
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.12f) break;
                yield return null;
            }
        }

        IEnumerator Wait(float seconds, System.Func<bool> abort = null)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                if (abort != null && abort()) yield break;
                while (npc.InConversation) { end += Time.deltaTime; yield return null; }
                yield return null;
            }
        }

bool InHome(Vector3 p) => p.x >= homeMin.x && p.x <= homeMax.x && p.z >= homeMin.z && p.z <= homeMax.z && p.y >= homeMin.y - 0.5f && p.y <= homeMax.y;

        /// <summary>
        /// A free point of this kind. Regulars mostly stay in their home area; seats next to someone already sitting
        /// are favored (people share tables and benches rather than spreading out one per table).
        /// </summary>
        StationPoint FindFree(PointKind kind)
        {
            bool home = hasHome && Random.value < homeBias;
            var free = new List<StationPoint>();
            var weights = new List<float>();
            float total = 0f;
            for (int pass = 0; pass < 2 && free.Count == 0; pass++)
                foreach (var p in StationPoint.All)
                {
                    if (p.kind != kind || p.Occupant != null) continue;
                    if (pass == 0 && home && !InHome(p.transform.position)) continue;
                    float w = 1f;
                    if (kind == PointKind.Seat)
                        foreach (var q in StationPoint.All)
                            if (q != p && q.kind == PointKind.Seat && q.Occupant != null && (q.transform.position - p.transform.position).sqrMagnitude < 2.6f) w += 3f;
                    free.Add(p); weights.Add(w); total += w;
                }
            if (free.Count == 0) return null;
            float pick = Random.value * total;
            for (int i = 0; i < free.Count; i++) { pick -= weights[i]; if (pick <= 0f) return free[i]; }
            return free[free.Count - 1];
        }

        IEnumerator DoPoint(PointKind kind, float minTime, float maxTime)
        {
            var p = FindFree(kind);
            if (p == null) { yield return DoWander(); yield break; }
            p.Occupant = this;
            point = p;
            yield return WalkTo(kind == PointKind.Seat ? p.ApproachPoint : p.transform.position);
            if (chatInvited) { Release(); yield break; }
            facingYaw = p.transform.eulerAngles.y;
            hasFacing = true;

            if (kind == PointKind.Seat)
            {
                activity = Activity.Sitting;
                yield return SitDown(p);
                yield return Wait(Random.Range(minTime, maxTime));
                yield return StandUp(p);
            }
            else
            {
                activity = kind == PointKind.Window ? Activity.Window : kind == PointKind.Console ? Activity.Working : Activity.Railing;
                npc.Typing = kind == PointKind.Console;
                yield return Wait(Random.Range(minTime, maxTime), () => chatInvited);
                npc.Typing = false;
            }
            Release();
        }

        void Release()
        {
            if (point != null && point.Occupant == this) point.Occupant = null;
            point = null;
        }

        IEnumerator SitDown(StationPoint seat)
        {
            agent.enabled = false;
            Vector3 from = transform.position;
            Quaternion fromRot = transform.rotation;
            Quaternion toRot = seat.transform.rotation;
            Vector3 to = new Vector3(seat.transform.position.x, from.y, seat.transform.position.z);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.9f;
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.position = Vector3.Lerp(from, to, k);
                transform.rotation = Quaternion.Slerp(fromRot, toRot, Mathf.Clamp01(t * 2f));
                npc.SitBlend = k;
                yield return null;
            }
            npc.SitBlend = 1f;
        }

        IEnumerator StandUp(StationPoint seat)
        {
            Vector3 from = transform.position;
            Vector3 to = seat.ApproachPoint;
            to.y = from.y;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 0.9f;
                float k = Mathf.SmoothStep(0f, 1f, t);
                transform.position = Vector3.Lerp(from, to, k);
                npc.SitBlend = 1f - k;
                yield return null;
            }
            npc.SitBlend = 0f;
            agent.enabled = true;
            agent.Warp(transform.position);
        }

        /// <summary>Settlers begin the scene already seated on the nearest free bench seat.</summary>
        IEnumerator StartSeated()
        {
            StationPoint best = null;
            float bd = float.MaxValue;
            foreach (var p in StationPoint.All)
            {
                if (p.kind != PointKind.Seat || p.Occupant != null) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p; }
            }
            if (best == null) yield break;
            best.Occupant = this;
            point = best;
            agent.enabled = false;
            transform.SetPositionAndRotation(new Vector3(best.transform.position.x, transform.position.y, best.transform.position.z), best.transform.rotation);
            npc.SitBlend = 1f;
            activity = Activity.Sitting;
            facingYaw = best.transform.eulerAngles.y;
            hasFacing = true;
            yield return Wait(Random.Range(60f, 200f));
            yield return StandUp(best);
            Release();
        }

        IEnumerator DoWander()
        {
            activity = Activity.Wandering;
            for (int i = 0; i < 10; i++)
            {
                var guess = hasHome && Random.value < homeBias
                    ? new Vector3(Random.Range(homeMin.x, homeMax.x), homeMin.y, Random.Range(homeMin.z, homeMax.z))
                    : transform.position + new Vector3(Random.Range(-12f, 12f), 0f, Random.Range(-12f, 12f));
                if (NavMesh.SamplePosition(guess, out var hit, 1.5f, NavMesh.AllAreas))
                {
                    var path = new NavMeshPath();
                    if (agent.isOnNavMesh && agent.CalculatePath(hit.position, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        yield return WalkTo(hit.position);
                        activity = Activity.Wandering;
                        yield return Wait(Random.Range(2f, 6f), () => chatInvited);
                        yield break;
                    }
                }
            }
            yield return Wait(2f);
        }

        // ---------- Chatting ----------

        IEnumerator StartChat()
        {
            AmbientNpc partner = null;
            float best = float.MaxValue;
            foreach (var other in All)
            {
                if (other == this || !other.Available || !other.agent.enabled) continue;
                float d = (other.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; partner = other; }
            }
            if (partner == null) { yield return DoWander(); yield break; }

            // Meet roughly halfway, on the NavMesh
            var mid = (transform.position + partner.transform.position) * 0.5f;
            if (!NavMesh.SamplePosition(mid, out var hit, 3f, NavMesh.AllAreas)) { yield return DoWander(); yield break; }
            var axis = (partner.transform.position - transform.position);
            axis.y = 0f;
            axis = axis.sqrMagnitude > 0.01f ? axis.normalized : transform.forward;

            chatPartner = partner;
            chatSpot = hit.position - axis * 0.6f;
            partner.chatPartner = this;
            partner.chatSpot = hit.position + axis * 0.6f;
            partner.chatInvited = true;
            chatInvited = true;
            yield return DoChat();
        }

        IEnumerator DoChat()
        {
            chatInvited = false;
            var partner = chatPartner;
            if (partner == null) yield break;
            Release();
            yield return WalkTo(chatSpot);
            activity = Activity.Chatting;
            // Wait for the partner to arrive (or give up)
            float giveUp = Time.time + 20f;
            while (partner != null && partner.activity != Activity.Chatting && Time.time < giveUp) yield return null;

            if (partner != null && partner.activity == Activity.Chatting)
            {
                npc.LookTarget = partner.npc.HeadTransform;
                var to = partner.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f) { facingYaw = Quaternion.LookRotation(to).eulerAngles.y; hasFacing = true; }

                float end = Time.time + Random.Range(20f, 40f);
                bool leader = All.IndexOf(this) < All.IndexOf(partner);
                bool myTurn = leader;
                while (Time.time < end && partner != null && partner.chatPartner == this)
                {
                    float turn = Random.Range(1.8f, 4f);
                    npc.Speaking = myTurn;
                    float turnEnd = Time.time + turn;
                    while (Time.time < turnEnd)
                    {
                        if (myTurn && Random.value < Time.deltaTime * 3f)
                            SoundManager.PlayAt(Sfx.TalkBlip, npc.FocusPoint, 0.12f, 0.25f);
                        while (npc.InConversation) { npc.Speaking = false; yield return null; }
                        yield return null;
                    }
                    if (leader)
                    {
                        // The leader sets the rhythm: alternate turns
                        myTurn = !myTurn;
                        partner.chatTurnFlip = true;
                    }
                    else if (chatTurnFlip) { myTurn = !myTurn; chatTurnFlip = false; }
                    else myTurn = !myTurn;
                }
            }
            npc.Speaking = false;
            npc.LookTarget = null;
            if (partner != null && partner.chatPartner == this) partner.chatPartner = null;
            chatPartner = null;
            yield return Wait(Random.Range(0.5f, 1.5f));
        }

bool chatTurnFlip;

        // ---------- Panic ----------
        // Gunfire or getting hit: drop everything, run somewhere away from the shooter and out of his sight, and huddle
        // there until it has been quiet for a while. Then back to normal life.
        float panicUntil = -100f;
        Vector3 panicFrom;
        bool panicking;
        Civilian.CornerLatch corner;
        bool suspended;   // (the body is someone else's for now: a fight)
        public bool Panicking => panicking;

public void Panic(Vector3 from, float seconds)
        {
            panicUntil = Mathf.Max(panicUntil, Time.time + seconds);
            panicFrom = from;
            if (panicking || suspended) return;   // already running (the flee loop re-plans only when its plan is compromised), or handed over to a fight
            panicking = true;
            if (routine != null) StopCoroutine(routine);
            npc.Typing = false; npc.Speaking = false; npc.LookTarget = null;
            if (chatPartner != null && chatPartner.chatPartner == this) chatPartner.chatPartner = null;
            chatPartner = null; chatInvited = false;
            Release();
            routine = StartCoroutine(Flee());
        }

        /// <summary>Hand the body over to something else for a while (a brave civilian picking a fight).</summary>
        public void Suspend()
        {
            suspended = true;
            if (routine != null) StopCoroutine(routine);
            routine = null;
            Release();
            if (chatPartner != null && chatPartner.chatPartner == this) chatPartner.chatPartner = null;
            chatPartner = null; chatInvited = false;
            npc.Typing = false; npc.Speaking = false; npc.HandsUp = 0f;
            if (!agent.enabled) { npc.SitBlend = 0f; agent.enabled = true; agent.Warp(transform.position); }
            panicking = false;
            hasFacing = false;
        }

        /// <summary>Back to life after Suspend: panicked (run and hide) or calm.</summary>
        public void Resume(bool panic, Vector3 from)
        {
            suspended = false;
            agent.speed = walkSpeed; agent.acceleration = 5f;
            if (panic) { panicking = false; Panic(from, 25f); }
            else routine = StartCoroutine(Life());
        }

        /// <summary>My flee plan is bad now: he's right on me, or the place I'm going is in his view and nearer to him than I am.</summary>
        bool Compromised(Vector3 dest)
        {
            float dMe = Vector3.Distance(transform.position, panicFrom);
            if (dMe < 6f) return true;
            bool destSeen = !Physics.Linecast(panicFrom + Vector3.up * 1.5f, dest + Vector3.up * 1.2f, ~((1 << 2) | (1 << 8)), QueryTriggerInteraction.Ignore);
            return destSeen && Vector3.Distance(dest, panicFrom) < dMe;
        }

        /// <summary>Stop everything for good (killed).</summary>
        public void Die()
        {
            StopAllCoroutines();
            Release();
            if (chatPartner != null && chatPartner.chatPartner == this) chatPartner.chatPartner = null;
            chatPartner = null;
            npc.Speaking = false; npc.Typing = false;
            enabled = false;
        }

IEnumerator Flee()
        {
            hasFacing = false;
            // Up off the seat (fast)
            if (!agent.enabled)
            {
                float t = 0f, s0 = npc.SitBlend;
                while (t < 1f) { t += Time.deltaTime / 0.35f; npc.SitBlend = Mathf.Lerp(s0, 0f, t); yield return null; }
                agent.enabled = true;
                agent.Warp(transform.position);
            }
            float lastPlan = -100f;
            while (Time.time < panicUntil)
            {
                // Run: pick a spot once, and stick to it unless it becomes clearly bad (re-planning at most every 3 s)
                activity = Activity.Fleeing;
                npc.SitBlend = 0f; npc.HandsUp = 0f;
                agent.speed = Random.Range(5.8f, 7.2f);   // (flat out: running for their lives)
                agent.acceleration = 12f;
                var dest = FleeSpot(panicFrom);
                lastPlan = Time.time;
                if (agent.isOnNavMesh && agent.SetDestination(dest))
                {
                    float give = Time.time + 15f;
                    while (agent.pathPending) yield return null;
                    bool replan = false;
                    while (Time.time < give && agent.remainingDistance > agent.stoppingDistance + 0.3f)
                    {
                        if (Time.time - lastPlan > 3f && Compromised(dest)) { replan = true; break; }
                        yield return null;
                    }
                    if (replan) continue;
                }
                // Huddle until quiet. Cornered (close and in his sight): hands up, facing him. Only move again if the
                // hiding spot is clearly compromised, and not more often than every 4 s.
                activity = Activity.Cowering;
                agent.speed = walkSpeed;
                var away = transform.position - panicFrom; away.y = 0f;
                if (away.sqrMagnitude > 0.01f) { facingYaw = Quaternion.LookRotation(away).eulerAngles.y; hasFacing = true; }
                float huddleStart = Time.time;
                bool move = false;
                while (Time.time < panicUntil)
                {
                    npc.SitBlend = Mathf.MoveTowards(npc.SitBlend, 0.55f, Time.deltaTime * 2f);
                    bool cornered = corner.Update(Civilian.Cornered(transform, panicFrom));
                    npc.HandsUp = cornered ? 1f : 0f;
                    if (cornered) { var to = panicFrom - transform.position; to.y = 0f; if (to.sqrMagnitude > 0.01f) facingYaw = Quaternion.LookRotation(to).eulerAngles.y; }
                    else if (Time.time - huddleStart > 4f && Civilian.Seen(transform, panicFrom, 15f))   // (spotted out here: run somewhere else)
                    { move = true; break; }
                    yield return null;
                }
                npc.HandsUp = 0f;
                if (!move) break;
            }
            while (npc.SitBlend > 0f) { npc.SitBlend = Mathf.MoveTowards(npc.SitBlend, 0f, Time.deltaTime * 1.5f); yield return null; }
            panicking = false;
            hasFacing = false;
            agent.speed = walkSpeed; agent.acceleration = 5f;
            routine = StartCoroutine(Life());
        }

        Vector3 FleeSpot(Vector3 threat) => Civilian.FleeSpot(agent, transform.position, threat);
    }
}
