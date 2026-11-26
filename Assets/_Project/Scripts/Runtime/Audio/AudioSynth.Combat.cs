using UnityEngine;

namespace Alcax.Audio
{
    /// <summary>
    /// Combat sound recipes: gunshots with a forest echo tail, melee whooshes and flesh impacts,
    /// weapon mechanisms (mags, slides, pumps, shells), and zombie voices.
    /// Any of these can be replaced by real recordings (see SoundBank / Audio/Overrides).
    /// </summary>
    public static partial class AudioSynth
    {
        // ---------- Building blocks ----------

        /// <summary>Band-passed noise burst with a swept center frequency.</summary>
        static void AddBandNoise(float[] d, float start, float dur, float f0, float f1, float q, float amp,
            float attack, float decay, int seed)
        {
            var r = new System.Random(seed);
            int s0 = (int)(start * Rate);
            int n = Mathf.Min((int)(dur * Rate), d.Length - s0);
            Biquad bp = default;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                if ((i & 15) == 0) bp.SetBandpass(Mathf.Lerp(f0, f1, t), q);
                float x = bp.Process((float)(r.NextDouble() * 2.0 - 1.0));
                float env = Mathf.Min(1f, i / (Rate * Mathf.Max(attack, 0.0002f))) * Mathf.Exp(-t * decay);
                d[s0 + i] += x * amp * env * 3f;
            }
        }

        /// <summary>Metallic resonance: noise excitation through narrow resonators (the clicks and clacks of gun parts).</summary>
        static void AddMetal(float[] d, float start, float dur, float[] freqs, float amp, float decay, int seed)
        {
            var r = new System.Random(seed);
            int s0 = (int)(start * Rate);
            int n = Mathf.Min((int)(dur * Rate), d.Length - s0);
            var res = new Biquad[freqs.Length];
            for (int k = 0; k < freqs.Length; k++) res[k].SetBandpass(freqs[k], 28f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                // A sharp 2 ms excitation strikes the resonators.
                float exc = i < Rate / 500 ? (float)(r.NextDouble() * 2.0 - 1.0) : 0f;
                float y = 0f;
                for (int k = 0; k < res.Length; k++) y += res[k].Process(exc) / (1f + k * 0.4f);
                d[s0 + i] += y * amp * 9f * Mathf.Exp(-t * decay);
            }
        }

        /// <summary>A saturated low thump (body of an impact or the boom of a gunshot).</summary>
        static void AddThump(float[] d, float start, float dur, float f0, float f1, float amp, float drive)
        {
            int s0 = (int)(start * Rate);
            int n = Mathf.Min((int)(dur * Rate), d.Length - s0);
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                ph += Mathf.Lerp(f0, f1, Mathf.Sqrt(t)) / Rate;
                float x = Mathf.Sin((float)(ph * 2.0 * System.Math.PI));
                x = (float)System.Math.Tanh(x * drive) / (float)System.Math.Tanh(drive);
                float env = Mathf.Min(1f, i / (Rate * 0.0015f)) * Mathf.Pow(1f - t, 2.2f);
                d[s0 + i] += x * amp * env;
            }
        }

        static void SoftClip(float[] d, float drive)
        {
            float norm = (float)System.Math.Tanh(drive);
            for (int i = 0; i < d.Length; i++) d[i] = (float)System.Math.Tanh(d[i] * drive) / norm;
        }

        /// <summary>
        /// The muzzle blast: a near-instant overpressure spike, a linear fall into rarefaction (an N-wave),
        /// and a ground reflection a few milliseconds later. This is the "crack" that makes a shot read as a gun.
        /// </summary>
        static void AddMuzzleBlast(float[] d, float amp, float riseMs, float fallMs, float groundDelayMs, float groundGain)
        {
            int rise = Mathf.Max(1, (int)(riseMs * 0.001f * Rate));
            int fall = Mathf.Max(2, (int)(fallMs * 0.001f * Rate));
            int recover = fall / 2;
            int total = rise + fall + recover;
            var w = new float[total];
            for (int i = 0; i < rise; i++) w[i] = i / (float)rise;
            for (int i = 0; i < fall; i++) w[rise + i] = Mathf.Lerp(1f, -0.55f, i / (float)fall);
            for (int i = 0; i < recover; i++) w[rise + fall + i] = Mathf.Lerp(-0.55f, 0f, i / (float)recover);
            int gd = (int)(groundDelayMs * 0.001f * Rate);
            for (int i = 0; i < total; i++)
            {
                if (i < d.Length) d[i] += w[i] * amp;
                if (i + gd < d.Length) d[i + gd] += w[i] * amp * groundGain;
            }
        }

        /// <summary>
        /// The blast body: broadband noise with a fast exponential decay (the gas expanding),
        /// plus a low-passed "punch" layer. Both are noise, never a pitched tone.
        /// </summary>
        static void AddBlastBody(float[] d, float amp, float tauMs, float bodyCutoff, float punchAmp, float punchTauMs, float punchCutoff, int seed)
        {
            var r = new System.Random(seed);
            float tau = tauMs * 0.001f * Rate, ptau = punchTauMs * 0.001f * Rate;
            int n = Mathf.Min(d.Length, (int)(Mathf.Max(tau, ptau) * 7f));
            float a1 = 1f - Mathf.Exp(-2f * Mathf.PI * bodyCutoff / Rate);
            float a2 = 1f - Mathf.Exp(-2f * Mathf.PI * punchCutoff / Rate);
            float lp1 = 0f, lp2 = 0f, lp3 = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                lp1 += a1 * (x - lp1);
                lp2 += a2 * (x - lp2);
                lp3 += a2 * (lp2 - lp3);
                d[i] += lp1 * amp * Mathf.Exp(-i / tau) + lp3 * punchAmp * 6f * Mathf.Exp(-i / ptau);
            }
        }

        /// <summary>
        /// Outdoor tail: no room reverb. Sparse reflections off trees and terrain arrive from
        /// about 0.08 s to 1.8 s, each darker and quieter, plus a low rolling rumble like distant thunder.
        /// </summary>
        static void AddOutdoorTail(float[] d, int seed, float gain, float length, int reflections, float rumbleGain)
        {
            var r = new System.Random(seed);
            int srcLen = Mathf.Min(d.Length, (int)(0.07f * Rate));
            var src = new float[srcLen];
            System.Array.Copy(d, src, srcLen);

            for (int k = 0; k < reflections; k++)
            {
                // Arrival times cluster early and thin out late.
                float u = (float)r.NextDouble();
                float t = 0.08f + Mathf.Pow(u, 1.7f) * (length - 0.08f);
                int off = (int)(t * Rate);
                float g = gain * Mathf.Exp(-t / (length * 0.32f)) * (0.35f + 0.65f * (float)r.NextDouble());
                float cutoff = Mathf.Lerp(3200f, 350f, t / length);
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate);
                float lp = 0f, lpB = 0f;
                for (int i = 0; i < srcLen && i + off < d.Length; i++)
                {
                    lp += a * (src[i] - lp);
                    lpB += a * (lp - lpB);
                    d[i + off] += lpB * g;
                }
            }

            // Rolling rumble: brown noise under 200 Hz that swells in, then decays slowly.
            float brown = 0f, rl = 0f;
            int n = Mathf.Min(d.Length, (int)(length * Rate));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                brown = Mathf.Clamp(brown + (float)(r.NextDouble() * 2.0 - 1.0) * 0.06f, -1f, 1f) * 0.999f;
                rl += 0.02f * (brown - rl);
                float env = Mathf.Min(1f, t / 0.12f) * Mathf.Exp(-t / (length * 0.38f));
                d[i] += rl * rumbleGain * env * (0.8f + 0.2f * Mathf.Sin(t * 9f));
            }
        }

        /// <summary>A loudness-maximizing limiter: guns are meant to sound loud and dense.</summary>
        static void Punchify(float[] d, float drive, float releaseMs)
        {
            float env = 0f, rel = Mathf.Exp(-1f / (releaseMs * 0.001f * Rate));
            for (int i = 0; i < d.Length; i++)
            {
                float x = d[i] * drive;
                float ax = Mathf.Abs(x);
                env = ax > env ? ax : env * rel + ax * (1f - rel);
                float gainReduction = env > 1f ? 1f / env : 1f;
                d[i] = (float)System.Math.Tanh(x * Mathf.Lerp(gainReduction, 1f, 0.35f));
            }
        }

        static void Normalize(float[] d, float peakTarget)
        {
            float peak = 0f;
            foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            if (peak <= 0f) return;
            float g = peakTarget / peak;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
        }

        // ---------- Guns ----------

        public static AudioClip Pistol()
        {
            var d = Buffer(2.0f);
            AddMuzzleBlast(d, 1.0f, 0.08f, 1.4f, 2.6f, 0.55f);
            AddBlastBody(d, 0.75f, 9f, 5500f, 0.6f, 28f, 260f, 7);
            AddMetal(d, 0.018f, 0.06f, new[] { 2900f, 4400f, 6100f }, 0.07f, 26f, 11);   // slide clack, subtle
            Punchify(d, 1.8f, 40f);
            AddOutdoorTail(d, 1301, 0.32f, 1.7f, 46, 0.22f);
            Normalize(d, 0.97f);
            return Make("sfx_pistol", d);
        }

        public static AudioClip Shotgun()
        {
            var d = Buffer(2.6f);
            AddMuzzleBlast(d, 1.0f, 0.12f, 2.4f, 3.2f, 0.6f);
            AddBlastBody(d, 0.85f, 18f, 4200f, 0.95f, 55f, 180f, 17);
            Punchify(d, 2.4f, 70f);
            AddOutdoorTail(d, 1717, 0.42f, 2.3f, 64, 0.38f);
            Normalize(d, 0.98f);
            return Make("sfx_shotgun", d);
        }

        /// <summary>Rifle: sharper, longer crack than the pistol with a supersonic snap and a long outdoor tail.</summary>
        public static AudioClip Rifle(int v)
        {
            var d = Buffer(2.4f);
            AddMuzzleBlast(d, 1.0f, 0.06f, 1.1f, 2.2f, 0.5f);
            AddBlastBody(d, 0.7f, 7f + v * 1.5f, 6500f, 0.7f, 34f, 230f, 31 + v);
            AddMetal(d, 0.012f, 0.05f, new[] { 3300f, 5200f }, 0.05f, 30f, 41 + v);
            Punchify(d, 2.1f, 45f);
            AddOutdoorTail(d, 2101 + v * 17, 0.38f, 2.1f, 56, 0.3f);
            Normalize(d, 0.97f);
            return Make("sfx_rifle_" + v, d);
        }

        /// <summary>Near-miss: a short, bright supersonic crack with a fast airy zip.</summary>
        public static AudioClip Flyby(int v)
        {
            var d = Buffer(0.22f);
            var r = new System.Random(900 + v);
            float hp = 0f, prev = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                hp = 0.82f * (hp + x - prev); prev = x;
                float crack = t < 0.004f ? (1f - t / 0.004f) * (i % 7 < 3 ? 1f : -1f) : 0f;
                float zip = Mathf.Exp(-t / (0.035f + v * 0.01f)) * Mathf.Min(1f, t / 0.003f);
                d[i] = crack * 0.9f + hp * zip * 0.55f;
            }
            Normalize(d, 0.85f);
            return Make("sfx_flyby_" + v, d);
        }

        /// <summary>Bullet into a body: a dull, wet thump.</summary>
        public static AudioClip BodyHit(int v)
        {
            var d = Buffer(0.25f);
            AddBlastBody(d, 0.5f, 10f, 1800f, 1.0f, 22f, 140f + v * 30f, 61 + v);
            Normalize(d, 0.8f);
            return Make("sfx_bodyhit_" + v, d);
        }

        /// <summary>A pained grunt through the voice formant model.</summary>
        public static AudioClip Grunt(int v)
        {
            var d = Buffer(0.4f);
            var r = new System.Random(70 + v);
            float f0 = 105f + v * 18f, phase = 0f;
            float b1 = 0f, b2 = 0f, c1 = 0f, c2 = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, t / 0.02f) * Mathf.Exp(-t / 0.12f);
                phase += (f0 * (1f - t * 0.6f)) / Rate;
                float src = (phase % 1f) * 2f - 1f + (float)(r.NextDouble() - 0.5) * 0.5f;
                // two resonant lowpass stages near the "uh" formants
                b1 += 0.09f * (src - b1); b2 += 0.09f * (b1 - b2);
                c1 += 0.25f * (src - c1); c2 += 0.25f * (c1 - c2);
                d[i] = (b2 * 1.2f + (c2 - b2) * 0.4f) * env;
            }
            Normalize(d, 0.6f);
            return Make("sfx_grunt_" + v, d);
        }

        /// <summary>A shouted callout: a run of barked syllables (pitch rises with urgency, vowels vary).</summary>
        public static AudioClip Shout(int syllables, int take)
        {
            var r = new System.Random(4100 + syllables * 31 + take * 7);
            float Rnd() => (float)r.NextDouble();
            float t = 0.01f;
            var lengths = new float[syllables];
            float total = 0.05f;
            for (int i = 0; i < syllables; i++) { lengths[i] = 0.11f + Rnd() * 0.1f; total += lengths[i] + 0.035f; }
            var d = Buffer(total + 0.1f);
            for (int i = 0; i < syllables; i++)
            {
                bool last = i == syllables - 1;
                float f = 150f + Rnd() * 30f + (i == 0 ? 15f : 0f);
                float end = last ? f * 0.82f : f * (0.95f + Rnd() * 0.1f);
                AddVoice(d, t, lengths[i], f * 0.95f, f * 1.08f, end, 0.9f, 0.25f, Rnd(), Rnd(), 0.18f, 4200 + i + take * 13 + syllables * 101);
                t += lengths[i] + 0.02f + Rnd() * 0.03f;
            }
            Normalize(d, 0.9f);
            return Make("sfx_shout_" + syllables + "_" + take, d);
        }

        /// <summary>The same voice through a radio: band-limited, clipped, with squelch clicks at both ends.</summary>
        public static AudioClip Radio(int syllables, int take)
        {
            var src = Shout(syllables, take + 5);
            var d = new float[src.samples + (int)(0.12f * Rate)];
            var tmp = new float[src.samples];
            src.GetData(tmp, 0);
            int off = (int)(0.05f * Rate);
            Biquad hp = default, lp = default;
            hp.SetBandpass(1400f, 0.6f);
            lp.SetBandpass(1400f, 0.6f);
            for (int i = 0; i < tmp.Length; i++)
            {
                float x = lp.Process(hp.Process(tmp[i])) * 3f;
                d[i + off] = (float)System.Math.Tanh(x * 2.2f) * 0.8f + Noise() * 0.025f;
            }
            AddNoise(d, 0f, 0.04f, 3000f, 2000f, 0.35f, 20f);
            AddNoise(d, (tmp.Length + off) / (float)Rate, 0.06f, 2500f, 1500f, 0.4f, 18f);
            Normalize(d, 0.75f);
            return Make("sfx_radio_" + syllables + "_" + take, d);
        }

        // ---------- Melee ----------

        /// <summary>Air whoosh: low band-passed noise sweeping up, then down, with a soft rising swell.</summary>
        public static AudioClip Swing(int v)
        {
            var d = Buffer(0.3f);
            int s = 30 + v;
            var r = new System.Random(s);
            Biquad bp = default, bp2 = default;
            int n = (int)(0.26f * Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float center = t < 0.6f ? Mathf.Lerp(260f, 820f + v * 120f, t / 0.6f) : Mathf.Lerp(820f + v * 120f, 320f, (t - 0.6f) / 0.4f);
                if ((i & 15) == 0) { bp.SetBandpass(center, 1.4f); bp2.SetBandpass(center * 0.5f, 1.2f); }
                float x = (float)(r.NextDouble() * 2.0 - 1.0);
                float y = bp.Process(x) * 0.9f + bp2.Process(x) * 0.6f;
                float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.8f)), 2.2f);
                d[i] += y * env * 1.6f;
            }
            return Make("sfx_swing_" + v, d);
        }

        /// <summary>Flesh impact shared by all melee hits: a thump, a smack, and a wet squelch.</summary>
        static void AddFleshHit(float[] d, float weight, int seed)
        {
            AddThump(d, 0f, 0.13f + weight * 0.05f, 95f - weight * 20f, 42f, 0.95f, 3f);
            AddBandNoise(d, 0f, 0.05f, 900f, 600f, 1.3f, 0.55f, 0.0003f, 9f, seed);          // smack
            // Squelch: low band noise with a fast wet flutter
            int n = (int)(0.16f * Rate);
            var r = new System.Random(seed + 9);
            Biquad bp = default;
            bp.SetBandpass(320f, 2.5f);
            for (int i = 0; i < n && i + Rate / 200 < d.Length; i++)
            {
                float t = i / (float)n;
                float flutter = 0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * (14f + (float)r.NextDouble() * 8f));
                d[i + Rate / 200] += bp.Process((float)(r.NextDouble() * 2.0 - 1.0)) * 0.9f * flutter * Mathf.Exp(-t * 5f);
            }
        }

        public static AudioClip Punch(int v)
        {
            var d = Buffer(0.25f);
            AddFleshHit(d, 0f, 40 + v);
            AddBandNoise(d, 0f, 0.02f, 2500f, 1500f, 1f, 0.25f, 0.0002f, 10f, 50 + v);   // knuckle slap
            SoftClip(d, 1.4f);
            return Make("sfx_punch_" + v, d);
        }

        public static AudioClip BluntHit(int v)
        {
            var d = Buffer(0.35f);
            AddFleshHit(d, 1f, 60 + v);
            AddMetal(d, 0f, 0.09f, new[] { 520f + v * 30f, 1240f, 1900f }, 0.22f, 14f, 70 + v); // wooden "tok" of the bat
            AddBandNoise(d, 0.006f, 0.05f, 1800f, 900f, 1.2f, 0.25f, 0.0002f, 9f, 75 + v); // crunch
            SoftClip(d, 1.8f);
            return Make("sfx_blunt_" + v, d);
        }

        public static AudioClip BladeHit(int v)
        {
            var d = Buffer(0.35f);
            AddFleshHit(d, 0.4f, 80 + v);
            AddBandNoise(d, 0f, 0.09f, 4200f, 1600f, 1.6f, 0.45f, 0.0002f, 7f, 90 + v);    // slice
            AddMetal(d, 0f, 0.25f, new[] { 2850f + v * 120f, 4100f }, 0.06f, 9f, 95 + v); // faint ring
            SoftClip(d, 1.5f);
            return Make("sfx_blade_" + v, d);
        }

        // ---------- Mechanisms ----------

        public static AudioClip MagOut()
        {
            var d = Buffer(0.15f);
            AddMetal(d, 0f, 0.1f, new[] { 2300f, 3900f }, 0.5f, 22f, 101);
            AddBandNoise(d, 0.01f, 0.06f, 2000f, 1200f, 1f, 0.15f, 0.001f, 8f, 102);
            return Make("sfx_magout", d);
        }

        public static AudioClip MagIn()
        {
            var d = Buffer(0.15f);
            AddMetal(d, 0f, 0.1f, new[] { 1800f, 3100f, 5200f }, 0.7f, 26f, 111);
            AddThump(d, 0f, 0.04f, 260f, 140f, 0.35f, 2f);
            return Make("sfx_magin", d);
        }

        public static AudioClip SlideRack()
        {
            var d = Buffer(0.3f);
            AddBandNoise(d, 0f, 0.06f, 3000f, 1800f, 1.5f, 0.2f, 0.002f, 6f, 121);
            AddMetal(d, 0.05f, 0.1f, new[] { 2600f, 4300f }, 0.55f, 20f, 122);
            AddMetal(d, 0.13f, 0.12f, new[] { 2100f, 3500f, 5600f }, 0.75f, 18f, 123);
            return Make("sfx_sliderack", d);
        }

        public static AudioClip PumpBack()
        {
            var d = Buffer(0.18f);
            AddBandNoise(d, 0f, 0.08f, 1400f, 900f, 1.2f, 0.25f, 0.003f, 5f, 131);
            AddMetal(d, 0.06f, 0.1f, new[] { 1500f, 2700f }, 0.6f, 18f, 132);
            AddThump(d, 0.06f, 0.05f, 180f, 110f, 0.3f, 2f);
            return Make("sfx_pumpback", d);
        }

        public static AudioClip PumpForward()
        {
            var d = Buffer(0.18f);
            AddBandNoise(d, 0f, 0.06f, 1600f, 1100f, 1.2f, 0.22f, 0.003f, 5f, 141);
            AddMetal(d, 0.05f, 0.11f, new[] { 1700f, 3000f, 4400f }, 0.8f, 16f, 142);
            AddThump(d, 0.05f, 0.05f, 200f, 120f, 0.35f, 2f);
            return Make("sfx_pumpfwd", d);
        }

        public static AudioClip ShellInsert(int v)
        {
            var d = Buffer(0.14f);
            AddBandNoise(d, 0f, 0.05f, 1200f, 800f, 1.5f, 0.18f, 0.002f, 6f, 151 + v);
            AddMetal(d, 0.03f, 0.08f, new[] { 1350f + v * 60f, 2600f }, 0.45f, 22f, 152 + v);
            return Make("sfx_shellin_" + v, d);
        }

        public static AudioClip CasingTink(int v)
        {
            var d = Buffer(0.2f);
            AddMetal(d, 0f, 0.18f, new[] { 4300f + v * 380f, 6900f + v * 250f, 9100f }, 0.35f, 12f, 161 + v);
            return Make("sfx_tink_" + v, d);
        }

        public static AudioClip ShellDrop(int v)
        {
            var d = Buffer(0.14f);
            AddMetal(d, 0f, 0.1f, new[] { 1900f + v * 150f, 3300f }, 0.25f, 18f, 171 + v);
            AddThump(d, 0f, 0.03f, 300f, 180f, 0.2f, 1.5f);
            return Make("sfx_shelldrop_" + v, d);
        }

        public static AudioClip MagDrop()
        {
            var d = Buffer(0.2f);
            AddThump(d, 0f, 0.05f, 220f, 120f, 0.4f, 2f);
            AddMetal(d, 0f, 0.15f, new[] { 1100f, 2400f, 3700f }, 0.3f, 14f, 181);
            return Make("sfx_magdrop", d);
        }

        // ---------- Dialogue and vehicles ----------

        /// <summary>A soft, low "voice" blip for typed dialogue.</summary>
        public static AudioClip TalkBlip(int v)
        {
            var d = Buffer(0.06f);
            AddTone(d, 0f, 0.05f, 300f + v * 40f, 280f + v * 30f, 0.25f, 6f);
            AddBandNoise(d, 0f, 0.03f, 900f, 700f, 2f, 0.05f, 0.002f, 6f, 600 + v);
            return Make("sfx_talk_" + v, d);
        }

        /// <summary>A looping shuttle engine: a deep rumble, a turbine whine, and jet hiss (loop-safe).</summary>
        public static AudioClip ShipEngine()
        {
            float dur = 4f;
            var d = Buffer(dur);
            var r = new System.Random(777);
            float brown = 0f, lp = 0f, hiss = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                brown = Mathf.Clamp(brown + (float)(r.NextDouble() * 2.0 - 1.0) * 0.05f, -1f, 1f) * 0.998f;
                lp += 0.03f * (brown - lp);
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                hiss += 0.35f * (n - hiss);
                // Whole-number cycles over 4 s so the loop is seamless
                float whine = Mathf.Sin(t * 2f * Mathf.PI * 1240f) * 0.05f + Mathf.Sin(t * 2f * Mathf.PI * 620f) * 0.04f;
                float hum = Mathf.Sin(t * 2f * Mathf.PI * 48f) * 0.25f;
                d[i] = lp * 2.5f + hum + whine + hiss * 0.12f;
            }
            int xf = Rate / 4;
            for (int i = 0; i < xf; i++)
            {
                float g = i / (float)xf;
                d[i] = d[i] * g + d[d.Length - xf + i] * (1f - g);
            }
            var trimmed = new float[d.Length - xf];
            System.Array.Copy(d, trimmed, trimmed.Length);
            Normalize(trimmed, 0.9f);
            var clip = AudioClip.Create("sfx_shipengine", trimmed.Length, 1, Rate, false);
            clip.SetData(trimmed, 0);
            return clip;
        }

        /// <summary>A sci-fi sliding door: a pneumatic hiss, a servo whir, and a soft clunk at the end of travel.</summary>
        public static AudioClip Door(bool opening)
        {
            // A soft pneumatic "psshh": airy band-passed noise with a quick attack and a breathy tail,
            // plus a very faint seal thump when closing.
            float len = opening ? 0.7f : 0.5f;
            var d = Buffer(len + 0.1f);
            AddBandNoise(d, 0f, len, opening ? 5200f : 4200f, opening ? 3000f : 2600f, 0.7f, 0.5f, 0.012f, 4.5f, opening ? 900 : 901);
            AddBandNoise(d, 0f, len * 0.8f, 9000f, 6500f, 0.9f, 0.18f, 0.008f, 6f, opening ? 904 : 905);
            if (!opening) AddThump(d, len - 0.08f, 0.08f, 120f, 80f, 0.12f, 1.5f);
            Normalize(d, 0.6f);
            return Make(opening ? "sfx_door_open" : "sfx_door_close", d);
        }

        /// <summary>A steel target plate ringing after a hit.</summary>
        public static AudioClip TargetClang(int v)
        {
            var d = Buffer(0.9f);
            AddMetal(d, 0f, 0.85f, new[] { 820f + v * 60f, 1490f + v * 70f, 2380f, 3310f }, 0.6f, 5f, 800 + v);
            AddThump(d, 0f, 0.05f, 240f, 160f, 0.4f, 2f);
            Normalize(d, 0.9f);
            return Make("sfx_clang_" + v, d);
        }
    }
}
