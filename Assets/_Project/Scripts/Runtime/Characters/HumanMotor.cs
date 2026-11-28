using UnityEngine;
using UnityEngine.AI;

namespace Alcax.Characters
{
    public enum MotorDrive
    {
        /// <summary>The feet move the body: the in-place clips advance the root exactly as fast as the planted foot pushes back (ambient NPCs).</summary>
        Feet,
        /// <summary>Something else moves the root (an AI's NavMeshAgent): the gait, its tempo, and the hips follow the real velocity in any direction.</summary>
        Follow,
    }

    /// <summary>
    /// The body of any Humanoid character on AC_Human (HumanAnimationAssets): physical walking (planted feet pinned
    /// to the ground, soft touchdown and push-off, steps when turning on the spot, knees aimed forward, pelvis and
    /// legs fitted to the real ground), the library clips' stance reshaped (feet under the hips, straight standing
    /// legs, calm idle, relaxed hands, level collarbones, matched arms), lean with acceleration and into turns, and a
    /// gaze that turns smoothly (a look point, the player when near, or glances).
    /// Follow drive (AI): the clip blend and its tempo match the real speed, and for strafing and backpedaling the
    /// hips turn toward the move (reversed playback for the back half) while the chest stays on the facing.
    /// Brains (HumanNPC, HumanOperativeBody) set the inputs; a HumanWeaponRig (later in the frame) owns the arms
    /// when HandsFree is off.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [DefaultExecutionOrder(10)]
    public class HumanMotor : MonoBehaviour
    {
        public MotorDrive drive = MotorDrive.Feet;

        // ---------- Inputs ----------
        /// <summary>Feet drive: the blend-tree speed to build up to (0 idle, ~1.4 walk, 3.4 jog, 6.2 sprint).</summary>
        public float GaitTarget { get; set; }
        /// <summary>Feet drive: turn on the spot to this yaw (feet stay planted and step).</summary>
        public float? FaceYaw { get; set; }
        /// <summary>Follow drive: the root's real velocity.</summary>
        public Vector3 Velocity { get; set; }
        /// <summary>0 standing, 1 crouched.</summary>
        public float Crouch { get; set; }
        /// <summary>Look here (overrides watching the player and glances).</summary>
        public Vector3? LookPoint { get; set; }
        public bool WatchPlayer { get; set; } = true;
        /// <summary>Off while a weapon rig holds the arms: no idle arm rest, relaxed hands, or arm calming.</summary>
        public bool HandsFree { get; set; } = true;
        /// <summary>Where the chest turns (deg from the root forward, set by the weapon rig): the hips turn toward the move only within maxTwist of it.</summary>
        public float ChestYaw { get; set; }
        /// <summary>Sitting (or sitting down, standing up): the clip owns the legs; no foot planting or leg straightening.</summary>
        public bool Seated { get; set; }
        /// <summary>Follow drive: turn the hips toward a move that isn't the facing (strafing, backpedaling). Off for people who just face where they go.</summary>
        public bool StrafeHips { get; set; } = true;
        float strideWarp = 1f, personTempo = 1f, personLean, bodyHeight;
        // Running style, each their own: extra elbow bend, how big the arms swing (and a lopsided side), how wild
        // they get flat out, and how far they lean into it
        float personElbow, personSwing = 1f, personAsym, personFlail = 1f, personRunLean, personArmOut = 12f, personKick = 0.16f;
        [SerializeField] float runLean = 12f;   // forward lean running flat out (deg; each person +-3)
        [SerializeField] float toeOut = 4f;      // how far the toes turn out while moving (deg)
        [SerializeField, Range(0f, 1f)] float runTwistDamp = 0.9f;   // how much of the run clips' shoulder swing (twist and sideways rock) to take out

        // ---------- Outputs ----------
        public Animator Anim => anim;
        public float Gait => speedParam;
        public float StrideSpeed => strideSpeed;
        /// <summary>The lower body's turn toward the move (deg); the chest is turned back by the same amount.</summary>
        public float HipYaw => hipYaw;
        public bool Moving => speedParam > 0.15f;
        public float CrouchBlend => crouchBlend;
        public float MaxTwist => maxTwist;
        /// <summary>The Animator posed the skeleton this frame (it skips culled frames; procedural layers must too, or they stack up).</summary>
        public bool AnimatedThisFrame => lastIKFrame == Time.frameCount;
        public bool Dead => dead;

        [Header("Gait")]
        [SerializeField] float walkParam = 1.4f;            // blend-tree speed for a plain walk
        [SerializeField] float accel = 1.5f;                // feet drive: how fast the gait builds up and winds down
        [SerializeField] float turnInPlace = 110f;          // deg/s when turning on the spot
        [SerializeField] float lookRange = 5f;
        [SerializeField] float footOffset = 0.09f;          // ankle height above the sole
        [SerializeField] LayerMask groundMask = ~((1 << 2) | (1 << 8));
        [SerializeField, Range(0.95f, 1f)] float straightLegs = 0.996f;   // standing hip-to-ankle as a share of the leg
        [SerializeField, Range(0f, 1f)] float plantFlatten = 0.85f;      // walking: planted foot eased this far toward flat (the clip walks on its toes)
        [SerializeField, Range(0f, 1f)] float footFlattenSwing = 0.35f; // and a swinging one
        [SerializeField] float footMaxTilt = 32f;                       // walking: the most a foot tips from flat (deg)
        [SerializeField] float minFootGap = 0.21f;           // walking: ankle centres at least this far apart (shoes clear each other)
        [SerializeField] float maxTwist = 45f;              // most the chest may turn from the hips (deg): beyond it a spine looks wrung

        // Ground speeds (m/s) of the clips at the blend-tree speeds (measured from the clips): 0, walk, jog, sprint
        static readonly float[] GaitParams = { 0f, 1.4f, 3.4f, 6.2f };
        static readonly float[] GaitSpeeds = { 0f, 0.87f, 4.68f, 5.2f };   // (measured: the UAL jog is a fast run)
        static readonly float[] ClipCadence = { 90f, 90f, 129f, 179f };   // steps a minute of each clip at normal speed
        /// <summary>Steps a minute people take at a speed: ~110 walking at 1.4 m/s, ~170 running at 4 m/s.</summary>
        static float Cadence(float v) => Mathf.Lerp(60f + 28f * v, 140f + 8f * v, Mathf.InverseLerp(1.8f, 2.6f, v));   // (walking ~93 steps a minute at 1.2 m/s, a long easy stride; running: quick, short steps: longer ones float, a jump each step)
        const float CrouchWalkSpeed = 0.92f;   // Crouch_Fwd_Loop at blend speed 1.4
        const float WalkPace = 0.95f;          // the walk's ground speed, used when off-screen (not animated)

        class Foot
        {
            public AvatarIKGoal goal;
            public bool locked, hasPrev;
            public Vector3 lockPos, prevLocal, stepFrom, lastOut;
            public Quaternion lockRot, stepFromRot;
            public float step = -1f, stepTime, lastW, ws, legLength;
            public Quaternion restRel; public bool hasRest;   // the foot lying flat at rest, relative to the root
            public Vector3 toeLocal, heelLocal; public bool hasSole;   // the sole's toe and heel, in the IK goal's frame
        }

        Animator anim;
        NavMeshAgent agent;
        readonly Foot[] feet = { new Foot { goal = AvatarIKGoal.LeftFoot }, new Foot { goal = AvatarIKGoal.RightFoot } };
        float phase, lookWeight, pelvisOffset, standRaise;
        float speedParam, strideSpeed, lean, roll, yawRate, lastYaw, lastSpeed;
        float hipYaw, dirSign = 1f, crouchBlend;
        bool backward;
        Vector3 lookDir, glanceLocal;
        float glanceUntil, nextGlance;
        bool watching, dead;
        int lastIKFrame = -10;
        Transform player;
        static readonly int SpeedId = Animator.StringToHash("Speed"), DirId = Animator.StringToHash("Dir"), CrouchId = Animator.StringToHash("Crouch");

        /// <summary>Per-frame motion record for tuning (set to a queue to start recording; null = off).</summary>
        [System.NonSerialized] public System.Collections.Generic.Queue<string> Trace;

        void Awake()
        {
            anim = GetComponent<Animator>();
            agent = GetComponent<NavMeshAgent>();
            anim.applyRootMotion = false;
        }

        void Start()
        {
            var p = FindAnyObjectByType<Alcax.Player.PlayerController>();
            if (p != null) player = p.transform;
            Alcax.FX.WindManager.AddBody(transform);
            phase = Random.value * 6.28f;
            personTempo = Random.Range(0.93f, 1.07f);
            personLean = Random.Range(-1.5f, 2.5f);
            personElbow = Random.Range(14f, 34f);
            personSwing = Random.Range(4.4f, 5.3f);
            personArmOut = Random.Range(22f, 32f);   // (elbows out from the shoulder: arms off the body)
            personKick = Random.Range(0.06f, 0.14f);   // (how high the heel kicks up behind, m)
            personAsym = Random.Range(-0.12f, 0.12f);
            personFlail = Random.Range(0.6f, 1.4f);
            personRunLean = Random.Range(-2.5f, 3.5f);
            anim.Play(0, 0, Random.value);   // (start somewhere in the cycle)
            lastYaw = transform.eulerAngles.y;
        }

        void OnDestroy() { Alcax.FX.WindManager.RemoveBody(transform); poseHandler?.Dispose(); }

        /// <summary>Stop all procedural work (death, ragdoll).</summary>
        public void Kill()
        {
            dead = true;
            // (the look-at weight drops in OnAnimatorIK: setting it here is outside the IK pass)
        }


        // ---------- Hit reactions (upper-body layer) ----------

        static readonly int HitReactId = Animator.StringToHash("HitReact"), HitKindId = Animator.StringToHash("HitKind");
        int upperLayer = -2;
        float hitWeight, hitPeak, hitHold;

        /// <summary>Play a hit reaction on the upper body (0 chest, 1 head, 2 knocked back); strength 0..1.5 sets how much of it shows.</summary>
        public void HitReaction(int kind, float strength)
        {
            if (dead) return;
            if (upperLayer == -2) upperLayer = anim.GetLayerIndex("Upper");
            if (upperLayer < 0) return;
            anim.SetInteger(HitKindId, kind);
            anim.SetTrigger(HitReactId);
            hitPeak = Mathf.Clamp(0.7f + strength * 0.3f, 0.75f, 1f);
            hitHold = kind == 2 ? 0.8f : kind == 1 ? 0.5f : 0.45f;
        }

        /// <summary>Throw a punch (0 jab, 1 cross, 2 hook): the library's arms, plus its body motion added on the torso.</summary>
        public void Punch(int kind)
        {
            if (dead) return;
            anim.SetInteger(PunchKindId, kind);
            anim.SetTrigger(PunchId);
            anim.SetTrigger(PunchBodyId);
        }

        /// <summary>Fists up: the arms layer holds the boxing guard (and throws the punches).</summary>
        public bool Fists { get; set; }

        static readonly int PunchId = Animator.StringToHash("Punch"), PunchKindId = Animator.StringToHash("PunchKind"),
            PunchBodyId = Animator.StringToHash("PunchBody"), FistsId = Animator.StringToHash("Fists");
        int armsLayer = -2;
        float fistWeight;

        /// <summary>How much the boxing clips own the arms right now (0..1).</summary>
        public float UpperWeight => fistWeight;

        void UpdateHitLayer(float dt)
        {
            if (upperLayer == -2) upperLayer = anim.GetLayerIndex("Upper");
            if (armsLayer == -2) armsLayer = anim.GetLayerIndex("Arms");
            if (upperLayer >= 0)
            {
                hitHold -= dt;
                float target = hitHold > 0f ? hitPeak : 0f;
                hitWeight = Mathf.MoveTowards(hitWeight, target, dt / (target > hitWeight ? 0.04f : 0.3f));
                anim.SetLayerWeight(upperLayer, hitWeight);
            }
            if (armsLayer >= 0)
            {
                anim.SetBool(FistsId, Fists);
                fistWeight = Mathf.MoveTowards(fistWeight, Fists ? 1f : 0f, dt / 0.25f);
                anim.SetLayerWeight(armsLayer, fistWeight);
            }
        }
        // ---------- Gait ----------

        static float Piecewise(float x, float[] xs, float[] ys)
        {
            if (x <= xs[0]) return ys[0];
            for (int i = 1; i < xs.Length; i++)
                if (x <= xs[i]) return Mathf.Lerp(ys[i - 1], ys[i], Mathf.InverseLerp(xs[i - 1], xs[i], x));
            return ys[ys.Length - 1] * x / xs[xs.Length - 1];
        }

        static float StandParam(float v) => v < 0.87f ? 1.4f * v / 0.87f : v < 1.8f ? 1.4f : v < 2.6f ? Mathf.Lerp(1.4f, 3.4f, (v - 1.8f) / 0.8f) : v < 5f ? 3.4f : Mathf.Lerp(3.4f, 6.2f, (v - 5f) / 1.5f);
        float SpeedToParam(float speed) => Mathf.Lerp(StandParam(speed), speed / CrouchWalkSpeed * walkParam, crouchBlend);
        float ParamToSpeed(float param) => Mathf.Lerp(Piecewise(param, GaitParams, GaitSpeeds), param / walkParam * CrouchWalkSpeed, crouchBlend);

        void Update()
        {
            if (dead) return;
            float dt = Time.deltaTime;
            crouchBlend = Mathf.MoveTowards(crouchBlend, Mathf.Clamp01(Crouch), dt / 0.25f);
            anim.SetFloat(CrouchId, Mathf.SmoothStep(0f, 1f, crouchBlend));
            UpdateHitLayer(dt);
            float prevParam = speedParam;
            float v;

            if (drive == MotorDrive.Feet)
            {
                // Turning on the spot (the feet stay planted and step: OnAnimatorIK)
                if (FaceYaw != null && speedParam < 0.2f)
                {
                    float d = Mathf.DeltaAngle(transform.eulerAngles.y, FaceYaw.Value);
                    float rate = Mathf.Min(turnInPlace, Mathf.Abs(d) * 3f + 15f);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, FaceYaw.Value, 0f), rate * dt);
                    if (Mathf.Abs(d) < 1f) FaceYaw = null;
                }
                speedParam = Mathf.MoveTowards(speedParam, GaitTarget, accel * dt);
                anim.SetFloat(SpeedId, speedParam);
                anim.SetFloat(DirId, 1f);
                // No two cycles alike: the gait's tempo wanders a few percent
                anim.speed = 1f + (Mathf.PerlinNoise(Time.time * 0.23f, phase) - 0.5f) * 0.08f;
                // Advance by the stride the feet measured (off-screen the Animator is culled: use the walk's pace)
                if (Time.frameCount - lastIKFrame > 2) strideSpeed = speedParam / walkParam * WalkPace;
                v = speedParam > 0.03f ? strideSpeed : 0f;
                var pos = transform.position + transform.forward * (v * dt);
                if (agent != null && agent.isOnNavMesh && !agent.updatePosition) { agent.nextPosition = pos; pos = agent.nextPosition; }
                transform.position = pos;
                hipYaw = 0f; dirSign = 1f;
            }
            else
            {
                // Follow: the root is moved for us; match the gait to the real velocity in any direction
                var vel = Velocity; vel.y = 0f;
                v = vel.magnitude;
                var local = transform.InverseTransformDirection(vel);
                float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                if (v > 0.2f)
                {
                    if (!backward && Mathf.Abs(angle) > 112f) backward = true;
                    else if (backward && Mathf.Abs(angle) < 78f) backward = false;
                }
                // The hips turn toward the move (up to 70 deg; the chest is turned back in LateUpdate); the back half
                // plays the gait in reverse
                float warp = v > 0.2f && StrafeHips ? Mathf.Clamp(backward ? Mathf.DeltaAngle(180f, angle) : angle, -60f, 60f) : 0f;
                warp = Mathf.Clamp(warp, ChestYaw - maxTwist, ChestYaw + maxTwist);   // the chest stays within reach of the hips
                hipYaw = Mathf.MoveTowardsAngle(hipYaw, warp, 300f * dt);
                dirSign = backward && v > 0.2f ? -1f : 1f;
                speedParam = Mathf.MoveTowards(speedParam, SpeedToParam(v), 6f * dt);
                anim.SetFloat(SpeedId, speedParam);
                anim.SetFloat(DirId, dirSign);
                // Tempo: the blend's own ground speed is what we asked for; small leftovers (blend error, clamps) go to the tempo
                float natural = ParamToSpeed(speedParam);
                // People run slower mostly by shortening the stride, a little by slowing the cadence: the tempo takes
                // part of the difference (each person keeps their own), the feet (stride warp, OnAnimatorIK) the rest
                // (the cadence a person really uses at this speed, over the clip blend's own: runners take quick,
                // short steps; the library jog bounds along at ~129 steps a minute)
                float tempo = v > 0.25f && natural > 0.2f ? Mathf.Clamp(Cadence(v) / Piecewise(speedParam, GaitParams, ClipCadence), 0.8f, 1.5f) : 1f;
                anim.speed = tempo * personTempo;
                strideWarp = Mathf.Lerp(strideWarp, v > 0.25f && natural > 0.2f ? Mathf.Clamp(v / (natural * anim.speed), 0.5f, speedParam < 2f ? 1.55f : 1.35f) : 1f, dt * 6f);
                if (Time.frameCount - lastIKFrame > 2) strideSpeed = v;
            }

            // Body sway: lean into acceleration and into turns (from the gait building up, not from each footfall)
            float a = dt > 0f ? (drive == MotorDrive.Feet ? (speedParam - prevParam) * 1f : (v - lastSpeed) * 0.6f) / dt : 0f;
            lastSpeed = v;
            lean = Mathf.Lerp(lean, Mathf.Clamp(a * 2.5f, -4f, 4f) + personLean * Mathf.Clamp01(v / 2f) + (runLean + personRunLean) * Mathf.Clamp01((speedParam - 1.6f) / 2.4f), dt * 3f);   // (runners lean into it)
            float yaw = transform.eulerAngles.y;
            yawRate = Mathf.Lerp(yawRate, dt > 0f ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f, dt * 5f); lastYaw = yaw;
            roll = Mathf.Lerp(roll, Mathf.Clamp(-yawRate * v * 0.05f, -6f, 6f), dt * 4f);
        }

        // ---------- IK: gaze, plant and step the feet ----------

        void OnAnimatorIK(int layer)
        {
            if (dead) { anim.SetLookAtWeight(0f); return; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Look();

            lastIKFrame = Time.frameCount;
            if (Seated)
            {
                // The sitting clips own the legs: let go of the ground (the feet re-plant on standing)
                foreach (var sf in feet) { anim.SetIKPositionWeight(sf.goal, 0f); anim.SetIKRotationWeight(sf.goal, 0f); sf.locked = false; sf.hasPrev = false; }
                anim.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, 0f); anim.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 0f);
                strideSpeed = 0f; pelvisOffset = 0f; standRaise = 0f;
                return;
            }
            float rootY = transform.position.y;
            bool walking = speedParam > 0.15f;
            float back = 0f;
            bool anyGrounded = false;
            float lowest = 0f;
            var warpRot = Quaternion.AngleAxis(hipYaw, Vector3.up);
            if (Mathf.Abs(hipYaw) > 0.01f) anim.bodyRotation = warpRot * anim.bodyRotation;
            var warpFwd = warpRot * transform.forward;
            float stand = 1f - crouchBlend;
            for (int i = 0; i < 2; i++)
            {
                var f = feet[i];
                var other = feet[1 - i];
                var A = anim.GetIKPosition(f.goal);
                var AR = anim.GetIKRotation(f.goal);
                var local = transform.InverseTransformPoint(A);
                float h = local.y - footOffset;                       // the clip's lift of this foot

                // Stride: a grounded foot sliding back in the clip (forward, when playing in reverse) pushes the body
                float vz = f.hasPrev ? (local.z - f.prevLocal.z) / dt : 0f;
                if (f.hasPrev && h < 0.05f) { back = Mathf.Max(back, -vz * dirSign); anyGrounded = true; }
                f.prevLocal = local; f.hasPrev = true;

                // Stance shaping: the library stands wide-legged and staggered (ankles ~48 cm apart) and walks wide
                // with a high swing (ankle up to 33 cm); bring the feet in under the hips and lower the swing
                float standW = (1f - Mathf.Clamp01(speedParam / 0.6f)) * stand;
                float sideSign = f.goal == AvatarIKGoal.LeftFoot ? -1f : 1f;
                float side = Mathf.Max(0f, local.x * sideSign);
                var shaped = local;
                // (walking: narrowed, but never so close that the swinging foot clips the planted one)
                float walkSide = Mathf.Max(side * 0.8f, minFootGap * 0.5f);
                shaped.x = sideSign * Mathf.Lerp(Mathf.Lerp(side, walkSide, stand), Mathf.Min(side, 0.125f), standW);
                shaped.z = Mathf.Lerp(local.z * strideWarp, local.z * 0.2f, standW);   // (stride warp: the step length that matches the speed)
                // (running: the full swing, and the heel kicks up behind, the knee folding: straight legs scissoring
                // under the body looked like two fingers running on a table)
                float runK = Mathf.InverseLerp(2.2f, 4.5f, speedParam);
                float runLift = Mathf.Lerp(0.62f, 1f, runK);
                if (h > 0f) shaped.y = footOffset + h * Mathf.Lerp(Mathf.Lerp(1f, runLift, stand), 1f, standW);
                if (h > 0.02f && runK > 0f)
                {
                    float behind = Mathf.InverseLerp(0.05f, -0.35f, local.z);   // (the trailing foot, behind the hips)
                    shaped.y += personKick * behind * Mathf.Clamp01(h / 0.12f) * runK * stand * (1f - standW);
                }
                // The hips' turn toward the move carries the feet round with them
                var hy = shaped.y; shaped.y = 0f; shaped = warpRot * shaped; shaped.y = hy;
                var S = transform.TransformPoint(shaped);

                // Where the foot goes, moved onto the real ground and tilted to it
                var target = S; var targetRot = warpRot * AR;
                if (Physics.Raycast(new Vector3(S.x, rootY + 0.5f, S.z), Vector3.down, out var hit, 1.0f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    target = S + Vector3.up * Mathf.Clamp(hit.point.y - rootY, -0.4f, 0.4f);
                    targetRot = Quaternion.FromToRotation(Vector3.up, hit.normal) * targetRot;
                }

                // How planted this foot is (1 on the ground, 0 swinging): the phase from which way the clip moves
                // the foot (forward relative to the body = swinging), the let-go and touchdown blended over time
                bool swinging = walking && vz * dirSign > 0.35f;
                if (speedParam < 0.03f && h < 0.02f && crouchBlend < 0.1f)
                {
                    var rel = Quaternion.Inverse(transform.rotation) * AR;
                    f.restRel = f.hasRest ? Quaternion.Slerp(f.restRel, rel, dt * 2f) : rel; f.hasRest = true;
                    if (!f.hasSole)
                    {
                        var toes = anim.GetBoneTransform(f.goal == AvatarIKGoal.LeftFoot ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                        var ankle = anim.GetBoneTransform(f.goal == AvatarIKGoal.LeftFoot ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                        if (toes != null && ankle != null)
                        {
                            var toe = toes.position - ankle.position; var flatToe = toe; flatToe.y = 0f;
                            f.toeLocal = Quaternion.Inverse(AR) * (toe + Vector3.down * Mathf.Max(0f, footOffset + toe.y) + flatToe.normalized * 0.05f);   // (the sole under the toe tip)
                            f.heelLocal = Quaternion.Inverse(AR) * (Vector3.down * footOffset - flatToe.normalized * 0.05f);
                            f.hasSole = true;
                        }
                    }
                }
                float wRaw = swinging ? 0f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.01f, 0.11f, h));
                if (!f.locked) { f.locked = true; f.lockPos = target; f.lockRot = targetRot; f.ws = wRaw; }
                f.ws = Mathf.MoveTowards(f.ws, wRaw, dt / (wRaw > f.ws ? 0.12f : 0.1f));
                float w = f.ws;

                Vector3 outPos; Quaternion outRot;
                if (f.step >= 0f && !walking)
                {
                    // A step while standing (turning on the spot): lift, swing, set down
                    f.step = Mathf.Min(1f, f.step + dt / f.stepTime);
                    float s = Mathf.SmoothStep(0f, 1f, f.step);
                    outPos = Vector3.Lerp(f.stepFrom, target, s) + Vector3.up * (Mathf.Sin(s * Mathf.PI) * 0.06f);
                    outRot = Quaternion.Slerp(f.stepFromRot, targetRot, s);
                    if (f.step >= 1f) { f.step = -1f; f.lockPos = target; f.lockRot = targetRot; }
                }
                else
                {
                    f.step = -1f;
                    // The pin follows the clip's foot while it's settling and holds once it's down (plus a slow creep
                    // while walking: soaks up the drift between the even body speed and the clip's uneven stance)
                    float follow = 1f - w * w;
                    f.lockPos = Vector3.Lerp(f.lockPos, target, Mathf.Max(follow, walking ? dt * 0.45f : 0f));
                    f.lockRot = Quaternion.Slerp(f.lockRot, targetRot, walking ? 1f : follow);
                    // Pin only across the ground: the height (heel-to-toe roll) and, while walking, the angle stay the clip's
                    var pinned = new Vector3(f.lockPos.x, target.y, f.lockPos.z);
                    outPos = Vector3.Lerp(target, pinned, w);
                    outRot = walking ? targetRot : Quaternion.Slerp(targetRot, f.lockRot, w);
                    // Walking: the clip lifts the heel early and holds it (on the toes): ease the foot toward lying
                    // flat on the ground, most while planted
                    if (walking && f.hasRest)
                    {
                        var flat = (hit.collider != null ? Quaternion.FromToRotation(Vector3.up, hit.normal) : Quaternion.identity) * warpRot * transform.rotation * f.restRel;
                        outRot = Quaternion.Slerp(outRot, flat, Mathf.Lerp(footFlattenSwing, plantFlatten, w));
                        if (Quaternion.Angle(flat, outRot) > footMaxTilt) outRot = Quaternion.RotateTowards(flat, outRot, footMaxTilt);   // push off, but never onto the toes
                    }
                    // A planted foot rests on its sole: lower (or raise) the ankle so the lowest of heel and toe touches
                    // the ground (turning the foot about the ankle lifted the toes off it: floating feet)
                    if (f.hasSole && hit.collider != null && w > 0.01f)
                    {
                        float lowestSole = Mathf.Min((outRot * f.toeLocal).y, (outRot * f.heelLocal).y);
                        float onGround = hit.point.y - lowestSole - 0.012f;   // (a hair into the floor: the sole sits on it, weighted, not hovering at its lowest point)
                        outPos.y = Mathf.Lerp(outPos.y, onGround, w);
                    }

                    // Standing: once a leg is twisted or stretched too far, step (one foot at a time)
                    if (!walking && w > 0.95f && other.step < 0f)
                    {
                        var e = target - f.lockPos; e.y = 0f;
                        if (e.magnitude > 0.09f || Quaternion.Angle(f.lockRot, targetRot) > 22f)
                        {
                            f.step = 0f; f.stepTime = 0.32f;
                            f.stepFrom = outPos; f.stepFromRot = outRot;
                        }
                    }
                    // Walking: the stride matches the ground, so a big drift only means something pushed him: let go
                    if (walking) { var e = target - f.lockPos; e.y = 0f; if (e.magnitude > 0.25f) f.lockPos = target; }
                }

                // Moving: the toes point where they're going (the library splays them ~10-13 deg; keep a natural ~4)
                if (walking && f.hasSole)
                {
                    var fd = outRot * (f.toeLocal - f.heelLocal); fd.y = 0f;
                    if (fd.sqrMagnitude > 1e-4f)
                    {
                        float yaw = Vector3.SignedAngle(warpFwd, fd, Vector3.up);
                        float want = sideSign * toeOut;
                        outRot = Quaternion.AngleAxis((want - yaw) * 0.8f, Vector3.up) * outRot;
                    }
                }
                anim.SetIKPositionWeight(f.goal, 1f);
                anim.SetIKRotationWeight(f.goal, 1f);
                anim.SetIKPosition(f.goal, outPos);
                anim.SetIKRotation(f.goal, outRot);
                f.lastOut = outPos; f.lastW = w;
                lowest = Mathf.Min(lowest, target.y - S.y);     // the ground under the foot, not the swing

                // Knees aim forward (the way the hips face): with the feet brought in under the hips the clip's
                // knees would fold out sideways
                bool left = f.goal == AvatarIKGoal.LeftFoot;
                var hipBone = anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var kneeBone = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                var footBone = anim.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (f.legLength <= 0f) f.legLength = (kneeBone.position - hipBone.position).magnitude + (footBone.position - kneeBone.position).magnitude;
                var hint = left ? AvatarIKHint.LeftKnee : AvatarIKHint.RightKnee;
                anim.SetIKHintPositionWeight(hint, 0.85f);
                anim.SetIKHintPosition(hint, (hipBone.position + outPos) * 0.5f + warpFwd * 0.45f + (warpRot * transform.right) * (sideSign * 0.02f));
            }
            anim.bodyPosition += Vector3.up * standRaise;
            // A shorter stride carries the hips higher (the clip keeps them low for its long bounds: folded knees)
            if (strideWarp < 1f) anim.bodyPosition += Vector3.up * ((1f - strideWarp) * 0.08f * Mathf.Clamp01(speedParam / 3.4f));
            // Running: take most of the clip's bounce out of the hips (it hopped with each stride); the legs absorb it
            {
                float runW = Mathf.Clamp01((speedParam - 1.6f) / 1.8f);
                var bp = anim.bodyPosition;
                float rel = bp.y - rootY;
                bodyHeight = runW > 0.01f ? Mathf.Lerp(bodyHeight, rel, dt * 3f) : rel;
                if (runW > 0.01f) anim.bodyPosition = new Vector3(bp.x, rootY + Mathf.Lerp(rel, bodyHeight, 0.7f * runW), bp.z);
            }
            // The body advances at the grounded foot's speed (feet drive); in a flight phase (sprint) it carries on
            if (drive == MotorDrive.Feet)
                strideSpeed = anyGrounded ? Mathf.Lerp(strideSpeed, Mathf.Clamp(back, 0f, 2.5f * WalkPace + speedParam), dt * 5f) : strideSpeed;
            else strideSpeed = Velocity.magnitude;

            // Drop the pelvis so the lower foot can reach
            pelvisOffset = Mathf.Lerp(pelvisOffset, Mathf.Max(lowest, -0.4f), dt * 8f);
            anim.bodyPosition += Vector3.up * pelvisOffset;
            if (Trace != null)
            {
                var st = anim.GetCurrentAnimatorStateInfo(0);
                var b = anim.bodyPosition;
                Trace.Enqueue($"{Time.time:F3} n={st.normalizedTime:F3} v={strideSpeed:F3} back={back:F3} hip={hipYaw:F0} dir={dirSign} pos={transform.position.x:F3},{transform.position.z:F3} body={b.x:F3},{b.y:F3},{b.z:F3} L={feet[0].lastOut.x:F3},{feet[0].lastOut.y:F3},{feet[0].lastOut.z:F3} w{feet[0].lastW:F2} R={feet[1].lastOut.x:F3},{feet[1].lastOut.y:F3},{feet[1].lastOut.z:F3} w{feet[1].lastW:F2}");
                while (Trace.Count > 600) Trace.Dequeue();
            }
        }

        /// <summary>
        /// The gaze is a direction that turns at a limited, eased rate, so the head never jumps: a look point when
        /// given, else the player while near and in front (with slack before it lets go), else glances, else forward;
        /// the look-at weight only fades once the gaze is back near forward.
        /// </summary>
        void Look()
        {
            float dt = Time.deltaTime;
            var head = transform.position + Vector3.up * Mathf.Lerp(1.6f, 1.05f, crouchBlend);
            if (lookDir == Vector3.zero) lookDir = transform.forward;
            Vector3 want = transform.forward;
            float wantW = 0f;
            if (LookPoint.HasValue)
            {
                var to = LookPoint.Value - head;
                if (to.sqrMagnitude > 0.01f) { want = to.normalized; wantW = 1f; }
                watching = false;
            }
            else if (WatchPlayer && player != null)
            {
                var to = player.position + Vector3.up * 1.65f - head;
                var flat = to; flat.y = 0f;
                float ang = Vector3.Angle(transform.forward, flat), d = to.magnitude;
                watching = watching ? d < lookRange + 1f && ang < 110f : d < lookRange && ang < 85f;
                if (watching) { want = to.normalized; wantW = 1f; }
            }
            if (!watching && !LookPoint.HasValue)
            {
                if (Time.time > nextGlance)
                {
                    glanceUntil = Time.time + Random.Range(1f, 2.5f);
                    nextGlance = glanceUntil + Random.Range(4f, 10f);
                    glanceLocal = Quaternion.Euler(Random.Range(-8f, 12f), Random.Range(-65f, 65f), 0f) * Vector3.forward;
                }
                if (Time.time < glanceUntil) { want = transform.TransformDirection(glanceLocal); wantW = 0.8f; }
            }
            float off = Vector3.Angle(lookDir, want);
            if (!LookPoint.HasValue && speedParam > 2.2f) { want = transform.forward; wantW = 0f; watching = false; glanceUntil = 0f; }   // running: eyes on the way ahead
            float maxRate = LookPoint.HasValue ? 320f : 160f;    // a target (combat) is tracked faster than a glance
            lookDir = Vector3.RotateTowards(lookDir, want, Mathf.Clamp(off * (LookPoint.HasValue ? 6f : 2.5f), 12f, maxRate) * Mathf.Deg2Rad * dt, 0f);
            float backToFront = Mathf.InverseLerp(4f, 20f, Vector3.Angle(lookDir, transform.forward));
            lookWeight = Mathf.MoveTowards(lookWeight, Mathf.Max(wantW, backToFront), dt * (LookPoint.HasValue ? 3f : 1.2f));
            anim.SetLookAtWeight(lookWeight, 0.12f, 0.65f, 1f, 0.6f);
            anim.SetLookAtPosition(head + lookDir * 3f);
        }

        // ---------- Posture, lean, and breathing on the spine ----------

        [Header("Posture (over every clip)")]
        [SerializeField] float chestRound = 1.5f;       // the library holds the chest lifted: round it forward (deg)
        [SerializeField] float clavicleYaw = -12f;      // collarbone angle (deg, + forward of straight out; ~-12 natural)
        [SerializeField] float clavicleLift = -8f;      // and its droop
        [SerializeField] float armPitch = 3f;           // idle: upper arms hang this far forward of vertical (deg)
        [SerializeField] float armSpread = 8f;          // idle: and this far out from the side (a natural hang)
        [SerializeField] float walkArmSpread = 9f;      // walking: the most an arm swings out to the side (the swing itself stays the clip's)
        [SerializeField] float minArmSpread = 5f;       // walking: and the least (closer, and the arm cuts into the body)
        [SerializeField] float crouchLean = 25f;        // the torso's forward lean crouched (deg)
        [SerializeField] float standUpright = 4f;       // the torso's forward lean standing (deg); the clips stoop more
        [SerializeField] float walkUpright = 8f;        // and walking: a little into the step
        [SerializeField, Range(-1f, 1f)] float idleForearm = 0.9f;   // idle elbow (muscle: 1 straight): a soft bend

        void Posture()
        {
            var up = transform.up; var right = transform.right; var fwd = transform.forward;
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            var upper = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            Quaternion headWas = head != null ? head.rotation : Quaternion.identity;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(chestRound * 0.6f, right) * chest.rotation;
            if (upper != null) upper.rotation = Quaternion.AngleAxis(chestRound * 0.4f, right) * upper.rotation;
            else if (chest != null) chest.rotation = Quaternion.AngleAxis(chestRound * 0.4f, right) * chest.rotation;
            // Collarbones: the library pulls them back unevenly (-48 vs -8 deg): idle and walking, each is measured and
            RestClavicle(HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, 1f, postureW, up, fwd);
            Upright(postureW, right);
            float walkW = locoW * Mathf.Clamp01(speedParam / 0.6f) * (1f - crouchBlend);
            LimitArmSpread(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, -1f, walkW, fwd);
            LimitArmSpread(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 1f, walkW, fwd);
            RestClavicle(HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, -1f, postureW, up, fwd);
            RestClavicle(HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, 1f, postureW, up, fwd);
            // Plain idle: both arms come to the same resting angle beside the torso (eased in over ~1.2 s once stopped)
            float idleW = Mathf.SmoothStep(0f, 1f, armRestW);
            RestArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, -1f, idleW, right, fwd);
            RestArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 1f, idleW, right, fwd);
            if (head != null) head.rotation = headWas;      // the gaze (look-at) stays where it was
        }

        HumanPoseHandler poseHandler;
        HumanPose humanPose;
        int[] relaxIdx;
        float[] relaxTarget;
        bool[] relaxIsElbow;
        float[] openTarget;

        /// <summary>Open, flat hands (0..1; hands up): set by HumanWeaponRig.</summary>
        public float OpenHands { get; set; }

        /// <summary>Plain idle: open the hands into a loose curl and let the elbows nearly straighten (Humanoid muscles).</summary>
        void RelaxHands(float elbowW, float fingerW, float openW = 0f)
        {
            if ((elbowW <= 0.001f && fingerW <= 0.001f) || anim.avatar == null || !anim.avatar.isHuman) return;
            openW = Mathf.Clamp01(openW);
            if (poseHandler == null)
            {
                poseHandler = new HumanPoseHandler(anim.avatar, transform);
                var idx = new System.Collections.Generic.List<int>(); var tgt = new System.Collections.Generic.List<float>(); var elb = new System.Collections.Generic.List<bool>();
                var open = new System.Collections.Generic.List<float>();
                for (int i = 0; i < HumanTrait.MuscleCount; i++)
                {
                    string n = HumanTrait.MuscleName[i];
                    float t = float.NaN;
                    if (n.EndsWith("Forearm Stretch")) t = idleForearm;
                    // thumb: let it hang in along the index finger (gravity), joints soft
                    else if (n.Contains("Thumb Spread")) t = -0.6f;
                    else if (n.Contains("Thumb 1 Stretched")) t = -0.8f;   // (the base joint is what brings it down and in)
                    else if (n.Contains("Thumb 2 Stretched")) t = -0.3f;
                    else if (n.Contains("Thumb 3 Stretched")) t = 0f;
                    else if (n.Contains(" 1 Stretched")) t = -0.35f;   // (knuckles: the main bend of a relaxed hand)
                    else if (n.Contains(" 2 Stretched")) t = -0.1f;
                    else if (n.Contains(" 3 Stretched")) t = 0.35f;    // (tips nearly straight: curled tips over straight knuckles is a claw)
                    else if (n.EndsWith("Spread") && (n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little"))) t = -0.2f;
                    // Open (hands up): fingers straight and together, the thumb along the index (no claw, no fist)
                    float o = t;
                    if (n.Contains("Thumb Spread")) o = -0.3f;
                    else if (n.Contains("Thumb 1 Stretched")) o = 0.1f;
                    else if (n.Contains("Thumb")) o = 0.5f;
                    else if (n.Contains(" Stretched")) o = 0.75f;
                    else if (n.EndsWith("Spread")) o = -0.7f;
                    if (!float.IsNaN(t)) { idx.Add(i); tgt.Add(t); elb.Add(n.EndsWith("Forearm Stretch")); open.Add(o); }
                }
                relaxIdx = idx.ToArray(); relaxTarget = tgt.ToArray(); relaxIsElbow = elb.ToArray(); openTarget = open.ToArray();
            }
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 hipsPos = hips.position; Quaternion hipsRot = hips.rotation;
            poseHandler.GetHumanPose(ref humanPose);
            for (int k = 0; k < relaxIdx.Length; k++)
                humanPose.muscles[relaxIdx[k]] = Mathf.Lerp(humanPose.muscles[relaxIdx[k]], relaxIsElbow[k] ? relaxTarget[k] : Mathf.Lerp(relaxTarget[k], openTarget[k], openW), relaxIsElbow[k] ? elbowW : fingerW);
            poseHandler.SetHumanPose(ref humanPose);
            hips.SetPositionAndRotation(hipsPos, hipsRot);   // the handler re-places the body in its own space: keep ours
        }

        void RestClavicle(HumanBodyBones clav, HumanBodyBones upperArm, float side, float w, Vector3 up, Vector3 fwd)
        {
            if (w <= 0.001f) return;
            var c = anim.GetBoneTransform(clav); var u = anim.GetBoneTransform(upperArm);
            if (c == null || u == null) return;
            // Measured in the torso's own frame: crouched (the torso pitched forward) the collarbones still settle
            // down on the ribs instead of riding up round the neck (a turtle)
            if (!TorsoFrame(out var tr, out var tu, out var tf)) return;
            var v = u.position - c.position;
            float dx = Vector3.Dot(v, tr), dy = Vector3.Dot(v, tu), dz = Vector3.Dot(v, tf);
            float yaw = Mathf.Atan2(dz, dx * side) * Mathf.Rad2Deg;
            float lift = Mathf.Atan2(dy, new Vector2(dx, dz).magnitude) * Mathf.Rad2Deg;
            // (about up: + swings the left one forward, the right one back; about forward: + lowers the left, raises the right)
            c.rotation = Quaternion.AngleAxis(-side * (clavicleYaw - yaw) * w, tu) * Quaternion.AngleAxis(side * (clavicleLift - lift) * w, tf) * c.rotation;
        }

        /// <summary>The torso's frame: across the collarbones' roots, up the spine to the neck, and forward.</summary>
        bool TorsoFrame(out Vector3 right, out Vector3 up, out Vector3 fwd)
        {
            right = up = fwd = Vector3.zero;
            var ls = anim.GetBoneTransform(HumanBodyBones.LeftShoulder); var rs = anim.GetBoneTransform(HumanBodyBones.RightShoulder);
            var neck = anim.GetBoneTransform(HumanBodyBones.Neck); var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            if (ls == null || rs == null || neck == null || spine == null) return false;
            right = (rs.position - ls.position).normalized;
            up = Vector3.ProjectOnPlane(neck.position - spine.position, right).normalized;
            fwd = Vector3.Cross(right, up);
            return true;
        }

        /// <summary>
        /// Stand and walk tall: measure the torso's forward lean (hips to neck) and take out what's beyond the upright lean (standing or walking),
        /// spread over the spine (the clips stoop, a hunch).
        /// </summary>
        void Upright(float w, Vector3 right)
        {
            if (w <= 0.001f) return;
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips); var neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            if (hips == null || neck == null) return;
            var d = transform.InverseTransformDirection(neck.position - hips.position);
            float lean = Mathf.Atan2(d.z, d.y) * Mathf.Rad2Deg;   // + forward
            // (crouched: a tactical crouch, torso well up; the library crouch folds ~60 deg and the head cranes up)
            float over = lean - (Mathf.Lerp(Mathf.Lerp(standUpright, walkUpright, Mathf.InverseLerp(0.3f, 1.4f, speedParam)), crouchLean, crouchBlend) + (runLean + personRunLean) * Mathf.Clamp01((speedParam - 1.6f) / 2.4f));   // (runners keep their lean)
            if (over <= 0f) return;
            var bones = new[] { anim.GetBoneTransform(HumanBodyBones.Spine), anim.GetBoneTransform(HumanBodyBones.Chest), anim.GetBoneTransform(HumanBodyBones.UpperChest) };
            float[] share = bones[2] != null ? new[] { 0.4f, 0.35f, 0.25f } : new[] { 0.55f, 0.45f, 0f };
            for (int i = 0; i < 3; i++)
                if (bones[i] != null) bones[i].rotation = Quaternion.AngleAxis(-over * share[i] * w, right) * bones[i].rotation;
        }

        /// <summary>
        /// Walking: an arm keeps the clip's swing but stays between minArmSpread and walkArmSpread out from the side,
        /// measured in the torso's frame: not flared out, and not swung into the body (strafing turns the hips, and
        /// the swing would cut across the chest).
        /// </summary>
        void LimitArmSpread(HumanBodyBones upper, HumanBodyBones lower, float side, float w, Vector3 fwd)
        {
            if (w <= 0.001f) return;
            var u = anim.GetBoneTransform(upper); var l = anim.GetBoneTransform(lower);
            if (u == null || l == null || !TorsoFrame(out var tr, out var tu, out var tf)) return;
            var v = (l.position - u.position).normalized;
            float spread = Mathf.Atan2(Vector3.Dot(v, tr) * side, -Vector3.Dot(v, tu)) * Mathf.Rad2Deg;
            float want = Mathf.Clamp(spread, minArmSpread, walkArmSpread);
            if (Mathf.Abs(want - spread) < 0.05f) return;
            u.rotation = Quaternion.AngleAxis(side * (want - spread) * w, tf) * u.rotation;
        }

        void RestArm(HumanBodyBones upper, HumanBodyBones lower, float side, float w, Vector3 right, Vector3 fwd)
        {
            if (w <= 0.001f) return;
            var u = anim.GetBoneTransform(upper); var l = anim.GetBoneTransform(lower);
            if (u == null || l == null) return;
            var d = transform.InverseTransformDirection((l.position - u.position).normalized);
            float pitch = Mathf.Atan2(d.z, -d.y) * Mathf.Rad2Deg;           // + hangs forward
            float spread = Mathf.Atan2(d.x * side, -d.y) * Mathf.Rad2Deg;   // + out from the body
            u.rotation = Quaternion.AngleAxis(-(armPitch - pitch) * w, right) * Quaternion.AngleAxis(side * (armSpread - spread) * w, fwd) * u.rotation;
        }

        static readonly HumanBodyBones[] CalmBones = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm };
        readonly Quaternion[] calmRot = new Quaternion[9];
        float calmW, locoW, postureW, armRestW;
        bool calmInit;

        /// <summary>Plain idle: hold the torso (and free arms) near a slow average of the clip: ~90% of its heave gone.</summary>
        void CalmIdle()
        {
            float dt = Time.deltaTime;
            bool plainIdle = anim.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && !anim.IsInTransition(0);
            locoW = Mathf.MoveTowards(locoW, plainIdle && HandsFree ? 1f : 0f, dt * 2f);
            postureW = Mathf.MoveTowards(postureW, plainIdle ? 1f : 0f, dt * 2f);   // (the torso and collarbones, armed or not)
            bool stopped = plainIdle && speedParam < 0.05f && HandsFree && crouchBlend < 0.5f;
            armRestW = Mathf.MoveTowards(armRestW, stopped ? 1f : 0f, dt / (stopped ? 1.2f : 0.35f));
            calmW = Mathf.MoveTowards(calmW, plainIdle ? 0.9f * (1f - Mathf.Clamp01(speedParam / 0.3f)) : 0f, dt * 2f);
            float k = 1f - Mathf.Exp(-dt / 1.5f);
            int n = HandsFree ? CalmBones.Length : 3;   // (a weapon rig poses the arms itself)
            for (int i = 0; i < n; i++)
            {
                var b = anim.GetBoneTransform(CalmBones[i]);
                if (b == null) continue;
                if (!calmInit || calmW <= 0.001f) { calmRot[i] = b.localRotation; continue; }
                calmRot[i] = Quaternion.Slerp(calmRot[i], b.localRotation, k);
                b.localRotation = Quaternion.Slerp(b.localRotation, calmRot[i], calmW);
            }
            calmInit = true;
        }

        /// <summary>Standing legs nearly straight (the library idle bends the knees ~35 deg): raise the body until they are.</summary>
        void StraightenLegs()
        {
            float standing = (1f - Mathf.Clamp01(speedParam / 0.6f)) * (1f - crouchBlend) * (Seated ? 0f : 1f);
            float deficit = 1f;
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                var hip = anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                var foot = anim.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (hip == null || foot == null || feet[i].legLength <= 0f) return;
                deficit = Mathf.Min(deficit, feet[i].legLength * straightLegs - (hip.position - foot.position).magnitude);
            }
            float want = Mathf.Clamp(standRaise + deficit, 0f, 0.08f) * standing;
            standRaise = Mathf.Lerp(standRaise, want, Time.deltaTime * 3f);
        }

        /// <summary>Turn the chest back by the hips' warp, spread over the spine, so the upper body keeps facing the root's forward.</summary>
        void CounterWarp()
        {
            if (Mathf.Abs(hipYaw) < 0.05f) return;
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            Quaternion headWas = head != null ? head.rotation : Quaternion.identity;
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            var upper = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            float[] share = upper != null ? new[] { 0.3f, 0.35f, 0.35f } : new[] { 0.45f, 0.55f, 0f };
            var bones = new[] { spine, chest, upper };
            for (int i = 0; i < 3; i++)
                if (bones[i] != null) bones[i].rotation = Quaternion.AngleAxis(-hipYaw * share[i], Vector3.up) * bones[i].rotation;
            if (head != null) head.rotation = headWas;
        }

        /// <summary>
        /// The library's jog and sprint swing the shoulders a long way against the hips (up to ~45 deg each stride)
        /// and rock the spine side to side:
        /// take most of that back while running, spread over the spine (the head keeps its own aim), so the chest
        /// stays square to the run with just a little counter-swing.
        /// </summary>
        void DampTorsoTwist()
        {
            float k = runTwistDamp * Mathf.InverseLerp(1.6f, 3f, speedParam) * (1f - crouchBlend);
            if (k < 0.01f) return;
            var lUp = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rUp = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var lSh = anim.GetBoneTransform(HumanBodyBones.LeftShoulder) ?? anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rSh = anim.GetBoneTransform(HumanBodyBones.RightShoulder) ?? anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (lUp == null || rUp == null || lSh == null || rSh == null) return;
            var hipLine = Vector3.ProjectOnPlane(rUp.position - lUp.position, Vector3.up);
            var shLine = Vector3.ProjectOnPlane(rSh.position - lSh.position, Vector3.up);
            if (hipLine.sqrMagnitude < 1e-4f || shLine.sqrMagnitude < 1e-4f) return;
            float twist = Vector3.SignedAngle(hipLine, shLine, Vector3.up);
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            Quaternion headWas = head != null ? head.rotation : Quaternion.identity;
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            var upper = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            float[] share = upper != null ? new[] { 0.3f, 0.35f, 0.35f } : new[] { 0.45f, 0.55f, 0f };
            var bones = new[] { spine, chest, upper };
            for (int i = 0; i < 3; i++)
                if (bones[i] != null) bones[i].rotation = Quaternion.AngleAxis(-twist * k * share[i], Vector3.up) * bones[i].rotation;
            if (head != null) head.rotation = headWas;   // (the yaw fix keeps the head's aim; through the roll fix it follows the steadied chest)
            // ...and the sideways rock (the sprint tips the spine ~13 deg and the shoulder line ~20 deg side to side
            // each stride, the neck swinging 10 cm: robotic): stand the spine up over the hips, then level the shoulders
            var neckB = anim.GetBoneTransform(HumanBodyBones.Neck);
            var hipsB = anim.GetBoneTransform(HumanBodyBones.Hips);
            if (neckB != null && hipsB != null)
            {
                var fwd = transform.forward;
                float spineRoll = Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(neckB.position - hipsB.position, fwd), fwd);
                for (int i = 0; i < 3; i++)
                    if (bones[i] != null) bones[i].rotation = Quaternion.AngleAxis(-spineRoll * k * share[i], fwd) * bones[i].rotation;
                var top = upper != null ? upper : chest;
                if (top != null)
                {
                    float shRoll = Vector3.SignedAngle(transform.right, Vector3.ProjectOnPlane(rSh.position - lSh.position, fwd), fwd);
                    top.rotation = Quaternion.AngleAxis(-shRoll * k, fwd) * top.rotation;
                }
            }
        }

        /// <summary>
        /// Running arms: the library's run swings them stiffly, elbows barely bent. Bend the elbows more (each person
        /// their own amount), swing wider (and one side a bit more than the other), and flat out, panicking, add quick
        /// irregular jerks (Perlin, a few times a second) so a fleeing crowd flails rather than marches.
        /// </summary>
        void RunArms()
        {
            if (!HandsFree) return;
            float w = Mathf.InverseLerp(2.2f, 3.4f, speedParam) * (1f - crouchBlend);
            if (w < 0.01f || !TorsoFrame(out var tr, out var tu, out var tf)) return;
            float flat = Mathf.InverseLerp(4.3f, 6f, Velocity.magnitude);   // (panic speeds)
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                var upper = anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                var fore = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                var hand = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (upper == null || fore == null || hand == null) continue;
                float side = left ? -1f : 1f;
                float seed = phase + s * 3.7f;
                // Swing: the arm's angle forward or back from hanging (about the torso's side axis), made bigger
                var d = fore.position - upper.position;
                var inPlane = Vector3.ProjectOnPlane(d, tr);
                if (inPlane.sqrMagnitude > 1e-4f)
                {
                    float swing = Vector3.SignedAngle(-tu, inPlane, tr);
                    // (about the side axis + is back) The library's run swings small and behind the body (~23 deg back,
                    // ~6 forward): re-centre it and stretch it to a sprinter's arc, ~55 back and ~55 forward, each
                    // person their own size (and one side a little bigger)
                    const float clipCentre = 8.5f;
                    float target = (swing - clipCentre) * personSwing * (1f + side * personAsym);
                    // flat out: jerky, uneven pumping on top (kept inside the arc)
                    float t = Time.time * (3.2f + personFlail);
                    target += (Mathf.PerlinNoise(t, seed) - 0.5f) * 22f * personFlail * flat;
                    target = Mathf.Clamp(target, -48f, 58f);   // (forward capped: past this the arm flies up level)
                    float add = target - swing;
                    upper.rotation = Quaternion.AngleAxis(add * w, tr) * upper.rotation;
                    // Coming forward, the fist drives across toward the chest (turned in about the torso's up axis, more the
                    // further forward it swings), not straight ahead like a pendulum
                    float fwdSwing = Mathf.Max(0f, -(swing + add));
                    upper.rotation = Quaternion.AngleAxis(-side * Mathf.Min(fwdSwing * 0.35f, 18f) * w, tu) * upper.rotation;
                    // and the arms ride well out from the body, unevenly
                    float outward = (personArmOut + (Mathf.PerlinNoise(t * 0.8f, seed + 9f) - 0.3f) * 12f * flat) * w;   // (arms well off the body, not hugging it)
                    upper.rotation = Quaternion.AngleAxis(side * outward, tf) * upper.rotation;   // (about forward: + takes the right arm out, the left in)
                }
                // Elbow: bend it further about its own hinge (never past ~135 deg)
                var u = (fore.position - upper.position).normalized;
                var f = (hand.position - fore.position).normalized;
                var hinge = Vector3.Cross(u, f);
                if (hinge.sqrMagnitude < 1e-4f) hinge = -tr;   // (straight: bend it forward, the natural way, either arm)
                hinge.Normalize();
                float bent = Vector3.Angle(u, f);
                float extra = (personElbow + (Mathf.PerlinNoise(Time.time * 2.5f, seed + 5f) - 0.5f) * 20f * flat) * w;
                extra = Mathf.Clamp(extra, 0f, Mathf.Max(0f, 135f - bent));
                fore.rotation = Quaternion.AngleAxis(extra, hinge) * fore.rotation;
            }
        }

        void LateUpdate()
        {
            if (dead || !AnimatedThisFrame) return;   // (culled frame: the pose wasn't reset, don't stack corrections)
            StraightenLegs();
            CalmIdle();
            if (HandsFree) RelaxHands(Mathf.SmoothStep(0f, 1f, armRestW), locoW);
            else if (OpenHands > 0.001f) RelaxHands(0f, OpenHands, 1f);   // (hands up: open palms; the rig poses the arms after)
            CounterWarp();
            DampTorsoTwist();
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            if (spine != null) spine.rotation = Quaternion.AngleAxis(lean, transform.right) * Quaternion.AngleAxis(roll, transform.forward) * spine.rotation;
            Posture();
            RunArms();
            var chest = anim.GetBoneTransform(HumanBodyBones.UpperChest) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) return;
            chest.localRotation *= Quaternion.Euler(Mathf.Sin(Time.time * 1.6f + phase) * 0.06f, 0f, 0f);   // barely there: the clip already breathes
            // Small wandering in the torso and neck (people never repeat a movement exactly)
            float t = Time.time * 0.35f;
            if (spine != null) spine.localRotation *= Quaternion.Euler((Mathf.PerlinNoise(t, phase + 3f) - 0.5f) * 2.5f, (Mathf.PerlinNoise(t, phase + 7f) - 0.5f) * 4f, (Mathf.PerlinNoise(t, phase + 11f) - 0.5f) * 2.5f);
            var neck = anim.GetBoneTransform(HumanBodyBones.Neck);
            if (neck != null) neck.localRotation *= Quaternion.Euler((Mathf.PerlinNoise(t * 1.6f, phase + 17f) - 0.5f) * 4f, (Mathf.PerlinNoise(t * 1.6f, phase + 23f) - 0.5f) * 5f, 0f);
        }
    }
}
