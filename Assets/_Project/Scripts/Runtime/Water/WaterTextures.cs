using UnityEngine;

namespace Downstream.Water
{
    /// <summary>
    /// Procedural, tileable stand-in textures for the greybox water and bed, generated at runtime so
    /// the repository ships no binary art. Everything is a sum of integer-frequency sines (so it tiles
    /// exactly) or a cellular pattern on a torus. The hand-painted foam strokes and ripple normals of
    /// the design replace these without any shader change.
    /// </summary>
    public static class WaterTextures
    {
        private static Texture2D _ripple, _foam, _pebbles, _soft, _sprite, _wake, _swirl;

        public static Texture2D Ripple => _ripple != null ? _ripple : (_ripple = RippleNormals(256));
        public static Texture2D Foam => _foam != null ? _foam : (_foam = FoamStrokes(256));
        public static Texture2D Pebbles => _pebbles != null ? _pebbles : (_pebbles = PebbleAlbedo(256));
        public static Texture2D SoftNoise => _soft != null ? _soft : (_soft = SoftNoiseTexture(256));
        public static Texture2D SoftSprite => _sprite != null ? _sprite : (_sprite = SoftSpriteTexture(96));
        public static Texture2D WakeBand => _wake != null ? _wake : (_wake = WakeBandTexture(128));
        public static Texture2D Swirl => _swirl != null ? _swirl : (_swirl = SwirlTexture(128));

        /// <summary>Tangent-space ripple normals (xy in RG), small capillary waves over longer swells.</summary>
        public static Texture2D RippleNormals(int size)
        {
            var rng = new System.Random(4201);
            const int components = 14;
            var fx = new int[components];
            var fz = new int[components];
            var amp = new float[components];
            var ph = new float[components];
            for (int k = 0; k < components; k++)
            {
                float f = 2f + 14f * (float)rng.NextDouble();
                float ang = (float)(rng.NextDouble() * System.Math.PI * 2);
                fx[k] = Mathf.RoundToInt(Mathf.Cos(ang) * f);
                fz[k] = Mathf.RoundToInt(Mathf.Sin(ang) * f);
                if (fx[k] == 0 && fz[k] == 0) fx[k] = 3;
                float freq = Mathf.Sqrt(fx[k] * fx[k] + fz[k] * fz[k]);
                amp[k] = 1f / (0.6f + freq);
                ph[k] = (float)(rng.NextDouble() * System.Math.PI * 2);
            }

            float Height(float u, float v)
            {
                float h = 0f;
                for (int k = 0; k < components; k++)
                    h += amp[k] * Mathf.Sin(2f * Mathf.PI * (fx[k] * u + fz[k] * v) + ph[k]);
                return h;
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "RippleNormals", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            float d = 1f / size;
            const float slopeScale = 1.6f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x * d, v = y * d;
                float dx = (Height(u + d, v) - Height(u - d, v)) / (2f * d) * slopeScale * d;
                float dz = (Height(u, v + d) - Height(u, v - d)) / (2f * d) * slopeScale * d;
                var n = new Vector3(-dx, -dz, 1f).normalized;
                px[y * size + x] = new Color32((byte)((n.x * 0.5f + 0.5f) * 255f), (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Elongated, broken streaks in R: long along u (the flow), short across it.</summary>
        public static Texture2D FoamStrokes(int size)
        {
            var rng = new System.Random(77);
            const int components = 10;
            var fx = new int[components];
            var fz = new int[components];
            var ph = new float[components];
            for (int k = 0; k < components; k++)
            {
                fx[k] = 1 + rng.Next(3);
                fz[k] = 5 + rng.Next(12);
                if (rng.NextDouble() < 0.5) fz[k] = -fz[k];
                ph[k] = (float)(rng.NextDouble() * System.Math.PI * 2);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "FoamStrokes", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            float d = 1f / size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x * d, v = y * d;
                float a = 0f, b = 0f;
                for (int k = 0; k < components; k++)
                {
                    float s = Mathf.Sin(2f * Mathf.PI * (fx[k] * u + fz[k] * v) + ph[k]);
                    if ((k & 1) == 0) a += s; else b += s;
                }
                // Ridges of two interleaved fields: thin bright strokes with ragged ends.
                float ridge = Mathf.Max(0f, 1f - Mathf.Abs(a / 3.6f)) * Mathf.Max(0f, 1f - Mathf.Abs(b / 3f) * 0.5f);
                float value = Mathf.Clamp01(Mathf.Pow(ridge, 1.3f) * 1.3f);
                byte c = (byte)(value * 255f);
                px[y * size + x] = new Color32(c, c, c, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Soft, tileable mottling in R (0..1) for breaking up flat grass and ground colours.</summary>
        /// <summary>Spiral foam arms on a clear disc: the whirlpool, spun by rotating the mesh.</summary>
        public static Texture2D SwirlTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "Swirl", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);
                float arms = 0.5f + 0.5f * Mathf.Sin(ang * 3f + r * 11f);
                float a = Mathf.Pow(Mathf.Clamp01(arms), 2.2f) * Mathf.Clamp01(1f - r) * Mathf.Clamp01(r * 4f);
                a *= 0.4f + 0.6f * Mathf.Clamp01(1f - r); // denser at the eye
                byte c = (byte)(Mathf.Clamp01(0.85f + 0.15f * a) * 255f);
                px[y * size + x] = new Color32(c, (byte)Mathf.Min(255, c + 6), 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>A wake ribbon: U runs along the trail, V across it with feathered edges and streaks.</summary>
        public static Texture2D WakeBandTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "WakeBand", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size * 2f - 1f;
                float across = Mathf.Clamp01(1f - Mathf.Abs(v));
                across = across * across * (3f - 2f * across);
                float streak = 0.75f + 0.25f * Mathf.Sin(u * Mathf.PI * 2f * 3f + v * 9f) * Mathf.Sin(v * Mathf.PI * 4f + 0.7f);
                float a = Mathf.Clamp01(across * streak);
                byte c = (byte)(Mathf.Clamp01(0.92f + 0.08f * a) * 255f);
                px[y * size + x] = new Color32(c, c, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>A soft round puff with a ragged edge: one spray droplet or mist cloud.</summary>
        public static Texture2D SoftSpriteTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "SoftSprite", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float ang = Mathf.Atan2(v, u);
                float edge = 1f + 0.05f * Mathf.Sin(ang * 4f + 1.3f) + 0.03f * Mathf.Sin(ang * 7f + 0.4f);
                float a = Mathf.Clamp01(1f - r / (0.95f * edge));
                a = a * a * (3f - 2f * a);
                a = Mathf.Pow(a, 1.6f);
                byte c = (byte)(Mathf.Clamp01(0.86f + 0.14f * a) * 255f);
                px[y * size + x] = new Color32(c, (byte)Mathf.Min(255, c + 4), 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        public static Texture2D SoftNoiseTexture(int size)
        {
            var rng = new System.Random(31);
            const int components = 12;
            var fx = new int[components]; var fz = new int[components]; var ph = new float[components]; var amp = new float[components];
            for (int k = 0; k < components; k++)
            {
                float f = 1f + 6f * (float)rng.NextDouble();
                float ang = (float)(rng.NextDouble() * System.Math.PI * 2);
                fx[k] = Mathf.RoundToInt(Mathf.Cos(ang) * f); fz[k] = Mathf.RoundToInt(Mathf.Sin(ang) * f);
                if (fx[k] == 0 && fz[k] == 0) fz[k] = 2;
                amp[k] = 1f / (1f + Mathf.Sqrt(fx[k] * fx[k] + fz[k] * fz[k]));
                ph[k] = (float)(rng.NextDouble() * System.Math.PI * 2);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = "SoftNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            float d = 1f / size, norm = 0f;
            for (int k = 0; k < components; k++) norm += amp[k];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x * d, v = y * d, h = 0f;
                for (int k = 0; k < components; k++) h += amp[k] * Mathf.Sin(2f * Mathf.PI * (fx[k] * u + fz[k] * v) + ph[k]);
                byte c = (byte)(Mathf.Clamp01(0.5f + 0.5f * h / norm * 1.6f) * 255f);
                px[y * size + x] = new Color32(c, c, c, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// <summary>Rounded stones of varied grey-brown over dark gaps, for the river bed.</summary>
        public static Texture2D PebbleAlbedo(int size)
        {
            var rng = new System.Random(9);
            const int stones = 140;
            var cx = new float[stones];
            var cy = new float[stones];
            var r = new float[stones];
            var tint = new Color[stones];
            for (int i = 0; i < stones; i++)
            {
                cx[i] = (float)rng.NextDouble();
                cy[i] = (float)rng.NextDouble();
                r[i] = 0.035f + 0.045f * (float)rng.NextDouble();
                float g = 0.50f + 0.14f * (float)rng.NextDouble();
                float warm = 0.04f * (float)rng.NextDouble();
                tint[i] = new Color(g + warm, g, g - warm * 0.5f, 1f);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "Pebbles", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color32[size * size];
            var gap = new Color(0.40f, 0.39f, 0.35f);
            float d = 1f / size;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x * d, v = y * d;
                float best = float.MaxValue;
                int bi = -1;
                for (int i = 0; i < stones; i++)
                {
                    float dx = Mathf.Abs(u - cx[i]); dx = Mathf.Min(dx, 1f - dx);
                    float dy = Mathf.Abs(v - cy[i]); dy = Mathf.Min(dy, 1f - dy);
                    float q = (dx * dx + dy * dy) / (r[i] * r[i]);
                    if (q < best) { best = q; bi = i; }
                }
                float inside = Mathf.Clamp01(1f - best);
                // Domed shading: brighter in the centre, dark at the rim.
                float shade = 0.85f + 0.2f * Mathf.Sqrt(inside);
                var c = best < 1f ? tint[bi] * shade : gap;
                px[y * size + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
