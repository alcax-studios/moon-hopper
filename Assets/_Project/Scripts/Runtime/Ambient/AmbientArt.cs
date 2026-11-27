using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Ambient
{
    /// <summary>Shared runtime-built pieces for the ambient robots: materials, the scan fan mesh, simple parts.</summary>
    public static class AmbientArt
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Mesh fan;

        public static Material Lit(string key, Color c, float smooth = 0.4f, float metal = 0.3f)
        {
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Ambient_" + key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            mats[key] = m;
            return m;
        }

        public static Material Glow(string key, Color c, float intensity = 3f)
        {
            if (mats.TryGetValue(key, out var m) && m != null) return m;
// Unlit, HDR bright: always reads as lit, and blooms
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Ambient_" + key };
            var hdr = c * Mathf.Max(1f, intensity * 0.6f); hdr.a = 1f;
            m.SetColor("_BaseColor", hdr);
            mats[key] = m;
            return m;
        }

        /// <summary>Unlit, alpha-blended, vertex-colored (the scan fan).</summary>
        public static Material Beam()
        {
            if (mats.TryGetValue("beam", out var m) && m != null) return m;
            m = new Material(Shader.Find("Sprites/Default")) { name = "Ambient_Beam" };
            m.renderQueue = 3100;
            mats["beam"] = m;
            return m;
        }

        /// <summary>A flat fan from the apex (origin) to a line one unit ahead (+z), one unit wide: soft inside, a bright far edge.
        /// Scale x for width and z for length.</summary>
        public static Mesh Fan()
        {
            if (fan != null) return fan;
            const int n = 16;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>();
            var apex = new Color(0.55f, 0.95f, 1f, 0.45f);
            var mid = new Color(0.55f, 0.95f, 1f, 0.07f);
            var edge = new Color(0.8f, 1f, 1f, 0.95f);
            v.Add(Vector3.zero); c.Add(apex);
            for (int i = 0; i <= n; i++)
            {
                float x = -0.5f + i / (float)n;
                v.Add(new Vector3(x * 0.94f, 0f, 0.94f)); c.Add(mid);    // just before the edge
                v.Add(new Vector3(x, 0f, 1f)); c.Add(edge);              // the bright edge line
            }
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i * 2, b = a + 2;
                t.Add(0); t.Add(a); t.Add(b);                 // fan body
                t.Add(a); t.Add(a + 1); t.Add(b + 1);         // edge band
                t.Add(a); t.Add(b + 1); t.Add(b);
            }
            fan = new Mesh { name = "ScanFan" };
            fan.SetVertices(v); fan.SetColors(c); fan.SetTriangles(t, 0);
            fan.RecalculateBounds();
            return fan;
        }

/// <summary>A small soft particle emitter: thruster wash under a drone, air puffing out under a hover robot.</summary>
        public static ParticleSystem Jet(Transform parent, Vector3 localPos, Vector3 localEuler, Color color, float rate, float speed, float size, float life, float spreadAngle, float radius = 0.02f)
        {
            var go = new GameObject("Jet");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = spreadAngle; sh.radius = radius;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Alcax.FX.FXManager.ParticleMaterial != null ? Alcax.FX.FXManager.ParticleMaterial : Beam();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

/// <summary>Turn a robot into a wreck: the named parts tear off as flying debris, everything left is scorched,
        /// lights and jets go out, and it becomes a searchable, draggable wreck.</summary>
        public static void Wreck(GameObject robot, string[] tearOff, Combat.Health life)
        {
            var scorched = Lit("Scorched", new Color(0.12f, 0.11f, 0.1f), 0.2f, 0.4f);
            var burnt = Lit("Burnt", new Color(0.3f, 0.29f, 0.28f), 0.25f, 0.5f);
            foreach (var ps in robot.GetComponentsInChildren<ParticleSystem>()) Object.Destroy(ps.gameObject);
            foreach (var l in robot.GetComponentsInChildren<Light>()) l.enabled = false;
            foreach (var t in robot.GetComponentsInChildren<Transform>())
            {
                if (System.Array.IndexOf(tearOff, t.name) < 0) continue;
                t.SetParent(null, true);
                if (t.TryGetComponent<Renderer>(out var tr)) tr.sharedMaterial = scorched;
                var col = t.gameObject.AddComponent<BoxCollider>();
                t.gameObject.layer = 2;
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.linearVelocity = (Random.onUnitSphere + Vector3.up).normalized * Random.Range(3f, 6f);
                rb.angularVelocity = Random.insideUnitSphere * 10f;
                Object.Destroy(t.gameObject, 30f);
            }
            foreach (var r in robot.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is MeshRenderer mr && mr.GetComponent<TMPro.TextMeshPro>() != null) continue;
                r.sharedMaterial = Random.value < 0.6f ? scorched : burnt;
            }
            var loot = robot.GetComponent<Items.LootContainer>();
            if (loot == null) loot = robot.AddComponent<Items.LootContainer>();
            loot.Configure("Wreck", "robot", null, 6, robot.transform, life);
            if (robot.GetComponent<Items.Grabbable>() == null) { var g = robot.AddComponent<Items.Grabbable>(); g.label = "WRECK"; g.carryMass = 10f; }
        }

        public static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, Vector3? euler = null, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (euler.HasValue) go.transform.localRotation = Quaternion.Euler(euler.Value);
            go.GetComponent<Renderer>().sharedMaterial = m;
            return go;
        }
    }
}
