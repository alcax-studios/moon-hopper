using System.Collections.Generic;
using Alcax.Player;
using UnityEngine;

namespace Alcax.Dialogue
{
    /// <summary>
    /// A talkable character on a primitive rig: Rig > (LegL > KneeL, LegR > KneeR, Body > Head > Jaw, ArmL, ArmR).
    /// It animates procedurally: idle breathing, a walk cycle driven by MoveSpeed, a sitting pose (SitBlend),
    /// typing, and head look (a LookTarget, else the player when near). It moves its jaw and gestures while Speaking.
    /// Movement decisions come from a brain such as AmbientNpc. A standing NPC like Ines needs no brain.
    /// </summary>
    public class Npc : MonoBehaviour
    {
        public static readonly List<Npc> Active = new();

        public string displayName = "Ines";
        public string role = "Guest Services";
        public DialogueTree dialogue;
        [Tooltip("Off: this only provides the conversation (name, dialogue, talking state); something else animates the body (a guard's combat rig).")]
        public bool drivesBody = true;
        [SerializeField] float lookRadius = 7f;
        [SerializeField] float stepLength = 0.65f;

        // Driven by a brain (or left at defaults for a standing NPC)
        public float MoveSpeed { get; set; }
        public float SitBlend { get; set; }
        public bool Typing { get; set; }
        public Transform LookTarget { get; set; }
        public bool Speaking { get; set; }
        /// <summary>True while the player is talking to this NPC (brains pause and face the player).</summary>
        public bool InConversation { get; set; }
        /// <summary>False while panicking (no small talk under fire).</summary>
        public bool CanTalk { get; set; } = true;
        /// <summary>0 to 1: hands raised (surrender or cower).</summary>
        public float HandsUp { get; set; }
        /// <summary>Punch progress 0 to 1 (negative = not punching): the right arm jabs forward.</summary>
        public float PunchT { get; set; } = -1f;
        float handsBlend;
        Transform elbowL, elbowR;
        Quaternion elbowLBase, elbowRBase;

        Transform rig, body, head, jaw, armL, armR, legL, legR, kneeL, kneeR;
        Quaternion headBase, jawBase, armLBase, armRBase;
        Vector3 bodyBase, rigBase;
        Transform player;
        float seed, gesture, gestureTarget, nextGesture, phase, walkBlend;
        Quaternion headLook = Quaternion.identity;

        public Vector3 FocusPoint => head != null ? head.position : transform.position + Vector3.up * 1.6f;
        public Transform HeadTransform => head;

        void Awake()
        {
            rig = transform.Find("Rig") ?? transform;
            body = rig.Find("Body");
            head = body.Find("Head");
            jaw = head.Find("Jaw");
            armL = body.Find("ArmL");
            armR = body.Find("ArmR");
            legL = rig.Find("LegL");
            legR = rig.Find("LegR");
            kneeL = legL != null ? legL.Find("Knee") : null;
            kneeR = legR != null ? legR.Find("Knee") : null;
            bodyBase = body.localPosition;
            rigBase = rig.localPosition;
            headBase = head.localRotation;
            jawBase = jaw.localRotation;
            armLBase = armL.localRotation;
            armRBase = armR.localRotation;
            elbowL = armL.Find("Elbow"); elbowR = armR.Find("Elbow");
            if (elbowL != null) elbowLBase = elbowL.localRotation;
            if (elbowR != null) elbowRBase = elbowR.localRotation;
            seed = Random.value * 10f;
        }

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        void Start()
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.Head;
        }

        // ---------- Foot IK ----------

        const float Thigh = 0.45f, Shin = 0.47f, HipY = 0.92f, HipX = 0.1f;
        const float StanceFraction = 0.6f, CycleSeconds = 0.95f, StepLift = 0.07f;
        Transform shoeL, shoeR;
        float gaitT;           // 0..1 cycle progress
        float strideBlend;     // how much stride is applied (0 standing still)

        void CacheShoes()
        {
            if (shoeL == null && kneeL != null) shoeL = kneeL.Find("Shoe");
            if (shoeR == null && kneeR != null) shoeR = kneeR.Find("Shoe");
        }

        float GroundY(Vector3 worldXZ, float fallback)
        {
            var origin = new Vector3(worldXZ.x, transform.position.y + 1.2f, worldXZ.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 2.4f, ~((1 << 2) | (1 << 8)), QueryTriggerInteraction.Ignore))   // not the player, not people's own hit colliders
                return hit.point.y;
            return fallback;
        }

        /// <summary>Foot offset along the walk direction (local z) and lift, for a leg at cycle position c (0..1).</summary>
        static void FootCycle(float c, float stride, out float z, out float lift)
        {
            if (c < StanceFraction)
            {
                float k = c / StanceFraction;                 // planted: foot slides back under the moving body
                z = Mathf.Lerp(stride * 0.5f, -stride * 0.5f, k);
                lift = 0f;
            }
            else
            {
                float k = (c - StanceFraction) / (1f - StanceFraction);   // swing: lift and reach forward
                float e = k * k * (3f - 2f * k);
                z = Mathf.Lerp(-stride * 0.5f, stride * 0.5f, e);
                lift = Mathf.Sin(k * Mathf.PI) * StepLift * Mathf.Clamp01(stride / 0.3f);
            }
        }

        /// <summary>Analytic two-bone IK in the leg's sagittal plane. Returns hip pitch and knee bend (degrees).</summary>
        static void SolveTwoBone(float ty, float tz, out float hipDeg, out float kneeDeg)
        {
            float d = Mathf.Clamp(Mathf.Sqrt(ty * ty + tz * tz), 0.05f, Thigh + Shin - 0.002f);
            float phi = Mathf.Atan2(-tz, -ty);   // angle of the target from straight down (+ = backward)
            float alpha = Mathf.Acos(Mathf.Clamp((Thigh * Thigh + d * d - Shin * Shin) / (2f * Thigh * d), -1f, 1f));
            float inner = Mathf.Acos(Mathf.Clamp((Thigh * Thigh + Shin * Shin - d * d) / (2f * Thigh * Shin), -1f, 1f));
            hipDeg = (phi - alpha) * Mathf.Rad2Deg;          // thigh forward of the target line, so the knee points forward
            kneeDeg = (Mathf.PI - inner) * Mathf.Rad2Deg;    // shin folds back
        }

        void SolveLegs(float dt, float speed, float sit)
        {
            if (legL == null || legR == null || kneeL == null || kneeR == null) return;
            CacheShoes();
            float scaleY = rig.localScale.y;

            // Stride grows with speed; the cycle runs at a steady cadence, so stance-foot speed matches body speed
            float targetStride = Mathf.Clamp(speed * CycleSeconds * StanceFraction, 0f, 0.75f);
            strideBlend = Mathf.MoveTowards(strideBlend, targetStride, dt * 1.5f);
            if (speed > 0.05f) gaitT = (gaitT + dt / CycleSeconds) % 1f;
            float stride = strideBlend / Mathf.Max(0.01f, scaleY);   // in rig-local units

            // Where each foot wants to be (rig-local), then find the real floor under it
            var fwd = transform.forward; var right = transform.right;
            float[] cyc = { gaitT, (gaitT + 0.5f) % 1f };
            float[] side = { -1f, 1f };
            var footZ = new float[2]; var footLift = new float[2]; var groundLocal = new float[2];
            float rootFloor = GroundY(transform.position, transform.position.y);
            for (int i = 0; i < 2; i++)
            {
                FootCycle(cyc[i], stride, out footZ[i], out footLift[i]);
                var world = transform.position + right * (side[i] * HipX * rig.localScale.x) + fwd * (footZ[i] * rig.localScale.z);
                groundLocal[i] = (GroundY(world, rootFloor) - transform.position.y);   // root-local height of the floor under the foot
            }

            // Pelvis sits so the lower foot can reach with a soft knee, plus a small gait bob
            float lowest = Mathf.Min(groundLocal[0] + footLift[0] * scaleY, groundLocal[1] + footLift[1] * scaleY);
            float bob = Mathf.Cos(gaitT * Mathf.PI * 4f) * 0.012f * Mathf.Clamp01(stride / 0.3f);
            float standRigY = lowest + (HipY - 0.035f) * scaleY - HipY * scaleY - bob;
            rig.localPosition = rigBase + Vector3.up * Mathf.Lerp(standRigY, -0.45f, sit);

            // IK for each leg in rig-local space, blended with the seated fold
            var legs = new[] { legL, legR };
            var knees = new[] { kneeL, kneeR };
            var shoes = new[] { shoeL, shoeR };
            for (int i = 0; i < 2; i++)
            {
                const float soleOffset = 0.015f;   // measured: the flattened shoe sat 1.5 cm low
                float footYRig = (groundLocal[i] - rig.localPosition.y) / scaleY + footLift[i] + soleOffset / scaleY;   // sole height in rig space
                float ty = footYRig - HipY, tz = footZ[i];
                SolveTwoBone(ty, tz, out float hip, out float knee);
                float seatHip = i == 0 ? -88f : -86f, seatKnee = i == 0 ? 90f : 88f;
                float h = Mathf.Lerp(hip, seatHip, sit), k = Mathf.Lerp(knee, seatKnee, sit);
                legs[i].localRotation = Quaternion.Euler(h, 0f, 0f);
                knees[i].localRotation = Quaternion.Euler(k, 0f, 0f);
                if (shoes[i] != null) shoes[i].localRotation = Quaternion.Euler(-(h + k) * (1f - sit), 0f, 0f);   // keep the foot flat
            }
        }

        void Update()
        {
            if (!drivesBody) return;
            float t = Time.time + seed;
            float dt = Time.deltaTime;
            float sit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(SitBlend));

            // Walk cycle
            float speed = MoveSpeed;
            walkBlend = Mathf.MoveTowards(walkBlend, speed > 0.08f ? 1f : 0f, dt / 0.25f);
            phase += dt * speed / Mathf.Max(0.1f, stepLength) * Mathf.PI;
            float s = Mathf.Sin(phase);
            float w = walkBlend * (1f - sit);

            // ---- Legs: a planted-foot gait with two-bone IK onto the real floor (blends to the seated pose) ----
            SolveLegs(dt, speed, sit);

            body.localPosition = bodyBase + new Vector3(Mathf.Sin(t * 0.35f) * 0.012f * (1f - w), Mathf.Sin(t * 1.6f) * 0.006f, 0f);
            body.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.6f) * 0.6f + 4f * w, Mathf.Sin(gaitT * Mathf.PI * 2f) * 4f * w, Mathf.Sin(t * 0.35f) * 1.2f * (1f - w) + Mathf.Sin(gaitT * Mathf.PI * 2f) * 2f * w);

            // Head: a look target, else the player when near, else idle glances
            Quaternion target = Quaternion.Euler(Mathf.Sin(t * 0.21f) * 4f, Mathf.Sin(t * 0.13f) * 18f * (1f - w * 0.6f), 0f);
            Transform look = LookTarget;
            if (look == null && player != null && (player.position - head.position).sqrMagnitude < lookRadius * lookRadius) look = player;
            if (look != null)
            {
                var local = head.parent.InverseTransformDirection((look.position - head.position).normalized);
                var e = Quaternion.LookRotation(local).eulerAngles;
                float yaw = Mathf.Clamp(Mathf.DeltaAngle(0f, e.y), -70f, 70f);
                float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -30f, 30f);
                if (Mathf.Abs(Mathf.DeltaAngle(0f, e.y)) < 110f) target = Quaternion.Euler(pitch, yaw, 0f);
            }
            headLook = Quaternion.Slerp(headLook, target, 1f - Mathf.Exp(-5f * dt));
            head.localRotation = headBase * headLook;

            // Jaw and gestures while speaking
            float jawOpen = Speaking ? (Mathf.Abs(Mathf.Sin(t * 13f)) * 0.6f + Mathf.Abs(Mathf.Sin(t * 7.3f)) * 0.4f) * 14f : 0f;
            jaw.localRotation = jawBase * Quaternion.Euler(jawOpen, 0f, 0f);
            if (Speaking && Time.time > nextGesture)
            {
                gestureTarget = gestureTarget > 0.5f ? 0f : 1f;
                nextGesture = Time.time + Random.Range(0.8f, 2f);
            }
            if (!Speaking) gestureTarget = 0f;
            gesture = Mathf.MoveTowards(gesture, gestureTarget, dt * 2.5f);
            float g = Mathf.SmoothStep(0f, 1f, gesture);

            // Arms: walk swing, typing, resting in the lap when seated, and talk gestures
            float armSwing = Mathf.Sin(gaitT * Mathf.PI * 2f) * 22f * w;
            float typeL = Typing ? -48f + Mathf.Sin(t * 17f) * 3f : 0f;
            float typeR = Typing ? -50f + Mathf.Sin(t * 19f + 1f) * 3f : 0f;
            float lap = -38f * sit;
            armL.localRotation = armLBase * Quaternion.Euler(-armSwing + typeL + lap + Mathf.Sin(t * 0.9f) * 2f, 0f, Typing ? -10f : 0f);
            armR.localRotation = armRBase * Quaternion.Euler(armSwing + typeR + lap - 55f * g + Mathf.Sin(t * 2f) * 4f * g, 0f, -12f * g + (Typing ? 10f : 0f));
            if (PunchT >= 0f)
            {
                float k = Mathf.Sin(Mathf.Clamp01(PunchT) * Mathf.PI);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, armRBase * Quaternion.Euler(-92f, 0f, -8f), k);
                armL.localRotation = Quaternion.Slerp(armL.localRotation, armLBase * Quaternion.Euler(-60f, 0f, 10f), 0.6f);   // guard up
            }
            // Hands up: upper arms raised beside the head, forearms bent, a nervous tremble
            handsBlend = Mathf.MoveTowards(handsBlend, Mathf.Clamp01(HandsUp), dt / 0.3f);
            if (handsBlend > 0.001f)
            {
                float h = Mathf.SmoothStep(0f, 1f, handsBlend);
                float tremble = Mathf.Sin(t * 23f) * 1.5f;
                armL.localRotation = Quaternion.Slerp(armL.localRotation, armLBase * Quaternion.Euler(-150f + tremble, 0f, 14f), h);
                armR.localRotation = Quaternion.Slerp(armR.localRotation, armRBase * Quaternion.Euler(-150f - tremble, 0f, -14f), h);
                if (elbowL != null) elbowL.localRotation = Quaternion.Slerp(elbowLBase, elbowLBase * Quaternion.Euler(-55f, 0f, 0f), h);
                if (elbowR != null) elbowR.localRotation = Quaternion.Slerp(elbowRBase, elbowRBase * Quaternion.Euler(-55f, 0f, 0f), h);
            }
            else
            {
                if (elbowL != null) elbowL.localRotation = elbowLBase;
                if (elbowR != null) elbowR.localRotation = elbowRBase;
            }
        }
    }
}
