using System;
using System.IO;
using UnityEngine;

namespace VampireRounds.Vs
{
    /// <summary>
    /// Pictures the mod loads without Unity's image decoders (stripped from IL2CPP builds): "VRI1", width, height, then
    /// RGBA rows bottom-up, as Unity keeps a texture. The engine writes Orange's and Blue's portraits this way;
    /// tools/art.py writes the mod's own art the same way.
    /// </summary>
    public static class RawImage
    {
        /// <summary>The engine draws portraits at 240 px, framed tightly around the ROUNDS player and its gun; VS's sprite of
        /// Orange Guy or Blue Guy is a sixth of that. VS sizes a character's picture by its pixels (3.2 UI units a pixel in
        /// character select, where a tile is 190 units; its own heroes are about 32 px): 40 px is 128 units there, and
        /// 0.4 units in a run.</summary>
        public const int PortraitSize = 40;

        public static string PortraitFile(int seat) => Path.Combine(Paths.DataDir(), "portraits", "seat" + seat + ".rgba");

        public static Texture2D Load(string file)
        {
            byte[] data;
            int w, h;
            if (!Read(file, out data, out w, out h)) return null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            Upload(tex, data);
            return tex;
        }

        /// <summary>Into an existing texture (sprites made from it change with it), scaled down to its size by whole steps
        /// (box filter) when the file is bigger.</summary>
        public static bool LoadInto(Texture2D tex, string file)
        {
            byte[] data;
            int w, h;
            if (!Read(file, out data, out w, out h)) return false;
            if (w != tex.width || h != tex.height)
            {
                if (w % tex.width != 0 || h % tex.height != 0 || w / tex.width != h / tex.height) return false;
                data = Shrink(data, w, h, w / tex.width);
            }
            Upload(tex, data);
            return true;
        }

        /// <summary>Box filter by k, in premultiplied colour so edges stay clean.</summary>
        private static byte[] Shrink(byte[] src, int w, int h, int k)
        {
            int ow = w / k, oh = h / k;
            var dst = new byte[ow * oh * 4];
            for (int y = 0; y < oh; y++)
                for (int x = 0; x < ow; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int dy = 0; dy < k; dy++)
                        for (int dx = 0; dx < k; dx++)
                        {
                            int i = (((y * k + dy) * w) + x * k + dx) * 4;
                            float al = src[i + 3] / 255f;
                            r += src[i] * al; g += src[i + 1] * al; b += src[i + 2] * al; a += al;
                        }
                    int o = (y * ow + x) * 4;
                    if (a > 0f) { dst[o] = (byte)Math.Min(255f, r / a); dst[o + 1] = (byte)Math.Min(255f, g / a); dst[o + 2] = (byte)Math.Min(255f, b / a); }
                    dst[o + 3] = (byte)Math.Min(255f, a / (k * k) * 255f);
                }
            return dst;
        }

        private static unsafe void Upload(Texture2D tex, byte[] data)
        {
            fixed (byte* p = data) tex.LoadRawTextureData((IntPtr)p, data.Length);
            tex.Apply(false, false);
        }

        private static bool Read(string file, out byte[] data, out int w, out int h)
        {
            data = null; w = h = 0;
            try
            {
                if (!File.Exists(file)) return false;
                var all = File.ReadAllBytes(file);
                if (all.Length < 12 || all[0] != 'V' || all[1] != 'R' || all[2] != 'I' || all[3] != '1') return false;
                w = BitConverter.ToInt32(all, 4);
                h = BitConverter.ToInt32(all, 8);
                if (w <= 0 || h <= 0 || w > 4096 || h > 4096 || all.Length < 12 + w * h * 4) return false;
                data = new byte[w * h * 4];
                Buffer.BlockCopy(all, 12, data, 0, data.Length);
                return true;
            }
            catch (Exception e) { Mod.Warn("image " + file + ": " + e.Message); return false; }
        }

        /// <summary>The stand-in until ROUNDS has drawn the real one: a round blob in the team colour with two eyes.</summary>
        public static void StandIn(Texture2D tex, string hex)
        {
            int n = tex.width;
            var px = new byte[n * n * 4];
            int rgb = Convert.ToInt32(hex, 16);
            byte r = (byte)(rgb >> 16), g = (byte)(rgb >> 8), b = (byte)rgb;
            float cx = n / 2f, cy = n * 0.42f, rad = n * 0.2f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy, d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float a = Math.Max(0f, Math.Min(1f, rad - d + 0.5f));
                    int i = (y * n + x) * 4;
                    bool eye = (Math.Abs(dx - rad * 0.38f) < rad * 0.12f || Math.Abs(dx + rad * 0.05f) < rad * 0.12f) && Math.Abs(dy - rad * 0.25f) < rad * 0.2f;
                    px[i] = eye ? (byte)20 : r; px[i + 1] = eye ? (byte)20 : g; px[i + 2] = eye ? (byte)20 : b; px[i + 3] = (byte)(a * 255f);
                }
            Upload(tex, px);
        }
    }
}
