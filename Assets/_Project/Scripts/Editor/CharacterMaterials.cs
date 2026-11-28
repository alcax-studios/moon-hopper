using System.IO;
using UnityEditor;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Materials for characters in Art/Models/Characters (from Tools/blender/characters/human_npc.py): skin on
    /// Alcax/Skin Lit (with its scatter mask and a shared pore normal), hair, eyebrows, and lashes on Alcax/Hair Lit,
    /// eyes glossy, teeth and tongue, and clothing on URP Lit with an outfit tint. Matched by the part name at the end
    /// of each material in the FBX (M_&lt;Name&gt;_&lt;Part&gt;). Menu: Alcax > Art > Build Character Materials.
    /// </summary>
    public static class CharacterMaterials
    {
        const string Dir = Root + "/Art/Models/Characters";
        const string PorePath = Dir + "/_Shared/T_Skin_Pores_Normal.png";

        // Outfit tints per character (the clothing scan recolored to a uniform)
        static readonly System.Collections.Generic.Dictionary<string, Color> OutfitTint = new()
        {
            // Station security: the work coverall dyed a dark navy uniform
            { "SK_Human_Security01", new Color(0.3f, 0.36f, 0.52f) },
            { "SK_Human_Security02", new Color(0.3f, 0.36f, 0.52f) },
            { "SK_Human_Security03", new Color(0.3f, 0.36f, 0.52f) },
        };

        [MenuItem("Alcax/Art/Build Character Materials")]
        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(Dir)) return;
            var pores = PoreNormal();
            foreach (var folder in AssetDatabase.GetSubFolders(Dir))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith("_")) continue;
                Build(folder, name, pores);
            }
            AssetDatabase.SaveAssets();
        }

        static void Build(string folder, string name, Texture2D pores)
        {
            var fbxPath = $"{folder}/{name}.fbx";
            if (!(AssetImporter.GetAtPath(fbxPath) is ModelImporter mi)) return;
            EnsureFolder(folder + "/Materials");
            string stem = "T_" + name.Substring(3);
            Texture2D T(string part, string map, bool linear = false, bool normal = false)
            {
                foreach (var ext in new[] { ".png", ".jpg" })
                {
                    var p = $"{folder}/Textures/{stem}_{part}_{map}{ext}";
                    if (!File.Exists(p)) continue;
                    if (AssetImporter.GetAtPath(p) is TextureImporter ti)
                    {
                        bool dirty = false;
                        if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; dirty = true; }
                        if (linear && ti.sRGBTexture) { ti.sRGBTexture = false; dirty = true; }
                        if (!normal && !linear && !ti.alphaIsTransparency) { ti.alphaIsTransparency = true; dirty = true; }
                        if (dirty) ti.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
                return null;
            }
            // Start from the FBX's own materials: clear earlier remaps (they hide the embedded materials)
            foreach (var kv in mi.GetExternalObjectMap())
                if (kv.Key.type == typeof(Material)) mi.RemoveRemap(kv.Key);
            mi.SaveAndReimport();
            var tint = OutfitTint.TryGetValue(name, out var tc) ? tc : Color.white;
            int n = 0;
            foreach (var src in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (!(src is Material sm)) continue;
                string part = sm.name.Substring(("M_" + name.Substring(3) + "_").Length);
                part = System.Text.RegularExpressions.Regex.Replace(part, @"\.\d+$", "");   // Blender name suffixes (.001)
                string key = part.ToLowerInvariant();
                Material m;
                if (key == "body")
                {
                    m = Mat(folder, sm.name, "Alcax/Skin Lit");
                    m.SetTexture("_BaseMap", T(part, "BaseColor"));
                    var sss = T(part, "SSS", linear: true);
                    if (sss) m.SetTexture("_ScatterMask", sss);
                    m.SetTexture("_PoreMap", pores);
                    m.SetFloat("_ScatterStrength", 0.35f);   // subtle: strong scatter reads as a red shadow
                    m.SetFloat("_ScatterWrap", 0.4f);
                    m.SetColor("_ScatterColor", new Color(0.8f, 0.32f, 0.22f));
                }
                else if (key.Contains("eyebrow") || key.Contains("eyelash") || IsHair(key))
                {
                    m = Mat(folder, sm.name, "Alcax/Hair Lit");
                    m.SetTexture("_BaseMap", T(part, "BaseColor"));
                    m.SetFloat("_Cutoff", key.Contains("eyelash") ? 0.3f : 0.45f);
                    // Soft, broad highlights: sharp strong ones read as plastic (3D-printed) hair
                    m.SetFloat("_SpecStrength", IsHair(key) ? 0.16f : 0.08f);
                    m.SetFloat("_Exp1", 60f);
                    m.SetFloat("_Exp2", 14f);
                    m.SetColor("_SpecColor1", new Color(0.55f, 0.52f, 0.48f));
                }
                else
                {
                    m = Mat(folder, sm.name, "Universal Render Pipeline/Lit");
                    m.SetTexture("_BaseMap", T(part, "BaseColor"));
                    var nrm = T(part, "Normal", normal: true);
                    if (nrm) { m.SetTexture("_BumpMap", nrm); m.EnableKeyword("_NORMALMAP"); }
                    bool eye = key.Contains("high-poly") || key.Contains("low-poly") || key.Contains("eye");
                    bool cloth = key.Contains("suit") || key.Contains("shoe") || key.Contains("shirt") || key.Contains("pants");
                    m.SetFloat("_Smoothness", eye ? 0.62f : key.Contains("teeth") ? 0.6f : key.Contains("tongue") ? 0.55f : cloth ? 0.25f : 0.35f);
                    m.SetColor("_BaseColor", cloth && !key.Contains("shoe") ? tint : Color.white);
                    if (eye)
                    {
                        // The cornea dome is see-through in the eye texture's alpha: cut it out so the iris shows
                        m.SetFloat("_AlphaClip", 1f);
                        m.SetFloat("_Cutoff", 0.5f);
                        m.EnableKeyword("_ALPHATEST_ON");
                        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
                    }
                }
                EditorUtility.SetDirty(m);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), sm.name), m);
                n++;
            }
            mi.SaveAndReimport();
            Debug.Log($"[CharacterMaterials] {name}: {n} materials");
        }

        static bool IsHair(string key) => key.StartsWith("short") || key.StartsWith("long") || key.Contains("hair") || key.Contains("bob") || key.Contains("ponytail") || key.Contains("braid") || key.Contains("afro");

        static Material Mat(string folder, string name, string shader)
        {
            var path = $"{folder}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            return m;
        }

        /// <summary>A tileable pore and fine-wrinkle normal map (generated once): small pits and faint creases.</summary>
        static Texture2D PoreNormal()
        {
            if (File.Exists(PorePath)) return AssetDatabase.LoadAssetAtPath<Texture2D>(PorePath);
            EnsureFolder(Dir + "/_Shared");
            const int S = 512;
            var h = new float[S, S];
            var rng = new System.Random(11);
            for (int k = 0; k < 2600; k++)
            {
                int cx = rng.Next(S), cy = rng.Next(S);
                float r = 1.2f + (float)rng.NextDouble() * 1.8f, depth = 0.6f + (float)rng.NextDouble() * 0.6f;
                int ri = Mathf.CeilToInt(r * 2.5f);
                for (int dy = -ri; dy <= ri; dy++)
                    for (int dx = -ri; dx <= ri; dx++)
                    {
                        float d2 = (dx * dx + dy * dy) / (r * r);
                        h[(cx + dx + S) % S, (cy + dy + S) % S] -= depth * Mathf.Exp(-d2);
                    }
            }
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    h[x, y] += 0.25f * Mathf.Sin((x * 0.11f + Mathf.Sin(y * 0.05f) * 3f)) * Mathf.Sin(y * 0.02f);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false, true);
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = h[(x + 1) % S, y] - h[(x - 1 + S) % S, y];
                    float dy = h[x, (y + 1) % S] - h[x, (y - 1 + S) % S];
                    var nrm = new Vector3(-dx * 0.6f, -dy * 0.6f, 1f).normalized;
                    px[y * S + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
                }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(PorePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PorePath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(PorePath);
            ti.textureType = TextureImporterType.NormalMap;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PorePath);
        }
    }
}
