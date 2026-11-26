using System.IO;
using Alcax.Core;
using UnityEditor;
using UnityEngine;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Procedural PBR textures for the station: tileable albedo, normal, and mask maps
    /// (mask: R = metallic, A = smoothness, matching URP Lit's _MetallicGlossMap), plus foliage cutouts.
    /// All colors come from the palette.
    /// </summary>
    public static class AtriumTextures
    {
        public const string Dir = BuildUtil.Root + "/Art/Textures/Atrium";

        public static Texture2D ConcreteA, ConcreteN, ConcreteM, ConcreteAO;   // Poly Haven concrete_slab_wall_02 (CC0)
        public static Texture2D PanelsA, PanelsN, PanelsM, PanelsAO;           // Poly Haven concrete_panels (CC0)
        public static Texture2D Blade;
        public static Texture2D FloorA, FloorN, FloorM;
        public static Texture2D MetalA, MetalN, MetalM;
        public static Texture2D SandA, SandN;
        public static Texture2D BarkA, BarkN;
        public static Texture2D Frond, Leaf, Ivy, Moss, Soil, Screen, Hazard;

        enum Kind { Color, ColorAlpha, Linear, Normal }

        public static void BuildAll()
        {
            BuildUtil.EnsureFolder(Dir);
            (ConcreteA, ConcreteN, ConcreteM, ConcreteAO) = ImportScan("concrete_slab_wall_02");
            (PanelsA, PanelsN, PanelsM, PanelsAO) = ImportScan("concrete_panels");
            Blade = BuildBlade();
            (AndoWallA, AndoWallN, AndoWallM) = BuildAndo("AndoWall", Palette.StConcreteTint, 0.016f, 1.6f, 0);
            (AndoFloorA, AndoFloorN, AndoFloorM) = BuildAndo("AndoFloor", Palette.StConcreteFloor, 0.011f, 0.6f, 7);
            BuildFloor();
            BuildMetal();
            BuildSand();
            BuildBark();
            Frond = BuildFrond();
            Leaf = BuildLeaf();
            Ivy = BuildIvy();
            Moss = BuildMoss();
            Soil = BuildSoil();
            Screen = BuildScreen();
            Hazard = BuildHazard();
        }

        public static Texture2D AndoWallA, AndoWallN, AndoWallM, AndoFloorA, AndoFloorN, AndoFloorM;

        /// <summary>The texture covers this many meters (2 x 4 panels of 1.82 x 0.91 m).</summary>
        public const float AndoTile = 3.64f;

        /// <summary>
        /// Architectural precast or board-tie concrete (Ando style): smooth, pale, dry panels of 1.82 x 0.91 m,
        /// butted together with hairline joints, each with six small form-tie holes near its corners and center.
        /// </summary>
        static (Texture2D a, Texture2D n, Texture2D m) BuildAndo(string name, Color baseColor, float holeRadius, float holeDepth, int seed, int size = 2048)
        {
            int S = size;
            const int px = 2, py = 4;
            float panelW = AndoTile / px, panelH = AndoTile / py;
            var a = new Color[S * S]; var m = new Color[S * S]; var hgt = new float[S * S];
            float[] hx = { 0.1f, 0.5f, 0.9f };
            float[] hy = { 0.2f, 0.8f };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    int ix = Mathf.FloorToInt(u * px), iy = Mathf.FloorToInt(v * py);
                    float pu = Frac(u * px), pv = Frac(v * py);
                    float du = Mathf.Min(pu, 1f - pu) * panelW, dv = Mathf.Min(pv, 1f - pv) * panelH;
                    float jd = Mathf.Min(du, dv);
                    float groove = 1f - SS(0.0015f, 0.004f, jd);
                    float edgeShade = 1f - SS(0f, 0.06f, jd);

                    // Tie holes
                    float hole = 0f, ring = 0f;
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 2; j++)
                        {
                            float ddx = (pu - hx[i]) * panelW, ddy = (pv - hy[j]) * panelH;
                            float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                            hole = Mathf.Max(hole, 1f - SS(holeRadius * 0.8f, holeRadius, d));
                            ring = Mathf.Max(ring, (1f - SS(holeRadius, holeRadius * 2.6f, d)) * (d > holeRadius ? 1f : 0f));
                        }

                    float panelTone = (Hash(ix + seed, iy, 401) - 0.5f) * 0.05f;
                    float mottle = Fbm(u, v, 3, 5, 411 + seed) - 0.5f;
                    float fine = Fbm(u, v, 64, 2, 421 + seed) - 0.5f;
                    float pore = Hash(x, y, 431 + seed) > 0.996f ? 1f : 0f;

                    float tone = 1f + panelTone + mottle * 0.09f + fine * 0.025f - edgeShade * 0.035f
                                 - groove * 0.28f - hole * 0.5f - ring * 0.05f - pore * 0.15f;
                    var c = baseColor * tone;
                    c.a = 1f;
                    a[y * S + x] = c;
                    hgt[y * S + x] = -groove * 1.2f - hole * holeDepth + mottle * 0.04f + fine * 0.02f - pore * 0.3f;
                    m[y * S + x] = Mask(0f, Mathf.Clamp01(0.3f + mottle * 0.05f - groove * 0.12f - hole * 0.2f));
                }
            var A = Save($"T_{name}_Albedo", a, S, S, Kind.Color);
            var N = Save($"T_{name}_Normal", NormalsFromHeight(hgt, S, S, 3f), S, S, Kind.Normal);
            var M = Save($"T_{name}_Mask", m, S, S, Kind.Linear);
            return (A, N, M);
        }

        // ---------- Tileable noise ----------

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        /// <summary>Periodic value noise: u,v in [0,1) tile seamlessly with the given lattice period.</summary>
        static float VNoise(float u, float v, int px, int py, int seed)
        {
            float x = u * px, y = v * py;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int X0 = ((x0 % px) + px) % px, Y0 = ((y0 % py) + py) % py;
            int X1 = (X0 + 1) % px, Y1 = (Y0 + 1) % py;
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int basePeriod, int octaves, int seed, float aspect = 1f)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int p = basePeriod;
            for (int o = 0; o < octaves; o++)
            {
                sum += VNoise(u, v, Mathf.Max(1, Mathf.RoundToInt(p * aspect)), p, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        /// <summary>Shader-style smoothstep(edge0, edge1, x). Unlike Mathf.SmoothStep, this is an edge test, not a blend.</summary>
        static float SS(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        // ---------- Saving ----------

        static Texture2D Save(string name, Color[] px, int w, int h, Kind kind, bool clamp = false)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, kind != Kind.Color && kind != Kind.ColorAlpha);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{Dir}/{name}.png";
            string full = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = kind == Kind.Normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = kind == Kind.Color || kind == Kind.ColorAlpha;
            imp.alphaSource = kind == Kind.Normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = kind == Kind.ColorAlpha;
            imp.mipmapEnabled = true;
            if (kind == Kind.ColorAlpha)
            {
                imp.mipMapsPreserveCoverage = true;
                imp.alphaTestReferenceValue = 0.5f;
            }
            imp.anisoLevel = 8;
            imp.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            imp.maxTextureSize = 2048;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Tangent-space normal map from a height field (wraps at the edges).</summary>
        static Color[] NormalsFromHeight(float[] hgt, int w, int h, float strength)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float l = hgt[y * w + (x - 1 + w) % w], r = hgt[y * w + (x + 1) % w];
                    float d = hgt[((y - 1 + h) % h) * w + x], u = hgt[((y + 1) % h) * w + x];
                    var n = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                    px[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            return px;
        }

        static Color Mask(float metallic, float smoothness) => new Color(metallic, 0f, 0f, smoothness);

        // ---------- Materials ----------

        /// <summary>
        /// Imports a Poly Haven scan (diffuse, OpenGL normal, roughness, AO) and builds a URP mask map
        /// (A = smoothness = 1 - roughness, R = metallic 0).
        /// </summary>
        static (Texture2D albedo, Texture2D normal, Texture2D mask, Texture2D ao) ImportScan(string id)
        {
            string dir = $"{BuildUtil.Root}/Art/Textures/PolyHaven/{id}";
            string diff = $"{dir}/{id}_diff_2k.jpg", nor = $"{dir}/{id}_nor_gl_2k.jpg";
            string rough = $"{dir}/{id}_rough_2k.jpg", aoPath = $"{dir}/{id}_ao_2k.jpg";

            void Configure(string path, TextureImporterType type, bool srgb, bool readable)
            {
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                if (imp == null) throw new System.Exception("Missing scan texture " + path);
                imp.textureType = type;
                imp.sRGBTexture = srgb;
                imp.isReadable = readable;
                imp.mipmapEnabled = true;
                imp.anisoLevel = 8;
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.maxTextureSize = 2048;
                imp.textureCompression = TextureImporterCompression.CompressedHQ;
                imp.SaveAndReimport();
            }
            Configure(diff, TextureImporterType.Default, true, false);
            Configure(nor, TextureImporterType.NormalMap, false, false);
            Configure(rough, TextureImporterType.Default, false, true);
            Configure(aoPath, TextureImporterType.Default, false, false);

            var roughTex = AssetDatabase.LoadAssetAtPath<Texture2D>(rough);
            // Read the roughness at full resolution through a temporary uncompressed copy.
            var rt = RenderTexture.GetTemporary(roughTex.width, roughTex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(roughTex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var src = tmp.GetPixels();
            Object.DestroyImmediate(tmp);
            var mask = new Color[src.Length];
            for (int i = 0; i < src.Length; i++) mask[i] = Mask(0f, Mathf.Clamp01(1f - src[i].r));
            int w = roughTex.width, h = roughTex.height;
            var m = Save($"T_{id}_Mask", mask, w, h, Kind.Linear);

            return (AssetDatabase.LoadAssetAtPath<Texture2D>(diff), AssetDatabase.LoadAssetAtPath<Texture2D>(nor),
                m, AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath));
        }

        /// <summary>
        /// Dark glossy tech floor (after the reference): 1 m tiles with grout seams, tone variation,
        /// a few grated service tiles, and wet "puddle" zones that are mirror-smooth. One tile is 4 m.
        /// </summary>
        static void BuildFloor()
        {
            const int S = 1024;
            var a = new Color[S * S]; var m = new Color[S * S]; var hgt = new float[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    int tx = Mathf.FloorToInt(u * 2f), ty = Mathf.FloorToInt(v * 2f);
                    float fu = Frac(u * 2f), fv = Frac(v * 2f);
                    float edge = Mathf.Min(Mathf.Min(fu, 1f - fu), Mathf.Min(fv, 1f - fv));
                    float seam = 1f - SS(0.002f, 0.005f, edge);
                    float tileTone = (Hash(tx, ty, 301) - 0.5f) * 0.06f;
                    bool grate = false;
                    float slot = grate && edge > 0.08f ? (Frac(fv * 14f) < 0.45f ? 1f : 0f) : 0f;
                    float cloud = Fbm(u, v, 4, 5, 321) - 0.5f;
                    float scratch = Mathf.Max(0f, Fbm(u, v, 3, 2, 331, 30f) - 0.62f) * 2f;
                    float speck = Hash(x, y, 351);
                    float agg = speck > 0.982f ? 0.12f : (speck < 0.015f ? -0.14f : 0f);
                    float tone = 1f + tileTone + cloud * 0.12f - scratch * 0.04f - seam * 0.3f + agg;
                    var c = Palette.StConcreteFloor * tone;   // dry, honed, light concrete
                    c.a = 1f;
                    a[y * S + x] = c;
                    hgt[y * S + x] = -seam * 1.4f - slot * 1.2f + cloud * 0.05f;
                    float smooth = 0.26f + cloud * 0.06f - scratch * 0.06f - seam * 0.15f;
                    m[y * S + x] = Mask(grate ? 0.6f : 0.05f, Mathf.Clamp01(smooth));
                }
            FloorA = Save("T_Floor_Albedo", a, S, S, Kind.Color);
            FloorN = Save("T_Floor_Normal", NormalsFromHeight(hgt, S, S, 2.5f), S, S, Kind.Normal);
            FloorM = Save("T_Floor_Mask", m, S, S, Kind.Linear);
        }

        /// <summary>A single long, narrow blade leaf (yucca/dracaena), cutout.</summary>
        static Texture2D BuildBlade()
        {
            const int W = 128, H = 512;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W * 2f - 1f, v = y / (float)H;
                    float half = 0.85f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(v * 0.98f + 0.02f)), 0.6f) * (1f - v * 0.5f);
                    float alpha = Mathf.Abs(u) < half ? 1f : 0f;
                    float rib = Mathf.Abs(u) < 0.06f ? 1f : 0f;
                    float stripe = Mathf.Abs(u) > half * 0.7f ? 1f : 0f;
                    var c = Color.Lerp(Palette.StFoliage, Palette.StFoliageDark, v * 0.4f);
                    c = Color.Lerp(c, Palette.StFoliageLight, rib * 0.4f + stripe * 0.25f);
                    c.a = alpha;
                    px[y * W + x] = c;
                }
            return Save("T_Blade", px, W, H, Kind.ColorAlpha, clamp: true);
        }

        /// <summary>Tech metal paneling: 2x2 panels per tile with seams, corner fasteners, brushing, and edge wear. One tile is 2 m.</summary>
        static void BuildMetal()
        {
            const int S = 512;
            var a = new Color[S * S]; var m = new Color[S * S]; var hgt = new float[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    float pu = Frac(u * 2f), pv = Frac(v * 2f);
                    float edge = Mathf.Min(Mathf.Min(pu, 1f - pu), Mathf.Min(pv, 1f - pv));
                    float seam = 1f - SS(0.004f, 0.009f, edge);
                    float bevel = 1f - SS(0.009f, 0.03f, edge);
                    float bu = Mathf.Min(pu, 1f - pu) - 0.05f, bvv = Mathf.Min(pv, 1f - pv) - 0.05f;
                    float bolt = 1f - SS(0.008f, 0.012f, Mathf.Sqrt(bu * bu + bvv * bvv));
                    float brush = Fbm(u, v, 2, 5, 101, 40f) - 0.5f;
                    float grime = Fbm(u, v, 4, 4, 111);
                    int panel = Mathf.FloorToInt(u * 2f) + Mathf.FloorToInt(v * 2f) * 7;
                    float panelTone = (Hash(panel, 3, 121) - 0.5f) * 0.12f;
                    float tone = 1f + brush * 0.04f + panelTone * 0.5f - seam * 0.45f + bevel * 0.05f - bolt * 0.15f - grime * 0.06f;
                    var c = Palette.StMetal * tone;
                    c.a = 1f;
                    a[y * S + x] = c;
                    hgt[y * S + x] = -seam * 1.5f + bevel * 0.3f + bolt * 0.8f + brush * 0.05f;
                    m[y * S + x] = Mask(0.15f, Mathf.Clamp01(0.34f + brush * 0.1f - grime * 0.1f - seam * 0.2f));
                }
            MetalA = Save("T_Metal_Albedo", a, S, S, Kind.Color);
            MetalN = Save("T_Metal_Normal", NormalsFromHeight(hgt, S, S, 4f), S, S, Kind.Normal);
            MetalM = Save("T_Metal_Mask", m, S, S, Kind.Linear);
        }

        /// <summary>Wind-rippled sand. One tile is 8 m.</summary>
        static void BuildSand()
        {
            const int S = 512;
            var a = new Color[S * S]; var hgt = new float[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    float warp = Fbm(u, v, 4, 3, 131) * 2f;
                    float ripple = Mathf.Sin((v * 22f + warp) * Mathf.PI * 2f) * 0.5f + 0.5f;
                    float grain = Hash(x, y, 141) - 0.5f;
                    float cloud = Fbm(u, v, 3, 4, 151) - 0.5f;
                    var c = Color.Lerp(Palette.StSandShadow, Palette.StSand, 0.7f + ripple * 0.2f + cloud * 0.3f) * (1f + grain * 0.06f);
                    c.a = 1f;
                    a[y * S + x] = c;
                    hgt[y * S + x] = ripple * 0.6f + grain * 0.1f;
                }
            SandA = Save("T_Sand_Albedo", a, S, S, Kind.Color);
            SandN = Save("T_Sand_Normal", NormalsFromHeight(hgt, S, S, 2f), S, S, Kind.Normal);
        }

        /// <summary>Palm bark: vertical fibers with horizontal leaf-scar rings.</summary>
        static void BuildBark()
        {
            const int S = 512;
            var a = new Color[S * S]; var hgt = new float[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    float fiber = Fbm(u, v, 24, 3, 161, 1f / 8f) - 0.5f;
                    float ring = Mathf.Pow(Mathf.Abs(Mathf.Sin(v * Mathf.PI * 10f + Fbm(u, v, 4, 2, 171) * 2f)), 6f);
                    float tone = 1f + fiber * 0.25f - ring * 0.3f;
                    var c = Palette.StBark * tone;
                    c.a = 1f;
                    a[y * S + x] = c;
                    hgt[y * S + x] = fiber * 0.6f - ring;
                }
            BarkA = Save("T_Bark_Albedo", a, S, S, Kind.Color);
            BarkN = Save("T_Bark_Normal", NormalsFromHeight(hgt, S, S, 3f), S, S, Kind.Normal);
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var pa = p - a; var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / ba.sqrMagnitude);
            return (pa - ba * h).magnitude;
        }

        /// <summary>A palm or fern frond: a central rib with tapered leaflets angled toward the tip (alpha cutout).</summary>
        static Texture2D BuildFrond()
        {
            const int S = 512;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(x / (float)S, y / (float)S);
                    float alpha = 0f, shade = 0f;
                    // Rib along the center from the bottom (base) to the top (tip)
                    float rib = Mathf.Abs(p.x - 0.5f);
                    if (rib < 0.012f * (1.2f - p.y)) { alpha = 1f; shade = -0.2f; }
                    // Leaflets
                    for (int i = 0; i < 26 && alpha < 1f; i++)
                    {
                        float t = 0.05f + i / 26f * 0.92f;
                        float len = 0.46f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.05f)) + 0.04f;
                        foreach (int side in new[] { -1, 1 })
                        {
                            var a0 = new Vector2(0.5f, t);
                            var dir = new Vector2(side * 0.82f, 0.57f).normalized;
                            var a1 = a0 + dir * len;
                            var pa = p - a0;
                            float along = Mathf.Clamp01(Vector2.Dot(pa, dir) / len);
                            float width = 0.022f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(along * 0.95f + 0.05f)) + 0.002f;
                            if (SegDist(p, a0, a1) < width) { alpha = 1f; shade = along * 0.15f + (Hash(i, side, 181) - 0.5f) * 0.12f; break; }
                        }
                    }
                    var c = Color.Lerp(Palette.StFoliageDark, Palette.StFoliage, 0.55f + shade + p.y * 0.15f);
                    c = Color.Lerp(c, Palette.StFoliageLight, Mathf.Clamp01(p.y - 0.6f) * 0.5f);
                    c.a = alpha;
                    px[y * S + x] = c;
                }
            return Save("T_Frond", px, S, S, Kind.ColorAlpha, clamp: true);
        }

        /// <summary>A broad split leaf (monstera-like): a heart-shaped blade with veins and fenestrations.</summary>
        static Texture2D BuildLeaf()
        {
            const int S = 512;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S * 2f - 1f, v = y / (float)S;
                    float halfW = Mathf.Sin(Mathf.PI * Mathf.Pow(v, 0.8f)) * 0.92f * (1f - v * 0.25f);
                    float alpha = Mathf.Abs(u) < halfW ? 1f : 0f;
                    // Fenestrations: slits from the edge inward between the veins
                    float vein = Mathf.Abs(Frac(v * 7f + Mathf.Abs(u) * 1.4f) - 0.5f);
                    if (alpha > 0f && Mathf.Abs(u) > halfW * 0.55f && vein < 0.07f && v > 0.15f && v < 0.9f) alpha = 0f;
                    float midrib = Mathf.Abs(u) < 0.02f ? 1f : 0f;
                    float veinLine = vein > 0.45f ? 0.5f : 0f;
                    float spec = Fbm(x / (float)S, v, 8, 3, 191) - 0.5f;
                    var c = Color.Lerp(Palette.StFoliageDark, Palette.StFoliage, 0.65f + spec * 0.3f);
                    c = Color.Lerp(c, Palette.StFoliageLight, midrib * 0.6f + veinLine * 0.15f);
                    c.a = alpha;
                    px[y * S + x] = c;
                }
            return Save("T_Leaf", px, S, S, Kind.ColorAlpha, clamp: true);
        }

        /// <summary>A hanging ivy strip: small leaves scattered along a vertical stem.</summary>
        static Texture2D BuildIvy()
        {
            const int W = 256, H = 1024;
            var px = new Color[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = Palette.WithAlpha(Palette.StFoliageDark, 0f);
            var r = new System.Random(7);
            for (int k = 0; k < 140; k++)
            {
                float cy = (float)r.NextDouble() * H;
                float cx = W * 0.5f + (float)(r.NextDouble() - 0.5) * W * 0.6f;
                float rad = 14f + (float)r.NextDouble() * 16f;
                float ang = (float)r.NextDouble() * Mathf.PI;
                var col = Color.Lerp(Palette.StFoliageDark, Palette.StFoliageLight, (float)r.NextDouble() * 0.7f);
                for (int y = Mathf.Max(0, (int)(cy - rad)); y < Mathf.Min(H, (int)(cy + rad)); y++)
                    for (int x = Mathf.Max(0, (int)(cx - rad)); x < Mathf.Min(W, (int)(cx + rad)); x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float rx = dx * Mathf.Cos(ang) - dy * Mathf.Sin(ang), ry = dx * Mathf.Sin(ang) + dy * Mathf.Cos(ang);
                        if ((rx * rx) / (rad * rad) + (ry * ry) / (rad * rad * 0.35f) < 1f)
                        {
                            var c = col * (0.9f + 0.2f * (1f - Mathf.Abs(rx) / rad));
                            c.a = 1f;
                            px[y * W + x] = c;
                        }
                    }
            }
            for (int y = 0; y < H; y++)
            {
                int sx = (int)(W * 0.5f + Mathf.Sin(y * 0.02f) * 10f);
                for (int x = sx - 2; x <= sx + 2; x++) { var c = Palette.StBark; c.a = 1f; px[y * W + x] = c; }
            }
            return Save("T_Ivy", px, W, H, Kind.ColorAlpha, clamp: true);
        }

        static Texture2D BuildMoss()
        {
            const int S = 512;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    float clump = Fbm(u, v, 8, 4, 201);
                    float fine = Hash(x, y, 211);
                    var c = Color.Lerp(Palette.StFoliageDark, Palette.StFoliageLight, SS(0.3f, 0.8f, clump) * 0.8f + fine * 0.1f);
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            return Save("T_Moss", px, S, S, Kind.Color);
        }

        static Texture2D BuildSoil()
        {
            const int S = 512;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    float n = Fbm(u, v, 12, 4, 221);
                    float pebble = Fbm(u, v, 40, 1, 231) > 0.78f ? 0.35f : 0f;
                    var c = Palette.StSoil * (0.8f + n * 0.4f + pebble);
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            return Save("T_Soil", px, S, S, Kind.Color);
        }

        /// <summary>An emissive status screen: graphs, bars, and text-like rows in cyan on near-black.</summary>
        static Texture2D BuildScreen()
        {
            const int W = 512, H = 256;
            var px = new Color[W * H];
            var bg = Palette.UIDark * 0.5f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W, v = y / (float)H;
                    Color c = bg;
                    // Border
                    if (u < 0.01f || u > 0.99f || v < 0.02f || v > 0.98f) c = Palette.StTechCyan * 0.6f;
                    // Line graph (left half)
                    if (u > 0.05f && u < 0.5f && v > 0.45f && v < 0.9f)
                    {
                        float g = 0.62f + 0.18f * Mathf.Sin(u * 40f) * Mathf.Sin(u * 7f + 1f);
                        if (Mathf.Abs(v - g) < 0.008f) c = Palette.StTechCyan;
                        if (Mathf.Abs(Frac(v * 10f) - 0.5f) < 0.02f) c = Color.Lerp(c, Palette.StTechCyan, 0.15f);
                    }
                    // Bars (right half)
                    if (u > 0.56f && u < 0.95f && v > 0.45f && v < 0.9f)
                    {
                        int bar = Mathf.FloorToInt((u - 0.56f) / 0.039f);
                        float bh = 0.5f + Hash(bar, 1, 241) * 0.38f;
                        if (Frac((u - 0.56f) / 0.039f) < 0.7f && v < bh) c = Color.Lerp(Palette.StTechCyan, Palette.StHazard, bar == 6 ? 1f : 0f) * 0.8f;
                    }
                    // Text rows (bottom)
                    if (v > 0.08f && v < 0.38f && u > 0.05f && u < 0.95f)
                    {
                        int row = Mathf.FloorToInt((v - 0.08f) / 0.06f);
                        int word = Mathf.FloorToInt(u * 40f);
                        bool on = Hash(word, row, 251) > 0.3f && Frac((v - 0.08f) / 0.06f) < 0.5f;
                        float rowLen = 0.4f + Hash(row, 0, 261) * 0.5f;
                        if (on && u < rowLen) c = Palette.StTechCyan * 0.7f;
                    }
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            return Save("T_Screen", px, W, H, Kind.Color, clamp: true);
        }

        static Texture2D BuildHazard()
        {
            const int S = 256;
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = x / (float)S, v = y / (float)S;
                    bool stripe = Frac((u + v) * 4f) < 0.5f;
                    float wear = Fbm(u, v, 8, 3, 271);
                    var c = stripe ? Palette.StHazard : Palette.UIDark;
                    c = Color.Lerp(c, Palette.StMetal, SS(0.65f, 0.8f, wear));
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            return Save("T_Hazard", px, S, S, Kind.Color);
        }
    }
}
