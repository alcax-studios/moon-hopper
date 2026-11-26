using UnityEngine;

namespace Alcax.Audio
{
    /// <summary>
    /// Builds placeholder sound effects from code (no audio assets).
    /// Every clip gets a 5 ms fade at both ends so the edges never click.
    /// </summary>
    public static partial class AudioSynth
    {
        public const int Rate = 44100;

        static System.Random rng = new System.Random(1234);
        static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);

        static AudioClip Make(string name, float[] data)
        {
            // Very short fade-in (keeps transients sharp) and a 5 ms fade-out.
            int fadeIn = Mathf.Min(data.Length / 2, Rate / 1600);
            int fadeOut = Mathf.Min(data.Length / 2, Rate / 200);
            for (int i = 0; i < fadeIn; i++) data[i] *= i / (float)fadeIn;
            for (int i = 0; i < fadeOut; i++) data[data.Length - 1 - i] *= i / (float)fadeOut;
            float peak = 0f;
            foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0.98f) for (int i = 0; i < data.Length; i++) data[i] *= 0.98f / peak;

            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float[] Buffer(float seconds) => new float[Mathf.Max(1, Mathf.CeilToInt(seconds * Rate))];

        /// <summary>Adds a pitched tone with an exponential decay.</summary>
        static void AddTone(float[] d, float start, float dur, float f0, float f1, float amp, float decay, int wave = 0)
        {
            int s0 = (int)(start * Rate);
            int n = (int)(dur * Rate);
            double phase = 0;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, t);
                phase += f / Rate;
                float p = (float)(phase - System.Math.Floor(phase));
                float v = wave switch
                {
                    1 => p < 0.5f ? 1f : -1f,           // square
                    2 => p * 2f - 1f,                    // saw
                    _ => Mathf.Sin(p * Mathf.PI * 2f),   // sine
                };
                float env = Mathf.Exp(-t * decay) * Mathf.Min(1f, i / (Rate * 0.004f));
                d[s0 + i] += v * amp * env;
            }
        }

        /// <summary>Adds low-passed noise. The cutoff sweeps from c0 to c1 Hz.</summary>
        static void AddNoise(float[] d, float start, float dur, float c0, float c1, float amp, float decay, float attack = 0.002f)
        {
            int s0 = (int)(start * Rate);
            int n = (int)(dur * Rate);
            float y = 0f;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = i / (float)n;
                float cutoff = Mathf.Lerp(c0, c1, t);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                y += a * (Noise() - y);
                float env = Mathf.Exp(-t * decay) * Mathf.Min(1f, i / (Rate * Mathf.Max(attack, 0.0005f)));
                d[s0 + i] += y * amp * env;
            }
        }

        /// <summary>A rising-then-falling envelope of band-limited noise (whoosh).</summary>
        static void AddWhoosh(float[] d, float start, float dur, float cutoff, float amp)
        {
            int s0 = (int)(start * Rate);
            int n = (int)(dur * Rate);
            float y = 0f, y2 = 0f;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = i / (float)n;
                float c = Mathf.Lerp(cutoff * 0.4f, cutoff, Mathf.Sin(t * Mathf.PI));
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * c / Rate);
                y += a * (Noise() - y);
                y2 += a * (y - y2);
                float env = Mathf.Sin(t * Mathf.PI);
                d[s0 + i] += (y - y2) * amp * env * 2.5f;
            }
        }

        /// <summary>RBJ band-pass biquad, used as a vocal-tract formant.</summary>
        struct Biquad
        {
            float b0, b2, a1, a2, x1, x2, y1, y2;

            public void SetBandpass(float freq, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(freq, 20f, Rate * 0.45f) / Rate;
                float alpha = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + alpha;
                b0 = alpha / a0;
                b2 = -alpha / a0;
                a1 = -2f * Mathf.Cos(w) / a0;
                a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        /// <summary>
        /// A throaty, creaky voice: a jittery glottal pulse train shaped by three formants,
        /// plus breath noise. The vowel morphs from "uh" (0) to "ah" (1).
        /// The result is normalized to <paramref name="amp"/> peak, then mixed in.
        /// </summary>
        static void AddVoice(float[] d, float start, float dur, float f0Start, float f0Mid, float f0End,
            float amp, float roughness, float vowelStart, float vowelEnd, float breath, int seed)
        {
            var r = new System.Random(seed);
            float Rnd() => (float)r.NextDouble();
            int s0 = (int)(start * Rate);
            int n = Mathf.Min((int)(dur * Rate), d.Length - s0);
            if (n <= 0) return;
            var buf = new float[n];

            Biquad f1 = default, f2 = default, f3 = default, nf1 = default, nf2 = default;
            float phase = 0f, jitter = 1f, ampJ = 1f, prevPulse = 0f, lp = 0f, breathLp = 0f;
            int pulseIndex = 0;
            float wobbleSeed = Rnd() * 10f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float time = i / (float)Rate;

                // Pitch contour (start, mid, end) with a slow, irregular wobble
                float f0 = t < 0.5f ? Mathf.Lerp(f0Start, f0Mid, t * 2f) : Mathf.Lerp(f0Mid, f0End, (t - 0.5f) * 2f);
                f0 *= 1f + 0.05f * (Mathf.PerlinNoise(wobbleSeed, time * 3f) - 0.5f) * 2f;
                f0 *= jitter;

                phase += f0 / Rate;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    jitter = 1f + (Rnd() - 0.5f) * roughness * 0.35f;
                    ampJ = 1f - Rnd() * roughness * 0.6f;
                    // Period doubling: alternate strong and weak pulses for a creaky, growling subharmonic.
                    pulseIndex++;
                    if ((pulseIndex & 1) == 1) ampJ *= 1f - roughness * 0.5f;
                }

                // Glottal open-phase pulse, differentiated for harmonic richness
                float pulse = phase < 0.55f ? Mathf.Pow(Mathf.Sin(Mathf.PI * phase / 0.55f), 2f) : 0f;
                float src = (pulse - prevPulse) * ampJ;
                prevPulse = pulse;

                // Formants (updated every 32 samples)
                if ((i & 31) == 0)
                {
                    float v = Mathf.Lerp(vowelStart, vowelEnd, Mathf.SmoothStep(0f, 1f, t));
                    f1.SetBandpass(Mathf.Lerp(420f, 760f, v), 6f);
                    f2.SetBandpass(Mathf.Lerp(850f, 1180f, v), 7f);
                    f3.SetBandpass(2450f, 9f);
                    nf1.SetBandpass(Mathf.Lerp(500f, 800f, v), 3f);
                    nf2.SetBandpass(1600f, 3f);
                }
                float voiced = f1.Process(src) * 1.0f + f2.Process(src) * 0.55f + f3.Process(src) * 0.18f;

                breathLp += 0.3f * (Noise() - breathLp);
                float air = (nf1.Process(breathLp) + nf2.Process(breathLp) * 0.4f) * breath;

                // Slow amplitude roughness (gurgle)
                float gurgle = 0.75f + 0.25f * Mathf.PerlinNoise(wobbleSeed + 3f, time * 11f);
                float env = Mathf.Min(1f, time / 0.07f) * (t > 0.6f ? Mathf.Pow(1f - (t - 0.6f) / 0.4f, 1.5f) : 1f);

                // Throat rattle: fast irregular amplitude flutter
                float rattle = 1f - roughness * 0.35f * (0.5f + 0.5f * Mathf.Sin(time * 2f * Mathf.PI * (24f + 6f * Mathf.PerlinNoise(wobbleSeed + 7f, time * 2f))));
                float x = (voiced + air * 2.5f) * gurgle * rattle * env;
                lp += 0.45f * (x - lp);   // tame the top end
                buf[i] = lp;
            }

            float peak = 0f;
            foreach (var x in buf) peak = Mathf.Max(peak, Mathf.Abs(x));
            float g = peak > 0f ? amp / peak : 0f;
            for (int i = 0; i < n; i++) d[s0 + i] += buf[i] * g;
        }

        public static AudioClip Pickup()
        {
            var d = Buffer(0.14f);
            AddTone(d, 0f, 0.09f, 880f, 1320f, 0.5f, 3f);
            AddTone(d, 0.04f, 0.1f, 1320f, 1760f, 0.25f, 5f);
            return Make("sfx_pickup", d);
        }

        public static AudioClip Click()
        {
            var d = Buffer(0.04f);
            AddNoise(d, 0f, 0.02f, 5000f, 3000f, 0.6f, 8f, 0.0003f);
            AddTone(d, 0f, 0.015f, 2500f, 1800f, 0.25f, 6f);
            return Make("sfx_click", d);
        }

        public static AudioClip Reload()
        {
            var d = Buffer(0.6f);
            AddNoise(d, 0f, 0.03f, 4000f, 2000f, 0.5f, 8f, 0.0003f);
            AddTone(d, 0f, 0.03f, 900f, 700f, 0.3f, 6f);
            AddWhoosh(d, 0.12f, 0.15f, 1500f, 0.2f);
            AddNoise(d, 0.42f, 0.04f, 5000f, 2500f, 0.7f, 8f, 0.0003f);
            AddTone(d, 0.42f, 0.04f, 1200f, 800f, 0.35f, 6f);
            return Make("sfx_reload", d);
        }

        public static AudioClip Equip()
        {
            var d = Buffer(0.25f);
            AddWhoosh(d, 0f, 0.15f, 1800f, 0.25f);
            AddNoise(d, 0.14f, 0.03f, 4000f, 2500f, 0.5f, 8f, 0.0003f);
            return Make("sfx_equip", d);
        }

        public static AudioClip PlayerHurt(int v)
        {
            var d = Buffer(0.3f);
            AddTone(d, 0f, 0.2f, 75f, 45f, 0.9f, 4f);
            AddNoise(d, 0f, 0.12f, 1400f, 300f, 0.5f, 5f);
            AddVoice(d, 0.01f, 0.22f, 150f + v * 12f, 135f, 110f, 0.45f, 0.25f, 0.5f, 0.3f, 0.3f, 500 + v);
            return Make("sfx_phurt_" + v, d);
        }

        public static AudioClip Footstep(int v)
        {
            var d = Buffer(0.12f);
            AddNoise(d, 0f, 0.09f, 500f + v * 80f, 200f, 0.6f, 6f, 0.004f);
            AddTone(d, 0f, 0.05f, 80f, 50f, 0.25f, 5f);
            return Make("sfx_step_" + v, d);
        }

        public static AudioClip UIClick()
        {
            var d = Buffer(0.03f);
            AddNoise(d, 0f, 0.02f, 3500f, 2500f, 0.5f, 6f, 0.0005f);
            return Make("sfx_uiclick", d);
        }

        public static AudioClip DeathSting()
        {
            var d = Buffer(2.2f);
            float[] notes = { 164.8f, 155.6f, 110f };
            for (int i = 0; i < notes.Length; i++)
                AddTone(d, i * 0.35f, 1.4f, notes[i], notes[i] * 0.97f, 0.28f, 2f, 2);
            AddNoise(d, 0f, 2.1f, 250f, 80f, 0.35f, 1.5f, 0.05f);
            return Make("sfx_death", d);
        }

        /// <summary>Station room tone: mains hum and harmonics, HVAC air, and slow ventilation swells.</summary>
        public static AudioClip StationHum()
        {
            float dur = 10f;
            var d = Buffer(dur);
            float air = 0f, air2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float hum = Mathf.Sin(t * 50f * Mathf.PI * 2f) * 0.05f + Mathf.Sin(t * 100f * Mathf.PI * 2f) * 0.035f
                          + Mathf.Sin(t * 150f * Mathf.PI * 2f) * 0.015f + Mathf.Sin(t * 41.3f * Mathf.PI * 2f) * 0.02f;
                air += 0.04f * (Noise() - air);
                air2 += 0.04f * (air - air2);
                float swell = 0.7f + 0.3f * Mathf.Sin(t / dur * Mathf.PI * 2f * 2f);
                d[i] = hum + air2 * 1.6f * swell;
            }
            int xf = Rate / 2;
            for (int i = 0; i < xf; i++)
            {
                float g = i / (float)xf;
                d[i] = d[i] * g + d[d.Length - xf + i] * (1f - g);
            }
            var trimmed = new float[d.Length - xf];
            System.Array.Copy(d, trimmed, trimmed.Length);
            var clip = AudioClip.Create("amb_station", trimmed.Length, 1, Rate, false);
            clip.SetData(trimmed, 0);
            return clip;
        }

        /// <summary>Wind bed for the forest: brown noise with slow gusts, plus a low drone.</summary>
        public static AudioClip AmbientBed()
        {
            float dur = 8f;
            var d = Buffer(dur);
            float brown = 0f, lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                brown = Mathf.Clamp(brown + Noise() * 0.02f, -1f, 1f);
                brown *= 0.998f;
                float gust = 0.55f + 0.45f * Mathf.Sin(t / dur * Mathf.PI * 2f * 2f) * Mathf.Sin(t / dur * Mathf.PI * 2f * 3f + 1f);
                lp += 0.05f * (brown - lp);
                float drone = Mathf.Sin(t * 55f * Mathf.PI * 2f) * 0.05f + Mathf.Sin(t * 82.4f * Mathf.PI * 2f) * 0.03f;
                drone *= 0.6f + 0.4f * Mathf.Sin(t / dur * Mathf.PI * 2f);
                d[i] = lp * 2.2f * gust + drone;
            }
            // Crossfade the loop seam so it repeats without a pop.
            int xf = Rate / 2;
            for (int i = 0; i < xf; i++)
            {
                float g = i / (float)xf;
                d[i] = d[i] * g + d[d.Length - xf + i] * (1f - g);
            }
            var clip = AudioClip.Create("amb_forest", d.Length - xf, 1, Rate, false);
            var trimmed = new float[d.Length - xf];
            System.Array.Copy(d, trimmed, trimmed.Length);
            clip.SetData(trimmed, 0);
            return clip;
        }
    }
}
