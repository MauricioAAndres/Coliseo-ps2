using UnityEngine;

namespace Gladiator.Audio
{
    /// <summary>
    /// Procedural "ancient Rome" music loops, rendered once per piece: plucked lyre (Karplus-Strong), frame drums,
    /// a low drone, and cornu stabs in the arena. D Phrygian dominant scale. Each piece is a seamless loop: notes
    /// still ringing at the end are folded back onto the start, and the drone frequencies are snapped so a whole
    /// number of cycles fits the loop.
    /// </summary>
    public static class MusicSynth
    {
        const int Rate = SoundSynth.Rate;
        static readonly int[] Scale = { 0, 1, 4, 5, 7, 8, 10 }; // Phrygian dominant
        const float RootHz = 146.83f;                            // D3

        public enum Piece { Menu, Ludus, Arena }

        static float Degree(int deg, int octave = 0)
        {
            int o = Mathf.FloorToInt(deg / 7f);
            int s = Scale[((deg % 7) + 7) % 7] + 12 * (o + octave);
            return RootHz * Mathf.Pow(2f, s / 12f);
        }

        public static AudioClip Render(Piece piece)
        {
            float bpm = piece == Piece.Menu ? 72f : piece == Piece.Ludus ? 92f : 118f;
            const int bars = 16;
            float beat = 60f / bpm, L = bars * 4 * beat;
            int len = (int)(L * Rate);
            var d = new float[len + Rate * 3];
            var rnd = new System.Random(piece == Piece.Menu ? 7 : piece == Piece.Ludus ? 11 : 23);

            // Chord roots per bar (scale degrees): i, bII, i, bVII ... with a turn at the end.
            int[] prog = { 0, 0, 1, 0, 0, -1, 1, 0, 0, 0, 1, 0, 3, 1, -1, 0 };

            // ---- drone (root + fifth), snapped to whole cycles per loop
            float droneGain = piece == Piece.Arena ? 0.22f : 0.16f;
            Drone(d, len, L, RootHz * 0.5f, droneGain);
            Drone(d, len, L, RootHz * 0.75f, droneGain * 0.6f);

            // ---- drums
            for (int bar = 0; bar < bars; bar++)
            {
                for (int s = 0; s < 8; s++) // eighths
                {
                    float t = (bar * 4 + s * 0.5f) * beat;
                    switch (piece)
                    {
                        case Piece.Menu:
                            if (s == 0) Drum(d, t, 0.35f, false);
                            break;
                        case Piece.Ludus:
                            if (s == 0 || s == 4) Drum(d, t, 0.5f, false);
                            if (s == 3 || s == 7) Drum(d, t, 0.22f, true);
                            if (s == 6 && bar % 2 == 1) Drum(d, t, 0.3f, false);
                            break;
                        case Piece.Arena:
                            if (s == 0 || s == 3 || s == 4) Drum(d, t, s == 0 ? 0.8f : 0.6f, false);
                            if (s % 2 == 1 || s == 6) Drum(d, t, 0.28f, true);
                            if (bar % 4 == 3 && s >= 4) Drum(d, t + beat * 0.25f, 0.35f, false); // fill
                            break;
                    }
                }
            }

            // ---- lyre: arpeggios (menu), melody + bass (ludus), driving ostinato (arena)
            int[] motif = new int[8];
            for (int i = 0; i < 8; i++) motif[i] = rnd.Next(-2, 5);
            for (int bar = 0; bar < bars; bar++)
            {
                int root = prog[bar];
                if (piece == Piece.Menu)
                {
                    int[] arp = { 0, 2, 4, 7, 4, 2 };
                    for (int k = 0; k < 6; k++)
                        Pluck(d, (bar * 4 + k * (4f / 6f)) * beat, Degree(root + arp[k]), 0.32f, 2.2f);
                }
                else if (piece == Piece.Ludus)
                {
                    Pluck(d, bar * 4 * beat, Degree(root, -1), 0.4f, 2.5f);
                    Pluck(d, (bar * 4 + 2) * beat, Degree(root + 4, -1), 0.3f, 2f);
                    bool vary = bar % 4 == 3;
                    for (int k = 0; k < 8; k++)
                    {
                        if (k % 2 == 1 && rnd.NextDouble() < 0.35) continue; // rests keep it singing
                        int deg = root + motif[k] + (vary ? rnd.Next(-1, 2) : 0) + 7;
                        Pluck(d, (bar * 4 + k * 0.5f) * beat, Degree(deg), 0.3f, 1.6f);
                    }
                }
                else
                {
                    int[] ost = { 0, 0, 7, 0, 1, 0, 4, 2 };
                    for (int k = 0; k < 8; k++)
                        Pluck(d, (bar * 4 + k * 0.5f) * beat, Degree(root + ost[k], k % 4 == 0 ? -1 : 0), k % 4 == 0 ? 0.38f : 0.26f, 1.1f);
                    if (bar % 4 == 0)
                    {
                        float[] stab = SoundSynth.Horn(new[] { Degree(root, -1), Degree(root + 4, -1) }, new[] { beat * 1.5f, beat * 2.5f });
                        Add(d, stab, bar * 4 * beat, 0.09f);
                    }
                }
            }

            // ---- fold the ringing tail into the start: seamless loop
            var o = new float[len];
            for (int i = 0; i < len; i++) o[i] = d[i];
            for (int i = len; i < d.Length; i++) o[i - len] += d[i];
            SoundSynth.LowPass(o, 5000f);
            return SoundSynth.Clip("music_" + piece.ToString().ToLowerInvariant(), o, 0.8f);
        }

        static void Add(float[] d, float[] src, float at, float gain)
        {
            int o = (int)(at * Rate);
            for (int i = 0; i < src.Length && o + i < d.Length; i++) d[o + i] += src[i] * gain;
        }

        static void Drone(float[] d, int len, float L, float hz, float gain)
        {
            float f = Mathf.Round(hz * L) / L, f2 = Mathf.Round(hz * 1.003f * L) / L;
            float y = 0f, a = 1f - Mathf.Exp(-2f * Mathf.PI * 420f / Rate);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)Rate;
                float saw = ((f * t) % 1f) * 2f - 1f + ((f2 * t) % 1f) * 2f - 1f;
                y += a * (saw - y);
                float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * t * 2f / L);
                d[i] += y * gain * swell;
            }
        }

        static void Drum(float[] d, float at, float gain, bool tak)
        {
            int o = (int)(at * Rate);
            float dur = tak ? 0.06f : 0.4f;
            float ph = 0f;
            for (int i = 0; i < dur * Rate && o + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s;
                if (tak) s = SoundSynth.Noise() * Mathf.Exp(-t / 0.012f) * 0.7f;
                else
                {
                    float f = 52f + 50f * Mathf.Exp(-t * 25f);
                    ph += 2f * Mathf.PI * f / Rate;
                    s = Mathf.Sin(ph) * Mathf.Exp(-t / 0.16f) + SoundSynth.Noise() * Mathf.Exp(-t / 0.015f) * 0.35f;
                }
                d[o + i] += s * gain;
            }
        }

        /// <summary>Karplus-Strong plucked string: a noise burst circulating through a damped delay line.</summary>
        static void Pluck(float[] d, float at, float hz, float gain, float dur)
        {
            int period = Mathf.Max(2, Mathf.RoundToInt(Rate / hz));
            var line = new float[period];
            for (int i = 0; i < period; i++) line[i] = SoundSynth.Noise();
            int o = (int)(at * Rate), n = (int)(dur * Rate), idx = 0;
            float decay = 0.996f;
            for (int i = 0; i < n && o + i < d.Length; i++)
            {
                int next = (idx + 1) % period;
                float v = line[idx];
                line[idx] = decay * 0.5f * (line[idx] + line[next]);
                idx = next;
                float fade = i > n - 400 ? (n - i) / 400f : 1f;
                d[o + i] += v * gain * fade;
            }
        }
    }
}
