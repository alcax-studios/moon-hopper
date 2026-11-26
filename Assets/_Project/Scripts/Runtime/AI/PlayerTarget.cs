using Alcax.Combat;
using Alcax.Player;
using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// What the combat AI can know about the player: body points to look for, how visible they are,
    /// and the noises they make. It also lets enemy bullets hit the player's capsule, since the player
    /// sits on the Ignore Raycast layer.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerTarget : MonoBehaviour, ICombatTarget
    {
        public string DisplayName => "Player";
        public static PlayerTarget Instance { get; private set; }

        PlayerController controller;
        WeaponHolder weapons;
        CharacterController cc;
        Flashlight flashlight;
        float stepTimer;
        bool wasReloading;

        public Health Health => controller.Health;
        public bool IsDead => controller.Health.IsDead;
        /// <summary>Flying in no-clip: not a target (dev tool).</summary>
        public bool NoClipping => controller.NoClip;
        public Vector3 Position => transform.position;
        public Vector3 Velocity => controller.PlanarVelocity;
        public Transform Head => controller.Head;
        public Vector3 HeadPosition => controller.Head.position;
        public Vector3 Forward => controller.Head.forward;
        public bool IsCrouching => controller.IsCrouching;
        public bool IsSprinting => controller.IsSprinting;
        public bool IsReloading => weapons != null && weapons.Reloading;
        public bool FlashlightOn => flashlight != null && flashlight.IsOn;
        public float LastShotTime { get; private set; } = -100f;
        /// <summary>Recent near misses and hits on the player (0 to 1); Phase 2 uses it for player suppression.</summary>
        public float Pressure { get; private set; }

        void Awake()
        {
            Instance = this;
            controller = GetComponent<PlayerController>();
            weapons = GetComponent<WeaponHolder>();
            cc = GetComponent<CharacterController>();
            flashlight = GetComponent<Flashlight>();
            if (weapons != null) weapons.Fired += OnFired;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (weapons != null) weapons.Fired -= OnFired;
        }

        void OnFired(bool melee)
        {
            if (melee) { Hearing.Emit(Position, 9f, NoiseKind.Melee, gameObject); return; }
            LastShotTime = Time.time;
            Hearing.Emit(HeadPosition, 90f, NoiseKind.Gunshot, gameObject);

            // Rounds cracking past an operative suppress them (fear, stress, and a rough fix on the shooter)
            var from = HeadPosition;
            var dir = Forward;
            float len = Physics.Raycast(from, dir, out var hit, 150f, AILayers.World, QueryTriggerInteraction.Ignore) ? hit.distance : 150f;
            var to = from + dir * len;
            foreach (var op in Operative.All)
            {
                if (op.IsDead) continue;
                var chest = op.ChestPosition;
                var seg = to - from;
                float t = Mathf.Clamp01(Vector3.Dot(chest - from, seg) / seg.sqrMagnitude);
                float miss = Vector3.Distance(from + seg * t, chest);
                if (miss < 2.5f && t > 0.02f) op.OnNearMiss(1f - miss / 2.5f, from, this);
            }
        }

        void Update()
        {
            Pressure = Mathf.MoveTowards(Pressure, 0f, Time.deltaTime * 0.35f);

            // Footsteps: sprinting carries far, walking a little, crouch-walking almost nothing
            float speed = Velocity.magnitude;
            if (controller.Grounded && speed > 0.6f)
            {
                stepTimer -= Time.deltaTime * speed;
                if (stepTimer <= 0f)
                {
                    stepTimer = 1.6f;
                    float radius = IsSprinting ? 16f : IsCrouching ? 2.5f : 7f;
                    Hearing.Emit(Position, radius, NoiseKind.Footstep, gameObject);
                }
            }
            if (IsReloading && !wasReloading) Hearing.Emit(Position + Vector3.up, 8f, NoiseKind.Reload, gameObject);
            wasReloading = IsReloading;
        }

        /// <summary>The points vision checks: head, chest, hips (lower when crouched).</summary>
        public void GetBodyPoints(Vector3[] points)
        {
            float h = cc.height;
            var p = transform.position;
            points[0] = HeadPosition;
            points[1] = p + Vector3.up * (h * 0.7f);
            points[2] = p + Vector3.up * (h * 0.45f);
        }

        /// <summary>A point to aim at: 0 hips, 1 chest, 2 head.</summary>
        public Vector3 AimPoint(int part)
        {
            float h = cc.height;
            return part switch
            {
                2 => HeadPosition + Vector3.down * 0.05f,
                1 => transform.position + Vector3.up * (h * 0.68f),
                _ => transform.position + Vector3.up * (h * 0.48f),
            };
        }

        /// <summary>Ray against the player's capsule. Returns the hit distance and whether it struck the head.</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out float dist, out bool head) => Raycast(origin, dir, maxDist, out dist, out head, out _);

        /// <summary>Ray against the player's capsule, with the damage multiplier for where it struck (head 1.8, body 1, legs 0.7).</summary>
        public bool Raycast(Vector3 origin, Vector3 dir, float maxDist, out float dist, out bool head, out float zone)
        {
            dist = 0f; head = false; zone = 1f;
            if (NoClipping) return false;
            if (IsDead) return false;
            float r = cc.radius + 0.02f;
            var a = transform.position + Vector3.up * r;
            var b = transform.position + Vector3.up * (cc.height - r);
            if (!RayCapsule(origin, dir, a, b, r, out dist) || dist > maxDist) return false;
            var hitPoint = origin + dir * dist;
            head = hitPoint.y > HeadPosition.y - 0.16f;
            bool legs = hitPoint.y < transform.position.y + cc.height * 0.45f;
            zone = head ? 1.8f : legs ? 0.7f : 1f;
            return true;
        }

        /// <summary>Closest distance from the head to a bullet's path (for near-miss cracks).</summary>
        public float MissDistance(Vector3 from, Vector3 to, out Vector3 closest)
        {
            var seg = to - from;
            float t = Mathf.Clamp01(Vector3.Dot(HeadPosition - from, seg) / Mathf.Max(0.0001f, seg.sqrMagnitude));
            closest = from + seg * t;
            return Vector3.Distance(closest, HeadPosition);
        }

        public void AddPressure(float amount) => Pressure = Mathf.Clamp01(Pressure + amount);

        static bool RayCapsule(Vector3 ro, Vector3 rd, Vector3 pa, Vector3 pb, float r, out float t)
        {
            // Inigo Quilez's ray-capsule intersection
            t = 0f;
            var ba = pb - pa; var oa = ro - pa;
            float baba = Vector3.Dot(ba, ba), bard = Vector3.Dot(ba, rd), baoa = Vector3.Dot(ba, oa);
            float rdoa = Vector3.Dot(rd, oa), oaoa = Vector3.Dot(oa, oa);
            float a = baba - bard * bard;
            float b = baba * rdoa - baoa * bard;
            float c = baba * oaoa - baoa * baoa - r * r * baba;
            float h = b * b - a * c;
            if (h >= 0f && a > 1e-6f)
            {
                t = (-b - Mathf.Sqrt(h)) / a;
                float y = baoa + t * bard;
                if (y > 0f && y < baba && t > 0f) return true;
                var oc = y <= 0f ? oa : ro - pb;
                b = Vector3.Dot(rd, oc);
                c = Vector3.Dot(oc, oc) - r * r;
                h = b * b - c;
                if (h > 0f) { t = -b - Mathf.Sqrt(h); return t > 0f; }
            }
            return false;
        }
    }
}
