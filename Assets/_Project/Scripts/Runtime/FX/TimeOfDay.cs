using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Alcax.FX
{
    /// <summary>
    /// Switches the time of day (Day, Dusk, Night; picked in the admin console for now, cycling later). On a map with
    /// a TimeOfDaySet it applies that preset's profile, including its own baked lightmaps and probes; elsewhere it
    /// adjusts the sun, ambient, and fog. The choice carries across map loads.
    /// </summary>
    public static class TimeOfDay
    {
        public static TimePreset Current { get; private set; } = TimePreset.Day;
        static Material skyInstance, skySource;
        static Volume lastVolume;
        static VolumeProfile volumeInstance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() { Current = TimePreset.Day; skyInstance = null; skySource = null; lastVolume = null; volumeInstance = null; fallbackSaved = false; }

        public static void Set(TimePreset p)
        {
            Current = p;
            ApplyCurrent();
        }

        public static void ApplyCurrent()
        {
            var set = TimeOfDaySet.Instance;
            var prof = set != null ? set.Get(Current) : null;
            if (prof != null) Apply(set, prof);
            else ApplyFallback();
        }

        static void Apply(TimeOfDaySet set, TimeOfDayProfile p)
        {
            // Sun
            if (set.sun != null)
            {
                set.sun.color = p.sunColor;
                set.sun.intensity = p.sunIntensity;
                set.sun.transform.rotation = Quaternion.LookRotation(-p.toSun.normalized);
                set.sun.enabled = p.sunIntensity > 0.001f;
            }
            // Sky: a runtime copy so the asset is never touched
            var sky = RenderSettings.skybox;
            if (sky != null && sky != skyInstance) { skySource = sky; skyInstance = new Material(sky) { name = sky.name + " (runtime)" }; RenderSettings.skybox = skyInstance; }
            if (skyInstance != null)
            {
                skyInstance.SetColor("_Zenith", p.zenith);
                skyInstance.SetColor("_Haze", p.haze);
                skyInstance.SetColor("_Ground", p.ground);
                skyInstance.SetColor("_SunColor", p.skySun);
                skyInstance.SetVector("_SunDir", p.skySunDir.normalized);
                skyInstance.SetFloat("_Exposure", p.skyExposure);
            }
            RenderSettings.ambientIntensity = p.ambientIntensity;
            RenderSettings.reflectionIntensity = p.reflectionIntensity;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
            if (p.ambientProbe != null && p.ambientProbe.Length == 27)
            {
                var sh = new SphericalHarmonicsL2();
                for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) sh[c, k] = p.ambientProbe[c * 9 + k];
                RenderSettings.ambientProbe = sh;
            }

            // Baked light: swap the lightmaps, probes, and reflections for this preset's bake
            if (p.lightmapColor != null && p.lightmapColor.Length > 0)
            {
                var maps = new LightmapData[p.lightmapColor.Length];
                for (int i = 0; i < maps.Length; i++)
                    maps[i] = new LightmapData { lightmapColor = p.lightmapColor[i], lightmapDir = p.lightmapDir != null && i < p.lightmapDir.Length ? p.lightmapDir[i] : null };
                LightmapSettings.lightmaps = maps;
            }
            if (p.probes != null) LightmapSettings.lightProbes = p.probes;
            if (set.probes != null && p.reflections != null)
                for (int i = 0; i < set.probes.Length && i < p.reflections.Length; i++)
                    if (set.probes[i] != null && p.reflections[i] != null) set.probes[i].bakedTexture = p.reflections[i];

            // Exposure (a runtime copy of the volume profile)
            if (set.volume != null)
            {
                if (lastVolume != set.volume || volumeInstance == null) { lastVolume = set.volume; volumeInstance = set.volume.profile; }
                if (volumeInstance.TryGet<ColorAdjustments>(out var ca)) ca.postExposure.value = p.postExposure;
            }
            // Window light shafts fade with the sun
            if (set.shafts != null)
            {
                var mpb = new MaterialPropertyBlock();
                foreach (var r in set.shafts)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetFloat("_Intensity", set.shaftBase * p.shafts);
                    r.SetPropertyBlock(mpb);
                    r.enabled = p.shafts > 0.01f;
                }
            }
        }

        /// <summary>Maps without baked presets (realtime light): dim the sun and the ambient.</summary>
        static void ApplyFallback()
        {
            var sun = RenderSettings.sun;
            if (sun == null) return;
            if (!fallbackSaved || savedSun != sun) { savedSun = sun; savedIntensity = sun.intensity; savedColor = sun.color; savedAmbient = RenderSettings.ambientIntensity; savedFog = RenderSettings.fogColor; fallbackSaved = true; }
            float k = Current == TimePreset.Day ? 1f : Current == TimePreset.Dusk ? 0.45f : 0.12f;
            sun.intensity = savedIntensity * k;
            sun.color = Current == TimePreset.Night ? new Color(0.6f, 0.7f, 1f) : Current == TimePreset.Dusk ? Color.Lerp(savedColor, new Color(1f, 0.5f, 0.3f), 0.6f) : savedColor;
            RenderSettings.ambientIntensity = savedAmbient * Mathf.Lerp(0.3f, 1f, k);
            RenderSettings.fogColor = savedFog * Mathf.Lerp(0.15f, 1f, k);
        }

        static bool fallbackSaved;
        static Light savedSun;
        static float savedIntensity, savedAmbient;
        static Color savedColor, savedFog;
    }
}
