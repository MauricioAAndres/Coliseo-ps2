using System;
using UnityEngine;

namespace Gladiator.Audio
{
    /// <summary>
    /// All the game's sound effects, synthesized at start-up (no audio files): filtered noise, decaying
    /// partials and simple brass/plucked voices, at 22 kHz mono for a lo-fi PS2 feel.
    /// </summary>
    public static class SoundSynth
    {
        public const int Rate = 22050;
        static System.Random rng = new System.Random(20261105);

        // ------------------------------------------------------------------ building blocks
        public static float Noise() => (float)(rng.NextDouble() * 2.0 - 1.0);
        public static float[] Buf(float seconds) => new float[Mathf.Max(1, (int)(seconds * Rate))];

        public static AudioClip Clip(string name, float[] d, float peak = 0.9f)
        {
            Normalize(d, peak);
            var c = AudioClip.Create(name, d.Length, 1, Rate, false);
            c.SetData(d, 0);
            return c;
        }

        public static void Normalize(float[] d, float peak)
        {
            float m = 1e-6f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        public static void LowPass(float[] d, float cutoff)
        {
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate), y = 0f;
            for (int i = 0; i < d.Length; i++) { y += a * (d[i] - y); d[i] = y; }
        }

        public static void HighPass(float[] d, float cutoff)
        {
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / Rate), y = 0f;
            for (int i = 0; i < d.Length; i++) { y += a * (d[i] - y); d[i] -= y; }
        }

        /// <summary>Band-pass (RBJ, 0 dB peak) with a centre frequency that can move over time (t in seconds).</summary>
        public static void BandPass(float[] d, Func<float, float> freq, float q)
        {
            float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
            for (int i = 0; i < d.Length; i++)
            {
                float f = Mathf.Clamp(freq(i / (float)Rate), 30f, Rate * 0.45f);
                float w = 2f * Mathf.PI * f / Rate, alpha = Mathf.Sin(w) / (2f * q), cw = Mathf.Cos(w);
                float a0 = 1f + alpha;
                float b0 = alpha / a0, b2 = -alpha / a0, a1 = -2f * cw / a0, a2 = (1f - alpha) / a0;
                float x = d[i];
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                d[i] = y;
            }
        }

        static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        static void Mix(float[] dst, float[] src, float gain, float atSeconds = 0f)
        {
            int o = (int)(atSeconds * Rate);
            for (int i = 0; i < src.Length && i + o < dst.Length; i++) dst[i + o] += src[i] * gain;
        }

        static float[] NoiseBuf(float seconds)
        {
            var d = Buf(seconds);
            for (int i = 0; i < d.Length; i++) d[i] = Noise();
            return d;
        }

        // ------------------------------------------------------------------ voices
        /// <summary>Air swish: band-passed noise sweeping up then down, bell-shaped.</summary>
        public static float[] Whoosh(float dur, float fLow, float fHigh, float q = 1.6f)
        {
            var d = NoiseBuf(dur);
            BandPass(d, t => Mathf.Lerp(fLow, fHigh, Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / dur))), q);
            for (int i = 0; i < d.Length; i++)
            {
                float u = i / (float)d.Length;
                float e = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f));
                d[i] *= e * e;
            }
            return d;
        }

        /// <summary>Body impact: a falling sine thump plus a short muffled noise burst.</summary>
        public static float[] Thump(float dur, float fStart, float fEnd, float decay, float noiseCut, float noiseGain)
        {
            var d = Buf(dur);
            var n = NoiseBuf(dur);
            LowPass(n, noiseCut);
            LowPass(n, noiseCut);
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = fEnd + (fStart - fEnd) * Mathf.Exp(-t * 18f);
                ph += 2f * Mathf.PI * f / Rate;
                d[i] = Mathf.Sin(ph) * Env(t, 0.002f, decay) + n[i] * noiseGain * Env(t, 0.001f, decay * 0.35f);
            }
            return d;
        }

        /// <summary>Metal: inharmonic partials with their own decays, plus a bright transient.</summary>
        public static float[] Clang(float dur, float baseHz, float decay)
        {
            float[] ratio = { 1f, 2.32f, 3.86f, 5.41f, 6.93f, 8.6f };
            float[] amp = { 1f, 0.7f, 0.5f, 0.35f, 0.25f, 0.15f };
            var d = Buf(dur);
            for (int p = 0; p < ratio.Length; p++)
            {
                float f = baseHz * ratio[p], f2 = f * 1.004f, dec = decay / (1f + p * 0.45f);
                for (int i = 0; i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float s = Mathf.Sin(2f * Mathf.PI * f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * f2 * t);
                    d[i] += s * amp[p] * Env(t, 0.001f, dec);
                }
            }
            var n = NoiseBuf(0.03f);
            HighPass(n, 2500f);
            for (int i = 0; i < n.Length; i++) n[i] *= Env(i / (float)Rate, 0.0005f, 0.006f) * 2.5f;
            Mix(d, n, 1f);
            return d;
        }

        /// <summary>Sand/grit: low-passed noise burst.</summary>
        public static float[] Grit(float dur, float cutoff, float attack, float decay)
        {
            var d = NoiseBuf(dur);
            LowPass(d, cutoff);
            HighPass(d, 200f);
            for (int i = 0; i < d.Length; i++) d[i] *= Env(i / (float)Rate, attack, decay);
            return d;
        }

        /// <summary>Roman cornu / tuba: additive brass with a filter that opens on the attack.</summary>
        public static float[] Horn(float[] notesHz, float[] lengths, float gapRelease = 0.08f)
        {
            float total = 0f;
            foreach (var l in lengths) total += l;
            var d = Buf(total + 0.6f);
            float at = 0f;
            for (int k = 0; k < notesHz.Length; k++)
            {
                float f0 = notesHz[k], len = lengths[k];
                int start = (int)(at * Rate), n = (int)((len + 0.5f) * Rate);
                float ph = 0f;
                for (int i = 0; i < n && start + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float env = t < 0.06f ? t / 0.06f : (t < len - gapRelease ? 1f : Mathf.Exp(-(t - (len - gapRelease)) / 0.12f));
                    float vib = 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 5.2f * t) * Mathf.Clamp01(t / 0.3f);
                    ph += 2f * Mathf.PI * f0 * vib / Rate;
                    float bright = 0.35f + 0.65f * Mathf.Clamp01(t / 0.08f) * (0.7f + 0.3f * env);
                    float s = 0f;
                    for (int h = 1; h <= 9; h++) s += Mathf.Sin(ph * h) / h * Mathf.Pow(bright, h * 0.5f);
                    d[start + i] += s * env;
                }
                at += len;
            }
            LowPass(d, 3200f);
            return d;
        }

        static float Hz(float semisFromA4) => 440f * Mathf.Pow(2f, semisFromA4 / 12f);

        // ------------------------------------------------------------------ the game's sounds
        public static AudioClip SwingLight() => Clip("swing_light", Whoosh(0.22f, 500f, 2400f), 0.8f);
        public static AudioClip SwingHeavy() => Clip("swing_heavy", Whoosh(0.36f, 280f, 1500f, 1.2f), 0.9f);
        public static AudioClip SwingFist() => Clip("swing_fist", Whoosh(0.15f, 350f, 1300f, 1.1f), 0.6f);
        public static AudioClip SwingKick() => Clip("swing_kick", Whoosh(0.2f, 250f, 900f, 0.9f), 0.7f);
        public static AudioClip Whiff() => Clip("whiff", Whoosh(0.18f, 900f, 3200f, 2.2f), 0.6f);

        public static AudioClip HitFlesh(int v)
        {
            var d = Thump(0.32f, 150f + v * 20f, 48f, 0.09f, 1100f, 0.9f);
            var crack = NoiseBuf(0.02f);
            HighPass(crack, 1800f);
            for (int i = 0; i < crack.Length; i++) crack[i] *= Env(i / (float)Rate, 0.0005f, 0.004f);
            Mix(d, crack, 0.8f);
            return Clip("hit_flesh_" + v, d, 0.95f);
        }

        public static AudioClip HitHeavy()
        {
            var d = Thump(0.5f, 120f, 38f, 0.14f, 800f, 1.1f);
            Mix(d, Grit(0.2f, 1500f, 0.001f, 0.05f), 0.5f);
            return Clip("hit_heavy", d, 1f);
        }

        public static AudioClip KickHit()
        {
            var d = Thump(0.4f, 110f, 42f, 0.12f, 600f, 1.2f);
            Mix(d, Whoosh(0.08f, 300f, 700f, 0.8f), 0.4f);
            return Clip("kick_hit", d, 1f);
        }

        public static AudioClip Block(int v) => Clip("block_" + v, Clang(0.9f, 520f + v * 70f, 0.5f), 0.85f);

        public static AudioClip GuardBreak()
        {
            var d = Clang(1.1f, 380f, 0.6f);
            Mix(d, Thump(0.4f, 100f, 40f, 0.12f, 900f, 1f), 0.9f);
            Mix(d, Grit(0.25f, 2000f, 0.001f, 0.06f), 0.5f);
            return Clip("guard_break", d, 1f);
        }

        public static AudioClip Step(int v)
        {
            var d = Grit(0.1f, 1300f + v * 250f, 0.003f, 0.025f);
            Mix(d, Thump(0.08f, 90f, 60f, 0.02f, 400f, 0.3f), 0.35f);
            return Clip("step_" + v, d, 0.7f);
        }

        public static AudioClip Jump()
        {
            var d = Whoosh(0.2f, 300f, 1000f, 1f);
            Mix(d, Grit(0.08f, 1500f, 0.002f, 0.02f), 0.6f);
            return Clip("jump", d, 0.6f);
        }

        public static AudioClip Land(bool hard)
        {
            var d = Thump(hard ? 0.45f : 0.25f, hard ? 110f : 130f, hard ? 40f : 55f, hard ? 0.13f : 0.06f, 700f, 0.8f);
            Mix(d, Grit(hard ? 0.35f : 0.18f, 1800f, 0.002f, hard ? 0.09f : 0.04f), hard ? 0.9f : 0.6f);
            return Clip(hard ? "land_hard" : "land", d, hard ? 1f : 0.75f);
        }

        public static AudioClip Roll()
        {
            var d = Grit(0.5f, 1100f, 0.02f, 0.25f);
            for (int i = 0; i < d.Length; i++) d[i] *= 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 11f * i / Rate);
            Mix(d, Thump(0.2f, 100f, 55f, 0.05f, 500f, 0.5f), 0.6f, 0.18f);
            Mix(d, Whoosh(0.3f, 250f, 800f, 0.9f), 0.5f);
            return Clip("roll", d, 0.8f);
        }

        public static AudioClip Climb()
        {
            var d = Grit(0.6f, 2200f, 0.03f, 0.18f);
            BandPass(d, t => 900f + 600f * Mathf.Sin(t * 9f), 1.2f);
            Mix(d, Thump(0.15f, 140f, 70f, 0.03f, 600f, 0.4f), 0.5f);
            Mix(d, Thump(0.15f, 130f, 65f, 0.03f, 600f, 0.4f), 0.4f, 0.32f);
            return Clip("climb", d, 0.7f);
        }

        public static AudioClip Stun()
        {
            var d = Buf(0.9f);
            float[] f = { 2200f, 2700f, 1900f, 2500f, 1700f };
            for (int k = 0; k < f.Length; k++)
            {
                int o = (int)(k * 0.15f * Rate);
                for (int i = 0; i < 0.25f * Rate && o + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    float vib = 1f + 0.03f * Mathf.Sin(2f * Mathf.PI * 14f * t);
                    d[o + i] += Mathf.Sin(2f * Mathf.PI * f[k] * vib * t) * Env(t, 0.004f, 0.06f);
                }
            }
            return Clip("stun", d, 0.5f);
        }

        public static AudioClip Death()
        {
            var d = Thump(0.8f, 90f, 32f, 0.2f, 500f, 1.2f);
            Mix(d, Grit(0.5f, 1500f, 0.005f, 0.15f), 0.8f, 0.05f);
            return Clip("death", d, 1f);
        }

        public static AudioClip Pickup() => Clip("pickup", Clang(0.45f, 1250f, 0.18f), 0.55f);

        public static AudioClip UiMove()
        {
            var d = Buf(0.05f);
            for (int i = 0; i < d.Length; i++) { float t = i / (float)Rate; d[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 880f * t)) * Env(t, 0.002f, 0.015f); }
            LowPass(d, 3000f);
            return Clip("ui_move", d, 0.35f);
        }

        public static AudioClip UiTwoTone(string name, float f1, float f2)
        {
            var d = Buf(0.14f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate, f = t < 0.06f ? f1 : f2;
                d[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t)) * Env(t % 0.06f, 0.002f, 0.03f);
            }
            LowPass(d, 3000f);
            return Clip(name, d, 0.35f);
        }

        /// <summary>Crowd murmur loop (seamless): layered band-passed noise with slow swells.</summary>
        public static AudioClip CrowdLoop()
        {
            const float L = 8f, X = 0.6f;
            var d = Buf(L + X);
            float[] centers = { 420f, 800f, 1400f };
            for (int b = 0; b < centers.Length; b++)
            {
                var n = NoiseBuf(L + X);
                float c = centers[b];
                BandPass(n, t => c * (1f + 0.1f * Mathf.Sin(t * (0.7f + b * 0.3f))), 0.9f);
                float p1 = (float)rng.NextDouble() * 6f, p2 = (float)rng.NextDouble() * 6f;
                for (int i = 0; i < n.Length; i++)
                {
                    float t = i / (float)Rate;
                    float sw = 0.6f + 0.25f * Mathf.Sin(t * 0.9f + p1) + 0.15f * Mathf.Sin(t * 2.3f + p2);
                    d[i] += n[i] * sw / (1f + b * 0.5f);
                }
            }
            // Fold the tail onto the start with a crossfade so the loop has no click.
            int len = (int)(L * Rate), xf = (int)(X * Rate);
            var o = new float[len];
            for (int i = 0; i < len; i++) o[i] = d[i];
            for (int i = 0; i < xf; i++)
            {
                float k = i / (float)xf;
                o[i] = d[i] * k + d[len + i] * (1f - k);
            }
            return Clip("crowd_loop", o, 0.5f);
        }

        /// <summary>Crowd roar: swelling formant noise plus dozens of "ahh" voices.</summary>
        public static AudioClip Cheer(float dur, string name)
        {
            var d = Buf(dur);
            float[] formants = { 650f, 1100f, 2400f };
            foreach (var fc in formants)
            {
                var n = NoiseBuf(dur);
                BandPass(n, t => fc, 1.4f);
                Mix(d, n, fc < 1000f ? 1f : 0.6f);
            }
            for (int v = 0; v < 36; v++)
            {
                float f0 = 160f + (float)rng.NextDouble() * 260f, start = (float)rng.NextDouble() * 0.5f;
                float len = dur * (0.5f + 0.4f * (float)rng.NextDouble()), vr = 4f + (float)rng.NextDouble() * 3f;
                int o = (int)(start * Rate);
                float ph = 0f;
                for (int i = 0; i < len * Rate && o + i < d.Length; i++)
                {
                    float t = i / (float)Rate;
                    ph += 2f * Mathf.PI * f0 * (1f + 0.02f * Mathf.Sin(2f * Mathf.PI * vr * t) + 0.05f * Mathf.Clamp01(t / 0.4f)) / Rate;
                    float saw = (ph / Mathf.PI) % 2f - 1f;
                    float env = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((len - t) / 0.6f);
                    d[o + i] += saw * env * 0.08f;
                }
            }
            LowPass(d, 3500f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] *= Mathf.Clamp01(t / 0.35f) * Mathf.Clamp01((dur - t) / (dur * 0.6f));
            }
            return Clip(name, d, 0.85f);
        }

        /// <summary>A voice-like burst: buzz through two moving formants, syllable by syllable.</summary>
        public static float[] Voice(float dur, float pitch, int syllables, float f1, float f2, float jitter)
        {
            var d = Buf(dur);
            float ph = 0f;
            float[] sylPitch = new float[syllables], sylF1 = new float[syllables];
            for (int k = 0; k < syllables; k++)
            {
                sylPitch[k] = pitch * (1f + ((float)rng.NextDouble() - 0.5f) * jitter);
                sylF1[k] = f1 * (0.8f + 0.4f * (float)rng.NextDouble());
            }
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate, u = t / dur * syllables;
                int k = Mathf.Min(syllables - 1, (int)u);
                float local = u - k;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(local)) * Mathf.Clamp01((dur - t) / 0.05f);
                ph += 2f * Mathf.PI * sylPitch[k] / Rate;
                float buzz = (ph / Mathf.PI) % 2f - 1f;
                d[i] = buzz * env;
            }
            var a = (float[])d.Clone();
            BandPass(d, t => f1 * (0.9f + 0.2f * Mathf.Sin(t * 13f)), 3f);
            BandPass(a, t => f2 * (0.9f + 0.15f * Mathf.Sin(t * 9f)), 4f);
            for (int i = 0; i < d.Length; i++) d[i] = d[i] + a[i] * 0.6f;
            LowPass(d, 3000f);
            return d;
        }

        public static AudioClip Murmur(int v) => Clip("murmur_" + v, Voice(1.1f + v * 0.2f, 110f + v * 18f, 4 + v, 520f, 1300f, 0.25f), 0.6f);
        public static AudioClip Complain() => Clip("complain", Voice(0.45f, 190f, 2, 700f, 1600f, 0.4f), 0.75f);
        public static AudioClip TauntShout() => Clip("taunt_shout", Voice(0.7f, 150f, 2, 650f, 1150f, 0.15f), 0.85f);

        /// <summary>Rising shimmer: a charged blow is ready.</summary>
        public static AudioClip PowerReady()
        {
            var d = Buf(0.45f);
            float ph = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(420f, 1250f, t / 0.45f);
                ph += 2f * Mathf.PI * f / Rate;
                d[i] = (Mathf.Sin(ph) + 0.4f * Mathf.Sin(ph * 2.01f)) * Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t / 0.3f);
            }
            return Clip("power_ready", d, 0.55f);
        }

        public static AudioClip HornStart() => Clip("horn_start", Horn(new[] { Hz(-14), Hz(-7), Hz(-2) }, new[] { 0.35f, 0.35f, 1.3f }), 0.8f);
        public static AudioClip HornVictory() => Clip("horn_win", Horn(new[] { Hz(-9), Hz(-5), Hz(-2), Hz(3), Hz(-2), Hz(3) }, new[] { 0.22f, 0.22f, 0.22f, 0.5f, 0.2f, 1.4f }), 0.85f);
        public static AudioClip HornDefeat() => Clip("horn_lose", Horn(new[] { Hz(-14), Hz(-18), Hz(-21) }, new[] { 0.5f, 0.5f, 1.8f }), 0.8f);
    }
}
