using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.PS2Style
{
    /// <summary>
    /// Procedural, tileable, low-res textures (<= 128px) with a posterised palette.
    /// Bilinear filtering and NO mipmaps — the PS2 look (shimmering at distance, soft up close).
    /// </summary>
    public static class PS2Textures
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        const int PaletteLevels = 16;

        public static Texture2D Sand => Get("sand", 128, GenSand);
        public static Texture2D Stone => Get("stone", 128, GenStone);
        public static Texture2D Cloth => Get("cloth", 64, GenCloth);
        public static Texture2D Skin => Get("skin", 64, GenSkin);
        public static Texture2D Metal => Get("metal", 64, GenMetal);
        public static Texture2D Wood => Get("wood", 64, GenWood);
        public static Texture2D Leather => Get("leather", 64, GenLeather);
        public static Texture2D White => Get("white", 8, (x, y, s) => Color.white);

        static Texture2D Get(string key, int size, Func<int, int, int, Color> gen)
        {
            if (cache.TryGetValue(key, out var t) && t != null) return t;
            t = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PS2_" + key,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 0
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = Posterize(gen(x, y, size), PaletteLevels);
            t.SetPixels32(px);
            t.Apply(false, true);
            cache[key] = t;
            return t;
        }

        static Color32 Posterize(Color c, int levels)
        {
            float l = levels - 1;
            return new Color(Mathf.Round(Mathf.Clamp01(c.r) * l) / l,
                             Mathf.Round(Mathf.Clamp01(c.g) * l) / l,
                             Mathf.Round(Mathf.Clamp01(c.b) * l) / l, 1f);
        }

        // ---------------- noise ----------------
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int ax = Mod(x0, period), bx = Mod(x0 + 1, period);
            int ay = Mod(y0, period), by = Mod(y0 + 1, period);
            float a = Hash(ax, ay, seed), b = Hash(bx, ay, seed), c = Hash(ax, by, seed), d = Hash(bx, by, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int baseFreq, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int f = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Noise(u * f, v * f, f, seed + o * 31);
                norm += amp; amp *= 0.5f; f *= 2;
            }
            return sum / norm;
        }

        // ---------------- generators ----------------
        static Color GenSand(int x, int y, int s)
        {
            float u = x / (float)s, v = y / (float)s;
            float n = Fbm(u, v, 4, 4, 11);
            Color c = Color.Lerp(new Color(0.60f, 0.46f, 0.30f), new Color(0.86f, 0.73f, 0.52f), n);
            float g = Hash(x, y, 7);
            if (g > 0.94f) c *= 1.12f;
            else if (g < 0.05f) c *= 0.82f;
            return c;
        }

        static Color GenStone(int x, int y, int s)
        {
            const int rowH = 16, brickW = 32;
            int row = y / rowH;
            int offset = (row % 2) * (brickW / 2);
            int bx = (x + offset) / brickW;
            int lx = (x + offset) % brickW, ly = y % rowH;
            if (lx < 2 || ly < 2) return new Color(0.33f, 0.29f, 0.25f);
            float tint = 0.82f + 0.3f * Hash(Mod(bx, s / brickW), row, 3);
            float n = Fbm(x / (float)s, y / (float)s, 8, 3, 2);
            Color c = new Color(0.70f, 0.63f, 0.54f) * tint * (0.78f + 0.4f * n);
            if (ly == 2) c *= 1.12f;
            if (ly == rowH - 1) c *= 0.85f;
            return c;
        }

        static Color GenCloth(int x, int y, int s)
        {
            float weave = ((x & 1) ^ (y & 1)) == 0 ? 0.92f : 0.82f;
            float n = Fbm(x / (float)s, y / (float)s, 4, 3, 21);
            float v = weave * (0.82f + 0.25f * n);
            return new Color(v, v, v);
        }

        static Color GenSkin(int x, int y, int s)
        {
            float n = Fbm(x / (float)s, y / (float)s, 4, 3, 5);
            return new Color(0.86f, 0.64f, 0.48f) * (0.9f + 0.18f * n);
        }

        static Color GenMetal(int x, int y, int s)
        {
            float n = Fbm(x / (float)s, y / (float)s, 2, 4, 9);
            float streak = Noise(x * 0.08f, y * 0.9f, Mathf.Max(1, Mathf.RoundToInt(s * 0.9f)), 13);
            float v = 0.5f + 0.25f * n + 0.15f * streak;
            if (Hash(x, y, 17) > 0.975f) v += 0.25f;
            return new Color(v * 0.95f, v * 0.97f, v * 1.03f);
        }

        static Color GenWood(int x, int y, int s)
        {
            float u = x / (float)s, v = y / (float)s;
            float n = Fbm(u, v, 4, 3, 41);
            float grain = Mathf.Sin((u * 6f + n * 1.5f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            return Color.Lerp(new Color(0.32f, 0.20f, 0.11f), new Color(0.56f, 0.38f, 0.21f), grain * 0.7f + n * 0.3f);
        }

        static Color GenLeather(int x, int y, int s)
        {
            float n = Fbm(x / (float)s, y / (float)s, 8, 3, 61);
            return new Color(0.42f, 0.26f, 0.15f) * (0.8f + 0.35f * n);
        }
    }
}
