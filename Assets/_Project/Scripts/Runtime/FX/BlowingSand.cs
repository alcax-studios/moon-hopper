using System.Collections.Generic;
using UnityEngine;

namespace Alcax.FX
{
    /// <summary>
    /// Blowing sand around the camera, driven by WindManager (the same wind that moves the plants):
    ///  - grains: tiny streaks hopping along the ground (saltation, how sand really travels),
    ///  - veils: wide, faint sheets of sand sweeping low over the ground with the gusts,
    ///  - crest plumes: sand streaming off dune crests (found once from the terrain),
    ///  - dust devils: now and then a spinning column forms out on the flats and drifts with the wind.
    /// Spawns only on terrain and outside the exclusion rects (buildings); veils die on hitting walls so nothing drifts
    /// indoors. intensity scales everything (a sandstorm is intensity 3).
    /// </summary>
    public class BlowingSand : MonoBehaviour
    {
        [Range(0f, 3f)] public float intensity = 1f;
        [SerializeField] float radius = 70f;
        [SerializeField] Rect[] exclusions = new Rect[0];
        [SerializeField] Color sandColor = new Color(0.78f, 0.6f, 0.42f, 1f);

        ParticleSystem grains, veils, plumes, devil;
        Terrain[] terrains;
        readonly List<Vector3> crests = new List<Vector3>();
        float devilTimer = 15f, devilLife, devilAge;
        Vector3 devilPos;
        float grainAcc, veilAcc, plumeAcc;

        public void SetExclusions(Rect[] r) => exclusions = r;

        void Start()
        {
            var lib = FXLibrary.Get();
            if (lib == null) { enabled = false; return; }
            terrains = Terrain.activeTerrains;
            grains = System("Grains", lib.sandGrain != null ? lib.sandGrain : lib.smoke, ParticleSystemRenderMode.Stretch, 1.1f, 3000, false);
            veils = System("Veils", lib.sandVeil != null ? lib.sandVeil : lib.smoke, ParticleSystemRenderMode.Billboard, -0.02f, 300, true);
            plumes = System("Plumes", lib.sandVeil != null ? lib.sandVeil : lib.smoke, ParticleSystemRenderMode.Billboard, 0.15f, 600, true);
            devil = System("DustDevil", lib.sandVeil != null ? lib.sandVeil : lib.smoke, ParticleSystemRenderMode.Billboard, -0.1f, 800, true);
            foreach (var ps in new[] { veils, plumes, devil })
            {
                var tsa = ps.textureSheetAnimation;
                tsa.enabled = true;
                tsa.numTilesX = lib.smokeTilesX; tsa.numTilesY = lib.smokeTilesY;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.2f, 1f, 1f));
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                var col = ps.colorOverLifetime; col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) },
                });
                var sz = ps.sizeOverLifetime;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.3f)));
            }
            var gc = grains.colorOverLifetime; gc.enabled = true;
            gc.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) },
            });
            FindCrests();
        }

        ParticleSystem System(string name, Material mat, ParticleSystemRenderMode mode, float gravity, int max, bool dieOnWalls)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            if (dieOnWalls)
            {
                var c = ps.collision;
                c.enabled = true;
                c.type = ParticleSystemCollisionType.World;
                c.mode = ParticleSystemCollisionMode.Collision3D;
                c.quality = ParticleSystemCollisionQuality.Low;
                c.lifetimeLoss = 1f;
                c.radiusScale = 0.05f;
                c.collidesWith = ~((1 << 2) | (1 << 8));
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = mode;
            if (mode == ParticleSystemRenderMode.Stretch) { r.velocityScale = 0.05f; r.lengthScale = 2f; }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // ---------- Ground ----------

        bool Ground(Vector3 p, out float y)
        {
            y = 0f;
            foreach (var r in exclusions) if (r.Contains(new Vector2(p.x, p.z))) return false;
            foreach (var t in terrains)
            {
                if (t == null) continue;
                var tp = t.transform.position; var sz = t.terrainData.size;
                if (p.x < tp.x || p.z < tp.z || p.x > tp.x + sz.x || p.z > tp.z + sz.z) continue;
                y = t.SampleHeight(p) + tp.y;
                return true;
            }
            return false;
        }

        /// <summary>Dune crests near the start: points higher than the ground just upwind and downwind.</summary>
        void FindCrests()
        {
            var cam = Camera.main != null ? Camera.main.transform.position : transform.position;
            var wind = WindDir();
            for (float z = -200f; z <= 200f; z += 6f)
                for (float x = -200f; x <= 200f; x += 6f)
                {
                    var p = cam + new Vector3(x, 0f, z);
                    if (!Ground(p, out float h)) continue;
                    if (!Ground(p - wind * 4f, out float hu) || !Ground(p + wind * 4f, out float hd)) continue;
                    if (h - hu > 0.5f && h - hd > 0.8f) crests.Add(new Vector3(p.x, h, p.z));
                }
        }

        Vector3 WindDir()
        {
            var w = WindManager.Instance;
            var d = w != null ? new Vector3(w.direction.x, 0f, w.direction.z) : Vector3.right;
            return d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.right;
        }

        // ---------- Emitting ----------

        void Update()
        {
            if (terrains == null || intensity <= 0f) return;
            var camT = Camera.main != null ? Camera.main.transform : null;
            if (camT == null) return;
            var cam = camT.position;
            float dt = Time.deltaTime;
            var wm = WindManager.Instance;
            var dir = WindDir();

            // Grains: emitted around the camera, mostly ahead, carried by the local wind
            grainAcc += 450f * intensity * dt;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            while (grainAcc >= 1f)
            {
                grainAcc -= 1f;
                var off = Random.insideUnitCircle * radius * 0.6f;
                var pos = cam + new Vector3(off.x, 0f, off.y);
                if (!Ground(pos, out float gy)) continue;
                var w = wm != null ? wm.At(pos) : dir * 0.5f;
                float gust = w.magnitude;
                if (Random.value > gust * 1.6f) continue;   // fewer grains when it's calm
                p.position = new Vector3(pos.x, gy + Random.Range(0.02f, 0.25f), pos.z);
                p.velocity = w * Random.Range(7f, 12f) + Vector3.up * Random.Range(0.4f, 1.8f);
                p.startSize = Random.Range(0.012f, 0.025f);
                p.startLifetime = Random.Range(0.35f, 0.9f);
                p.startColor = sandColor * Random.Range(0.85f, 1.1f);
                grains.Emit(p, 1);
            }

            // Veils: big faint sheets, spawned upwind so they sweep across the view
            veilAcc += 5f * intensity * dt;
            while (veilAcc >= 1f)
            {
                veilAcc -= 1f;
                var off = Random.insideUnitCircle * radius;
                var pos = cam + new Vector3(off.x, 0f, off.y) - dir * radius * 0.4f;
                if (!Ground(pos, out float gy)) continue;
                var w = wm != null ? wm.At(pos) : dir * 0.5f;
                p.position = new Vector3(pos.x, gy + Random.Range(0.3f, 1.2f), pos.z);
                p.velocity = w * Random.Range(5f, 8f) + Vector3.up * Random.Range(-0.05f, 0.15f);
                p.startSize = Random.Range(2.5f, 6f);
                p.startLifetime = Random.Range(5f, 9f);
                p.rotation = Random.Range(0f, 360f);
                var c = sandColor; c.a = Random.Range(0.07f, 0.16f) * Mathf.Clamp01(w.magnitude * 1.5f);
                p.startColor = c;
                veils.Emit(p, 1);
            }

            // Crest plumes
            if (crests.Count > 0)
            {
                plumeAcc += 40f * intensity * dt;
                while (plumeAcc >= 1f)
                {
                    plumeAcc -= 1f;
                    var cpos = crests[Random.Range(0, crests.Count)];
                    if ((cpos - cam).sqrMagnitude > 140f * 140f) continue;
                    var w = wm != null ? wm.At(cpos) : dir * 0.5f;
                    if (w.magnitude < 0.45f) continue;
                    p.position = cpos + Vector3.up * 0.15f + Vector3.Cross(Vector3.up, dir) * Random.Range(-3f, 3f);
                    p.velocity = w * Random.Range(6f, 10f) + Vector3.up * Random.Range(0.5f, 1.6f);
                    p.startSize = Random.Range(0.4f, 1.3f);
                    p.startLifetime = Random.Range(1.2f, 2.6f);
                    p.rotation = Random.Range(0f, 360f);
                    var c = sandColor; c.a = Random.Range(0.12f, 0.25f);
                    p.startColor = c;
                    plumes.Emit(p, 1);
                }
            }

            UpdateDevil(dt, cam, dir, wm, ref p);
        }

        void UpdateDevil(float dt, Vector3 cam, Vector3 dir, WindManager wm, ref ParticleSystem.EmitParams p)
        {
            if (devilLife <= 0f)
            {
                devilTimer -= dt * intensity;
                if (devilTimer > 0f) return;
                devilTimer = Random.Range(40f, 90f);
                for (int tries = 0; tries < 8; tries++)
                {
                    var off = Random.insideUnitCircle.normalized * Random.Range(50f, 160f);
                    var pos = cam + new Vector3(off.x, 0f, off.y);
                    if (Ground(pos, out float gy)) { devilPos = new Vector3(pos.x, gy, pos.z); devilLife = Random.Range(15f, 25f); devilAge = 0f; break; }
                }
                return;
            }
            devilAge += dt;
            float fade = Mathf.Clamp01(devilAge / 3f) * Mathf.Clamp01((devilLife - devilAge) / 4f);
            if (devilAge >= devilLife) { devilLife = 0f; return; }
            // It wanders downwind
            devilPos += (dir * 2.5f + new Vector3(Mathf.Sin(devilAge * 0.7f), 0f, Mathf.Cos(devilAge * 0.5f)) * 1.5f) * dt;
            if (Ground(devilPos, out float y)) devilPos.y = y;
            int n = Mathf.RoundToInt(60f * dt * intensity * fade + Random.value);
            for (int i = 0; i < n; i++)
            {
                float h = Random.value;
                float height = h * h * 14f;
                float r = 0.4f + height * 0.22f;
                float ang = Random.value * Mathf.PI * 2f;
                var radial = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var tangent = Vector3.Cross(Vector3.up, radial);
                p.position = devilPos + radial * r + Vector3.up * (height + 0.2f);
                p.velocity = tangent * (6f - height * 0.2f) + Vector3.up * Random.Range(1.5f, 3.5f) + dir * 2f;
                p.startSize = Random.Range(0.6f, 1.4f) * (1f + height * 0.12f);
                p.startLifetime = Random.Range(1.5f, 3f);
                p.rotation = Random.Range(0f, 360f);
                var c = sandColor; c.a = Random.Range(0.12f, 0.22f) * fade * (1f - h * 0.6f);
                p.startColor = c;
                devil.Emit(p, 1);
            }
        }
    }
}
