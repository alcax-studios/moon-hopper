using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Import presets and checks for art under Assets/_Project/Art/Models/&lt;Class&gt;/&lt;Asset&gt;/.
    /// Presets apply on the first import only (later hand tweaks in the Inspector stay). Checks run on every
    /// import and log a warning per problem: triangle budget, missing LODs, scale, naming, UVs, texture size.
    /// Menu Alcax > Validate Art Assets checks everything at once. Rules: docs/art/asset-pipeline.md.
    /// </summary>
    public class ArtImportValidator : AssetPostprocessor
    {
        const string Tag = "[ArtCheck] ";

        // ---------- Presets (first import) ----------

        void OnPreprocessModel()
        {
            var b = AssetBudgets.For(assetPath, out _);
            if (b == null || !assetImporter.importSettingsMissing) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f;
            m.useFileScale = true;
            m.importCameras = false;
            m.importLights = false;
            m.importVisibility = false;
            m.isReadable = false;
            m.meshCompression = ModelImporterMeshCompression.Off;
            m.importNormals = ModelImporterNormals.Import;
            m.importTangents = ModelImporterTangents.CalculateMikk;
            m.generateSecondaryUV = b.lightmapUVs;
            m.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            m.animationType = b.rig == AssetBudgets.Rig.Humanoid ? ModelImporterAnimationType.Human
                            : b.rig == AssetBudgets.Rig.Generic ? ModelImporterAnimationType.Generic
                            : ModelImporterAnimationType.None;
            m.importAnimation = b.rig != AssetBudgets.Rig.None;
        }

        void OnPreprocessTexture()
        {
            var b = AssetBudgets.For(assetPath, out _);
            if (b == null || !assetImporter.importSettingsMissing) return;
            var t = (TextureImporter)assetImporter;
            var n = Path.GetFileNameWithoutExtension(assetPath);
            t.mipmapEnabled = true;
            t.maxTextureSize = b.maxTexture;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            if (n.EndsWith("_Normal")) t.textureType = TextureImporterType.NormalMap;
            // Data maps are linear
            if (n.EndsWith("_MetallicSmoothness") || n.EndsWith("_Occlusion") || n.EndsWith("_Mask") || n.EndsWith("_Height"))
                t.sRGBTexture = false;
        }

        // ---------- Checks (every import) ----------

        void OnPostprocessModel(GameObject root)
        {
            var b = AssetBudgets.For(assetPath, out var assetName);
            if (b == null) return;
            foreach (var w in CheckModel(root, b, assetName, Path.GetFileNameWithoutExtension(assetPath)))
                Debug.LogWarning(Tag + assetPath + ": " + w);
        }

        void OnPostprocessTexture(Texture2D tex)
        {
            var b = AssetBudgets.For(assetPath, out _);
            if (b == null) return;
            foreach (var w in CheckTexture(assetPath, tex.width, tex.height, b)) Debug.LogWarning(Tag + assetPath + ": " + w);
        }

        static List<string> CheckModel(GameObject root, AssetBudgets.Budget b, string assetName, string fileName)
        {
            var w = new List<string>();
            if (!fileName.StartsWith(b.prefix)) w.Add($"name should start with {b.prefix} ({b.name})");
            var lod0 = 0; var lods = 0; bool anyMesh = false;
            var bounds = new Bounds(); bool hasBounds = false;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) Count(mf.sharedMesh, mf.name, mf.GetComponent<Renderer>());
            foreach (var sk in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Count(sk.sharedMesh, sk.name, sk);
            void Count(Mesh mesh, string n, Renderer r)
            {
                if (mesh == null) return;
                anyMesh = true;
                bool isLod = n.Contains("_LOD") && !n.EndsWith("_LOD0");
                if (isLod) { lods++; return; }
                int tris = 0; for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
                lod0 += tris;
                if (mesh.uv == null || mesh.uv.Length == 0) w.Add($"{n} has no UVs");
                if (r != null) { if (!hasBounds) { bounds = r.bounds; hasBounds = true; } else bounds.Encapsulate(r.bounds); }
            }
            if (!anyMesh) { w.Add("no meshes"); return w; }
            if (lod0 > b.maxTris) w.Add($"{lod0} triangles, over the {b.name} budget of {b.maxTris}");
            if (lod0 > b.lodFromTris && lods == 0) w.Add($"{lod0} triangles and no LODs (name them _LOD0, _LOD1, _LOD2 above {b.lodFromTris})");
            if (hasBounds)
            {
                float big = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                if (big > b.maxSize) w.Add($"{big:F2} m across, larger than {b.maxSize} m for {b.name}: wrong scale? (1 unit = 1 m)");
                if (big < b.minSize) w.Add($"{big:F3} m across, smaller than {b.minSize} m for {b.name}: wrong scale? (1 unit = 1 m)");
                if (b.originAtBase && (bounds.min.y < -0.02f || bounds.min.y > 0.02f)) w.Add($"origin is not at the base (lowest point at {bounds.min.y:F2} m)");
            }
            return w;
        }

        static List<string> CheckTexture(string path, int width, int height, AssetBudgets.Budget b)
        {
            var w = new List<string>();
            var n = Path.GetFileNameWithoutExtension(path);
            if (!n.StartsWith("T_")) w.Add("texture name should start with T_ and end with a map suffix (_BaseColor, _Normal, ...)");
            if (Mathf.Max(width, height) > b.maxTexture) w.Add($"{width}x{height}, over the {b.name} limit of {b.maxTexture}");
            if (!Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height)) w.Add($"{width}x{height} is not a power of two");
            return w;
        }

        // ---------- Whole-project check ----------

        [MenuItem("Alcax/Validate Art Assets")]
        public static void ValidateAll()
        {
            var sb = new StringBuilder();
            int problems = 0, files = 0;
            if (!AssetDatabase.IsValidFolder(AssetBudgets.ModelsRoot.TrimEnd('/'))) { Debug.Log(Tag + "no Art/Models folder yet"); return; }
            foreach (var guid in AssetDatabase.FindAssets("", new[] { AssetBudgets.ModelsRoot.TrimEnd('/') }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(p)) continue;
                var rel = p.Substring(AssetBudgets.ModelsRoot.Length).Split('/');
                var b = AssetBudgets.For(p, out var assetName);
                if (b == null)
                {
                    if (rel.Length >= 3) { sb.AppendLine($"{p}: unknown class folder '{rel[0]}' (use {string.Join(", ", AssetBudgets.ClassNames)})"); problems++; }
                    continue;
                }
                List<string> w = null;
                if (AssetImporter.GetAtPath(p) is ModelImporter)
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (go != null) w = CheckModel(go, b, assetName, Path.GetFileNameWithoutExtension(p));
                }
                else if (AssetImporter.GetAtPath(p) is TextureImporter)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                    if (tex != null) w = CheckTexture(p, tex.width, tex.height, b);
                }
                if (w == null) continue;
                files++;
                foreach (var x in w) { sb.AppendLine($"{p}: {x}"); problems++; }
            }
            if (problems == 0) Debug.Log(Tag + $"{files} art files checked, all within budget");
            else Debug.LogWarning(Tag + $"{problems} problems in {files} files\n" + sb);
        }
    }
}
