using System.IO;
using UnityEditor;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Materials for the plants in Art/Models/Foliage (from Tools/blender/environment/desert_plants.py): one
    /// Alcax/Foliage Lit material per plant part (bark, leaves, succulent, grass), with its own stiffness, flutter,
    /// cutout, and light passing through, assigned to every plant model. Menu: Alcax > Art > Build Foliage Materials.
    /// </summary>
    public static class FoliageAssets
    {
        const string Dir = Root + "/Art/Models/Foliage";

        // material name in the FBX -> texture, alpha cutout, stiffness, leaf flutter, translucency
        static readonly (string mat, string tex, float cutoff, float stiffness, float flutter, float transl)[] Parts =
        {
            ("M_Plant_Bark", "T_Plant_Bark_BaseColor", 0f, 1.6f, 0.2f, 0.1f),
            ("M_Plant_ScrubLeaf", "T_Plant_ScrubLeaf_BaseColor", 0.45f, 1.0f, 1.4f, 0.9f),
            ("M_Plant_Succulent", "T_Plant_Succulent_BaseColor", 0f, 2.6f, 0.15f, 0.5f),
            ("M_Plant_Grass", "T_Plant_Grass_BaseColor", 0.4f, 0.55f, 0.8f, 0.7f),
        };

        [MenuItem("Alcax/Art/Build Foliage Materials")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) return;
            EnsureFolder(Dir + "/_Materials");
            var shader = Shader.Find("Alcax/Foliage Lit");
            var mats = new System.Collections.Generic.Dictionary<string, Material>();
            foreach (var p in Parts)
            {
                var tex = FindTexture(p.tex);
                if (tex == null) continue;
                var path = $"{Dir}/_Materials/{p.mat}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
                m.shader = shader;
                m.SetTexture("_BaseMap", tex);
                m.SetFloat("_Cutoff", p.cutoff);
                m.SetFloat("_Stiffness", p.stiffness);
                m.SetFloat("_Flutter", p.flutter);
                m.SetFloat("_TranslucencyStrength", p.transl);
                m.SetFloat("_Smoothness", p.mat.Contains("Succulent") ? 0.45f : 0.2f);
                m.enableInstancing = true;
                EditorUtility.SetDirty(m);
                mats[p.mat] = m;
            }
            int models = 0;
            foreach (var folder in AssetDatabase.GetSubFolders(Dir))
                foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    var mp = AssetDatabase.GUIDToAssetPath(g);
                    if (!(AssetImporter.GetAtPath(mp) is ModelImporter mi)) continue;
                    foreach (var src in AssetDatabase.LoadAllAssetsAtPath(mp))
                        if (src is Material sm && mats.TryGetValue(sm.name, out var dst))
                            mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sm.name), dst);
                    mi.SaveAndReimport();
                    models++;
                }
            AssetDatabase.SaveAssets();
            Debug.Log($"[FoliageAssets] {mats.Count} foliage materials on {models} plants");
        }

        static Texture2D FindTexture(string name)
        {
            foreach (var g in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { Dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) != name) continue;
                if (AssetImporter.GetAtPath(p) is TextureImporter ti && !ti.alphaIsTransparency)
                {
                    ti.alphaIsTransparency = true;
                    ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }
    }
}
