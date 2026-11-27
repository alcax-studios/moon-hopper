using UnityEngine;

namespace Alcax.FX
{
    public enum TimePreset { Day, Dusk, Night }

    /// <summary>
    /// One time of day: the sun, sky, ambient, fog, exposure, and (for a baked map) its own lightmaps, light probes,
    /// and reflection probe cubemaps. Written by the Atrium lighting bake (one bake per preset).
    /// </summary>
    [CreateAssetMenu(menuName = "Alcax/Time Of Day Profile", fileName = "TOD_")]
    public class TimeOfDayProfile : ScriptableObject
    {
        public TimePreset preset;

        [Header("Sun")]
        public Color sunColor = Color.white;
        public float sunIntensity = 2f;
        [Tooltip("Direction toward the sun (world).")]
        public Vector3 toSun = new Vector3(-0.26f, 0.2f, 1f);

        [Header("Sky (Alcax/AlienSky)")]
        public Color zenith, haze, ground, skySun;
        public float skyExposure = 1f;
        [Tooltip("Where the sky draws the sun disk (below the horizon at night).")]
        public Vector3 skySunDir = new Vector3(-0.26f, 0.2f, 1f);

        [Header("Environment")]
        public float ambientIntensity = 1f;
        public float reflectionIntensity = 0.6f;
        public Color fogColor;
        public float fogDensity = 0.0011f;
        public float postExposure = 0.55f;
        [Tooltip("Scale for the window light shafts.")]
        public float shafts = 1f;

        [Header("Baked (empty on realtime maps)")]
        public Texture2D[] lightmapColor;
        public Texture2D[] lightmapDir;
        public LightProbes probes;
        public Texture[] reflections;
        [Tooltip("The ambient probe the bake produced (27 floats: 3 colors x 9 coefficients).")]
        public float[] ambientProbe;
    }
}
