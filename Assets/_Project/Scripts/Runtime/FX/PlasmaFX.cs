using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// Pooled plasma effects, layered per the art bible: a hot core flash, plasma splash, sparks that bounce off the
    /// world, lit smoke, glowing embers, a short real light, and a scorch decal whose core cools. Also the shared
    /// smoke for vent steam and the sparks a bolt sheds. Created on first use in the current scene.
    /// </summary>
    public class PlasmaFX : MonoBehaviour
    {
        public static readonly Color Energy = new Color(0.44f, 0.89f, 1f);         // #6FE3FF
        public static readonly Color EnergyHot = new Color(0.85f, 0.97f, 1f);

        static PlasmaFX instance;
        public static PlasmaFX Instance
        {
            get
            {
                if (instance == null && FXLibrary.Get() != null)
                    instance = new GameObject("PlasmaFX").AddComponent<PlasmaFX>();
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() => instance = null;

        FXLibrary lib;
        ParticleSystem glow, splash, sparks, smoke, embers;
        Light[] lights;
        float[] lightLife, lightAge, lightPeak;
        int nextLight;
        Transform[] decals;
        MeshRenderer[] decalR;
        float[] decalAge;
        int nextDecal;
        MaterialPropertyBlock mpb;
        const float DecalLife = 25f, DecalHot = 1.4f;

        void Awake()
        {
            lib = FXLibrary.Get();
            mpb = new MaterialPropertyBlock();
            glow = System("Glow", lib.glow, ParticleSystemRenderMode.Billboard, 0f, 400);
            splash = System("Splash", lib.spark, ParticleSystemRenderMode.Stretch, 0.6f, 600);
            sparks = System("Sparks", lib.spark, ParticleSystemRenderMode.Stretch, 1f, 1200);
            var col = sparks.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium;
            col.bounce = 0.35f;
            col.dampen = 0.25f;
            col.lifetimeLoss = 0.15f;
            col.radiusScale = 0.3f;
            col.collidesWith = ~((1 << 2) | (1 << 8) | (1 << 30));
            embers = System("Embers", lib.glow, ParticleSystemRenderMode.Billboard, 0f, 300);
            smoke = System("Smoke", lib.smoke, ParticleSystemRenderMode.Billboard, -0.05f, 500);
            var tsa = smoke.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = lib.smokeTilesX;
            tsa.numTilesY = lib.smokeTilesY;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var sz = smoke.sizeOverLifetime;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.3f, 0.8f), new Keyframe(1f, 1f)));
            var rot = smoke.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            var lim = smoke.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.limit = 0.3f;
            lim.dampen = 0.08f;
            var fade = smoke.colorOverLifetime;
            fade.enabled = true;
            fade.color = Fade(0.12f, 0.55f);
            var sfade = sparks.colorOverLifetime;
            sfade.enabled = true;
            sfade.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Energy, 0.35f), new GradientColorKey(new Color(1f, 0.55f, 0.25f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) },
            });
            var efade = embers.colorOverLifetime;
            efade.enabled = true;
            efade.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(EnergyHot, 0f), new GradientColorKey(Energy, 0.3f), new GradientColorKey(new Color(1f, 0.4f, 0.15f), 1f) },
                alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) },
            });
            var gfade = glow.colorOverLifetime;
            gfade.enabled = true;
            gfade.color = Fade(0f, 0.2f);
            var gsize = glow.sizeOverLifetime;
            gsize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(1f, 1.15f)));

            lights = new Light[10];
            lightLife = new float[lights.Length]; lightAge = new float[lights.Length]; lightPeak = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                var l = new GameObject("FlashLight" + i).AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.enabled = false;
                lights[i] = l;
            }

            decals = new Transform[48];
            decalR = new MeshRenderer[decals.Length];
            decalAge = new float[decals.Length];
            var quad = QuadMesh();
            for (int i = 0; i < decals.Length; i++)
            {
                var go = new GameObject("Scorch" + i);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = lib.decal;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.SetActive(false);
                decals[i] = go.transform;
                decalR[i] = r;
                decalAge[i] = DecalLife;
            }
        }

        static ParticleSystem.MinMaxGradient Fade(float fadeIn, float holdTo)
        {
            return new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f), new GradientAlphaKey(1f, Mathf.Max(0.01f, fadeIn)), new GradientAlphaKey(1f, holdTo), new GradientAlphaKey(0f, 1f) },
            });
        }

        ParticleSystem System(string name, Material mat, ParticleSystemRenderMode mode, float gravity, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.025f; r.lengthScale = 1.2f; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingFudge = mode == ParticleSystemRenderMode.Stretch ? -2f : 0f;
            ps.Play();
            return ps;
        }

        static Mesh QuadMesh()
        {
            var m = new Mesh { name = "ScorchQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---------- Emitting ----------

        static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color color, float rotation = 0f)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startSize = size, startLifetime = life, startColor = color,
                rotation = rotation, applyShapeToPosition = false,
            };
            ps.Emit(p, 1);
        }

        static Vector3 Cone(Vector3 axis, float spread)
        {
            return Vector3.Slerp(axis.normalized, Random.onUnitSphere, spread).normalized;
        }

        public void Flash(Vector3 pos, Color color, float intensity, float range, float life)
        {
            var l = lights[nextLight];
            lightLife[nextLight] = life; lightAge[nextLight] = 0f; lightPeak[nextLight] = intensity;
            nextLight = (nextLight + 1) % lights.Length;
            l.transform.position = pos;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.enabled = true;
        }

        /// <summary>The bolt forming and leaving the emitter.</summary>
        public static void Muzzle(Vector3 pos, Vector3 fwd)
        {
            var fx = Instance; if (fx == null) return;
            Emit(fx.glow, pos, fwd * 0.4f, Random.Range(0.18f, 0.24f), 0.06f, EnergyHot * 3f);
            Emit(fx.glow, pos + fwd * 0.05f, fwd * 1.2f, Random.Range(0.3f, 0.4f), 0.09f, Energy * 1.4f);
            for (int i = 0; i < 6; i++)
                Emit(fx.splash, pos, Cone(fwd, 0.35f) * Random.Range(3f, 7f), Random.Range(0.006f, 0.012f), Random.Range(0.05f, 0.12f), EnergyHot * 2f);
            for (int i = 0; i < 2; i++)
                Emit(fx.smoke, pos + fwd * 0.08f, fwd * Random.Range(0.3f, 0.7f) + Vector3.up * 0.1f, Random.Range(0.08f, 0.14f), Random.Range(0.5f, 0.9f), new Color(0.75f, 0.8f, 0.85f, 0.25f), Random.Range(0f, 6.28f));
            fx.Flash(pos + fwd * 0.1f, Energy, 5f, 6f, 0.07f);
        }

        /// <summary>A bolt hitting something. flesh: no scorch, no smoke column.</summary>
        public static void Impact(Vector3 pos, Vector3 normal, Vector3 incoming, bool flesh, bool leaveMark)
        {
            var fx = Instance; if (fx == null) return;
            var refl = Vector3.Reflect(incoming.normalized, normal);
            // Hot core and glow
            Emit(fx.glow, pos + normal * 0.02f, Vector3.zero, Random.Range(0.35f, 0.45f), 0.08f, EnergyHot * 4f);
            Emit(fx.glow, pos + normal * 0.05f, normal * 0.3f, Random.Range(0.7f, 0.9f), 0.16f, Energy * 1.2f);
            // Plasma splash: droplets thrown along the surface and back out
            for (int i = 0; i < 16; i++)
            {
                var d = Cone(Vector3.Lerp(normal, refl, 0.5f), 0.55f);
                Emit(fx.splash, pos + normal * 0.01f, d * Random.Range(2f, 6f), Random.Range(0.008f, 0.016f), Random.Range(0.12f, 0.28f), EnergyHot * 2.2f);
            }
            // Sparks that bounce
            int sparkCount = flesh ? 6 : 22;
            for (int i = 0; i < sparkCount; i++)
            {
                var d = Cone(Vector3.Lerp(normal, refl, 0.35f), 0.65f);
                Emit(fx.sparks, pos + normal * 0.02f, d * Random.Range(2.5f, 8f), Random.Range(0.006f, 0.012f), Random.Range(0.4f, 1.1f), Color.white * 2.5f);
            }
            if (!flesh)
            {
                // Lit smoke: a burnt puff that rises and spreads
                for (int i = 0; i < 4; i++)
                    Emit(fx.smoke, pos + normal * 0.06f, (normal * Random.Range(0.2f, 0.6f) + Random.insideUnitSphere * 0.15f + Vector3.up * 0.15f),
                        Random.Range(0.22f, 0.4f), Random.Range(1.6f, 2.8f), new Color(0.32f, 0.3f, 0.28f, 0.75f), Random.Range(0f, 6.28f));
                // Embers: molten points that cool where it hit
                for (int i = 0; i < 5; i++)
                    Emit(fx.embers, pos + normal * 0.01f + Random.insideUnitSphere * 0.03f, Vector3.zero, Random.Range(0.015f, 0.03f), Random.Range(0.8f, 1.6f), Color.white * 2f);
                if (leaveMark) fx.Scorch(pos, normal);
            }
            fx.Flash(pos + normal * 0.25f, Energy, flesh ? 3f : 6f, 5f, 0.16f);
        }

        /// <summary>Vent steam (lit smoke).</summary>
        public static void Steam(Vector3 pos, Vector3 dir, float amount)
        {
            var fx = Instance; if (fx == null) return;
            Emit(fx.smoke, pos + Random.insideUnitSphere * 0.01f, Cone(dir, 0.15f) * Random.Range(0.6f, 1.4f) * amount + Vector3.up * 0.2f,
                Random.Range(0.05f, 0.09f), Random.Range(0.5f, 1.0f), new Color(0.9f, 0.93f, 0.96f, 0.35f * amount), Random.Range(0f, 6.28f));
        }

        /// <summary>A spark shed by a bolt in flight.</summary>
        public static void Shed(Vector3 pos, Vector3 vel)
        {
            var fx = Instance; if (fx == null) return;
            Emit(fx.splash, pos, vel * 0.05f + Random.insideUnitSphere * 0.8f, Random.Range(0.005f, 0.01f), Random.Range(0.08f, 0.2f), EnergyHot * 1.6f);
        }

        void Scorch(Vector3 pos, Vector3 normal)
        {
            int i = nextDecal;
            nextDecal = (nextDecal + 1) % decals.Length;
            var t = decals[i];
            t.gameObject.SetActive(true);
            t.position = pos + normal * 0.002f;
            t.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            t.localScale = Vector3.one * Random.Range(0.16f, 0.24f);
            decalAge[i] = 0f;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < lights.Length; i++)
            {
                if (!lights[i].enabled) continue;
                lightAge[i] += dt;
                float k = 1f - lightAge[i] / lightLife[i];
                if (k <= 0f) { lights[i].enabled = false; continue; }
                lights[i].intensity = lightPeak[i] * k * k;
            }
            for (int i = 0; i < decals.Length; i++)
            {
                if (decalAge[i] >= DecalLife) continue;
                decalAge[i] += dt;
                if (decalAge[i] >= DecalLife) { decals[i].gameObject.SetActive(false); continue; }
                mpb.SetFloat("_Heat", Mathf.Clamp01(1f - decalAge[i] / DecalHot));
                mpb.SetFloat("_Fade", Mathf.Clamp01((DecalLife - decalAge[i]) / 4f));
                decalR[i].SetPropertyBlock(mpb);
            }
        }
    }
}
