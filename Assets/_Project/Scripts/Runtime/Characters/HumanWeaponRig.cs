using UnityEngine;

namespace Alcax.Characters
{
    public enum HandWeapon { None, Pistol, Rifle }

    /// <summary>
    /// Arms and weapon for a HumanMotor character (after the motor in the frame), the third-person-shooter way: the
    /// weapon is posed from the aim (raised: the rifle butt in the shoulder pocket or the pistol out at eye level in
    /// two hands, pointing exactly along the aim; low ready; holstered: pistol on the hip, long gun slung on the back;
    /// blind fire over or around cover; a bash), the upper body turns toward the aim and leans out around corners, and
    /// both hands are put on the weapon's grips (GripR, GripL) by a two-bone arm IK with natural elbows and gripping
    /// hand orientations. Reload: the support hand goes to the pouch and back. Unarmed: a fists-up guard and punches
    /// (alternating jab and cross, the torso turning into them). Hands up: surrender. Any Humanoid rig.
    /// </summary>
    [RequireComponent(typeof(HumanMotor))]
    [DefaultExecutionOrder(20)]
    public class HumanWeaponRig : MonoBehaviour
    {
        // ---------- Inputs ----------
        public HandWeapon Kind = HandWeapon.None;
        /// <summary>The weapon model: +Z forward, origin at the butt (pistol: the grip), children GripR, GripL, Muzzle.</summary>
        public Transform Weapon;
        public bool Raised { get; set; }
        public bool Holstered { get; set; }
        public bool HandsUp { get; set; }
        /// <summary>Unarmed and fighting: fists up.</summary>
        public bool Fists { get; set; }
        public Vector3 AimDirection { get; set; } = Vector3.forward;
        public float ReloadProgress { get; set; } = -1f;
        public float MeleeT { get; set; } = -1f;
        public float BlindFire { get; set; }
        public float BlindSide { get; set; }
        public float SideLean { get; set; }
        /// <summary>Flinch on the spine (pitch, yaw, roll in degrees), from hits.</summary>
        public Vector3 Flinch { get; set; }
        /// <summary>Cowering: crouched, forearms up around the head.</summary>
        public bool CoverHead { get; set; }

        public Transform Muzzle => muzzle;
        /// <summary>The upper body's turn toward the aim last frame (deg from the root forward).</summary>
        public float Twist { get; private set; }
        public bool Drawing => holsterB > 0.02f && holsterB < 0.98f;

        HumanMotor motor;
        Animator anim;
        Transform gripR, gripL, muzzle;
        Transform hips, spine, chest, upperChest, head, upArmL, upArmR, foreL, foreR, handL, handR;
        Quaternion handBasisL, handBasisR;       // each hand's local (wrist-to-knuckles, index-side) frame
        float raiseB, holsterB, blindB, leanB, fistB, upB, coverB, punchExt;
        int punchSide = 1;
        float lastMelee = -1f;
        bool calibrated;
        float armLength = 0.52f;   // shoulder to wrist (measured)

        /// <summary>How this person holds a gun: no two characters aim alike.</summary>
        struct Style
        {
            public float height, reach, flare, supportFlare, cant, sway, swayRate, tremor, lag;
        }
        Style style;
        float seed;
        Vector3 gunLocalPos;
        Quaternion gunLocalRot;
        bool gunInit;

        void Awake()
        {
            motor = GetComponent<HumanMotor>();
            anim = GetComponent<Animator>();
            seed = Random.value * 100f;
            style = new Style
            {
                height = Random.Range(-0.035f, 0.025f),    // grip height (m)
                reach = Random.Range(-0.04f, 0.03f),       // how far out the arms go (m)
                flare = Random.Range(0.3f, 0.75f),         // firing elbow: tucked to flared
                supportFlare = Random.Range(0.25f, 0.6f),
                cant = Random.Range(-5f, 4f),              // a slight tilt of the gun (deg)
                sway = Random.Range(0.35f, 0.85f),         // breathing sway (deg)
                swayRate = Random.Range(0.18f, 0.32f),     // breaths per second
                tremor = Random.Range(0.08f, 0.2f),        // fine shake (deg)
                lag = Random.Range(11f, 17f),              // how quickly the gun catches up with the aim
            };
        }

        /// <summary>Turn a hand toward the line of its forearm (a locked wrist), by w; it pivots at the wrist.</summary>
        static void StraightenWrist(Transform fore, Transform hand, Vector3 handFwd, float w)
        {
            if (w <= 0.001f || fore == null || hand == null) return;
            var forearm = (hand.position - fore.position).normalized;
            hand.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(handFwd, forearm), w) * hand.rotation;
        }

        public void SetWeapon(Transform weapon, HandWeapon kind)
        {
            Weapon = weapon; Kind = weapon != null ? kind : HandWeapon.None;
            gripR = weapon != null ? weapon.Find("GripR") : null;
            gripL = weapon != null ? weapon.Find("GripL") : null;
            muzzle = weapon != null ? weapon.Find("Muzzle") : null;
        }

        void Start()
        {
            if (Weapon != null && gripR == null) SetWeapon(Weapon, Kind);
        }

        bool Calibrate()
        {
            if (calibrated) return true;
            hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            upArmL = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); upArmR = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            foreL = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm); foreR = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            handL = anim.GetBoneTransform(HumanBodyBones.LeftHand); handR = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (hips == null || head == null || handL == null || handR == null || foreL == null || foreR == null) return false;
            handBasisL = HandBasis(handL, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal);
            handBasisR = HandBasis(handR, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal);
            armLength = (foreR.position - upArmR.position).magnitude + (handR.position - foreR.position).magnitude;
            calibrated = true;
            return true;
        }

        /// <summary>The hand's own frame in its local space: forward = wrist to the middle knuckle, up = little to index knuckle.</summary>
        Quaternion HandBasis(Transform hand, HumanBodyBones middle, HumanBodyBones index, HumanBodyBones little)
        {
            var m = anim.GetBoneTransform(middle); var i = anim.GetBoneTransform(index); var l = anim.GetBoneTransform(little);
            if (m == null || i == null || l == null) return Quaternion.identity;
            var f = hand.InverseTransformDirection(m.position - hand.position).normalized;
            var s = hand.InverseTransformDirection(i.position - l.position);
            return Quaternion.LookRotation(f, Vector3.ProjectOnPlane(s, f).normalized);
        }

        /// <summary>The hand rotation whose knuckles point along fwd with the index side toward side.</summary>
        static Quaternion HandRotation(Quaternion basis, Vector3 fwd, Vector3 side)
            => Quaternion.LookRotation(fwd, Vector3.ProjectOnPlane(side, fwd).normalized) * Quaternion.Inverse(basis);

        void LateUpdate()
        {
            if (!Calibrate() || !motor.AnimatedThisFrame || motor.Dead) return;
            float dt = Time.deltaTime;
            raiseB = Mathf.MoveTowards(raiseB, Raised && !Holstered ? 1f : 0f, dt / 0.2f);
            holsterB = Mathf.MoveTowards(holsterB, Holstered || Kind == HandWeapon.None ? 1f : 0f, dt / 0.35f);
            blindB = Mathf.MoveTowards(blindB, BlindFire, dt / 0.2f);
            leanB = Mathf.MoveTowards(leanB, SideLean, dt / 0.18f);
            fistB = Mathf.MoveTowards(fistB, Fists && Kind == HandWeapon.None && !HandsUp ? 1f : 0f, dt / 0.25f);
            bool flinching = Time.time < interruptUntil;   // (a hit: the arms come in for a moment, then back up)
            motor.Fists = Fists && Kind == HandWeapon.None && !HandsUp;
            upB = Mathf.MoveTowards(upB, HandsUp && !flinching ? 1f : 0f, dt / (flinching ? 0.12f : 0.3f));
            coverB = Mathf.MoveTowards(coverB, CoverHead && !HandsUp && !flinching ? 1f : 0f, dt / (flinching ? 0.12f : 0.25f));
            float r = Ease(raiseB), hb = Ease(holsterB), le = leanB * (2f - Mathf.Abs(leanB));

            // Arms on the weapon (fading out as it goes into the holster), on the guard, or up
            float gunHands = (1f - Ease(Mathf.InverseLerp(0.88f, 1f, hb))) * (1f - upB);          // firing hand: on the gun until it is home
            float supportHand = (1f - Ease(Mathf.InverseLerp(0.12f, 0.45f, hb))) * (1f - upB);     // support hand: off early (on late drawing)
            bool armsBusy = gunHands > 0.001f || supportHand > 0.001f || fistB > 0.001f || upB > 0.001f || coverB > 0.001f;
            motor.HandsFree = !armsBusy;
            motor.OpenHands = Ease(upB);   // (surrendering: open, flat hands)

            var up = Vector3.up;
            var aim = AimDirection.sqrMagnitude > 0.001f ? AimDirection.normalized : transform.forward;
            var aimLocal = transform.InverseTransformDirection(aim);
            float aimYaw = Mathf.Clamp(Mathf.Atan2(aimLocal.x, aimLocal.z) * Mathf.Rad2Deg, -50f, 50f);
            float aimPitch = -Mathf.Asin(Mathf.Clamp(aimLocal.y, -1f, 1f)) * Mathf.Rad2Deg;   // + down

            // ---- Upper body: turn toward the aim, bend with its pitch (raised), lean out, flinch; the gaze is kept ----
            float combat = Mathf.Max(Mathf.Max(r, fistB), gunHands * 0.5f);
            float punch = PunchUpdate(dt);
            float twist = aimYaw * combat;   // (a punch's own shoulder drive comes from its clip, on the torso layer)
            Twist = twist;
            motor.ChestYaw = twist;
            float bend = aimPitch * 0.45f * Mathf.Max(r, fistB * 0.6f);
            Quaternion headWas = head.rotation;
            var spineBones = upperChest != null ? new[] { spine, chest, upperChest } : new[] { spine, chest };
            float[] share = upperChest != null ? new[] { 0.3f, 0.35f, 0.35f } : new[] { 0.45f, 0.55f };
            var fwd0 = transform.forward;
            for (int i = 0; i < spineBones.Length; i++)
            {
                if (spineBones[i] == null) continue;
                float s = share[i];
                var torsoRight = Quaternion.AngleAxis(twist, up) * transform.right;
                spineBones[i].rotation = Quaternion.AngleAxis(twist * s + Flinch.y * s, up)
                    * Quaternion.AngleAxis((bend + Flinch.x) * s, torsoRight)
                    * Quaternion.AngleAxis((-16f * le + Flinch.z) * s, Quaternion.AngleAxis(twist, up) * fwd0)
                    * spineBones[i].rotation;
            }
            // The measured result is what counts (the clip's own shoulder swing and the hips' turn add up): keep the
            // shoulders within the twist budget of the hips, or the spine looks wrung
            {
                var lul = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rul = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                var hipsF = Vector3.Cross(rul.position - lul.position, up); hipsF.y = 0f;
                var chestF = Vector3.Cross(upArmR.position - upArmL.position, up); chestF.y = 0f;
                float net = Vector3.SignedAngle(hipsF, chestF, up);
                float over = net - Mathf.Clamp(net, -motor.MaxTwist, motor.MaxTwist);
                if (Mathf.Abs(over) > 0.1f)
                    for (int i = 0; i < spineBones.Length; i++)
                        if (spineBones[i] != null) spineBones[i].rotation = Quaternion.AngleAxis(-over * share[i], up) * spineBones[i].rotation;
            }
            head.rotation = headWas;
            if (Flinch.sqrMagnitude > 0.01f)   // a hit snaps the head too
                head.rotation = Quaternion.AngleAxis(Flinch.y * 0.6f, up) * Quaternion.AngleAxis(Flinch.x * 0.8f, transform.right) * head.rotation;

            // Torso frame (after the turn)
            var torsoFwd = Quaternion.AngleAxis(twist, up) * transform.forward;
            var T = Quaternion.LookRotation(torsoFwd, up);
            var chestP = (upperChest != null ? upperChest : chest != null ? chest : spine).position;

            // ---- Weapon pose ----
            // Everyone holds a gun a little differently (style), nobody holds it still (breathing sway, tremor, the
            // step's dip and roll), and it rides the hands with some inertia rather than being bolted to the aim
            float moving = Mathf.Clamp01(motor.Gait / 1.4f);
            float nt = anim.GetCurrentAnimatorStateInfo(0).normalizedTime * Mathf.PI * 2f;   // gait cycle (two steps)
            if (Weapon != null)
            {
                Vector3 pos; Quaternion rot;
                bool pistol = Kind == HandWeapon.Pistol;
                var eye = head.position + up * 0.08f + torsoFwd * 0.08f;
                // Pistol: at chin height with the arms out (compressed nearer and lower while moving); rifle: butt in the shoulder
                var shoulders = (upArmL.position + upArmR.position) * 0.5f;
                float reach = armLength + 0.07f + style.reach - 0.12f * moving;   // arms near full extension (+0.07: the grip sits past the wrist)
                Vector3 raisedPos = pistol ? shoulders + aim * reach + up * (0.07f + style.height - 0.04f * moving)
                                           : upArmR.position + T * new Vector3(-0.045f, -0.035f + style.height * 0.5f, 0.02f - 0.02f * moving);
                Vector3 lowPos = pistol ? hips.position + T * new Vector3(0.12f, 0.2f + style.height, 0.24f)
                                        : upArmR.position + T * new Vector3(-0.08f, -0.17f, 0.1f);
                float st = Time.time * style.swayRate * Mathf.PI * 2f + seed;
                float swayYaw = Mathf.Sin(st) * style.sway * (1f + moving) + (Mathf.PerlinNoise(Time.time * 2.7f, seed) - 0.5f) * 2f * style.tremor;
                float swayPitch = Mathf.Sin(st * 2f) * 0.5f * style.sway * (1f + moving) + (Mathf.PerlinNoise(Time.time * 2.3f, seed + 5f) - 0.5f) * 2f * style.tremor;
                float stepRoll = Mathf.Sin(nt) * 1.8f * moving;
                raisedPos += (up * (-0.012f * Mathf.Abs(Mathf.Sin(nt))) + T * Vector3.right * (0.008f * Mathf.Sin(nt))) * moving;
                var raisedRot = Quaternion.LookRotation(aim, up) * Quaternion.Euler(swayPitch, swayYaw, style.cant + stepRoll);
                var lowRot = T * (pistol ? Quaternion.Euler(55f, 0f, 0f) : Quaternion.Euler(32f, -24f, 18f)) * Quaternion.Euler(swayPitch * 2f, swayYaw * 2f, stepRoll * 1.5f);
                pos = Vector3.Lerp(lowPos, raisedPos, r);
                rot = Quaternion.Slerp(lowRot, raisedRot, r);
                // Blind fire: gun up over the cover (or out to the side), arms extended
                if (blindB > 0.001f)
                {
                    var blindPos = BlindSide == 0f ? chestP + T * new Vector3(0.04f, 0.55f, 0.18f) : chestP + T * new Vector3(0.5f * Mathf.Sign(BlindSide), 0.1f, 0.22f);
                    float b = Ease(blindB);
                    pos = Vector3.Lerp(pos, blindPos, b);
                    rot = Quaternion.Slerp(rot, raisedRot, b);
                }
                // Bash
                float melee = MeleeT >= 0f && Kind != HandWeapon.None ? Mathf.Sin(Mathf.Clamp01(MeleeT) * Mathf.PI) : 0f;
                pos += T * new Vector3(0f, 0.05f, 0.32f) * melee;
                rot *= Quaternion.Euler(-25f * melee, 0f, 0f);
                // Reload: the gun tips toward the support hand
                float reload = ReloadAway();
                rot *= Quaternion.Euler(12f * reload, 0f, 28f * reload);
                // Holstered: pistol on the right hip (muzzle down), long gun slung across the back
                if (hb > 0.001f)
                {
                    var holsterPos = pistol ? hips.position + T * new Vector3(0.21f, 0.0f, 0.03f) : chestP + T * new Vector3(0.02f, 0.02f, -0.17f);
                    var holsterRot = T * (pistol ? Quaternion.Euler(95f, 0f, 0f) : Quaternion.Euler(-62f, 0f, 90f));
                    pos = Vector3.Lerp(pos, holsterPos, hb);
                    rot = Quaternion.Slerp(rot, holsterRot, hb);
                }
                // Inertia: the pose is followed in the torso frame with a short lag (it trails a quick turn of the aim)
                var localPos = Quaternion.Inverse(T) * (pos - chestP);
                var localRot = Quaternion.Inverse(T) * rot;
                if (!gunInit) { gunLocalPos = localPos; gunLocalRot = localRot; gunInit = true; }
                float k = 1f - Mathf.Exp(-style.lag * dt);
                gunLocalPos = Vector3.Lerp(gunLocalPos, localPos, k);
                gunLocalRot = Quaternion.Slerp(gunLocalRot, localRot, k);
                Weapon.SetPositionAndRotation(chestP + T * gunLocalPos, T * gunLocalRot);
                if (gripR != null && hb < 0.5f)
                {
                    float over = (gripR.position - upArmR.position).magnitude - (armLength + 0.06f);
                    if (over > 0f) Weapon.position -= (gripR.position - upArmR.position).normalized * over;   // (never out of the hand's reach)
                }
            }

            // ---- Arms ----
            var right = T * Vector3.right;
            if (upB > 0.001f)
            {
                // Surrender: open hands up either side of the head, palms forward
                var top = head.position + up * 0.24f + torsoFwd * 0.06f;
                float sway = Mathf.Sin(Time.time * 3f) * 0.01f;
                SolveArm(upArmR, foreR, handR, top + right * 0.25f + up * sway, HandRotation(handBasisR, up, -right), -up + right * 0.9f, Ease(upB));
                SolveArm(upArmL, foreL, handL, top - right * 0.25f, HandRotation(handBasisL, up, right), -up - right * 0.9f, Ease(upB));
            }
            if (coverB > 0.001f)
            {
                // Cowering: hands clasped over the back of the head, elbows forward. Wrists at the back sides of the
                // skull (the head bone pivots at its base: the top is ~0.18 up), fingers wrapping in toward the middle,
                // palms pressed forward and down onto the head (the index side follows from the palm: right hand
                // palm = fingers x index, left hand index x fingers)
                var crown = head.position + up * 0.12f - torsoFwd * 0.07f;
                var palmOn = (torsoFwd * 0.55f - up * 0.85f).normalized;
                var fingR = (-right * 0.75f - torsoFwd * 0.3f + up * 0.1f).normalized;
                var fingL = (right * 0.75f - torsoFwd * 0.3f + up * 0.1f).normalized;
                SolveArm(upArmR, foreR, handR, crown + right * 0.1f, HandRotation(handBasisR, fingR, Vector3.Cross(palmOn, fingR)), torsoFwd + right * 0.3f, Ease(coverB));
                SolveArm(upArmL, foreL, handL, crown - right * 0.1f, HandRotation(handBasisL, fingL, Vector3.Cross(fingL, palmOn)), torsoFwd - right * 0.3f, Ease(coverB));
            }
            if (gunHands > 0.001f && Weapon != null && gripR != null)
            {
                var wf = Weapon.forward; var wu = Weapon.up; var wr = Weapon.right;
                bool pistol = Kind == HandWeapon.Pistol;
                // Firing hand: wrapped round the grip, the knuckles going forward and round its left side (square to
                // the grip's rake, so the wrist stays in line with the forearm), the index side up the grip
                var rFwd = (wf * 0.92f + wu * 0.08f - wr * 0.38f).normalized;
                var rRot = HandRotation(handBasisR, rFwd, wu - wf * 0.25f);
                var rTarget = gripR.position - rFwd * 0.07f + wr * 0.018f;
                // Support hand: a rifle handguard from below (palm up, fingers wrapping round), or wrapped over the pistol hand
                Vector3 lFwd, lSide;
                if (pistol) { lFwd = (wr * 0.72f + wf * 0.6f - wu * 0.05f).normalized; lSide = wu * 0.85f + wf * 0.25f; }
                else { lFwd = (wr * 0.75f + wu * 0.45f + wf * 0.2f).normalized; lSide = wf + wr * 0.1f; }
                var lRot = HandRotation(handBasisL, lFwd, lSide);
                var lTarget = (gripL != null ? gripL.position : gripR.position) - lFwd * 0.07f - wu * (pistol ? 0f : 0.02f);
                float reload = ReloadAway();
                if (reload > 0.001f)
                {
                    var pouch = hips.position + T * new Vector3(-0.13f, 0.12f, 0.14f);
                    lTarget = Vector3.Lerp(lTarget, pouch, reload);
                    lRot = Quaternion.Slerp(lRot, HandRotation(handBasisL, -up + torsoFwd * 0.3f, torsoFwd), reload);
                }
                float w = gunHands * (1f - upB);
                // Elbows: each person carries them their own way (flared or tucked), the support elbow lower
                SolveArm(upArmR, foreR, handR, rTarget, rRot, -up + right * (pistol ? style.flare : 0.85f) - torsoFwd * 0.1f, w);
                SolveArm(upArmL, foreL, handL, lTarget, lRot, -up - right * (pistol ? style.supportFlare : 0.3f) - torsoFwd * 0.05f, supportHand);
                // A locked wrist: turn each hand partway into line with its forearm (the hand pivots at the wrist)
                StraightenWrist(foreR, handR, rFwd, w * 0.15f);
                StraightenWrist(foreL, handL, lFwd, supportHand * 0.1f);
            }
            // Punches land where the aim is (a fighter's: the face in front of them): the library throws at a taller
            // opponent's head, over ours. Swing an extended arm down about its shoulder until the fist is on the aim
            // line (the clip's elbow and timing kept); the guard by the chin is left alone.
            if (fistB > 0.001f)
            {
                float fw = Ease(fistB) * motor.UpperWeight;
                var target = (upArmL.position + upArmR.position) * 0.5f + aim * 1.4f;   // (the face the aim points at, about arm's length and a bit)
                LowerFist(upArmR, handR, aim, target, fw);
                LowerFist(upArmL, handL, aim, target, fw);
            }
        }


        /// <summary>
        /// Put a punch on target: as the arm reaches out, turn it (about the shoulder, the clip's elbow kept) onto the
        /// line from its shoulder to the target, so it goes straight in, not off to the side; and never let a fist in
        /// front of the body rise over the aim line (bent-arm punches came up over the head).
        /// </summary>
        void LowerFist(Transform upper, Transform hand, Vector3 aim, Vector3 target, float w)
        {
            if (w <= 0.001f || upper == null || hand == null) return;
            var d = hand.position - upper.position;
            var flat = new Vector3(d.x, 0f, d.z);
            if (flat.sqrMagnitude < 0.0025f) return;
            var fwdFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float ext = Mathf.InverseLerp(0.08f, 0.25f, Vector3.Dot(d, fwdFlat));
            if (ext <= 0f) return;
            // Sideways: onto the shoulder-to-target line, more the further the arm reaches
            var toT = target - upper.position; toT.y = 0f;
            float reach = Mathf.InverseLerp(0.6f, 0.9f, d.magnitude / Mathf.Max(armLength, 0.1f));   // (only as it lands: earlier, a strong pull folded the arm across the chest)
            if (toT.sqrMagnitude > 0.01f && reach > 0f)
            {
                float yawErr = Mathf.Clamp(Vector3.SignedAngle(flat, toT, Vector3.up), -25f, 25f);
                upper.rotation = Quaternion.AngleAxis(yawErr * 0.7f * reach * w, Vector3.up) * upper.rotation;
                d = hand.position - upper.position;
                flat = new Vector3(d.x, 0f, d.z);
            }
            // Height: never over the aim line
            float elev = Mathf.Atan2(d.y, flat.magnitude) * Mathf.Rad2Deg;
            float aimElev = Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg;
            float over = elev - (aimElev + 3f);
            if (over <= 0f) return;
            var axis = Vector3.Cross(d, Vector3.up).normalized;   // (turning about it by + raises the arm)
            upper.rotation = Quaternion.AngleAxis(-over * w * ext, axis) * upper.rotation;
        }
        /// <summary>
        /// Each new punch (MeleeT restarting) throws the library's: jab and cross in turn. Returns
        /// the punch's progress 0..1 (fast out, a beat, back) for anything that wants to follow it.
        /// </summary>
        float PunchUpdate(float dt)
        {
            if (MeleeT < 0f || Kind != HandWeapon.None) { lastMelee = -1f; punchExt = Mathf.MoveTowards(punchExt, 0f, dt * 6f); return punchExt; }
            if (lastMelee < 0f || MeleeT < lastMelee - 0.2f)
            {
                punchSide = -punchSide;
                motor.Punch(punchSide > 0 ? 1 : 0);   // (straight punches: the hook swings in from the side)
            }
            lastMelee = MeleeT;
            float t = Mathf.Clamp01(MeleeT);
            punchExt = t < 0.2f ? 1f - (1f - t / 0.2f) * (1f - t / 0.2f) : t < 0.3f ? 1f : 1f - Ease((t - 0.3f) / 0.5f);
            return punchExt;
        }

        /// <summary>A hit: hands up or over the head come in for a moment (the arms recoil with the body), then go back.</summary>
        public void Interrupt(float seconds) => interruptUntil = Mathf.Max(interruptUntil, Time.time + seconds);
        float interruptUntil;

        /// <summary>How far the support hand is from the gun during a reload (0 on the gun, 1 at the pouch).</summary>
        float ReloadAway()
        {
            if (ReloadProgress < 0f) return 0f;
            float p = ReloadProgress;
            float away = p < 0.25f ? p / 0.25f : p > 0.8f ? (1f - p) / 0.2f : 1f;
            return Mathf.SmoothStep(0f, 1f, away);
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>
        /// Two-bone arm IK on the real bones: the elbow goes where the arm lengths and the pole (elbow direction) put
        /// it, the forearm reaches the target, the hand takes the grip rotation; blended with the animated pose by w.
        /// </summary>
        static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Quaternion handRot, Vector3 pole, float w)
        {
            if (w <= 0.001f || upper == null || lower == null || hand == null) return;
            var u0 = upper.rotation; var l0 = lower.rotation; var h0 = hand.rotation;
            var S = upper.position; var E = lower.position; var W = hand.position;
            float a = (E - S).magnitude, b = (W - E).magnitude;
            var to = target - S;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.002f);
            var dir = to.sqrMagnitude > 1e-6f ? to.normalized : (W - S).normalized;
            var perp = Vector3.ProjectOnPlane(pole, dir);
            perp = perp.sqrMagnitude > 1e-6f ? perp.normalized : Vector3.ProjectOnPlane(Vector3.down, dir).normalized;
            float ca = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            var elbow = S + dir * (a * ca) + perp * (a * Mathf.Sqrt(1f - ca * ca));
            upper.rotation = Quaternion.FromToRotation(E - S, elbow - S) * upper.rotation;
            E = lower.position; W = hand.position;
            lower.rotation = Quaternion.FromToRotation(W - E, S + dir * d - E) * lower.rotation;
            hand.rotation = handRot;
            if (w < 0.999f)
            {
                upper.rotation = Quaternion.Slerp(u0, upper.rotation, w);
                lower.rotation = Quaternion.Slerp(l0, lower.rotation, w);
                hand.rotation = Quaternion.Slerp(h0, hand.rotation, w);
            }
        }
    }
}
