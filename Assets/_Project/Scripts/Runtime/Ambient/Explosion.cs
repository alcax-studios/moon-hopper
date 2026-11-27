using System.Collections.Generic;
using Alcax.Audio;
using Alcax.Combat;
using Alcax.Core;
using UnityEngine;

namespace Alcax.Ambient
{
    /// <summary>
    /// A blast: a flash, a fireball, smoke, sparks, flying debris, a shake, and damage (falling off with distance)
    /// to anything with Health in the radius, plus a push on loose physics objects. Used by the robots when they die.
    /// </summary>
    public static class Explosion
    {
        public static void Blast(Vector3 at, float radius, float damage, GameObject source, float scale = 1f, Material debris = null)
        {
            // Light flash
            var lg = new GameObject("BlastLight");
            lg.transform.position = at;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.65f, 0.3f); l.range = 8f * scale; l.intensity = 9f * scale; l.shadows = LightShadows.None;
            lg.AddComponent<FadeAndDie>().Init(l, 0.45f);

            Burst(at, scale, new Color(1f, 0.6f, 0.2f, 0.9f), 26, 0.35f, 0.7f, 4.5f * scale, 0.5f * scale, 0f);      // fireball
            Burst(at, scale, new Color(0.25f, 0.24f, 0.23f, 0.55f), 22, 1.6f, 3.2f, 1.8f * scale, 0.9f * scale, -0.12f);   // smoke, rising
            FX.FXManager.Sparks(at, Vector3.up, Mathf.RoundToInt(20 * scale));
            for (int i = 0; i < Mathf.RoundToInt(10 * scale); i++) Chunk(at, debris, scale);

            SoundManager.PlayAt(Sfx.Shotgun, at, 1f, 0.15f);
            SoundManager.PlayAt(Sfx.TargetClang, at, 0.8f, 0.2f);
            var cam = Camera.main;
            if (cam != null) FeelManager.Shake(Mathf.Clamp01(1f - Vector3.Distance(cam.transform.position, at) / (radius * 4f)) * 0.9f * scale);

            // Damage and push
            var hurt = new HashSet<Health>();
            foreach (var c in Physics.OverlapSphere(at, radius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic)
                    c.attachedRigidbody.AddExplosionForce(400f * scale, at, radius, 0.4f, ForceMode.Impulse);
                var h = c.GetComponentInParent<Health>();
                if (h == null || h.IsDead || (source != null && h.gameObject == source) || !hurt.Add(h)) continue;
                var p = c.ClosestPoint(at);
                float k = 1f - Mathf.Clamp01(Vector3.Distance(at, p) / radius);
                if (k <= 0f) continue;
                if (Physics.Linecast(at, p, out var block, AI.AILayers.World, QueryTriggerInteraction.Ignore) && block.collider != c) k *= 0.35f;   // behind cover
                h.TakeDamage(new DamageInfo { Amount = damage * k, Point = p, Direction = (p - at).normalized, Knockback = 3f * k, Source = source });
            }
        }

        static void Burst(Vector3 at, float scale, Color color, int count, float minLife, float maxLife, float speed, float size, float gravity)
        {
            var go = new GameObject("Blast");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startColor = color;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.2f * scale;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.5f, 0.45f, 0.4f), 1f) },
                      new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a * 0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FX.FXManager.ParticleMaterial != null ? FX.FXManager.ParticleMaterial : AmbientArt.Beam();
            ps.Play();
            Object.Destroy(go, maxLife + 0.5f);
        }

        static void Chunk(Vector3 at, Material mat, float scale)
        {
            var c = GameObject.CreatePrimitive(Random.value < 0.5f ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            c.name = "Debris";
            c.layer = 2;   // never blocks bullets or sight
            c.transform.position = at + Random.insideUnitSphere * 0.2f;
            c.transform.rotation = Random.rotation;
            c.transform.localScale = new Vector3(Random.Range(0.04f, 0.14f), Random.Range(0.02f, 0.08f), Random.Range(0.04f, 0.16f)) * scale;
            c.GetComponent<Renderer>().sharedMaterial = mat != null ? mat : AmbientArt.Lit("Scorched", new Color(0.12f, 0.11f, 0.1f), 0.2f, 0.4f);
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.linearVelocity = (Random.onUnitSphere + Vector3.up * 0.8f).normalized * Random.Range(3f, 8f) * scale;
            rb.angularVelocity = Random.insideUnitSphere * 15f;
            Object.Destroy(c, Random.Range(12f, 20f));
        }
    }
}
