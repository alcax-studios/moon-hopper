using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Dev tool: holds an operative's body in one state so it can be looked at and tuned. Pauses the mind (Operative)
    /// and the agent, then drives IOperativeBody directly: aim at a point (or the player), raise or lower the weapon,
    /// holster, reload, melee, hands up, crouch, lean, blind fire, and walk in a direction relative to the aim
    /// (strafe, backpedal) by moving the root itself. Remove it to give the body back to the mind.
    /// </summary>
    public class BodyPoseTester : MonoBehaviour
    {
        public enum Pose { Holstered, Low, Raised, Reload, Melee, HandsUp, Crouch, CrouchRaised, LeanRight, BlindOver }
        public Pose pose = Pose.Raised;
        /// <summary>World point to aim at (zero: two metres ahead of the player, or straight ahead).</summary>
        public Vector3 aimPoint;
        /// <summary>Walk velocity in the aim's frame (x right, z forward), m/s.</summary>
        public Vector3 move;
        public bool unarmed;

        IOperativeBody body;
        Operative op;
        UnityEngine.AI.NavMeshAgent agent;
        float t0;
        Vector3 facing;

        void OnEnable()
        {
            body = GetComponent<IOperativeBody>();
            op = GetComponent<Operative>();
            agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (op != null) op.enabled = false;
            if (agent != null) agent.enabled = false;
            if (unarmed) body.DropWeapon();
            t0 = Time.time;
            facing = transform.forward;
        }

        void OnDisable()
        {
            if (agent != null) agent.enabled = true;
            if (op != null) op.enabled = true;
        }

        void Update()
        {
            if (body == null) return;
            var head = body.Head.position;
            var target = aimPoint;
            if (target == Vector3.zero)
            {
                var pc = FindAnyObjectByType<Alcax.Player.PlayerController>();
                target = pc != null ? pc.transform.position + Vector3.up * 1.5f : head + facing * 5f;
            }
            var aim = (target - head).normalized;
            var flat = aim; flat.y = 0f;
            if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 360f * Time.deltaTime);
            // Move relative to the aim (strafing keeps facing the target)
            var v = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) * move : Vector3.zero;
            transform.position += v * Time.deltaTime;
            body.Velocity = v;
            body.AimDirection = aim;
            body.LookPoint = null;
            body.Scanning = false;
            body.Holstered = pose == Pose.Holstered;
            body.WeaponRaised = pose == Pose.Raised || pose == Pose.Reload || pose == Pose.Melee || pose == Pose.CrouchRaised || pose == Pose.LeanRight || pose == Pose.BlindOver;
            body.Crouch = pose == Pose.Crouch || pose == Pose.CrouchRaised || pose == Pose.BlindOver ? 1f : 0f;
            body.HandsUp = pose == Pose.HandsUp;
            body.SideLean = pose == Pose.LeanRight ? 1f : 0f;
            body.BlindFire = pose == Pose.BlindOver ? 1f : 0f;
            body.BlindSide = 0f;
            float t = Time.time - t0;
            body.ReloadProgress = pose == Pose.Reload ? (t % 2.4f) / 2.4f : -1f;
            float melee = (t % 0.9f) / 0.45f;   // a strike every 0.9 s, each 0.45 s long
            body.MeleeT = pose == Pose.Melee && melee <= 1f ? melee : -1f;
        }
    }
}
