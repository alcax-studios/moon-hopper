using UnityEngine;
using UnityEngine.Rendering;

namespace Alcax.FX
{
    /// <summary>
    /// The time-of-day setup of a map: its sun, volume, reflection probes, light shafts, and one profile per preset.
    /// TimeOfDay applies a profile at runtime (swapping the baked lightmaps on baked maps). Written by the builder.
    /// </summary>
    public class TimeOfDaySet : MonoBehaviour
    {
        public static TimeOfDaySet Instance { get; private set; }

        public Light sun;
        public Volume volume;
        public ReflectionProbe[] probes;
        public Renderer[] shafts;
        public TimeOfDayProfile[] profiles;
        [Tooltip("The shaft material's base intensity.")]
        public float shaftBase = 0.014f;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

void Start()
        {
            // Far scenery (the desert, distant rocks, ships on approach) sits outside the probe volume and would borrow the
            // probes by the glass, lit by the interior: give it the sky ambient instead, which follows the time of day
            foreach (var r in FindObjectsByType<Renderer>())
                if (r.lightmapIndex < 0 && r.lightProbeUsage != LightProbeUsage.Off && r.bounds.center.magnitude > farFrom)
                    r.lightProbeUsage = LightProbeUsage.Off;
            TimeOfDay.ApplyCurrent();
        }

        [Tooltip("Unlightmapped renderers farther than this from the origin use the ambient instead of light probes.")]
        public float farFrom = 120f;

        public TimeOfDayProfile Get(TimePreset p)
        {
            if (profiles == null) return null;
            foreach (var pr in profiles) if (pr != null && pr.preset == p) return pr;
            return null;
        }
    }
}
