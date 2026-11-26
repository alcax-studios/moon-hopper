using UnityEngine;

namespace Alcax.AI
{
    /// <summary>
    /// Human aiming, not an aimbot. The weapon direction is a critically damped follow in yaw and pitch
    /// with a capped turn speed, chasing a target point that is:
    ///  - perceived late (visual latency), which skilled shooters partly cancel by leading,
    ///  - offset by a slow, wandering error that grows with stress, own movement, and target speed,
    ///  - wide right after acquiring a target, then settling as they steady.
    /// Recoil from the weapon adds on top and is dragged back by skill. No deliberate misses: when the
    /// error is small, they hit.
    /// </summary>
    public class OperativeAim
    {
        float yaw, pitch, yawVel, pitchVel;
        readonly float noiseSeedA, noiseSeedB;
        float acquiredAt = -100f;

        public float ErrorDegrees { get; private set; }

        public OperativeAim(Vector3 forward)
        {
            Snap(forward);
            noiseSeedA = Random.value * 100f;
            noiseSeedB = Random.value * 100f;
        }

        public void Snap(Vector3 dir)
        {
            var e = Quaternion.LookRotation(dir).eulerAngles;
            yaw = e.y; pitch = Mathf.DeltaAngle(0f, e.x);
            yawVel = pitchVel = 0f;
        }

        /// <summary>Call when a new target is picked up (sight regained): the aim starts wide and settles.</summary>
        public void Acquire() => acquiredAt = Time.time;

        public Vector3 Direction(Vector2 kick) => Quaternion.Euler(pitch - kick.x, yaw + kick.y, 0f) * Vector3.forward;

        /// <summary>The direction without recoil (where the shooter is trying to hold).</summary>
        public Vector3 Held => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        /// <param name="errorDeg">Error radius from the shooter's state (skill, stress, movement).</param>
        public void Track(Vector3 origin, Vector3 targetPoint, float errorDeg, float skill, float dt)
        {
            float settle = Mathf.Lerp(2.2f, 1f, Mathf.Clamp01((Time.time - acquiredAt) / Mathf.Lerp(0.9f, 0.4f, skill)));
            ErrorDegrees = errorDeg * settle;
            float t = Time.time * Mathf.Lerp(0.7f, 1.1f, skill);
            float ey = (Mathf.PerlinNoise(noiseSeedA, t) - 0.5f) * 2f * ErrorDegrees;
            float ep = (Mathf.PerlinNoise(noiseSeedB, t * 1.13f) - 0.5f) * 2f * ErrorDegrees * 0.8f;

            var to = targetPoint - origin;
            if (to.sqrMagnitude < 0.0001f) return;
            var e = Quaternion.LookRotation(to).eulerAngles;
            float ty = e.y + ey, tp = Mathf.DeltaAngle(0f, e.x) + ep;

            // Critically damped (SmoothDamp is the stable closed form) with a capped turn speed
            float smooth = Mathf.Lerp(0.3f, 0.13f, skill);
            float maxSpeed = Mathf.Lerp(200f, 520f, skill);
            yaw = Mathf.SmoothDampAngle(yaw, ty, ref yawVel, smooth, maxSpeed, dt);
            pitch = Mathf.Clamp(Mathf.SmoothDamp(pitch, tp, ref pitchVel, smooth, maxSpeed, dt), -80f, 80f);
        }

        /// <summary>Relax toward a direction (low ready, scanning) without the error model.</summary>
        public void Relax(Vector3 dir, float dt, float rate = 5f)
        {
            var e = Quaternion.LookRotation(dir).eulerAngles;
            yaw = Mathf.LerpAngle(yaw, e.y, 1f - Mathf.Exp(-rate * dt));
            pitch = Mathf.Lerp(pitch, Mathf.DeltaAngle(0f, e.x), 1f - Mathf.Exp(-rate * dt));
            yawVel = pitchVel = 0f;
        }
    }
}
