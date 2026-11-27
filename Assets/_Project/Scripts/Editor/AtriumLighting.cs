using System.Collections.Generic;
using Alcax.FX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Bakes the Atrium once per time of day (Night, Dusk, then Day so the scene ends on Day) and keeps a copy of each
    /// bake's lightmaps, light probes, ambient probe, and reflection cubemaps in its TimeOfDayProfile
    /// (Lighting/Atrium/&lt;preset&gt;). TimeOfDay swaps them at runtime. About three bakes' worth of time.
    /// </summary>
    public static class AtriumLighting
    {
        static readonly TimePreset[] Order = { TimePreset.Night, TimePreset.Dusk, TimePreset.Day };
        static int step = -1;
        public static bool Running => step >= 0;

        public static void BakeAll()
        {
            if (Lightmapping.isRunning) { Debug.LogWarning("[AtriumLighting] a bake is already running"); return; }
            step = 0;
            Next();
        }

        static void Next()
        {
            var set = Object.FindAnyObjectByType<TimeOfDaySet>();
            if (set == null) { Debug.LogError("[AtriumLighting] no TimeOfDaySet in the scene"); step = -1; return; }
            if (step >= Order.Length) { Finish(); return; }
            var prof = set.Get(Order[step]);
            if (prof == null) { step++; Next(); return; }
            ApplyForBake(set, prof);
            Debug.Log($"[AtriumLighting] baking {Order[step]} ({step + 1}/{Order.Length})...");
            Lightmapping.bakeCompleted -= Done;
            Lightmapping.bakeCompleted += Done;
            Lightmapping.BakeAsync();
        }

        static void Done()
        {
            Lightmapping.bakeCompleted -= Done;
            var set = Object.FindAnyObjectByType<TimeOfDaySet>();
            var prof = set != null ? set.Get(Order[step]) : null;
            if (prof != null) Capture(set, prof);
            step++;
            EditorApplication.delayCall += Next;
        }

        static void Finish()
        {
            step = -1;
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[AtriumLighting] Day, Dusk, and Night baked; scene saved on Day.");
        }

        /// <summary>Set the scene's sun, sky, and environment to a preset (what the bake sees).</summary>
        public static void ApplyForBake(TimeOfDaySet set, TimeOfDayProfile p)
        {
            if (set.sun != null)
            {
                set.sun.color = p.sunColor;
                set.sun.intensity = p.sunIntensity;
                set.sun.transform.rotation = Quaternion.LookRotation(-p.toSun.normalized);
            }
            var sky = RenderSettings.skybox;
            if (sky != null)
            {
                sky.SetColor("_Zenith", p.zenith); sky.SetColor("_Haze", p.haze); sky.SetColor("_Ground", p.ground);
                sky.SetColor("_SunColor", p.skySun); sky.SetVector("_SunDir", p.skySunDir.normalized); sky.SetFloat("_Exposure", p.skyExposure);
                EditorUtility.SetDirty(sky);
            }
            RenderSettings.ambientIntensity = p.ambientIntensity;
            RenderSettings.reflectionIntensity = p.reflectionIntensity;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
        }

        static void Capture(TimeOfDaySet set, TimeOfDayProfile p)
        {
            string dir = $"{Root}/Lighting/Atrium/{p.preset}";
            if (AssetDatabase.IsValidFolder(dir)) AssetDatabase.DeleteAsset(dir);
            EnsureFolder(dir);
            var colors = new List<Texture2D>(); var dirs = new List<Texture2D>();
            int i = 0;
            foreach (var lm in LightmapSettings.lightmaps)
            {
                colors.Add(CopyTex(lm.lightmapColor, $"{dir}/LM_{i}_color"));
                dirs.Add(CopyTex(lm.lightmapDir, $"{dir}/LM_{i}_dir"));
                i++;
            }
            p.lightmapColor = colors.ToArray();
            p.lightmapDir = dirs.ToArray();
            if (LightmapSettings.lightProbes != null)
            {
                var probes = Object.Instantiate(LightmapSettings.lightProbes);
                probes.name = "Probes_" + p.preset;
                AssetDatabase.CreateAsset(probes, $"{dir}/Probes.asset");
                p.probes = probes;
            }
            var refl = new Texture[set.probes != null ? set.probes.Length : 0];
            for (int k = 0; k < refl.Length; k++)
                if (set.probes[k] != null && set.probes[k].bakedTexture != null) refl[k] = CopyTex(set.probes[k].bakedTexture, $"{dir}/Reflection_{k}");
            p.reflections = refl;
            var sh = RenderSettings.ambientProbe;
            var amb = new float[27];
            for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) amb[c * 9 + k] = sh[c, k];
            p.ambientProbe = amb;
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
        }

        static T CopyTex<T>(T tex, string pathNoExt) where T : Texture
        {
            if (tex == null) return null;
            string src = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(src)) return null;
            string dst = pathNoExt + System.IO.Path.GetExtension(src);
            AssetDatabase.CopyAsset(src, dst);
            return AssetDatabase.LoadAssetAtPath<T>(dst);
        }
    }
}
