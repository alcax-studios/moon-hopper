using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Ambient
{
    /// <summary>A floor grate that breathes steam every few seconds. Something for the scanner drones to look at.</summary>
    public class SteamVent : MonoBehaviour
    {
        public static readonly List<SteamVent> All = new List<SteamVent>();

        ParticleSystem ps;
        float next;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            // The grate
            var grate = AmbientArt.Lit("Grate", new Color(0.18f, 0.19f, 0.2f), 0.5f, 0.8f);
            AmbientArt.Part(PrimitiveType.Cube, "Frame", transform, new Vector3(0f, 0.012f, 0f), new Vector3(0.9f, 0.024f, 0.9f), grate);
            var slot = AmbientArt.Lit("GrateDark", new Color(0.03f, 0.03f, 0.035f), 0.1f, 0f);
            for (int i = 0; i < 6; i++)
                AmbientArt.Part(PrimitiveType.Cube, "Slot" + i, transform, new Vector3(-0.32f + i * 0.128f, 0.025f, 0f), new Vector3(0.06f, 0.004f, 0.75f), slot);

            // The steam
            var go = new GameObject("Steam");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false;
            main.duration = 1.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startColor = new Color(0.9f, 0.92f, 0.95f, 0.22f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 120;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 45f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.3f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.25f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Alcax.FX.FXManager.ParticleMaterial != null ? Alcax.FX.FXManager.ParticleMaterial : AmbientArt.Beam();
            next = Time.time + Random.Range(1f, 5f);
        }

        void Update()
        {
            if (Time.time < next || ps == null) return;
            next = Time.time + Random.Range(5f, 11f);
            ps.Play();
        }

        public Vector3 Point => transform.position + Vector3.up * 0.6f;
    }
}
