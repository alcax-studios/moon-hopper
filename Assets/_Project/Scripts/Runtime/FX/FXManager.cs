using System.Collections;
using Alcax.Core;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// Pooled event effects. One ParticleSystem per effect type, fed through Emit(),
    /// plus a small pool of tracer LineRenderers and one muzzle-flash light.
    /// </summary>
    public class FXManager : MonoBehaviour
    {
        public static FXManager Instance { get; private set; }
        public static Material ParticleMaterial => Instance != null ? Instance.particleMaterial : null;

        [SerializeField] Material particleMaterial;

        ParticleSystem blood, dust, sparks, pickup;
        LineRenderer[] tracers;
        int nextTracer;
        Light muzzleLight;
        Coroutine muzzleRoutine;

        void Awake()
        {
            Instance = this;
            blood = MakeSystem("FX_Blood", 1.6f);
            dust = MakeSystem("FX_Dust", 0.4f);
            sparks = MakeSystem("FX_Sparks", 0f);
            pickup = MakeSystem("FX_Pickup", 0.6f);

            tracers = new LineRenderer[8];
            for (int i = 0; i < tracers.Length; i++)
            {
                var go = new GameObject("Tracer" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = particleMaterial;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.03f;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                tracers[i] = lr;
            }

            var lightGo = new GameObject("MuzzleLight");
            lightGo.transform.SetParent(transform, false);
            muzzleLight = lightGo.AddComponent<Light>();
            muzzleLight.type = LightType.Point;
            muzzleLight.color = Palette.AccentHot;
            muzzleLight.range = 9f;
            muzzleLight.intensity = 0f;
            muzzleLight.shadows = LightShadows.None;
            muzzleLight.enabled = false;
        }

        ParticleSystem MakeSystem(string name, float gravity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.gravityModifier = gravity;
            main.startColor = Palette.UIBone;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = particleMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Burst(ParticleSystem ps, Vector3 pos, Vector3 dir, int count, float speedMin, float speedMax,
            float spread, float sizeMin, float sizeMax, float lifeMin, float lifeMax, Color a, Color b)
        {
            if (ps == null) return;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                var d = Vector3.Slerp(dir.normalized, Random.onUnitSphere, spread).normalized;
                p.position = pos + Random.insideUnitSphere * 0.05f;
                p.velocity = d * Random.Range(speedMin, speedMax);
                p.startSize = Random.Range(sizeMin, sizeMax);
                p.startLifetime = Random.Range(lifeMin, lifeMax);
                p.startColor = Color.Lerp(a, b, Random.value);
                ps.Emit(p, 1);
            }
        }

        public static void Blood(Vector3 pos, Vector3 dir, int count = 14)
        {
            if (Instance == null) return;
            Burst(Instance.blood, pos, dir + Vector3.up * 0.4f, count, 1.5f, 4.5f, 0.45f, 0.06f, 0.16f, 0.35f, 0.7f,
                Palette.Blood, Palette.BloodBright);
        }

        public static void Dust(Vector3 pos, Vector3 normal, int count = 8)
        {
            if (Instance == null) return;
            Burst(Instance.dust, pos, normal, count, 0.8f, 2.2f, 0.6f, 0.08f, 0.22f, 0.3f, 0.6f,
                Palette.StoneDark, Palette.Ground);
        }

        public static void Sparks(Vector3 pos, Vector3 dir, int count = 6)
        {
            if (Instance == null) return;
            Burst(Instance.sparks, pos, dir, count, 3f, 7f, 0.25f, 0.03f, 0.07f, 0.05f, 0.12f,
                Palette.AccentHot, Palette.Accent);
        }

        public static void PickupBurst(Vector3 pos)
        {
            if (Instance == null) return;
            Burst(Instance.pickup, pos, Vector3.up, 12, 2f, 3f, 0.9f, 0.05f, 0.11f, 0.4f, 0.7f,
                Palette.Accent, Palette.AccentHot);
        }

        public static void Tracer(Vector3 from, Vector3 to)
        {
            if (Instance == null) return;
            var lr = Instance.tracers[Instance.nextTracer];
            Instance.nextTracer = (Instance.nextTracer + 1) % Instance.tracers.Length;
            Instance.StartCoroutine(Instance.TracerRoutine(lr, from, to));
        }

        IEnumerator TracerRoutine(LineRenderer lr, Vector3 from, Vector3 to)
        {
            lr.SetPosition(0, from);
            lr.SetPosition(1, to);
            lr.enabled = true;
            float t = 0f;
            const float life = 0.07f;
            while (t < life)
            {
                float k = 1f - t / life;
                lr.startColor = Palette.WithAlpha(Palette.AccentHot, k);
                lr.endColor = Palette.WithAlpha(Palette.Accent, k * 0.4f);
                t += Time.deltaTime;
                yield return null;
            }
            lr.enabled = false;
        }

        public static void MuzzleFlash(Vector3 pos, Vector3 dir, float strength)
        {
            if (Instance == null) return;
            Sparks(pos, dir, Mathf.RoundToInt(5 * strength));
            if (Instance.muzzleRoutine != null) Instance.StopCoroutine(Instance.muzzleRoutine);
            Instance.muzzleRoutine = Instance.StartCoroutine(Instance.MuzzleRoutine(pos, strength));
        }

        IEnumerator MuzzleRoutine(Vector3 pos, float strength)
        {
            muzzleLight.transform.position = pos;
            muzzleLight.enabled = true;
            float t = 0f;
            const float life = 0.06f;
            while (t < life)
            {
                muzzleLight.intensity = 6f * strength * (1f - t / life);
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            muzzleLight.enabled = false;
            muzzleRoutine = null;
        }
    }
}
