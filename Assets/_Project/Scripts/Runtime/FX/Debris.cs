using Alcax.Audio;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>A short-lived physics prop (dropped magazine, casing, or shell) that clinks on impact, then removes itself.</summary>
    public class Debris : MonoBehaviour
    {
        Sfx impact;
        int bounces;
        float nextSound;

        public void Init(Sfx impactSound, float life)
        {
            impact = impactSound;
            Destroy(gameObject, life);
        }

        void OnCollisionEnter(Collision c)
        {
            if (bounces >= 3 || Time.time < nextSound) return;
            float speed = c.relativeVelocity.magnitude;
            if (speed < 0.6f) return;
            bounces++;
            nextSound = Time.time + 0.08f;
            SoundManager.PlayAt(impact, transform.position, Mathf.Clamp01(speed / 4f) * (bounces == 1 ? 0.8f : 0.45f), 0.15f);
        }
    }
}
