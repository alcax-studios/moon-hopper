using System.IO;
using UnityEditor;
using UnityEngine;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Builds a URP Lit material for each asset under Art/Models/&lt;Class&gt;/&lt;Asset&gt;/ from its baked texture set
    /// (Textures/T_&lt;Name&gt;_BaseColor, _Normal, _MetallicSmoothness, _Occlusion, _Emission) and assigns it to the
    /// asset's model in place of the materials inside the FBX. A material in the FBX that has its own asset of the same
    /// name in the asset's Materials folder (an animated shader part) is assigned that instead.
    /// Menu: Alcax > Art > Build Materials From Textures.
    /// </summary>
    public static class ArtMaterials
    {
        const float EmissionIntensity = 6f;   // HDR multiplier on the baked emission color (art bible: energy blooms)

        [MenuItem("Alcax/Art/Build Materials From Textures")]
        public static void BuildAll()
        {
            int n = 0;
            var root = AssetBudgets.ModelsRoot.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root)) return;
            foreach (var cls in AssetDatabase.GetSubFolders(root))
                foreach (var asset in AssetDatabase.GetSubFolders(cls))
                    if (Build(asset)) n++;
            Debug.Log($"[ArtMaterials] {n} asset materials built");
        }

        /// <summary>Build the material for one asset folder. False when it has no baked texture set.</summary>
        public static bool Build(string folder)
        {
            if (folder.Contains("/Foliage/")) return false;   // plants use the wind shader (FoliageAssets)
            var texDir = folder + "/Textures";
            if (!AssetDatabase.IsValidFolder(texDir)) return false;
            string baseColor = null;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { texDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p).EndsWith("_BaseColor")) { baseColor = p; break; }
            }
            if (baseColor == null) return false;
            var stem = Path.GetFileNameWithoutExtension(baseColor);
            stem = stem.Substring(0, stem.Length - "_BaseColor".Length);          // T_<Name>
            var name = stem.Substring(2);
            Texture2D T(string map) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{texDir}/{stem}_{map}.png");

            var matDir = folder + "/Materials";
            if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder(folder, "Materials");
            var matPath = $"{matDir}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", T("BaseColor"));
            mat.SetColor("_BaseColor", Color.white);
            var nrm = T("Normal");
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1f);
            if (nrm) mat.EnableKeyword("_NORMALMAP");
            var ms = T("MetallicSmoothness");
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_WorkflowMode", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);   // smoothness in the metallic map's alpha
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_Metallic", 1f);
            if (ms) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            var ao = T("Occlusion");
            mat.SetTexture("_OcclusionMap", ao);
            mat.SetFloat("_OcclusionStrength", 1f);
            if (ao) mat.EnableKeyword("_OCCLUSIONMAP");
            var em = T("Emission");
            if (em)
            {
                mat.SetTexture("_EmissionMap", em);
                mat.SetColor("_EmissionColor", Color.white * EmissionIntensity);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);

            // Assign it to every material slot in the asset's models
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                var mp = AssetDatabase.GUIDToAssetPath(g);
                if (!(AssetImporter.GetAtPath(mp) is ModelImporter mi)) continue;
                foreach (var src in AssetDatabase.LoadAllAssetsAtPath(mp))
                {
                    if (!(src is Material m)) continue;
                    // A part with its own material asset of the same name (e.g. M_PlasmaFluid) keeps it
                    var own = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/{m.name}.mat");
                    mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), own != null ? own : mat);
                }
                mi.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
