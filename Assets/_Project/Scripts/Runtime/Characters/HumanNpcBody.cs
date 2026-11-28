using Alcax.Combat;
using Alcax.Dialogue;
using UnityEngine;
using UnityEngine.AI;

namespace Alcax.Characters
{
    /// <summary>
    /// The body for talkable civilians on a human model: reads what their logic asks of the Npc (AmbientNpc wanders,
    /// sits, chats; Civilian flees, puts its hands up, fights back) and drives the HumanMotor (follow drive: the
    /// gait matches the agent; sitting down and standing up; foot IK off while seated), the talking gestures while
    /// speaking, the gaze (a look target, the player in conversation, else the motor's own), hands up and fists
    /// (HumanWeaponRig), hit reactions with a push back, and death (the motor stops; the ragdoll is Civilian's).
    /// </summary>
    [RequireComponent(typeof(HumanMotor), typeof(HumanWeaponRig))]
    [DefaultExecutionOrder(5)]
    public class HumanNpcBody : MonoBehaviour
    {
        Npc npc;
        Alcax.Dialogue.Civilian civilian;
        Alcax.Dialogue.AmbientNpc ambient;
        float lastSit, sitStill;
        bool movingLatch;
        Vector3 flinch, flinchVel;
        bool wasSeated;
        Vector3? clearTo;
        Vector3 clearFrom;
        float clearT;
        HumanMotor motor;
        HumanWeaponRig rig;
        Animator anim;
        NavMeshAgent agent;
        Health health;
        Transform head;
        Vector3 lastPos, push;
        bool dead;
        static readonly int SittingId = Animator.StringToHash("Sitting"), PoseId = Animator.StringToHash("Pose");

        void Awake()
        {
            npc = GetComponent<Npc>();
            civilian = GetComponent<Alcax.Dialogue.Civilian>();
            ambient = GetComponent<Alcax.Dialogue.AmbientNpc>();
            motor = GetComponent<HumanMotor>();
            rig = GetComponent<HumanWeaponRig>();
            anim = GetComponent<Animator>();
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            motor.drive = MotorDrive.Follow;
            motor.StrafeHips = false;   // (people just face where they go: no sideways-running hips)
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            lastPos = transform.position;
            if (health != null) { health.Damaged += OnDamaged; health.Died += OnDied; }
        }

        void OnDestroy()
        {
            if (health != null) { health.Damaged -= OnDamaged; health.Died -= OnDied; }
        }

        void Update()
        {
            if (dead || npc == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            // The agent's velocity while it walks; otherwise how the logic moved us (sitting down slides us to the seat)
            var v = agent != null && agent.enabled && agent.isOnNavMesh ? agent.velocity : (transform.position - lastPos) / dt;
            lastPos = transform.position;
            v.y = 0f;
            // Panic says "duck" through the seat value (0.55-0.6; a real seat is 1): crouch and cover up, or hands up if
            // cornered. The crowd's panic lives in AmbientNpc, a guard-post civilian's in Civilian. Only ever while
            // standing still: someone moving is walking or running, whatever the value says (no squatting slide), and
            // a seat pose only on a real seat (never sitting on air).
            bool panicking = (civilian != null && civilian.Panicking) || (ambient != null && ambient.Panicking);
            // (with a margin: shuffling in place while huddled must not flick between crouched and walking)
            float speedNow = agent != null && agent.enabled ? v.magnitude : 0f;
            if (speedNow > 0.35f) movingLatch = true; else if (speedNow < 0.15f) movingLatch = false;   // (setting off ends a crouch at once)
            bool moving = movingLatch;
            float sit = npc.SitBlend, dSit = sit - lastSit; lastSit = sit;
            if (Mathf.Abs(dSit) > 1e-4f) sitStill = 0f; else sitStill += dt;
            if (sit > 0.97f) wasSeated = true;
            else if (sit < 0.05f && clearTo == null) wasSeated = false;
            bool ducking = panicking && !moving && (civilian == null || !civilian.Fighting) && sit > 0.05f && sit < 0.95f;
            // From a seat: falling toward 0 is standing up (the sit clip does it); stopping half way is ducking, so
            // stand up, step clear of the chair (crouching in it makes no sense), then crouch
            bool rising = wasSeated && clearTo == null && sitStill < 0.15f;
            if (ducking && wasSeated && !rising && clearTo == null) BeginClear();
            bool cower = ducking && !rising && clearTo == null;
            // On a seat only with the agent off (the logic parks it to sit; anyone it walks is on their feet) and never
            // in a fight
            bool agentOn = agent != null && agent.enabled;
            bool fightingNow = npc.PunchT >= 0f || (civilian != null && civilian.Fighting);
            bool onSeat = !agentOn && !fightingNow && (wasSeated || !panicking) && !moving && clearTo == null && !cower;
            bool seated = onSeat && sit > 0.5f;
            anim.SetBool(SittingId, seated);
            motor.Seated = onSeat && sit > 0.05f;
            motor.Crouch = cower ? Mathf.Clamp01(sit / 0.6f) * (npc.HandsUp > 0.5f ? 0.3f : 1f) : 0f;
            rig.CoverHead = cower && npc.HandsUp <= 0.5f;
            if (clearTo != null) ClearStep(dt, ref v);
            motor.Velocity = motor.Seated ? Vector3.zero : v;
            // Face exactly where we go (the agent turns slower than its path bends: legs and torso drift sideways)
            if (agent != null && agent.enabled && v.sqrMagnitude > 0.09f && !motor.Seated)
            {
                agent.updateRotation = false;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(v), Mathf.Lerp(240f, 720f, Mathf.InverseLerp(1f, 4f, v.magnitude)) * dt);
            }
            // Talking gestures while speaking (standing still or seated)
            anim.SetInteger(PoseId, npc.Speaking && (seated || v.sqrMagnitude < 0.04f) ? 1 : 0);

            // Hands: up when cornered, fists when fighting back
            bool fighting = npc.PunchT >= 0f || (civilian != null && civilian.Fighting);   // (fists up for the whole fight, not just each punch)
            rig.HandsUp = npc.HandsUp > 0.5f && !fighting;   // (a fighter never surrenders mid-swing)
            if (fighting) rig.CoverHead = false;
            rig.Fists = fighting;
            rig.Raised = fighting;
            rig.MeleeT = npc.PunchT;
            var aimDir = transform.forward;
            if (fighting && Camera.main != null)
            {
                // At the player's face (a little under the eyes), from shoulder height, when they're in front and close
                var to = Camera.main.transform.position - Vector3.up * 0.12f - (transform.position + Vector3.up * 1.4f);
                if (to.sqrMagnitude < 16f && Vector3.Dot(to.normalized, transform.forward) > 0.3f) aimDir = to.normalized;
            }
            rig.AimDirection = aimDir;

            // Gaze: what the logic points at, the player while talking to them, else the motor's own (watches the
            // player when near, glances)
            Vector3? look = null;
            if (npc.LookTarget != null) look = npc.LookTarget.position;
            else if (npc.InConversation && Camera.main != null) look = Camera.main.transform.position;
            motor.LookPoint = look;

            flinchVel += (-flinch * 220f - flinchVel * 16f) * dt;
            flinch += flinchVel * dt;
            rig.Flinch = flinch;
            // The push from a hit: a quick step back that dies out
            if (push.sqrMagnitude > 1e-4f)
            {
                if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(push * dt);
                push = Vector3.MoveTowards(push, Vector3.zero, dt * 7f);
            }
        }


        /// <summary>Pick a free spot beside or behind the seat (on the NavMesh, nothing in the way) to step out to.</summary>
        void BeginClear()
        {
            var p = transform.position; var f = transform.forward; var r = transform.right;
            clearFrom = p; clearT = 0f;
            foreach (var off in new[] { r * 0.75f, -r * 0.75f, -f * 0.85f, r * 0.6f - f * 0.6f, -r * 0.6f - f * 0.6f, r * 1.1f, -r * 1.1f })
            {
                var c = p + off;
                if (!NavMesh.SamplePosition(c, out var hit, 0.45f, NavMesh.AllAreas)) continue;
                var q = hit.position;
                if (Physics.CheckCapsule(q + Vector3.up * 0.35f, q + Vector3.up * 1.5f, 0.26f, ~((1 << 2) | (1 << 8)), QueryTriggerInteraction.Ignore)) continue;
                clearTo = q;
                return;
            }
            clearTo = p - f * 0.6f;   // (nothing clean: at least back off the seat)
        }

        void ClearStep(float dt, ref Vector3 v)
        {
            if (agent != null && agent.enabled) { clearTo = null; wasSeated = false; return; }   // (the logic moves us now)
            clearT = Mathf.Min(1f, clearT + dt / 0.7f);
            float k = clearT * clearT * (3f - 2f * clearT);
            var p = Vector3.Lerp(clearFrom, clearTo.Value, k);
            v = (p - transform.position) / dt; v.y = 0f;   // (the legs step it)
            transform.position = p;
            lastPos = p;
            if (clearT >= 1f) { clearTo = null; wasSeated = false; }
        }
        void OnDamaged(DamageInfo d)
        {
            if (dead || health == null || health.IsDead) return;
            bool atHead = d.Headshot || (head != null && d.Point.y > head.position.y - 0.06f);
            float s = Mathf.Clamp(d.Amount / 30f + d.Knockback * 0.05f, 0.3f, 1.5f);
            var local = transform.InverseTransformDirection(d.Direction);
            flinchVel += new Vector3(-local.z * 260f * (atHead ? 1.4f : 1f), local.x * 190f, -local.x * 150f) * s * 0.6f;   // (on top of the clip)
            // The library's hit reaction, added on the upper body (additive layer: no squat), and the hands come in
            motor.HitReaction(d.Knockback > 6f ? 2 : atHead ? 1 : 0, s);
            rig.Interrupt(0.45f + 0.2f * s);
            var dir = d.Direction; dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f) push += dir.normalized * (0.6f + Mathf.Min(d.Knockback, 8f) * 0.12f) * s;
        }

        void OnDied()
        {
            dead = true;
            motor.Kill();
            rig.enabled = false;
        }
    }
}
