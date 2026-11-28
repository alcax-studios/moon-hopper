using System.IO;
using Alcax.FX;
using UnityEditor;
using UnityEngine;
using static Alcax.EditorTools.BuildUtil;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Builds the effect textures and materials and Resources/FXLibrary (menu: Alcax > Rebuild FX Assets).
    /// Textures are generated (glow, spark, arc, scorch). The smoke flipbook is kept if one exists in
    /// Art/FX/Textures/T_FX_Smoke.png (rendered from a fluid simulation in Blender), otherwise a procedural one is made.
    /// </summary>
    public static class FXAssets
    {
        const string Dir = Root + "/Art/FX";

        [MenuItem("Alcax/Rebuild FX Assets")]
        public static void BuildAll()
        {
            EnsureFolder(Dir + "/Textures");
            EnsureFolder(Dir + "/Materials");
            EnsureFolder(Root + "/Resources");
            var glowTex = Tex("T_FX_Glow", 128, (u, v) => { float r = Len(u, v); float a = Mathf.Exp(-r * r * 9f) * 0.75f + Mathf.Exp(-r * r * 40f) * 0.25f; return new Color(1, 1, 1, Edge(r) * a); });
            var sparkTex = Tex("T_FX_Spark", 64, (u, v) => { float r = Len(u, v); return new Color(1, 1, 1, Edge(r) * Mathf.Exp(-r * r * 22f)); });
            var arcTex = Tex("T_FX_Arc", 64, (u, v) => { float d = Mathf.Abs(v - 0.5f) * 2f; float a = Mathf.Exp(-d * d * 18f) * 0.6f + Mathf.Exp(-d * d * 120f) * 0.4f; return new Color(1, 1, 1, a); });
            var scorchTex = Tex("T_FX_Scorch", 256, Scorch);
            var smokePath = Dir + "/Textures/T_FX_Smoke.png";
            if (!File.Exists(smokePath)) ProceduralSmoke(smokePath, 8, 256);
            AssetDatabase.ImportAsset(smokePath, ImportAssetOptions.ForceUpdate);
            SetupTexture(smokePath, true);
            var smokeTex = AssetDatabase.LoadAssetAtPath<Texture2D>(smokePath);

            var lib = AssetDatabase.LoadAssetAtPath<FXLibrary>(Root + "/Resources/FXLibrary.asset");
            if (lib == null) { lib = ScriptableObject.CreateInstance<FXLibrary>(); AssetDatabase.CreateAsset(lib, Root + "/Resources/FXLibrary.asset"); }
            lib.glow = Mat("M_FX_Glow", "Alcax/FX Additive", glowTex, m => { m.SetFloat("_Intensity", 6f); m.SetFloat("_SoftFade", 0.12f); });
            lib.spark = Mat("M_FX_Spark", "Alcax/FX Additive", sparkTex, m => { m.SetFloat("_Intensity", 9f); m.SetFloat("_SoftFade", 0.02f); });
            lib.arc = Mat("M_FX_Arc", "Alcax/FX Additive", arcTex, m => { m.SetFloat("_Intensity", 14f); m.SetFloat("_SoftFade", 0.0f); });
            lib.smoke = Mat("M_FX_Smoke", "Alcax/FX Smoke Lit", smokeTex, m => { m.SetFloat("_Density", 1f); m.SetFloat("_Wrap", 0.5f); m.SetFloat("_Ambient", 1f); m.SetFloat("_SoftFade", 0.3f); });
            lib.decal = Mat("M_FX_Scorch", "Alcax/FX Decal", scorchTex, m => { m.SetColor("_HotColor", new Color(2.5f, 6f, 8f, 1f)); });
            lib.sandGrain = Mat("M_FX_SandGrain", "Alcax/FX Smoke Lit", sparkTex, m => { m.SetFloat("_Density", 1.4f); m.SetFloat("_Wrap", 0.6f); m.SetFloat("_Ambient", 1.1f); m.SetFloat("_SoftFade", 0.02f); });
            lib.sandVeil = Mat("M_FX_SandVeil", "Alcax/FX Smoke Lit", smokeTex, m => { m.SetFloat("_Density", 1f); m.SetFloat("_Wrap", 0.7f); m.SetFloat("_Ambient", 1.1f); m.SetFloat("_SoftFade", 0.8f); });
            lib.smokeTilesX = lib.smokeTilesY = 8;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log("[FXAssets] FX library built");
        }

        static float Len(float u, float v) { float x = u * 2f - 1f, y = v * 2f - 1f; return Mathf.Sqrt(x * x + y * y); }
        static float Edge(float r) => Mathf.Clamp01((1f - r) * 6f);

        static Color Scorch(float u, float v)
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float r = Mathf.Sqrt(x * x + y * y);
            float ang = Mathf.Atan2(y, x);
            // A ragged, streaked burn: radial streaks and noise on the edge
            float streak = Mathf.PerlinNoise(ang * 3f + 10f, 0.5f) * 0.35f + Mathf.PerlinNoise(ang * 9f, 2.5f) * 0.2f;
            float edgeNoise = Mathf.PerlinNoise(u * 9f, v * 9f) * 0.3f;
            float rr = r / (0.62f + streak + edgeNoise * 0.6f);
            float a = Mathf.Clamp01(1f - rr) ;
            a = a * a * (3f - 2f * a);
            float core = Mathf.Clamp01(1f - r * 2.2f);
            float soot = Mathf.Lerp(0.16f, 0.03f, core) * (0.85f + Mathf.PerlinNoise(u * 30f, v * 30f) * 0.3f);
            return new Color(soot * 1.05f, soot, soot * 0.92f, a * 0.92f);
        }

        static Texture2D Tex(string name, int size, System.Func<float, float, Color> f)
        {
            var path = $"{Dir}/Textures/{name}.png";
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + 0.5f) / size, (y + 0.5f) / size);
            t.SetPixels(px);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            SetupTexture(path, false);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void SetupTexture(string path, bool mips)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = mips || true;
            ti.sRGBTexture = true;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Material Mat(string name, string shader, Texture2D tex, System.Action<Material> set)
        {
            var path = $"{Dir}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_BaseMap", tex);
            set(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A placeholder smoke flipbook (tiles x tiles frames): a noise puff that billows and thins out.</summary>
        static void ProceduralSmoke(string path, int tiles, int frame)
        {
            int size = tiles * frame;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var px = new Color[size * size];
            for (int f = 0; f < tiles * tiles; f++)
            {
                float k = f / (tiles * tiles - 1f);
                int fx = f % tiles, fy = tiles - 1 - f / tiles;   // frames read left to right, top to bottom
                float radius = Mathf.Lerp(0.45f, 0.95f, Mathf.Sqrt(k));
                float density = Mathf.Lerp(1f, 0.15f, k * k);
                for (int y = 0; y < frame; y++)
                    for (int x = 0; x < frame; x++)
                    {
                        float u = (x + 0.5f) / frame * 2f - 1f, v = (y + 0.5f) / frame * 2f - 1f;
                        float n = 0f, amp = 0.5f, freq = 2.2f;
                        for (int o = 0; o < 5; o++)
                        {
                            n += amp * Mathf.PerlinNoise(u * freq + 17.3f + k * 1.6f * (o + 1), v * freq + 4.1f - k * 2.2f);
                            amp *= 0.5f; freq *= 2.1f;
                        }
                        float r = Mathf.Sqrt(u * u + v * v) / radius;
                        float shape = Mathf.Clamp01(1f - r * r) * Mathf.Clamp01(n * 1.9f - 0.35f - k * 0.25f);
                        float a = Mathf.Clamp01(shape * density * 1.6f);
                        float shade = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(n + 0.2f + v * 0.25f));
                        px[(fy * frame + y) * size + fx * frame + x] = new Color(shade, shade, shade, a);
                    }
            }
            t.SetPixels(px);
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
        }
    }
}
