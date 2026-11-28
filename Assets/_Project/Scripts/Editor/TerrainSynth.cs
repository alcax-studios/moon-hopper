using UnityEngine;

namespace Alcax.EditorTools
{
    /// <summary>
    /// Generates Kelxor desert terrain: wind-built dunes (gentle windward side, steep slip face, sinuous crests),
    /// terraced rock mesas, flattened zones (pads, roads, building footprints), then hydraulic erosion (water droplets
    /// carving gullies into the rock) and thermal erosion (material sliding to its angle of repose). Works in world
    /// meters: origin is the terrain's min corner (x, z); heights are world y. Optionally blends its edges into an
    /// outside height function (a distant backdrop) so the seam doesn't show. Also returns the masks the painter and
    /// scatter use: rock, dune sand supply, and where eroded material was deposited.
    /// </summary>
    public class TerrainSynth
    {
        public struct Flat { public Rect rect; public float y; public float falloff; }

        public int res = 1025;           // heightmap resolution (2^n + 1)
        public float size = 256f;        // meters per side
        public Vector2 origin = Vector2.zero;   // world x, z of the min corner
        public float baseY = 6f;         // world height of the open desert floor
        public float maxHeight = 48f;    // height range of the TerrainData
        public int seed = 5071;
        public int droplets = 70000;
        public Vector2 wind = new Vector2(1f, 0.35f);
        // Where the rock rises: toward +z (rockFrom..rockTo), or in a ring around rockCenter (rockRing)
        public bool rockRing;
        public Vector2 rockCenter;
        public float rockFrom = 90f, rockTo = 230f;
        public float duneScale = 1f;
        public System.Collections.Generic.List<Flat> flats = new System.Collections.Generic.List<Flat>();
        public System.Func<float, float, float> edgeHeight;   // world height outside the terrain
        public float edgeBlend = 80f;
        /// <summary>Optional: (x, z) to (cap height, weight 0..1). Heights above the cap ease down to it (keeps a
        /// view open, e.g. from a window).</summary>
        public System.Func<float, float, Vector2> heightCap;

        public float[,] height;          // world y
        public float[,] rock, sand, deposit;
        public float TerrainY { get; private set; }   // world y of the TerrainData's 0

        float cell;
        System.Random rng;

        public float WorldX(int x) => origin.x + x * cell;
        public float WorldZ(int z) => origin.y + z * cell;

        float RockAmount(float wx, float wz)
        {
            float d = rockRing ? Vector2.Distance(new Vector2(wx, wz), rockCenter) : wz;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(rockFrom, rockTo, d));
        }

        public void Generate()
        {
            cell = size / (res - 1);
            rng = new System.Random(seed);
            height = new float[res, res];
            rock = new float[res, res];
            sand = new float[res, res];
            deposit = new float[res, res];
            var wd = wind.normalized;
            var across = new Vector2(-wd.y, wd.x);
            float ox = (float)rng.NextDouble() * 1000f, oz = (float)rng.NextDouble() * 1000f;

            // Pass 1: the shelf mask (ridged noise where the rock rises), blurred and re-thresholded so only shelves
            // of real size survive (isolated specks became tiny terraced cones)
            var shelfRaw = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = WorldX(x), wz = WorldZ(z);
                    float north = RockAmount(wx, wz);
                    float warp = Fbm(wx * 0.01f + 50f, wz * 0.01f + 80f, 3) * 25f;
                    float ridge = Ridged((wx + warp) * 0.011f + ox, (wz - warp) * 0.011f + oz, 4);
                    shelfRaw[x, z] = Mathf.InverseLerp(0.52f, 0.6f, ridge * (0.55f + 0.6f * north));
                }
            BoxBlur(shelfRaw, Mathf.RoundToInt(3f / cell));
            BoxBlur(shelfRaw, Mathf.RoundToInt(3f / cell));

            // Pass 2: compose base, mesas, and dunes
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float wx = WorldX(x), wz = WorldZ(z);
                    var p = new Vector2(wx, wz);
                    float north = RockAmount(wx, wz);
                    float h = Fbm(wx * 0.006f + ox, wz * 0.006f + oz, 4) * 7f + Fbm(wx * 0.025f + ox, wz * 0.025f, 3) * 1.2f;
                    float shelf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.62f, shelfRaw[x, z]));
                    float mesa = shelf * 11f;
                    float step = 1.8f;
                    float terr = Mathf.Floor(mesa / step) * step + Mathf.SmoothStep(0f, step, mesa % step) * 0.35f;
                    h += Mathf.Lerp(mesa, terr, 0.65f * shelf) + shelf * Fbm(wx * 0.08f, wz * 0.08f, 3) * 0.8f;
                    rock[x, z] = shelf;
                    float u = Vector2.Dot(p, wd), v = Vector2.Dot(p, across);
                    float lambda = 34f + Fbm(v * 0.01f + 9f, 3f, 2) * 10f;
                    float uu = u + Fbm(v * 0.018f + ox, 7f, 3) * 14f;
                    float f = Frac(uu / lambda);
                    float prof = f < 0.72f ? f / 0.72f : (1f - f) / 0.28f;   // long stoss, short lee
                    prof = prof * prof * (3f - 2f * prof);
                    float supply = Mathf.Clamp01(1f - shelf * 2.5f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, Fbm(wx * 0.007f + 21f, wz * 0.007f + 4f, 3) + 0.15f * (1f - north)));
                    float amp = 5.5f * duneScale * supply * (0.7f + 0.6f * Fbm(v * 0.02f, u * 0.004f, 2));
                    h += prof * amp;
                    sand[x, z] = supply;
                    height[x, z] = baseY + h - 3f;
                }

            ApplyCap();
            ApplyFlats();
            HydraulicErosion(droplets);
            SmoothDeposits(6);
            ThermalErosion(35);
            ApplyCap();
            ApplyFlats();      // keep erosion and sliding off the pads and floors
            BlendEdges();
            float lo = float.MaxValue;
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) lo = Mathf.Min(lo, height[x, z]);
            TerrainY = lo - 0.5f;
        }

        void ApplyCap()
        {
            if (heightCap == null) return;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    var c = heightCap(WorldX(x), WorldZ(z));
                    if (c.y <= 0f || height[x, z] <= c.x) continue;
                    // Soft: what's above the cap is mostly flattened, keeping a little of its shape
                    float over = height[x, z] - c.x;
                    float capped = c.x + over * 0.12f;
                    height[x, z] = Mathf.Lerp(height[x, z], capped, c.y);
                    sand[x, z] *= 1f - 0.5f * c.y;
                    rock[x, z] *= 1f - c.y;
                }
        }

        /// <summary>Flatten pads, roads, and footprints to their heights, easing out over each zone's falloff.</summary>
        void ApplyFlats()
        {
            foreach (var fl in flats)
                for (int z = 0; z < res; z++)
                    for (int x = 0; x < res; x++)
                    {
                        var p = new Vector2(WorldX(x), WorldZ(z));
                        float d = DistRect(p, fl.rect);
                        if (d > fl.falloff) continue;
                        float k = 1f - d / Mathf.Max(fl.falloff, 0.01f);
                        k = k * k * (3f - 2f * k);
                        height[x, z] = Mathf.Lerp(height[x, z], fl.y, k);
                        rock[x, z] *= 1f - k;
                        sand[x, z] *= 1f - k;
                    }
        }

        /// <summary>Ease the outer band into the outside height function so the terrain meets the backdrop.</summary>
        void BlendEdges()
        {
            if (edgeHeight == null) return;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x, res - 1 - x), Mathf.Min(z, res - 1 - z)) * cell;
                    if (d >= edgeBlend) continue;
                    float k = 1f - d / edgeBlend;
                    k = k * k * (3f - 2f * k);
                    height[x, z] = Mathf.Lerp(height[x, z], edgeHeight(WorldX(x), WorldZ(z)), k);
                    rock[x, z] *= 1f - k;
                }
        }

        static float DistRect(Vector2 p, Rect r)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax), dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>True inside a flat zone (plus a margin): nothing is scattered there.</summary>
        public bool InFlat(float wx, float wz, float margin)
        {
            var p = new Vector2(wx, wz);
            foreach (var fl in flats) if (DistRect(p, fl.rect) < margin) return true;
            return false;
        }

        // ---------- Erosion ----------

        /// <summary>Particle-based hydraulic erosion (droplets pick up sediment going downhill and drop it where they
        /// slow), weighted toward the rock so gullies form on the mesas; the deposits feed the sand mask.</summary>
        void HydraulicErosion(int drops)
        {
            const float inertia = 0.05f, capacityK = 4f, minCapacity = 0.01f, depositK = 0.3f, erodeK = 0.3f, evaporate = 0.015f, gravity = 4f;
            const int maxSteps = 48, radius = 3;
            // Brush weights
            var bx = new System.Collections.Generic.List<int>(); var bz = new System.Collections.Generic.List<int>(); var bw = new System.Collections.Generic.List<float>();
            float sumW = 0f;
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > radius) continue;
                    float w = 1f - d / radius;
                    bx.Add(dx); bz.Add(dz); bw.Add(w); sumW += w;
                }
            for (int i = 0; i < bw.Count; i++) bw[i] /= sumW;
            // Work in cell units (height / cell) so slopes are dimensionless
            float scale = 1f / cell;
            for (int d = 0; d < drops; d++)
            {
                float px = (float)rng.NextDouble() * (res - 2), pz = (float)rng.NextDouble() * (res - 2);
                int ix0 = (int)px, iz0 = (int)pz;
                if (rock[ix0, iz0] < 0.25f) continue;   // only on the rock (sand doesn't gully, and stray deposits left spikes)
                float dirX = 0f, dirZ = 0f, speed = 1f, water = 1f, sediment = 0f;
                for (int s = 0; s < maxSteps; s++)
                {
                    int ix = (int)px, iz = (int)pz;
                    float fx = px - ix, fz = pz - iz;
                    Gradient(px, pz, out float gx, out float gz, out float hOld);
                    dirX = dirX * inertia - gx * (1f - inertia);
                    dirZ = dirZ * inertia - gz * (1f - inertia);
                    float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
                    if (len < 1e-6f) break;
                    dirX /= len; dirZ /= len;
                    px += dirX; pz += dirZ;
                    if (px < 1 || pz < 1 || px >= res - 2 || pz >= res - 2) break;
                    // Off the rock: the runoff spreads what it carries as a soft fan and stops
                    if (rock[(int)px, (int)pz] < 0.1f)
                    {
                        int cx = (int)px, cz = (int)pz;
                        for (int b = 0; b < bw.Count; b++)
                        {
                            int x = cx + bx[b] * 2, z = cz + bz[b] * 2;
                            if (x < 0 || z < 0 || x >= res || z >= res) continue;
                            height[x, z] += sediment * bw[b] / scale;
                            deposit[x, z] += sediment * bw[b];
                        }
                        break;
                    }
                    Gradient(px, pz, out _, out _, out float hNew);
                    float dh = (hNew - hOld) * scale;
                    float capacity = Mathf.Max(-dh * speed * water * capacityK, minCapacity);
                    if (sediment > capacity || dh > 0f)
                    {
                        float amount = dh > 0f ? Mathf.Min(dh, sediment) : (sediment - capacity) * depositK;
                        sediment -= amount;
                        float a = amount / scale;
                        height[ix, iz] += a * (1 - fx) * (1 - fz);
                        height[ix + 1, iz] += a * fx * (1 - fz);
                        height[ix, iz + 1] += a * (1 - fx) * fz;
                        height[ix + 1, iz + 1] += a * fx * fz;
                        deposit[ix, iz] += amount;
                    }
                    else
                    {
                        float amount = Mathf.Min((capacity - sediment) * erodeK, -dh);
                        for (int b = 0; b < bw.Count; b++)
                        {
                            int x = ix + bx[b], z = iz + bz[b];
                            if (x < 0 || z < 0 || x >= res || z >= res) continue;
                            float take = amount * bw[b];
                            height[x, z] -= take / scale;
                            sediment += take;
                        }
                    }
                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed + dh * gravity));
                    water *= 1f - evaporate;
                }
            }
        }

        void Gradient(float px, float pz, out float gx, out float gz, out float h)
        {
            int x = (int)px, z = (int)pz;
            float fx = px - x, fz = pz - z;
            float h00 = height[x, z], h10 = height[x + 1, z], h01 = height[x, z + 1], h11 = height[x + 1, z + 1];
            gx = (h10 - h00) * (1 - fz) + (h11 - h01) * fz;
            gz = (h01 - h00) * (1 - fx) + (h11 - h10) * fx;
            gx /= cell; gz /= cell;
            h = h00 * (1 - fx) * (1 - fz) + h10 * fx * (1 - fz) + h01 * (1 - fx) * fz + h11 * fx * fz;
        }

        /// <summary>Separable box blur in place (radius in cells).</summary>
        void BoxBlur(float[,] m, int r)
        {
            if (r < 1) return;
            var tmp = new float[res, res];
            for (int z = 0; z < res; z++)
            {
                float acc = 0f; int n = 0;
                for (int x = -r; x <= r; x++) if (x >= 0 && x < res) { acc += m[x, z]; n++; }
                for (int x = 0; x < res; x++)
                {
                    tmp[x, z] = acc / n;
                    int xo = x - r, xi = x + r + 1;
                    if (xo >= 0) { acc -= m[xo, z]; n--; }
                    if (xi < res) { acc += m[xi, z]; n++; }
                }
            }
            for (int x = 0; x < res; x++)
            {
                float acc = 0f; int n = 0;
                for (int z = -r; z <= r; z++) if (z >= 0 && z < res) { acc += tmp[x, z]; n++; }
                for (int z = 0; z < res; z++)
                {
                    m[x, z] = acc / n;
                    int zo = z - r, zi = z + r + 1;
                    if (zo >= 0) { acc -= tmp[x, zo]; n--; }
                    if (zi < res) { acc += tmp[x, zi]; n++; }
                }
            }
        }

        /// <summary>Soften where sediment piled up, so deposits read as fans, not spikes.</summary>
        void SmoothDeposits(int passes)
        {
            for (int p = 0; p < passes; p++)
            {
                var copy = (float[,])height.Clone();
                for (int z = 1; z < res - 1; z++)
                    for (int x = 1; x < res - 1; x++)
                    {
                        float k = Mathf.Clamp01(deposit[x, z] * 4f);
                        if (k <= 0f) continue;
                        float avg = (copy[x - 1, z] + copy[x + 1, z] + copy[x, z - 1] + copy[x, z + 1] + copy[x, z] * 4f) / 8f;
                        height[x, z] = Mathf.Lerp(copy[x, z], avg, k);
                    }
            }
        }

        /// <summary>Material slides until slopes fall below the angle of repose: about 33 degrees for sand, steeper
        /// for rock (so cliffs stay, dunes soften).</summary>
        void ThermalErosion(int iterations)
        {
            float tanSand = Mathf.Tan(33f * Mathf.Deg2Rad) * cell, tanRock = Mathf.Tan(58f * Mathf.Deg2Rad) * cell;
            int[] ox = { 1, -1, 0, 0 }, oz = { 0, 0, 1, -1 };
            for (int it = 0; it < iterations; it++)
                for (int z = 1; z < res - 1; z++)
                    for (int x = 1; x < res - 1; x++)
                    {
                        float talus = Mathf.Lerp(tanSand, tanRock, Mathf.Clamp01(rock[x, z] * 1.5f));
                        float h = height[x, z];
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + ox[k], nz = z + oz[k];
                            float d = h - height[nx, nz];
                            if (d <= talus) continue;
                            float move = (d - talus) * 0.25f;
                            height[x, z] -= move;
                            height[nx, nz] += move;
                            deposit[nx, nz] += move * 0.2f;
                            h -= move;
                        }
                    }
        }

        // ---------- Queries ----------

        public float Sample(float wx, float wz)
        {
            float px = Mathf.Clamp((wx - origin.x) / cell, 0, res - 1.001f), pz = Mathf.Clamp((wz - origin.y) / cell, 0, res - 1.001f);
            int x = (int)px, z = (int)pz;
            float fx = px - x, fz = pz - z;
            return Mathf.Lerp(Mathf.Lerp(height[x, z], height[x + 1, z], fx), Mathf.Lerp(height[x, z + 1], height[x + 1, z + 1], fx), fz);
        }

        public float SlopeDeg(int x, int z)
        {
            int x0 = Mathf.Max(x - 1, 0), x1 = Mathf.Min(x + 1, res - 1), z0 = Mathf.Max(z - 1, 0), z1 = Mathf.Min(z + 1, res - 1);
            float gx = (height[x1, z] - height[x0, z]) / ((x1 - x0) * cell);
            float gz = (height[x, z1] - height[x, z0]) / ((z1 - z0) * cell);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        public float Cell => cell;

        // ---------- Noise ----------

        static float Frac(float v) => v - Mathf.Floor(v);

        static float Fbm(float x, float z, int oct)
        {
            float s = 0f, a = 0.5f, f = 1f, norm = 0f;
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + i * 17.1f, z * f - i * 9.3f); norm += a; a *= 0.5f; f *= 2.03f; }
            return s / norm;
        }

        static float Ridged(float x, float z, int oct)
        {
            float s = 0f, a = 0.5f, f = 1f, norm = 0f;
            for (int i = 0; i < oct; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * f + i * 31.7f, z * f + i * 5.9f) * 2f - 1f);
                s += a * n * n; norm += a; a *= 0.5f; f *= 2.1f;
            }
            return s / norm;
        }
    }
}
