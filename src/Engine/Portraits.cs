using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VampireRounds
{
    /// <summary>
    /// Orange Guy's and Blue Guy's pictures for Vampire Survivors's character select and stand-in sprite (sheets/systems.json
    /// "portraits"), rendered once per engine session into %LOCALAPPDATA%\VampireRounds\portraits\seat0.rgba / seat1.rgba
    /// (a .png beside each for people). The VS mod loads them into its character sprites.
    /// Each is a posed copy of ROUNDS's own player (its particle skin, the player's face, IK legs and arm, the gun), standing
    /// on a floor with the gun held level, as ROUNDS shows a player at rest; the arena's live players are parked off the
    /// floor between runs, legs hanging and gun up. Gameplay scripts are off, as for Roundhead's world-map puppet (MIT, the
    /// same creator). The picture is framed tightly around what was drawn.
    /// </summary>
    public static class Portraits
    {
        /// <summary>Six times VS's 40-px sprite (RawImage.PortraitSize), which VS shrinks it to in whole steps.</summary>
        public const int Size = 240;
        private const int Layer = 30;
        /// <summary>Frames the pose settles for (legs plant, skin particles fill in) before the picture is taken.</summary>
        private const int SettleFrames = 90;
        private static readonly bool[] done = new bool[ArenaLink.Seats];
        private static readonly Pose[] posing = new Pose[ArenaLink.Seats];

        /// <summary>Raw RGBA (RawImage format: "VRI1", width, height, rows bottom-up), and a PNG beside it for people.</summary>
        public static string FileFor(int seat) => Path.Combine(Path.Combine(Paths.DataDir(), "portraits"), "seat" + seat + ".rgba");

        /// <summary>Once the arena's players exist (ROUNDS has given them their faces), each seat is posed and pictured.</summary>
        public static void Tick(Player[] players)
        {
            for (int i = 0; i < players.Length && i < done.Length; i++)
            {
                if (done[i] || players[i] == null) continue;
                try
                {
                    if (posing[i] == null)
                    {
                        posing[i] = Pose.Create(i);
                        if (posing[i] == null) { done[i] = true; continue; }
                    }
                    var p = posing[i];
                    p.Tick();
                    if (p.Frames < SettleFrames) continue;
                    done[i] = true;
                    posing[i] = null;
                    try { p.Render(FileFor(i)); EnginePlugin.L.LogInfo("portraits: seat " + i + " posed and written to " + FileFor(i)); }
                    finally { p.Destroy(); }
                }
                catch (Exception e)
                {
                    done[i] = true;
                    EnginePlugin.L.LogWarning("portraits: seat " + i + ": " + e);
                    try { posing[i]?.Destroy(); } catch { }
                    posing[i] = null;
                }
            }
        }

        /// <summary>A copy of ROUNDS's player prefab far from the arena, animated only by ROUNDS's look scripts (also posed
        /// for the launcher's face pictures, Faces.DrawSheet).</summary>
        internal class Pose
        {
            private static readonly HashSet<string> Animate = new HashSet<string>
            {
                "SetTeamColor", "PlayerSkinHandler", "SetPlayerSpriteLayer", "PlayerSkinParticle", "CharacterCreatorItemEquipper",
                "CharacterItem", "CharacterItemMirror", "IKArmMove", "LimbIK", "LegRenderer", "IkLeg", "LegRotator", "ArmRotator",
                "FollowInactiveHand", "Unparent", "FollowTransform", "FollowScale", "PlayerWobblePosition", "CodeAnimation",
                "RightLeftMirrorSpring", "HandPos", "Holdable", "Rotate"
            };
            private static readonly FieldInfo VelField = typeof(PlayerVelocity).GetField("velocity", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            private readonly int seat;
            private GameObject root, ground;
            private CharacterData data;
            private Holding holding;
            private Transform holdingObject, handPos;
            private Vector3 basePos;
            private float scale = 1f;
            public int Frames { get; private set; }

            private Pose(int seat) { this.seat = seat; }

            public static Pose Create(int seat)
            {
                var pa = PlayerAssigner.instance ?? Resources.FindObjectsOfTypeAll<PlayerAssigner>().FirstOrDefault();
                if (pa == null || pa.playerPrefab == null) { EnginePlugin.L.LogWarning("portraits: no ROUNDS player prefab"); return null; }
                var p = new Pose(seat);
                p.Build(pa.playerPrefab);
                return p;
            }

            private void Build(GameObject prefab)
            {
                basePos = new Vector3(-9000f - seat * 400f, 9000f, 0f);
                root = UnityEngine.Object.Instantiate(prefab, basePos, Quaternion.identity);
                root.name = "Vampire Rounds portrait " + seat;
                UnityEngine.Object.DontDestroyOnLoad(root);
                scale = Mathf.Abs(root.transform.localScale.x);
                data = root.GetComponent<CharacterData>();
                holding = root.GetComponent<Holding>();
                var player = root.GetComponent<Player>();
                if (player != null) { player.playerID = seat; player.teamID = seat; }
                if (data != null) { data.isPlaying = false; data.isGrounded = true; data.aimDirection = Vector3.right; }
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true)) Keep(mb);
                if (holding != null && holding.holdable != null)
                {
                    foreach (var mb in holding.holdable.GetComponentsInChildren<MonoBehaviour>(true)) Keep(mb);
                    var rig = holding.holdable.GetComponent<Rigidbody2D>();
                    if (rig != null) { rig.gravityScale = 0f; rig.isKinematic = true; }
                }
                foreach (string off in new[] { "WobbleObjects/Healthbar", "WobbleObjects/Canvas_Chat", "LevelMapper", "Effects", "ObjectCollider" })
                {
                    var t = root.transform.Find(off);
                    if (t != null) t.gameObject.SetActive(false);
                }
                foreach (var col in root.GetComponentsInChildren<Collider2D>(true)) col.enabled = false;
                holdingObject = root.GetComponentInChildren<HoldingObject>(true)?.transform;
                handPos = root.GetComponentInChildren<HandPos>(true)?.transform;
                // A floor under its feet, on a layer the IK legs look for, so it stands as ROUNDS players do.
                int groundLayer = 0;
                var leg = root.GetComponentInChildren<IkLeg>(true);
                if (leg != null && leg.mask.value != 0)
                    for (int i = 0; i < 32; i++) if ((leg.mask.value & (1 << i)) != 0) { groundLayer = i; break; }
                ground = new GameObject("Vampire Rounds portrait floor " + seat);
                ground.layer = groundLayer;
                ground.AddComponent<BoxCollider2D>().size = new Vector2(400f, 2f);
                ground.transform.position = new Vector3(basePos.x, basePos.y - 1.27f * scale - 1f, 0f);
                UnityEngine.Object.DontDestroyOnLoad(ground);
                SetLayers();
            }

            private static void Keep(MonoBehaviour mb)
            {
                if (mb != null && !Animate.Contains(mb.GetType().Name)) mb.enabled = false;
            }

            /// <summary>Everything of the pose on its own layer, which only the portrait camera draws (and the arena's never).</summary>
            private void SetLayers()
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                foreach (var u in UnityEngine.Object.FindObjectsOfType<Unparent>())
                    if (u.parent == root.transform) foreach (var c in u.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = Layer;
                if (holding != null && holding.holdable != null) foreach (var c in holding.holdable.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = Layer;
            }

            /// <summary>Team colours and the player's face (the ones ROUNDS gave the arena's player of this seat).</summary>
            private void Dress()
            {
                SetTeamColor.TeamColorThis(root, PlayerSkinBank.GetPlayerSkinColors(seat));
                if (holding != null && holding.holdable != null) GunLook.Blacken(holding.holdable, 50);
                var eq = root.GetComponentInChildren<CharacterCreatorItemEquipper>(true);
                var handler = CharacterCreatorHandler.instance ?? Resources.FindObjectsOfTypeAll<CharacterCreatorHandler>().FirstOrDefault();
                PlayerFace face = handler != null && handler.selectedPlayerFaces != null && seat < handler.selectedPlayerFaces.Length ? handler.selectedPlayerFaces[seat] : null;
                if (eq != null && face != null) eq.EquipFace(face);
                FaceLook.Apply(eq);
            }

            /// <summary>Standing still, aiming right: the gun sits at the hand, pointing along the aim.</summary>
            public void Tick()
            {
                Frames++;
                if (Frames == 3) Dress();
                if (Frames % 20 == 0) SetLayers();   // objects ROUNDS spawns or unparents at runtime (skin particles, legs)
                root.transform.position = basePos;
                if (VelField != null) VelField.SetValue(root.GetComponent<PlayerVelocity>(), Vector2.zero);
                if (data != null) { data.isGrounded = true; data.sinceGrounded = 0f; data.isWallGrab = false; data.aimDirection = Vector3.right; }
                if (holdingObject != null) holdingObject.rotation = Quaternion.LookRotation(Vector3.right, Vector3.back);
                if (holding != null && holding.holdable != null && handPos != null)
                {
                    var gun = holding.holdable.transform;
                    gun.position = handPos.position;
                    gun.rotation = Quaternion.LookRotation(Vector3.forward, handPos.forward);
                }
            }

            /// <summary>Two renders: a wide one to find what was drawn, then the picture framed square around it.</summary>
            public void Render(string file)
            {
                const int probe = 256;
                float wide = 7f * scale;
                var center = basePos + new Vector3(0.4f * scale, -0.3f * scale, 0f);
                var px = Shoot(probe, center, wide);
                int x0 = probe, y0 = probe, x1 = -1, y1 = -1;
                for (int y = 0; y < probe; y++)
                    for (int x = 0; x < probe; x++)
                        if (px[y * probe + x].a > 24) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 >= x0 && y1 >= y0)
                {
                    float perPx = wide / probe;
                    // Pixel centres to world: the probe's bottom-left is centre - wide/2.
                    float wx0 = center.x - wide / 2f + x0 * perPx, wx1 = center.x - wide / 2f + (x1 + 1) * perPx;
                    float wy0 = center.y - wide / 2f + y0 * perPx, wy1 = center.y - wide / 2f + (y1 + 1) * perPx;
                    float side = Mathf.Max(wx1 - wx0, wy1 - wy0) * 1.06f;
                    // Feet on the picture's floor (a little margin), centred across.
                    center = new Vector3((wx0 + wx1) / 2f, wy0 - side * 0.03f + side / 2f, 0f);
                    wide = side;
                }
                else EnginePlugin.L.LogWarning("portraits: seat " + seat + " drew nothing in the probe; using the default framing");
                var final = Shoot(Size, center, wide);
                Straighten(final);
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                try
                {
                    tex.SetPixels32(final);
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
                finally { UnityEngine.Object.Destroy(tex); }
            }

            private Color32[] Shoot(int size, Vector3 center, float units, int layer = Layer)
            {
                var go = new GameObject("Vampire Rounds portrait camera");
                var cam = go.AddComponent<Camera>();
                var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                try
                {
                    cam.enabled = false;
                    cam.orthographic = true;
                    cam.orthographicSize = units / 2f;
                    cam.aspect = 1f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                    cam.cullingMask = 1 << layer;
                    cam.nearClipPlane = 0.01f;
                    cam.farClipPlane = 200f;
                    cam.transform.position = new Vector3(center.x, center.y, -50f);
                    cam.targetTexture = rt;
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    return tex.GetPixels32();
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

            /// <summary>Premultiplied render target to straight alpha, as the VS sprite shader wants it.</summary>
            private static void Straighten(Color32[] px)
            {
                for (int k = 0; k < px.Length; k++)
                {
                    var q = px[k];
                    if (q.a == 0 || q.a == 255) continue;
                    px[k] = new Color32((byte)Math.Min(255, q.r * 255 / q.a), (byte)Math.Min(255, q.g * 255 / q.a), (byte)Math.Min(255, q.b * 255 / q.a), q.a);
                }
            }

            public Vector3 BodyCenter => basePos;
            public float Scale => scale;

            /// <summary>Another of ROUNDS's faces on the pose (it shows from the next frame on, once it is on the pose's layer).</summary>
            public void EquipFace(PlayerFace face)
            {
                var eq = root.GetComponentInChildren<CharacterCreatorItemEquipper>(true);
                if (eq == null || face == null) return;
                // ROUNDS's Equip skips a part the face has none of (no detail) and would leave the last face's there.
                if (eq.itemParent != null)
                    for (int i = eq.itemParent.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(eq.itemParent.transform.GetChild(i).gameObject);
                eq.EquipFace(face);
                FaceLook.Apply(eq);
                SetLayers();
            }

            public void ShowGun(bool on)
            {
                if (holding != null && holding.holdable != null && holding.holdable.gameObject.activeSelf != on) holding.holdable.gameObject.SetActive(on);
            }

            public void Relayer() => SetLayers();

            private CharacterCreatorItemEquipper Equipper => root.GetComponentInChildren<CharacterCreatorItemEquipper>(true);
            /// <summary>Where ROUNDS puts the face's parts (an item at offset o sits at (item.offset + o) x ScaleM in it).</summary>
            public Transform FaceParent => Equipper != null && Equipper.itemParent != null ? Equipper.itemParent.transform : root.transform;
            public float FaceScaleM => Equipper != null ? Equipper.scaleM : 1f;

            public void ClearFace()
            {
                var eq = Equipper;
                if (eq == null || eq.itemParent == null) return;
                for (int i = eq.itemParent.transform.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(eq.itemParent.transform.GetChild(i).gameObject);
            }

            /// <summary>One face part alone, at its own place (offset 0), on a layer of its own for the part pictures.</summary>
            public void EquipPart(CharacterItem item, CharacterItemType type, int layer)
            {
                ClearFace();
                var eq = Equipper;
                if (eq == null || item == null) return;
                eq.Equip(item, type, Vector2.zero, 0);
                FaceLook.Apply(eq);
                foreach (var t in eq.itemParent.GetComponentsInChildren<Transform>(true)) if (t != eq.itemParent.transform) t.gameObject.layer = layer;
            }

            /// <summary>A square picture of the pose around a point, as a PNG.</summary>
            public void SavePng(string file, Vector3 center, float units, int size, int layer = Layer)
            {
                var px = Shoot(size, center, units, layer);
                Straighten(px);
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                try
                {
                    tex.SetPixels32(px);
                    tex.Apply(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllBytes(file + ".tmp", tex.EncodeToPNG());
                    if (File.Exists(file)) File.Delete(file);
                    File.Move(file + ".tmp", file);
                }
                finally { UnityEngine.Object.Destroy(tex); }
            }

            public void Destroy()
            {
                if (holding != null && holding.holdable != null) UnityEngine.Object.Destroy(holding.holdable.gameObject);
                foreach (var u in UnityEngine.Object.FindObjectsOfType<Unparent>())
                    if (u != null && u.parent == root.transform) UnityEngine.Object.Destroy(u.gameObject);
                if (root != null) UnityEngine.Object.Destroy(root);
                if (ground != null) UnityEngine.Object.Destroy(ground);
            }
        }
    }
}
