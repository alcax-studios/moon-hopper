using Alcax.Audio;
using Alcax.Combat;
using Alcax.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Alcax.Player
{
    /// <summary>
    /// First-person movement and look: CharacterController locomotion, mouse look,
    /// head bob, footsteps, landing dip, recoil, and the death camera drop.
    /// The Cinemachine camera hard-locks to <see cref="Head"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Health))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;
        [SerializeField] Transform head;

        [Header("Movement")]
        [SerializeField] float walkSpeed = 4.5f;
        [SerializeField] float sprintSpeed = 7f;
        [SerializeField] float acceleration = 35f;
        [SerializeField] float airControl = 0.35f;
        [SerializeField] float jumpHeight = 1.0f;
        [SerializeField] float gravity = -20f;
        [SerializeField] float crouchSpeedFactor = 0.5f;
        [SerializeField] float crouchHeight = 1.15f;
        [SerializeField] float crouchHeadY = 1.0f;

        [Header("Look")]
        [SerializeField] float lookSensitivity = 0.1f;
        [SerializeField] float pitchLimit = 85f;

        [Header("Head motion")]
        [SerializeField] float bobAmplitude = 0.045f;
        [SerializeField] float stepsPerSecondWalk = 1.8f;
        [SerializeField] float strafeRoll = 1.5f;
        [SerializeField] float landingDip = 0.12f;

        [Header("Debug drive (verification only)")]
        public bool debugOverride;
        public Vector2 debugMove;
        public Vector2 debugLook;
        public bool debugSprint;

        InputAction move, look, jump, sprint, crouch;
        PlayerVitals vitals;
        float standHeight, crouchBlend, sprintBob;
        CharacterController cc;
        Health health;

        float yaw, pitch;
        float verticalVelocity;
        float bobPhase, bobBlend;
        float landOffset, landVelocity;
        float recoilTarget, recoilCurrent;
        float roll;
        Vector3 shove;
        Vector3 headBase;
        bool dead;
        float deathT;
        int lastStepIndex;

        public Transform Head => head;
        public Vector3 PlanarVelocity { get; private set; }
        public float SpeedNormalized => PlanarVelocity.magnitude / walkSpeed;
        public Vector2 LookDelta { get; private set; }
        public Vector3 BobOffset { get; private set; }
        public bool Grounded => cc.isGrounded;
        public bool IsSprinting { get; private set; }
        /// <summary>Hold C or Left Ctrl. Lowers the head and capsule, slows movement, and quiets footsteps.</summary>
        public bool IsCrouching { get; private set; }
        /// <summary>Dev fly mode: no collision or gravity, move where you look (Space up, C down, Shift fast).</summary>
        public bool NoClip { get; private set; }

        public void SetNoClip(bool on)
        {
            NoClip = on;
            cc.enabled = !on;
            verticalVelocity = 0f;
            PlanarVelocity = Vector3.zero;
        }

        void UpdateNoClip(float dt, Vector2 mv)
        {
            var kb = Keyboard.current;
            var fwd = head.forward;
            var right = head.right;
            Vector3 wish = fwd * mv.y + right * mv.x;
            if (kb != null && kb.spaceKey.isPressed) wish += Vector3.up;
            if (kb != null && kb.cKey.isPressed) wish += Vector3.down;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            float speed = (kb != null && kb.leftShiftKey.isPressed) ? 28f : 10f;
            transform.position += wish * speed * dt;
        }
        public Health Health => health;

        /// <summary>Walking into loose physics objects (ragdolls, dropped guns, props) shoves them.</summary>
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var rb = hit.collider.attachedRigidbody;
            if (rb == null || rb.isKinematic || hit.moveDirection.y < -0.3f) return;
            var push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            rb.AddForceAtPosition(push * 500f, hit.point, ForceMode.Force);
        }

        /// <summary>Set by WeaponHolder while aiming down sights.</summary>
        public float AimSlowdown { get; set; } = 1f;
        public float AimSensitivity { get; set; } = 1f;
        public InputActionAsset Actions => actions;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            health = GetComponent<Health>();
            vitals = GetComponent<PlayerVitals>();
            if (vitals == null) vitals = gameObject.AddComponent<PlayerVitals>();
            var map = actions.FindActionMap("Player", true);
            move = map.FindAction("Move", true);
            look = map.FindAction("Look", true);
            jump = map.FindAction("Jump", true);
            sprint = map.FindAction("Sprint", true);
            crouch = map.FindAction("Crouch", false);
            standHeight = cc.height;
            headBase = head.localPosition;
            yaw = transform.eulerAngles.y;
            health.Died += OnDied;
            health.Damaged += OnDamaged;
            health.DamageBlocked = () => AdminSettings.GodMode;
        }

        void OnEnable()
        {
            actions.FindActionMap("Player", true).Enable();
            AdminSettings.FullHealRequested += FullHeal;
        }

        void OnDisable()
        {
            actions.FindActionMap("Player", true).Disable();
            AdminSettings.FullHealRequested -= FullHeal;
        }

        void FullHeal() => health.Heal(health.Max);

        bool InputLive => GameManager.IsPlaying && !dead;

        void Update()
        {
            float dt = Time.deltaTime;
            if (dead) { UpdateDeath(dt); return; }

            Vector2 mv = debugOverride ? debugMove : (InputLive ? move.ReadValue<Vector2>() : Vector2.zero);
            Vector2 lk = debugOverride ? debugLook : (InputLive && !AdminSettings.ConsoleOpen ? look.ReadValue<Vector2>() : Vector2.zero);
            LookDelta = lk;

            // Look
            float sens = lookSensitivity * GameSettings.LookSensitivity * AimSensitivity;
            yaw += lk.x * sens;
            pitch = Mathf.Clamp(pitch - lk.y * sens, -pitchLimit, pitchLimit);

            // Recoil: kick snaps in, then recovers.
            recoilCurrent = Mathf.Lerp(recoilCurrent, recoilTarget, 1f - Mathf.Exp(-recoilRise * dt));
            recoilTarget = Mathf.Lerp(recoilTarget, 0f, 1f - Mathf.Exp(-recoilRecover * dt));

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (NoClip) { UpdateNoClip(dt, mv); UpdateHeadMotion(dt, Vector2.zero); return; }

            // Move
            bool sprintHeld = debugOverride ? debugSprint : (InputLive && sprint.IsPressed());
            UpdateCrouch(dt);
            IsSprinting = sprintHeld && mv.y > 0.1f && !IsCrouching && (vitals == null || vitals.CanSprint);
            Vector3 wish = transform.right * mv.x + transform.forward * mv.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= (IsSprinting ? sprintSpeed : walkSpeed) * AimSlowdown * Mathf.Lerp(1f, crouchSpeedFactor, crouchBlend);
            float accel = cc.isGrounded ? acceleration : acceleration * airControl;
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, wish, accel * dt);

            bool wasGrounded = cc.isGrounded;
            if (wasGrounded)
            {
                if (verticalVelocity < 0f) verticalVelocity = -2f;
                if (InputLive && !debugOverride && jump.WasPressedThisFrame())
                {
                    verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);
                    vitals?.OnJump();
                }
            }
            float fallSpeed = verticalVelocity;
            verticalVelocity += gravity * dt;
            cc.Move((PlanarVelocity + shove + Vector3.up * verticalVelocity) * dt);
            shove = Vector3.Lerp(shove, Vector3.zero, 1f - Mathf.Exp(-8f * dt));

            if (!wasGrounded && cc.isGrounded && fallSpeed < -5f)
            {
                landVelocity -= Mathf.Clamp01(-fallSpeed / 15f) * landingDip * 12f;
                SoundManager.PlayAt(Sfx.Footstep, transform.position, 0.9f);
            }

            UpdateHeadMotion(dt, mv);
        }

        void UpdateCrouch(float dt)
        {
            var kb = Keyboard.current;
#if UNITY_EDITOR
            // In the Editor, Ctrl+S (crouch + walk back) triggers Unity's Save shortcut, so crouch is C only here
            bool ctrl = false;
#else
            bool ctrl = kb != null && kb.leftCtrlKey.isPressed;
#endif
            bool want = InputLive && !debugOverride && ((crouch != null && crouch.IsPressed()) || ctrl);
            if (!want && IsCrouching)
            {
                // Only stand where there is headroom
                var bottom = transform.position + Vector3.up * (cc.radius + 0.05f);
                want = Physics.SphereCast(bottom, cc.radius * 0.9f, Vector3.up, out _, standHeight - cc.radius, ~(1 << 2), QueryTriggerInteraction.Ignore);
            }
            IsCrouching = want;
            crouchBlend = Mathf.MoveTowards(crouchBlend, IsCrouching ? 1f : 0f, dt / 0.18f);
            float e = crouchBlend * crouchBlend * (3f - 2f * crouchBlend);
            cc.height = Mathf.Lerp(standHeight, crouchHeight, e);
            cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
        }

        void UpdateHeadMotion(float dt, Vector2 mv)
        {
            float speedN = Mathf.Clamp(PlanarVelocity.magnitude / walkSpeed, 0f, 1.6f);
            float targetBlend = cc.isGrounded && speedN > 0.1f ? 1f : 0f;
            bobBlend = Mathf.MoveTowards(bobBlend, targetBlend, dt / 0.3f);

            // Sprint changes the bob gradually (no jump in size or pace when starting or stopping a run)
            sprintBob = Mathf.MoveTowards(sprintBob, IsSprinting ? 1f : 0f, dt / 0.3f);
            float stepRate = stepsPerSecondWalk * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(speedN)) * Mathf.Lerp(1f, 1.25f, sprintBob);
            bobPhase += dt * stepRate * Mathf.PI * Mathf.Max(speedN, 0.2f) * (bobBlend > 0f ? 1f : 0f);

            // Each half-cycle of the phase is one footstep.
            int stepIndex = Mathf.FloorToInt(bobPhase / Mathf.PI);
            if (stepIndex != lastStepIndex)
            {
                lastStepIndex = stepIndex;
                if (bobBlend > 0.5f)
                    SoundManager.PlayAt(Sfx.Footstep, transform.position, IsSprinting ? 0.55f : 0.35f, 0.12f);
            }

            float amp = bobAmplitude * Mathf.Clamp01(speedN) * bobBlend * Mathf.Lerp(1f, 1.4f, sprintBob);
            float bobY = -Mathf.Abs(Mathf.Sin(bobPhase)) * amp * 1.4f + amp * 0.7f;
            float bobX = Mathf.Cos(bobPhase) * amp * 0.6f;
            float breathe = Mathf.Sin(Time.time * Mathf.PI * 2f * 0.25f) * 0.006f * (1f - bobBlend);

            // Landing dip: a damped spring.
            landVelocity += (-landOffset * 120f - landVelocity * 14f) * dt;
            landOffset += landVelocity * dt;

            BobOffset = new Vector3(bobX, bobY + breathe, 0f);
            float ce = crouchBlend * crouchBlend * (3f - 2f * crouchBlend);
            head.localPosition = headBase + BobOffset + Vector3.up * (landOffset + (crouchHeadY - headBase.y) * ce);

            roll = Mathf.Lerp(roll, -mv.x * strafeRoll, 1f - Mathf.Exp(-8f * dt));
            head.localRotation = Quaternion.Euler(pitch - recoilCurrent, 0f, roll);
        }

        /// <summary>
        /// Kick the view up. rise: how fast the view follows the kick; recover: how fast it settles back.
        /// The defaults are the pistol and shotgun feel; automatic weapons pass gentler values for a smooth climb and recovery.
        /// </summary>
        public void AddRecoil(float degrees, float recover = 7f, float rise = 30f)
        {
            recoilTarget += degrees;
            recoilRecover = recover;
            recoilRise = rise;
        }
        float recoilRecover = 7f, recoilRise = 30f;

        void OnDamaged(DamageInfo info)
        {
            SoundManager.Play2D(Sfx.PlayerHurt, 0.8f);
            FeelManager.Shake(0.35f + info.Amount * 0.015f);
            Vector3 d = info.Direction;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) shove += d.normalized * info.Knockback;
        }

        void OnDied()
        {
            dead = true;
            deathT = 0f;
            PlanarVelocity = Vector3.zero;
            SoundManager.Play2D(Sfx.DeathSting, 0.9f, 0f);
            if (GameManager.Instance != null) GameManager.Instance.EndRun();
        }

        void UpdateDeath(float dt)
        {
            // Collapse: the camera drops to the ground and rolls onto its side.
            deathT = Mathf.Min(1f, deathT + dt / 1.1f);
            float e = Ease.OutCubic(deathT);
            head.localPosition = Vector3.Lerp(head.localPosition, new Vector3(0f, 0.25f, 0f), e * 0.2f);
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(-15f, 0f, 70f), e * 0.15f);
            if (!cc.isGrounded) cc.Move(Vector3.up * gravity * 0.5f * dt);
        }
    }
}
