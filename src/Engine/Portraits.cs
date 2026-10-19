using System;
using System.IO;
using UnityEngine;

namespace VampireRounds
{
    /// <summary>
    /// Orange's and Blue's pictures for Vampire Survivors's character select and stand-in sprite (sheets/systems.json
    /// "portraits"): each ROUNDS player, as ROUNDS draws it (particle skin, face, gun), rendered once per engine session into
    /// %LOCALAPPDATA%\VampireRounds\portraits\seat0.png / seat1.png. The VS mod loads them into its character sprites.
    /// </summary>
    public static class Portraits
    {
        public const int Size = 256;
        public const float Units = 3.8f;
        private static readonly bool[] done = new bool[ArenaLink.Seats];
        private static readonly float[] since = new float[ArenaLink.Seats];

        /// <summary>Raw RGBA (RawImage format: "VRI1", width, height, rows bottom-up), and a PNG beside it for people.</summary>
        public static string FileFor(int seat) => Path.Combine(Path.Combine(Paths.DataDir(), "portraits"), "seat" + seat + ".rgba");

        public static void Tick(Player[] players)
        {
            for (int i = 0; i < players.Length && i < done.Length; i++)
            {
                var p = players[i];
                if (done[i] || p == null) { since[i] = 0f; continue; }
                if (since[i] <= 0f) since[i] = Time.unscaledTime;
                if (Time.unscaledTime - since[i] < 3f) continue; // the particle skin settles, the face is on
                done[i] = true;
                try { Render(p, FileFor(i)); EnginePlugin.L.LogInfo("portraits: seat " + i + " written to " + FileFor(i)); }
                catch (Exception e) { EnginePlugin.L.LogWarning("portraits: seat " + i + ": " + e.Message); }
            }
        }

        private static void Render(Player p, string file)
        {
            var go = new GameObject("Vampire Rounds portrait camera");
            var cam = go.AddComponent<Camera>();
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            try
            {
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = Units / 2f;
                cam.aspect = 1f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                var main = MainCam.instance != null ? MainCam.instance.cam : Camera.main;
                cam.cullingMask = main != null ? main.cullingMask : ~0;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 1000f;
                // Just the player: its health bar above it stays out of the picture.
                var bar = p.transform.Find("WobbleObjects/Healthbar");
                bool barWas = bar != null && bar.gameObject.activeSelf;
                if (bar != null) bar.gameObject.SetActive(false);
                Vector3 c = p.transform.position;
                cam.transform.position = new Vector3(c.x, c.y + 0.55f, -100f); // room for hats above the head
                cam.targetTexture = rt;
                cam.Render();
                if (bar != null) bar.gameObject.SetActive(barWas);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = prev;
                // Premultiplied render target to straight alpha, as the VS sprite shader wants it.
                var px = tex.GetPixels32();
                for (int k = 0; k < px.Length; k++)
                {
                    var q = px[k];
                    if (q.a == 0 || q.a == 255) continue;
                    px[k] = new Color32((byte)Math.Min(255, q.r * 255 / q.a), (byte)Math.Min(255, q.g * 255 / q.a), (byte)Math.Min(255, q.b * 255 / q.a), q.a);
                }
                tex.SetPixels32(px);
                tex.Apply(false);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var raw = tex.GetRawTextureData();
                using (var fs = new FileStream(file + ".tmp", FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(new[] { (byte)'V', (byte)'R', (byte)'I', (byte)'1' });
                    bw.Write(Size);
                    bw.Write(Size);
                    bw.Write(raw);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".tmp", file);
                File.WriteAllBytes(Path.ChangeExtension(file, ".png"), tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                UnityEngine.Object.Destroy(tex);
                UnityEngine.Object.Destroy(go);
            }
        }
    }
}
