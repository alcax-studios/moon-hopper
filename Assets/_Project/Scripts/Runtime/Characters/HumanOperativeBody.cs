using Alcax.AI;
using UnityEngine;

namespace Alcax.Characters
{
    /// <summary>
    /// The Operative body for animated humans: takes what the mind drives each frame (IOperativeBody) and hands it to
    /// the HumanMotor (follow drive: gait, tempo and hips matched to the agent's velocity; crouch; gaze) and the
    /// HumanWeaponRig (weapon pose from the aim, hands on the grips, holster, reload, bash, blind fire, lean, hands
    /// up; fists when unarmed). Hits flinch the spine on a spring; death is a humanoid ragdoll (HumanRagdoll).
    /// The same body serves guards, insurgents, and civilians (no weapon: fists).
    /// </summary>
    [RequireComponent(typeof(HumanMotor), typeof(HumanWeaponRig))]
    [DefaultExecutionOrder(5)]
    public class HumanOperativeBody : MonoBehaviour, IOperativeBody
    {
        public Vector3 Velocity { get; set; }
        public float Crouch { get; set; }
        public bool WeaponRaised { get; set; }
        public Vector3 AimDirection { get; set; } = Vector3.forward;
        public Vector3? LookPoint { get; set; }
        public bool Scanning { get; set; }
        public float ReloadProgress { get; set; } = -1f;
        public float MeleeT { get; set; } = -1f;
        public bool HandsUp { get; set; }
        public bool Holstered { get; set; }
        public float SideLean { get; set; }
        public float BlindFire { get; set; }
        public float BlindSide { get; set; }
        public bool Drawing => rig.Drawing;

        bool pistol;
        public bool Pistol
        {
            get => pistol;
            set { pistol = value; if (rig != null && rig.Weapon != null) rig.Kind = value ? HandWeapon.Pistol : HandWeapon.Rifle; }
        }

        public Transform Head => head != null ? head : transform;
        public Transform Muzzle => rig.Muzzle;
        public Transform Torso => torso != null ? torso : transform;

        HumanMotor motor;
        HumanWeaponRig rig;
        Animator anim;
        Transform head, torso;
        Vector3 flinch, flinchVel, push;
        Alcax.Combat.Health health;
        UnityEngine.AI.NavMeshAgent agent;
        float seed;
        bool frozen;

        void Awake()
        {
            motor = GetComponent<HumanMotor>();
            rig = GetComponent<HumanWeaponRig>();
            anim = GetComponent<Animator>();
            motor.drive = MotorDrive.Follow;
            // The mind reads the head and muzzle every frame, seen or not: always animate
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            torso = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
            seed = Random.value * 50f;
            if (rig.Weapon != null) rig.SetWeapon(rig.Weapon, rig.Kind == HandWeapon.None ? HandWeapon.Rifle : rig.Kind);
            agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            health = GetComponent<Alcax.Combat.Health>();
            if (health != null) health.Damaged += OnDamaged;
        }

        public void Flinch(Vector3 worldDir, float strength)
        {
            var local = transform.InverseTransformDirection(worldDir);
            // Hit from the front rocks the torso back; from the side, twists it
            flinchVel += new Vector3(-local.z * 160f, local.x * 120f, -local.x * 90f) * strength * 1.8f;   // (a real body shows it more than the mannequin did)
        }


        void OnDestroy() { if (health != null) health.Damaged -= OnDamaged; }

        /// <summary>
        /// A hit shows: an upper-body reaction clip (head snap for hits at head height, a knock-back for heavy ones,
        /// else the chest), and a push back a step along the hit (the pinned feet take a catch step).
        /// </summary>
        void OnDamaged(Alcax.Combat.DamageInfo d)
        {
            if (frozen || health == null || health.IsDead) return;
            bool atHead = d.Headshot || (head != null && d.Point.y > head.position.y - 0.06f);
            float s = Mathf.Clamp(d.Amount / 30f + d.Knockback * 0.05f, 0.3f, 1.5f);
            flinchVel += new Vector3(atHead ? -60f : 0f, 0f, 0f) * s;   // (a head snap on top)
            // The library's hit reaction, added on the upper body (additive layer: no squat), and the hands come in
            motor.HitReaction(d.Knockback > 6f ? 2 : atHead ? 1 : 0, s);
            rig.Interrupt(0.45f + 0.2f * s);
            var dir = d.Direction; dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f) push += dir.normalized * (0.6f + Mathf.Min(d.Knockback, 8f) * 0.12f) * s;
        }
        void Update()
        {
            if (frozen) return;
            float dt = Time.deltaTime;
            flinchVel += (-flinch * 220f - flinchVel * 18f) * dt;
            flinch += flinchVel * dt;
            // The push from a hit: a quick step back that dies out
            if (push.sqrMagnitude > 1e-4f)
            {
                if (agent != null && agent.enabled && agent.isOnNavMesh) agent.Move(push * dt);
                push = Vector3.MoveTowards(push, Vector3.zero, dt * 7f);
            }

            motor.Velocity = Velocity;
            motor.Crouch = Crouch;
            bool armed = rig.Weapon != null && rig.Kind != HandWeapon.None;
            rig.Raised = WeaponRaised;
            rig.Holstered = Holstered;
            rig.HandsUp = HandsUp;
            rig.Fists = !armed && WeaponRaised;
            rig.AimDirection = AimDirection;
            rig.ReloadProgress = ReloadProgress;
            rig.MeleeT = MeleeT;
            rig.BlindFire = BlindFire;
            rig.BlindSide = BlindSide;
            rig.SideLean = SideLean;
            rig.Flinch = flinch;

            // Gaze: what the mind points at, else down the gun when it's up, else a scan, else free (the motor's own)
            var eye = Head.position;
            Vector3? look = LookPoint;
            if (look == null && (WeaponRaised || BlindFire > 0.5f)) look = eye + AimDirection * 10f;
            if (look == null && Scanning)
            {
                float t = Time.time + seed;
                look = eye + Quaternion.Euler(Mathf.Sin(t * 0.37f) * 6f, Mathf.Sin(t * 0.29f) * 55f + Mathf.Sin(t * 0.81f) * 12f, 0f) * transform.forward * 10f;
            }
            motor.LookPoint = look;
            motor.WatchPlayer = !WeaponRaised && !Scanning && Holstered;
        }

        public Transform DropWeapon()
        {
            var w = rig.Weapon;
            if (w == null) return null;
            rig.SetWeapon(null, HandWeapon.None);
            w.SetParent(null, true);
            var col = w.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, pistol ? 0.05f : 0.4f);
            col.size = pistol ? new Vector3(0.04f, 0.12f, 0.2f) : new Vector3(0.07f, 0.15f, 0.85f);
            var rb = w.gameObject.AddComponent<Rigidbody>();
            rb.mass = 3.5f;
            rb.AddForce(transform.forward * 1.2f + Vector3.up * 0.8f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);
            return w;
        }

        public void Freeze()
        {
            frozen = true;
            motor.Kill();
            rig.enabled = false;
        }

        public void Ragdoll(Vector3 hitDir, Vector3 hitPoint, float impulse) => Alcax.Combat.HumanRagdoll.Go(gameObject, hitDir, hitPoint, impulse);
    }
}
