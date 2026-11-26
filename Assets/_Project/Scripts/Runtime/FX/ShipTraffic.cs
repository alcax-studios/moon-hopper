using System.Collections;
using Alcax.Audio;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// Flies a shuttle through a loop: approach from the horizon, hover over its pad, touch down
    /// (engines flaring, dust kicking up), wait, lift off, and climb away. Then it rests off-screen and repeats.
    /// The engine light, exhaust, and rumble all follow the thrust.
    /// </summary>
    public class ShipTraffic : MonoBehaviour
    {
        [SerializeField] Transform pad;
        [SerializeField] Light engineLight;
        [SerializeField] ParticleSystem exhaust;
        [SerializeField] ParticleSystem padDust;
        [SerializeField] Vector3 approachFrom = new Vector3(-0.7f, 0.35f, 1f);
        [SerializeField] Vector3 departTo = new Vector3(0.6f, 0.5f, 1f);
        [SerializeField] float startDelay = 3f;
        [SerializeField] float groundTime = 18f;
        [SerializeField] Vector2 restTime = new Vector2(10f, 22f);
        [SerializeField] float engineLightMax = 9f;
        [Tooltip("Height of the ship pivot above the pad when landed: the landing gear length (feet sit on the pad).")]
        [SerializeField] float landingHeight = 2.71f;

        AudioSource engine;
        AudioLowPassFilter glassFilter;
        float thrust;
        float heading;

        void Awake()
        {
            engine = gameObject.AddComponent<AudioSource>();
            engine.loop = true;
            engine.spatialBlend = 1f;
            engine.rolloffMode = AudioRolloffMode.Linear;
            engine.minDistance = 20f;
            engine.maxDistance = 700f;
            engine.dopplerLevel = 0f;
            engine.volume = 0f;
            glassFilter = gameObject.AddComponent<AudioLowPassFilter>();
            glassFilter.cutoffFrequency = 900f;   // heard through the glass
        }

        void Start()
        {
            engine.clip = SoundManager.GetClip(Sfx.ShipEngine);
            if (engine.clip != null) engine.Play();
            SetVisible(false);
            StartCoroutine(Loop());
        }

        void Update()
        {
            if (engineLight != null) engineLight.intensity = engineLightMax * thrust;
            if (exhaust != null)
            {
                var em = exhaust.emission;
                em.rateOverTime = 120f * thrust;
            }
            // Outside sound reaches the station only through the atrium window: when you leave the atrium it is
            // contained like the atrium ambience (quieter through the open hall door, silent once it closes)
            if (engine != null) engine.volume = Mathf.Clamp01(thrust) * 0.11f * SoundManager.SfxGain * RoomAmbience.Containment;   // 80% quieter than the first pass (was 0.55)
            if (glassFilter != null) glassFilter.cutoffFrequency = Mathf.Min(900f, RoomAmbience.ContainmentCutoff);
        }

        void SetVisible(bool on)
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) r.enabled = on;
            if (!on) thrust = 0f;
        }

        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        IEnumerator Loop()
        {
            yield return new WaitForSeconds(startDelay);
            Vector3 padTop = pad.position + Vector3.up * landingHeight;
            Vector3 hover = padTop + Vector3.up * 22f;
            float padYaw = pad.eulerAngles.y;

            while (true)
            {
                // Approach
                Vector3 from = padTop + approachFrom.normalized * 1400f;
                Vector3 ctrl = hover + new Vector3(approachFrom.x, 0f, approachFrom.z).normalized * 260f + Vector3.up * 90f;
                SetVisible(true);
                yield return Fly(from, ctrl, hover, 16f, 0.35f, 0.7f, easeIn: false);

                // Settle and descend
                yield return Move(hover, padTop, 7f, 0.75f, 1f, padYaw, dustFrom: 0.5f);
                thrust = 0.15f;
                if (padDust != null) padDust.Stop();
                yield return new WaitForSeconds(1.5f);
                thrust = 0f;

                yield return new WaitForSeconds(groundTime);

                // Lift off
                if (padDust != null) padDust.Play();
                yield return Move(padTop, hover, 6f, 1f, 0.9f, padYaw, dustFrom: 0f, easeOut: false);
                if (padDust != null) padDust.Stop();

                // Depart
                Vector3 to = padTop + departTo.normalized * 1600f;
                Vector3 c2 = hover + Vector3.up * 160f + new Vector3(departTo.x, 0f, departTo.z).normalized * 120f;
                yield return Fly(hover, c2, to, 15f, 0.9f, 1f, easeIn: true);

                SetVisible(false);
                yield return new WaitForSeconds(Random.Range(restTime.x, restTime.y));
            }
        }

        IEnumerator Fly(Vector3 a, Vector3 ctrl, Vector3 b, float duration, float thrustA, float thrustB, bool easeIn)
        {
            float t = 0f;
            Vector3 prev = a;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                float k = easeIn ? t * t : 1f - (1f - t) * (1f - t);
                var p = Bezier(a, ctrl, b, Mathf.Clamp01(k));
                var v = p - prev;
                prev = p;
                transform.position = p;
                if (v.sqrMagnitude > 1e-6f)
                {
                    float targetYaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    float yawDelta = Mathf.DeltaAngle(heading, targetYaw);
                    heading = Mathf.LerpAngle(heading, targetYaw, 1f - Mathf.Exp(-3f * Time.deltaTime));
                    float pitch = -Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg * 0.5f;
                    float bank = Mathf.Clamp(-yawDelta * 1.5f, -30f, 30f);
                    transform.rotation = Quaternion.Euler(pitch, heading, bank);
                }
                thrust = Mathf.Lerp(thrustA, thrustB, t);
                yield return null;
            }
        }

        IEnumerator Move(Vector3 a, Vector3 b, float duration, float thrustA, float thrustB, float targetYaw, float dustFrom, bool easeOut = true)
        {
            float t = 0f;
            float startYaw = heading;
            bool dustOn = false;
            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                float k = easeOut ? 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f) : Mathf.Pow(Mathf.Clamp01(t), 2f);
                transform.position = Vector3.Lerp(a, b, k) + Vector3.up * Mathf.Sin(Time.time * 1.3f) * 0.15f * (1f - Mathf.Abs(2f * t - 1f));
                heading = Mathf.LerpAngle(startYaw, targetYaw, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t * 1.5f)));
                float wobble = Mathf.Sin(Time.time * 0.9f) * 1.2f;
                transform.rotation = Quaternion.Euler(wobble, heading, Mathf.Sin(Time.time * 0.7f) * 1.5f);
                thrust = Mathf.Lerp(thrustA, thrustB, t);
                if (!dustOn && dustFrom > 0f && t >= dustFrom && padDust != null) { padDust.Play(); dustOn = true; }
                yield return null;
            }
            transform.position = b;
        }
    }
}
