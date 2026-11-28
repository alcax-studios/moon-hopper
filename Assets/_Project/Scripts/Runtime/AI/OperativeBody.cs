using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Procedural body for combat characters on the shared rig, with elbows:
    /// Rig > LegL/LegR (> Knee > Shoe), Body > Head, ArmL/ArmR (> Elbow), Weapon (> Muzzle, GripR, GripL).
    /// Legs: a planted-foot gait along the real move direction (so strafing and backpedaling step correctly),
    /// two-bone IK onto the ground, and a pelvis drop for crouching and long running strides.
    /// Arms: two-bone IK onto the weapon grips. The weapon points exactly where the aim model points,
    /// so what you see is where the bullets go.
    /// </summary>
    public class OperativeBody : MonoBehaviour, IOperativeBody
    {
        // Driven by the Operative each frame
        public Vector3 Velocity { get; set; }
        public float Crouch { get; set; }
        public bool WeaponRaised { get; set; }
        public Vector3 AimDirection { get; set; } = Vector3.forward;
        public Vector3? LookPoint { get; set; }
        public bool Scanning { get; set; }
        public float ReloadProgress { get; set; } = -1f;
        public float MeleeT { get; set; } = -1f;
        public bool Pistol { get; set; }
        public bool HandsUp { get; set; }
        /// <summary>Weapon put away: a pistol in its hip holster, a long gun slung on the back; arms hang and swing.</summary>
        public bool Holstered { get; set; }
        float holsterBlend;
        public bool Drawing => holsterBlend > 0.02f && holsterBlend < 0.98f;
        /// <summary>-1 to 1: lean the upper body sideways around a corner (legs stay planted).</summary>
        public float SideLean { get; set; }
        /// <summary>0 to 1: blind fire, the gun held up over cover (or out around it) while the body stays tucked.</summary>
        public float BlindFire { get; set; }
        /// <summary>Which way blind fire goes: 0 over the top, -1 / 1 around the left / right side.</summary>
        public float BlindSide { get; set; }
        float leanBlend, blindBlend;

        public Transform Head => head;
        public Transform Muzzle => muzzle;
        public Transform Torso => body;
        public void Ragdoll(Vector3 hitDir, Vector3 hitPoint, float impulse) => Alcax.Combat.RagdollCorpse.Go(gameObject, hitDir, hitPoint, impulse);

        Transform rig, body, head, armL, armR, elbowL, elbowR, legL, legR, kneeL, kneeR, shoeL, shoeR, weapon, muzzle, gripR, gripL;
        Vector3 rigBase, bodyBase;
        float crouchBlend, raiseBlend, gaitT, strideBlend, seed;
        Vector3 moveLocalSmoothed;
        Vector3 flinch, flinchVel;
        Quaternion headLook = Quaternion.identity;
        bool frozen;

        const float Thigh = 0.45f, Shin = 0.47f, HipY = 0.92f, HipX = 0.1f;
        const float StanceFraction = 0.6f, StepLift = 0.08f;
        const float UpperArm = 0.3f, ForeArm = 0.3f;

        void Awake()
        {
            rig = transform.Find("Rig");
            body = rig.Find("Body");
            head = body.Find("Head");
            armL = body.Find("ArmL"); armR = body.Find("ArmR");
            elbowL = armL.Find("Elbow"); elbowR = armR.Find("Elbow");
            legL = rig.Find("LegL"); legR = rig.Find("LegR");
            kneeL = legL.Find("Knee"); kneeR = legR.Find("Knee");
            shoeL = kneeL.Find("Shoe"); shoeR = kneeR.Find("Shoe");
            weapon = body.Find("Weapon");
            if (weapon != null)
            {
                muzzle = weapon.Find("Muzzle");
                gripR = weapon.Find("GripR");
                gripL = weapon.Find("GripL");
            }
            rigBase = rig.localPosition;
            bodyBase = body.localPosition;
            seed = Random.value * 50f;
        }

        public void Flinch(Vector3 worldDir, float strength)
        {
            var local = transform.InverseTransformDirection(worldDir);
            // Hit from the front rocks the torso back; from the side, twists it
            flinchVel += new Vector3(-local.z * 160f, local.x * 120f, -local.x * 90f) * strength;
        }

        /// <summary>Let go of the weapon: it falls to the ground as a physics object.</summary>
        public Transform DropWeapon()
        {
            if (weapon == null) return null;
            var w = weapon;
            weapon = null;
            w.SetParent(null, true);
            var col = w.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, Pistol ? 0.05f : 0.4f);
            col.size = Pistol ? new Vector3(0.04f, 0.12f, 0.2f) : new Vector3(0.07f, 0.15f, 0.85f);
            var rb = w.gameObject.AddComponent<Rigidbody>();
            rb.mass = 3.5f;
            rb.AddForce(transform.forward * 1.2f + Vector3.up * 0.8f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);
            return w;
        }

        /// <summary>Stop animating (the physics death takes over).</summary>
        public void Freeze() => frozen = true;

        void LateUpdate()
        {
            if (frozen) return;
            float dt = Time.deltaTime;
            float t = Time.time + seed;
            crouchBlend = Mathf.MoveTowards(crouchBlend, Crouch, dt / 0.22f);
            raiseBlend = Mathf.MoveTowards(raiseBlend, WeaponRaised ? 1f : 0f, dt / 0.2f);
            holsterBlend = Mathf.MoveTowards(holsterBlend, Holstered ? 1f : 0f, dt / 0.35f);
            float c = crouchBlend * crouchBlend * (3f - 2f * crouchBlend);

            // Flinch spring
            flinchVel += (-flinch * 220f - flinchVel * 18f) * dt;
            flinch += flinchVel * dt;

            var local = transform.InverseTransformDirection(Velocity);
            local.y = 0f;
            moveLocalSmoothed = Vector3.Lerp(moveLocalSmoothed, local, 1f - Mathf.Exp(-10f * dt));
            SolveLegs(dt, moveLocalSmoothed, c);

            // Torso: lean into a run, fold when crouched, twist toward the aim
            float speed = moveLocalSmoothed.magnitude;
            var aimLocal = transform.InverseTransformDirection(AimDirection);
            float aimYaw = Mathf.Clamp(Mathf.Atan2(aimLocal.x, aimLocal.z) * Mathf.Rad2Deg, -50f, 50f);
            float aimPitch = -Mathf.Asin(Mathf.Clamp(aimLocal.y, -1f, 1f)) * Mathf.Rad2Deg;
            float lean = Mathf.Clamp(moveLocalSmoothed.z * 2.2f, -4f, 12f) * (1f - raiseBlend * 0.5f);
            float bob = Mathf.Sin(gaitT * Mathf.PI * 4f) * 0.015f * Mathf.Clamp01(speed / 2f);
            leanBlend = Mathf.MoveTowards(leanBlend, SideLean, dt / 0.18f);
            blindBlend = Mathf.MoveTowards(blindBlend, BlindFire, dt / 0.2f);
            float le = leanBlend * (2f - Mathf.Abs(leanBlend));   // ease
            body.localPosition = bodyBase + new Vector3(0.3f * le, Mathf.Sin(t * 1.5f) * 0.005f + bob - 0.05f * Mathf.Abs(le), -0.04f * c);
            body.localRotation = Quaternion.Euler(lean + 16f * c + aimPitch * 0.35f * raiseBlend + flinch.x,
                                                  aimYaw * raiseBlend + flinch.y,
                                                  Mathf.Sin(gaitT * Mathf.PI * 2f) * 2.5f * Mathf.Clamp01(speed / 2f) + flinch.z - 16f * le);

            PoseWeapon(dt, c);
            SolveArms();
            PoseHead(dt, t);
        }

        // ---------- Legs ----------

        static void FootCycle(float c, float stride, out float z, out float lift)
        {
            if (c < StanceFraction)
            {
                float k = c / StanceFraction;
                z = Mathf.Lerp(stride * 0.5f, -stride * 0.5f, k);
                lift = 0f;
            }
            else
            {
                float k = (c - StanceFraction) / (1f - StanceFraction);
                float e = k * k * (3f - 2f * k);
                z = Mathf.Lerp(-stride * 0.5f, stride * 0.5f, e);
                lift = Mathf.Sin(k * Mathf.PI) * StepLift * Mathf.Clamp01(stride / 0.3f);
            }
        }

        static void SolveTwoBone(float ty, float tz, out float hipDeg, out float kneeDeg)
        {
            float d = Mathf.Clamp(Mathf.Sqrt(ty * ty + tz * tz), 0.05f, Thigh + Shin - 0.002f);
            float phi = Mathf.Atan2(-tz, -ty);
            float alpha = Mathf.Acos(Mathf.Clamp((Thigh * Thigh + d * d - Shin * Shin) / (2f * Thigh * d), -1f, 1f));
            float inner = Mathf.Acos(Mathf.Clamp((Thigh * Thigh + Shin * Shin - d * d) / (2f * Thigh * Shin), -1f, 1f));
            hipDeg = (phi - alpha) * Mathf.Rad2Deg;
            kneeDeg = (Mathf.PI - inner) * Mathf.Rad2Deg;
        }

        float GroundY(Vector3 worldXZ, float fallback)
        {
            var origin = new Vector3(worldXZ.x, transform.position.y + 1.2f, worldXZ.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 2.6f, AILayers.World, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return fallback;
        }

        void SolveLegs(float dt, Vector3 moveLocal, float c)
        {
            float scale = rig.localScale.y;
            float speed = moveLocal.magnitude;
            var dir = speed > 0.05f ? moveLocal / speed : Vector3.forward;
            float cycle = Mathf.Lerp(0.95f, 0.62f, Mathf.InverseLerp(1.5f, 5.5f, speed));
            float targetStride = Mathf.Clamp(speed * cycle * StanceFraction, 0f, 0.95f);
            strideBlend = Mathf.MoveTowards(strideBlend, targetStride, dt * 3f);
            if (speed > 0.05f) gaitT = (gaitT + dt / cycle) % 1f;
            float stride = strideBlend / scale;

            float[] cyc = { gaitT, (gaitT + 0.5f) % 1f };
            float[] side = { -1f, 1f };
            var fx = new float[2]; var fz = new float[2]; var lift = new float[2]; var ground = new float[2];
            float rootFloor = GroundY(transform.position, transform.position.y);
            for (int i = 0; i < 2; i++)
            {
                FootCycle(cyc[i], stride, out float along, out lift[i]);
                // A crouch is a staggered stance: left foot forward, right foot back
                float stagger = (i == 0 ? 0.2f : -0.14f) * c * (1f - Mathf.Clamp01(speed));
                fx[i] = side[i] * HipX * (1f + 0.4f * c) + dir.x * along;
                fz[i] = dir.z * along + stagger;
                var world = transform.TransformPoint(new Vector3(fx[i] * rig.localScale.x, 0f, fz[i] * rig.localScale.z));
                ground[i] = GroundY(world, rootFloor) - transform.position.y;
            }

            // Long strides need a lower pelvis, and crouching drops it a lot
            float halfStride = stride * 0.5f;
            float reach = Thigh + Shin - 0.03f;
            float strideDrop = HipY - Mathf.Sqrt(Mathf.Max(0.01f, reach * reach - halfStride * halfStride));
            float lowest = Mathf.Min(ground[0] + lift[0] * scale, ground[1] + lift[1] * scale);
            float bob = Mathf.Cos(gaitT * Mathf.PI * 4f) * 0.014f * Mathf.Clamp01(stride / 0.3f);
            float rigY = lowest - 0.035f * scale - Mathf.Max(0f, strideDrop) * scale - 0.4f * c * scale - bob;
            rig.localPosition = rigBase + Vector3.up * rigY;

            var legs = new[] { legL, legR };
            var knees = new[] { kneeL, kneeR };
            var shoes = new[] { shoeL, shoeR };
            for (int i = 0; i < 2; i++)
            {
                const float soleOffset = 0.015f;
                float footY = (ground[i] - rig.localPosition.y) / scale + lift[i] + soleOffset / scale;
                float ty = footY - HipY;
                float dx = fx[i] - side[i] * HipX;
                float roll = Mathf.Atan2(dx, -ty) * Mathf.Rad2Deg;
                float tyRolled = -Mathf.Sqrt(ty * ty + dx * dx);
                SolveTwoBone(tyRolled, fz[i], out float hip, out float knee);
                legs[i].localRotation = Quaternion.Euler(0f, 0f, roll) * Quaternion.Euler(hip, 0f, 0f);
                knees[i].localRotation = Quaternion.Euler(knee, 0f, 0f);
                if (shoes[i] != null) shoes[i].localRotation = Quaternion.Euler(-(hip + knee), 0f, 0f);
            }
        }

        // ---------- Weapon and arms ----------

        void PoseWeapon(float dt, float c)
        {
            if (weapon == null) return;
            float r = raiseBlend * raiseBlend * (3f - 2f * raiseBlend);
            Vector3 raisedPos = Pistol ? new Vector3(0.02f, 0.5f, 0.4f) : new Vector3(0.08f, 0.48f, 0.06f);
            Vector3 lowPos = Pistol ? new Vector3(0.12f, 0.22f, 0.2f) : new Vector3(0.09f, 0.3f, 0.12f);
            var pos = Vector3.Lerp(lowPos, raisedPos, r);
            float melee = MeleeT >= 0f ? Mathf.Sin(Mathf.Clamp01(MeleeT) * Mathf.PI) : 0f;
            pos += new Vector3(0f, 0.05f, 0.32f) * melee;
            // Blind fire: gun up over the cover (or out to the side), arms extended, head kept down
            if (blindBlend > 0.001f)
            {
                var blindPos = BlindSide == 0f ? new Vector3(0.04f, 0.98f, 0.18f) : new Vector3(0.5f * Mathf.Sign(BlindSide), 0.5f, 0.22f);
                pos = Vector3.Lerp(pos, blindPos, blindBlend * blindBlend * (3f - 2f * blindBlend));
            }
// Holstered: hip holster (pistol, muzzle down) or slung across the back (long guns)
            float hb = holsterBlend * holsterBlend * (3f - 2f * holsterBlend);
            if (hb > 0.001f)
            {
                var holsterPos = Pistol ? new Vector3(0.21f, 0.02f, 0.02f) : new Vector3(0.02f, 0.32f, -0.17f);
                pos = Vector3.Lerp(pos, holsterPos, hb);
            }
            weapon.localPosition = pos;

            var low = body.rotation * Quaternion.Euler(Pistol ? 55f : 32f, Pistol ? 0f : -24f, Pistol ? 0f : 18f);
            var aimed = Quaternion.LookRotation(AimDirection.sqrMagnitude > 0.001f ? AimDirection : transform.forward, Vector3.up);
            var rot = Quaternion.Slerp(low, aimed, r);
            if (melee > 0f) rot *= Quaternion.Euler(-25f * melee, 0f, 0f);
            if (hb > 0.001f)
            {
                var holsterRot = body.rotation * (Pistol ? Quaternion.Euler(95f, 0f, 0f) : Quaternion.Euler(-62f, 0f, 90f));
                rot = Quaternion.Slerp(rot, holsterRot, hb);
            }
            weapon.rotation = rot;
        }

        void SolveArms()
        {
            if (HandsUp || weapon == null)
            {
                // Hands raised, open, either side of the head
                float t = Time.time + seed;
                var up = body.up; var fw = body.forward; var rt = body.right;
                var top = head.position + up * 0.32f + fw * 0.08f;
                IKArm(armR, elbowR, top + rt * 0.24f + up * Mathf.Sin(t * 3f) * 0.01f, 1f);
                IKArm(armL, elbowL, top - rt * 0.24f, -1f);
                return;
            }
            if (gripR == null) return;
            var targetR = gripR.position;
            var targetL = gripL != null ? gripL.position : gripR.position;
            if (ReloadProgress >= 0f)
            {
                // Support hand drops to the magazine pouch and back
                float p = ReloadProgress;
                float away = p < 0.25f ? p / 0.25f : p > 0.8f ? (1f - p) / 0.2f : 1f;
                var pouch = body.TransformPoint(new Vector3(-0.12f, 0.18f, 0.16f));
                targetL = Vector3.Lerp(targetL, pouch, Mathf.SmoothStep(0f, 1f, away));
            }
            float hb = holsterBlend * holsterBlend * (3f - 2f * holsterBlend);
            if (hb > 0.001f)
            {
                float swing = Mathf.Sin(gaitT * Mathf.PI * 2f) * 0.16f * Mathf.Clamp01(moveLocalSmoothed.magnitude / 2f);
                Vector3 Hang(Transform shoulder, float side, float sw) => shoulder.position - body.up * 0.55f + body.forward * (0.05f + sw) + body.right * side * 0.07f;
                targetR = Vector3.Lerp(targetR, Hang(armR, 1f, -swing), hb);
                targetL = Vector3.Lerp(targetL, Hang(armL, -1f, swing), hb);
            }
            IKArm(armR, elbowR, targetR, 1f);
            IKArm(armL, elbowL, targetL, -1f);
        }

        void IKArm(Transform shoulder, Transform elbow, Vector3 target, float side)
        {
            if (shoulder == null || elbow == null) return;
            var s = shoulder.position;
            var to = target - s;
            float d = Mathf.Clamp(to.magnitude, 0.08f, UpperArm + ForeArm - 0.005f);
            var dir = to.normalized;
            // Elbows hang down and out
            var pole = -body.up * 1f + body.right * side * 0.6f - body.forward * 0.2f;
            var perp = Vector3.ProjectOnPlane(pole, dir).normalized;
            float a = (UpperArm * UpperArm + d * d - ForeArm * ForeArm) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, UpperArm * UpperArm - a * a));
            var elbowPos = s + dir * a + perp * h;
            shoulder.rotation = Quaternion.LookRotation(elbowPos - s, -perp) * Quaternion.Euler(-90f, 0f, 0f);
            elbow.rotation = Quaternion.LookRotation(target - elbow.position, -perp) * Quaternion.Euler(-90f, 0f, 0f);
        }

        void PoseHead(float dt, float t)
        {
            Vector3 lookDir;
            if (LookPoint.HasValue) lookDir = (LookPoint.Value - head.position).normalized;
            else if (raiseBlend > 0.5f) lookDir = AimDirection;
            else lookDir = transform.forward;
            var target = Quaternion.LookRotation(head.parent.InverseTransformDirection(lookDir));
            if (Scanning) target = Quaternion.Euler(Mathf.Sin(t * 0.37f) * 6f, Mathf.Sin(t * 0.29f) * 55f + Mathf.Sin(t * 0.81f) * 12f, 0f) * target;
            var e = target.eulerAngles;
            target = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -45f, 45f), Mathf.Clamp(Mathf.DeltaAngle(0f, e.y), -75f, 75f), 0f);
            headLook = Quaternion.Slerp(headLook, target, 1f - Mathf.Exp(-9f * dt));
            head.localRotation = headLook;
        }
    }
}
