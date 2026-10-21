using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace VampireRounds
{
    /// <summary>
    /// Each guy's ROUNDS cards for Vampire Survivors's item area (sheets/systems.json "card_logos"): VS's item slots give
    /// way to ROUNDS's own card bar. Each seat's deck (its ROUNDS player's cards, in the order taken) goes to
    /// %LOCALAPPDATA%\VampireRounds\deck&lt;seat&gt;.txt, and each card's square of ROUNDS's card bar (its two letters on the
    /// seat's colour, as ROUNDS draws it) is pictured into cards\chip&lt;seat&gt;-&lt;key&gt;.rgba; the seat's Q_DeckSerial goes up
    /// whenever the deck or one of its squares changes, and VS shows them. A developer run with --vampirerounds-cards
    /// pictures every card (whole, and the square around its art) and lists what each is drawn with (cards\audit.txt).
    /// </summary>
    public static class CardIcons
    {
        public const string SheetFlag = "--vampirerounds-cards";
        /// <summary>Logo size in pixels (VS draws it in its slot, about 40 px on a 1080p screen).</summary>
        public const int Size = 96;
        /// <summary>A layer only the logo camera draws (portraits use 30, the face parts 31).</summary>
        private const int Layer = 29;
        /// <summary>The logo square, as a share of the card face's width, around the art's centre.</summary>
        private const float ArtShare = 0.8f;
        public static string Dir => Path.Combine(Paths.DataDir(), "cards");
        public static string DeckFile(int seat) => Path.Combine(Paths.DataDir(), "deck" + seat + ".txt");
        public static bool SheetMode => Environment.GetCommandLineArgs().Any(a => a == SheetFlag);
        private static readonly bool Dev = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV"));

        private static readonly HashSet<string> done = new HashSet<string>();
        private static readonly Dictionary<string, float> firstSeen = new Dictionary<string, float>();
        private static readonly string[] deckSig = new string[ArenaLink.Seats];
        private static readonly int[] deckSerial = new int[ArenaLink.Seats];
        /// <summary>Serials start somewhere new each engine start, so VS never takes a restarted engine's deck for one it read.</summary>
        private static readonly int serialBase = (Environment.TickCount & 0x3FFF) * 10000;
        private static float nextCheck;
        private static Camera cam;

        /// <summary>A file name for a card: its ROUNDS id with anything odd replaced, and a hash of the id (two mods may
        /// name cards alike).</summary>
        public static string Key(CardInfo c)
        {
            string id = Cards.Id(c);
            var sb = new StringBuilder();
            foreach (char ch in id) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            if (sb.Length > 40) sb.Length = 40;
            uint h = 2166136261;
            foreach (char ch in id) { h ^= ch; h *= 16777619; }
            return sb + "-" + h.ToString("x8");
        }

        // ------------------------------------------------------------------ decks (each arena frame)

        /// <summary>Each seat's cards: a changed deck is written for VS, and each card's square of ROUNDS's card bar (its two
        /// letters on the seat's colour) is pictured for VS's item slots if it is not yet.</summary>
        public static void Tick(Player[] players, ArenaLink link, MonoBehaviour host)
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            for (int s = 0; s < players.Length && s < ArenaLink.Seats; s++)
            {
                var p = players[s];
                var cards = p != null && p.data != null && p.data.currentCards != null ? p.data.currentCards.Where(c => c != null).ToList() : new List<CardInfo>();
                string sig = string.Join("|", cards.Select(Cards.Id).ToArray());
                bool drew = false;
                foreach (var c in cards)
                {
                    string chip = s + ":" + Key(c);
                    if (done.Contains(chip)) continue;
                    // A moment after the card came, so its square is in the bar (with the letters ROUNDS gave it).
                    float seen;
                    if (!firstSeen.TryGetValue(chip, out seen)) { firstSeen[chip] = Time.unscaledTime; continue; }
                    if (Time.unscaledTime - seen < 0.2f) continue;
                    done.Add(chip);
                    try { drew |= DrawChip(p, s, c); }
                    catch (Exception e) { EnginePlugin.L.LogWarning("chips: " + c.cardName + ": " + e.Message); }
                }
                if (sig != deckSig[s] || drew)
                {
                    deckSig[s] = sig;
                    WriteDeck(s, cards, link);
                }
            }
        }

        /// <summary>A new run (or a ROUNDS started again): every deck is written afresh.</summary>
        public static void Reset()
        {
            for (int s = 0; s < deckSig.Length; s++) deckSig[s] = null;
        }

        /// <summary>"key TAB name TAB rarity TAB colour" per card, in the order taken; written whole, then the serial.</summary>
        private static void WriteDeck(int seat, List<CardInfo> cards, ArenaLink link)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var c in cards)
                {
                    Color col = c.cardColor;
                    try { if (CardChoice.instance != null) col = CardChoice.instance.GetCardColor(c.colorTheme); } catch { }
                    sb.Append(Key(c)).Append('\t').Append((c.cardName ?? "").Replace('\t', ' ').Replace('\n', ' '))
                      .Append('\t').Append(c.rarity.ToString()).Append('\t').Append(ColorUtility.ToHtmlStringRGB(col)).Append('\n');
                }
                string f = DeckFile(seat);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f + ".tmp", sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(f)) File.Delete(f);
                File.Move(f + ".tmp", f);
                link.I(ArenaLink.RSlot(seat) + ArenaLink.Q_DeckSerial, serialBase + ++deckSerial[seat]);
            }
            catch (Exception e) { EnginePlugin.L.LogWarning("logos: deck " + seat + ": " + e.Message); }
        }

        /// <summary>The cards' own animations start and their particles fill in, in real time (VS may have ROUNDS paused,
        /// its time scale at 0); then each art's animator is run on past its opening (Critical Hit's starburst pops in a
        /// second after the card appears: at half a second the logo showed only the card's background).</summary>
        private static IEnumerator Settle(Picture[] pics)
        {
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until)
            {
                foreach (var p in pics) p?.Relayer();
                yield return null;
            }
            foreach (var p in pics) { p?.Rest(); p?.Advance(1.5f); }
            yield return null;
            foreach (var p in pics) { p?.Rest(); p?.Relayer(); }
        }

        // ------------------------------------------------------------------ a card's square of ROUNDS's card bar

        /// <summary>Chip pictures are this many pixels square.</summary>
        public const int ChipSize = 72;
        private static readonly AccessTools.FieldRef<CardBarButton, CardInfo> ButtonCard = AccessTools.FieldRefAccess<CardBarButton, CardInfo>("card");
        private static GameObject chipCanvas;
        private static int drawnChips;
        public static string ChipFile(int seat, string key) => Path.Combine(Dir, "chip" + seat + "-" + key + ".rgba");

        /// <summary>The card bar ROUNDS keeps for a player (CardBarHandler gives each player id its own).</summary>
        private static CardBar BarFor(Player p)
        {
            if (p == null || CardBarHandler.instance == null) return null;
            var bars = Traverse.Create(CardBarHandler.instance).Field("cardBars").GetValue<CardBar[]>();
            return bars != null && p.playerID >= 0 && p.playerID < bars.Length ? bars[p.playerID] : null;
        }

        private static Canvas ChipCanvas()
        {
            if (chipCanvas != null) return chipCanvas.GetComponent<Canvas>();
            chipCanvas = new GameObject("Vampire Rounds chip canvas", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(chipCanvas);
            chipCanvas.layer = Layer;
            var cv = chipCanvas.AddComponent<Canvas>();
            cv.renderMode = RenderMode.WorldSpace;
            chipCanvas.transform.position = new Vector3(40000f, 40000f, 0f);
            chipCanvas.transform.localScale = Vector3.one * 0.01f;
            ((RectTransform)chipCanvas.transform).sizeDelta = new Vector2(1000f, 1000f);
            return cv;
        }

        /// <summary>
        /// The card's square of ROUNDS's own card bar (sheets/systems.json "card_logos"): ROUNDS's square for this player's
        /// bar (its colours, its frame, ROUNDS's font) with the letters ROUNDS gave the card there (its first two, "Co"). It
        /// is pictured alone into cards\chip(seat)-(key).rgba, as 0.2.0 showed the bar over VS's stages.
        /// </summary>
        private static bool DrawChip(Player p, int seat, CardInfo card)
        {
            var bar = BarFor(p);
            if (bar == null) return false;
            GameObject button = null, template = null;
            for (int i = 0; i < bar.transform.childCount; i++)
            {
                var c = bar.transform.GetChild(i);
                var b = c.GetComponent<CardBarButton>();
                if (b == null) continue;
                if (!c.gameObject.activeSelf) { if (template == null) template = c.gameObject; continue; }
                var bc = ButtonCard(b);
                if (bc != null && (bc == card || Cards.Id(bc) == Cards.Id(card))) button = c.gameObject;
            }
            // ROUNDS's template square (the bar's first, hidden child) as it is drawn once it has come in: a new square
            // fades in after a pick, and copied then it was all but empty. Its letters are the ones ROUNDS gave the card.
            var from = template ?? button;
            if (from == null) return false;
            string letters = null;
            if (button != null)
            {
                var bt = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (bt != null && !string.IsNullOrEmpty(bt.text)) letters = bt.text;
            }
            if (string.IsNullOrEmpty(letters)) letters = CardLook.Letters(card.cardName);
            var canvas = ChipCanvas();
            var clone = UnityEngine.Object.Instantiate(from, canvas.transform, false);
            try
            {
                clone.name = "Vampire Rounds chip " + card.cardName;
                clone.SetActive(true);
                foreach (var b in clone.GetComponentsInChildren<CardBarButton>(true)) b.enabled = false;
                foreach (var c in clone.GetComponentsInChildren<CurveAnimation>(true)) c.enabled = false;
                foreach (var c in clone.GetComponentsInChildren<ScaleShake>(true)) c.enabled = false;
                foreach (var a in clone.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                foreach (var g in clone.GetComponentsInChildren<CanvasGroup>(true)) g.alpha = 1f;
                var rt = (RectTransform)clone.transform;
                var srt = (RectTransform)from.transform;
                var size = srt.rect.size;
                if (size.x < 1f || size.y < 1f) size = srt.sizeDelta;
                if (size.x < 1f || size.y < 1f) size = new Vector2(100f, 100f);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one;
                rt.sizeDelta = size;
                var text = clone.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (text != null) text.text = letters;
                foreach (var t in clone.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                Vector3 center = (corners[0] + corners[2]) / 2f;
                float side = Mathf.Max(Vector3.Distance(corners[0], corners[3]), Vector3.Distance(corners[0], corners[1])) * 1.06f;
                // As 0.2.0 showed ROUNDS's bar over VS (ArenaRenderer.UiFrame): pictured over black and over white, the
                // coverage is what the white lost, raised with tuning uiOpacityBoost; ROUNDS draws the square's fill as a
                // faint wash for its own dark screen, and a single picture of it was all but see-through.
                var black = Shoot(ChipSize, ChipSize, center, Quaternion.identity, side / 2f, new Color(0f, 0f, 0f, 0f), false);
                var white = Shoot(ChipSize, ChipSize, center, Quaternion.identity, side / 2f, Color.white, false);
                var px = new Color32[black.Length];
                for (int k = 0; k < px.Length; k++) px[k] = ArenaRenderer.UiPixel(black[k], white[k]);
                WriteRgba(ChipFile(seat, Key(card)), ChipSize, ChipSize, px);
                EnginePlugin.L.LogInfo("chips: " + (seat == 0 ? "Orange" : "Blue") + "'s " + card.cardName + " (" + letters + (button != null ? "" : ", no square in the bar") + ")");
                if (Dev && drawnChips++ < 2)
                {
                    var sb = new StringBuilder("chips: dev square of " + card.cardName + ":");
                    foreach (var t in from.GetComponentsInChildren<Transform>(true))
                    {
                        sb.Append(" [" + t.name + (t.gameObject.activeSelf ? "" : " off"));
                        foreach (var c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) sb.Append(" " + c.GetType().Name);
                        var g = t.GetComponent<Graphic>();
                        if (g != null) sb.Append(" colour " + ColorUtility.ToHtmlStringRGBA(g.color) + " mat " + (g.material != null ? g.material.name : "-"));
                        sb.Append("]");
                    }
                    for (var t = bar.transform; t != null; t = t.parent)
                    {
                        var g = t.GetComponent<Graphic>();
                        var cg = t.GetComponent<CanvasGroup>();
                        sb.Append(" <" + t.name + (g != null ? " colour " + ColorUtility.ToHtmlStringRGBA(g.color) : "") + (cg != null ? " group " + cg.alpha : "") + ">");
                    }
                    EnginePlugin.L.LogInfo(sb.ToString());
                }
                return true;
            }
            finally { UnityEngine.Object.Destroy(clone); }
        }

        // ------------------------------------------------------------------ one card, pictured

        /// <summary>A real ROUNDS card made for a picture: far from everything, on the logo layer, its colliders off.</summary>
        private sealed class Picture
        {
            public CardInfo card;
            public GameObject go;

            public static Picture Make(CardInfo card, int i)
            {
                try
                {
                    var go = CardChoice.instance.AddCardVisual(card, new Vector3(30000f + i * 60f, 30000f, 0f));
                    go.name = "Vampire Rounds logo " + card.cardName;
                    foreach (var col in go.GetComponentsInChildren<Collider2D>(true)) col.enabled = false;
                    var ray = go.GetComponentInChildren<GraphicRaycaster>(true);
                    if (ray != null) ray.enabled = false;
                    var zero = go.GetComponentInChildren<SetScaleToZero>(true);
                    if (zero != null) { zero.enabled = false; zero.transform.localScale = Vector3.one; }
                    var p = new Picture { card = card, go = go };
                    p.Relayer();
                    return p;
                }
                catch (Exception e) { EnginePlugin.L.LogWarning("logos: could not make " + card.cardName + ": " + e.Message); return null; }
            }

            public void Relayer()
            {
                if (go == null) return;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.gameObject.layer != Layer) t.gameObject.layer = Layer;
                CardLook.Apply(go);
                // The picture's animations run in real time, whether or not ROUNDS's own time stands still.
                foreach (var a in go.GetComponentsInChildren<Animator>(true)) if (a.updateMode != AnimatorUpdateMode.UnscaledTime) a.updateMode = AnimatorUpdateMode.UnscaledTime;
                foreach (var c in go.GetComponentsInChildren<CurveAnimation>(true)) c.useTimeScale = false;
                foreach (var c in go.GetComponentsInChildren<ScaleShake>(true)) c.useTimeScale = false;
                foreach (var c in go.GetComponentsInChildren<GeneralParticleSystem>(true)) c.useTimeScale = false;
            }

            /// <summary>The card's face shown, whatever its fly-in has got to: ROUNDS fades a card's face in and turns it over
            /// as it appears, in its own time, and a card dealt as a run starts was still turned over (its face's CanvasGroup
            /// at 0, its back showing) when its logo was taken. The logo is the face: its CanvasGroup full, the back away.</summary>
            public void Rest()
            {
                if (go == null) return;
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                if (vis == null) return;
                var front = vis.transform.Find("Canvas/Front");
                var back = vis.transform.Find("Canvas/Back");
                if (back != null && back.gameObject.activeSelf) back.gameObject.SetActive(false);
                if (front != null)
                {
                    if (!front.gameObject.activeSelf) front.gameObject.SetActive(true);
                    var g = front.GetComponent<CanvasGroup>();
                    if (g != null && g.alpha < 1f) g.alpha = 1f;
                }
            }

            /// <summary>Every animator of the card run on by this many seconds at once.</summary>
            public void Advance(float seconds)
            {
                if (go == null) return;
                foreach (var a in go.GetComponentsInChildren<Animator>(false))
                    try { if (a.isActiveAndEnabled && a.runtimeAnimatorController != null) a.Update(seconds); } catch { }
            }

            private RectTransform Front()
            {
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                Transform t = vis != null ? vis.transform.Find("Canvas/Front") : null;
                if (t == null) foreach (var c in go.GetComponentsInChildren<Transform>(true)) if (c.name == "Front") { t = c; break; }
                return t as RectTransform;
            }

            private Transform Art()
            {
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                return vis != null ? vis.transform.Find("Canvas/Front/Background/Art") : null;
            }

            /// <summary>The square around the art: as wide as most of the card's face, centred where ROUNDS puts the art.</summary>
            public bool ArtSquare(out Vector3 center, out float side, out Quaternion rot)
            {
                center = Vector3.zero; side = 0f; rot = Quaternion.identity;
                var front = Front();
                if (front == null) return false;
                var corners = new Vector3[4];
                front.GetWorldCorners(corners);
                float width = Vector3.Distance(corners[0], corners[3]);
                if (width < 0.0001f) return false;
                var art = Art();
                center = art != null ? art.position : (corners[1] + corners[2]) / 2f - (corners[1] - corners[0]).normalized * width * 0.45f;
                side = width * ArtShare;
                rot = Quaternion.LookRotation(front.forward, front.up);
                return true;
            }

            /// <summary>The whole card face with its title (developer sheet).</summary>
            public bool CardRect(out Vector3 center, out float w, out float h, out Quaternion rot)
            {
                center = Vector3.zero; w = h = 0f; rot = Quaternion.identity;
                var front = Front();
                if (front == null) return false;
                var c = new Vector3[4];
                front.GetWorldCorners(c);
                Vector3 right = (c[3] - c[0]).normalized, up = (c[1] - c[0]).normalized;
                float minY = 0f, maxY = Vector3.Distance(c[0], c[1]);
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                if (vis != null && vis.nameText != null)
                {
                    var tc = new Vector3[4];
                    vis.nameText.rectTransform.GetWorldCorners(tc);
                    foreach (var p in tc) { float d = Vector3.Dot(p - c[0], up); minY = Mathf.Min(minY, d); maxY = Mathf.Max(maxY, d); }
                }
                w = Vector3.Distance(c[0], c[3]);
                h = maxY - minY;
                center = c[0] + right * w / 2f + up * (minY + maxY) / 2f;
                rot = Quaternion.LookRotation(front.forward, front.up);
                return w > 0.0001f && h > 0.0001f;
            }

            public bool SaveLogo(string file)
            {
                if (!ArtSquare(out var center, out var side, out var rot)) { EnginePlugin.L.LogWarning("logos: " + card.cardName + " has no card face"); return false; }
                Rest();
                Relayer();
                if (Dev) Diagnose();
                // The world-space canvas takes the pose it has now, not the one it had when Unity last rebuilt it this frame.
                Canvas.ForceUpdateCanvases();
                if (!ArtSquare(out center, out side, out rot)) return false;
                var px = Shoot(Size, Size, center, rot, side / 2f);
                WriteRgba(file, Size, Size, px);
                return true;
            }

            public void SaveCardPng(string file)
            {
                Relayer();
                Canvas.ForceUpdateCanvases();
                if (!CardRect(out var center, out var w, out var h, out var rot)) return;
                int ph = 420, pw = Mathf.Clamp(Mathf.RoundToInt(ph * w / h), 120, 800);
                var px = Shoot(pw, ph, center, rot, h / 2f * 1.04f);
                WritePng(file, pw, ph, px);
            }

            public void Destroy()
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                go = null;
            }

            private string PathOf(Transform t)
            {
                string path = t.name;
                for (var p = t.parent; p != null && p != go.transform; p = p.parent) path = p.name + "/" + path;
                return path;
            }

            /// <summary>Developer runs: how the card stands when its logo is taken (faces, turns, sizes).</summary>
            private void Diagnose()
            {
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                Transform front = vis != null ? vis.transform.Find("Canvas/Front") : null, back = vis != null ? vis.transform.Find("Canvas/Back") : null;
                var sb = new StringBuilder("logos: dev " + card.cardName + " at " + go.transform.position + " root rot " + go.transform.eulerAngles);
                sb.Append(" vis " + (vis != null ? vis.name + " rot " + vis.transform.eulerAngles + " scale " + vis.transform.lossyScale : "none"));
                sb.Append(" front " + (front != null ? front.gameObject.activeInHierarchy + " rot " + front.eulerAngles + " fwd " + front.forward + " scale " + front.lossyScale : "none"));
                sb.Append(" back " + (back != null ? back.gameObject.activeInHierarchy + " rot " + back.eulerAngles : "none"));
                var fg = front != null ? front.GetComponent<CanvasGroup>() : null;
                if (fg != null) sb.Append(" front alpha " + fg.alpha);
                if (front != null)
                {
                    sb.Append(" | chain:");
                    for (var t = front; t != null && t != go.transform.parent; t = t.parent)
                    {
                        sb.Append(" " + t.name + " s" + t.localScale.ToString("F4") + " r" + t.localEulerAngles.ToString("F1") + (t.gameObject.activeSelf ? "" : " off") + " [");
                        foreach (var c in t.GetComponents<Component>()) if (c != null && !(c is Transform)) sb.Append(c.GetType().Name + " ");
                        foreach (var c in t.GetComponents<CurveAnimation>()) sb.Append("(curve start s" + c.startScale.ToString("F4") + " r" + c.startRotation.ToString("F1") + " state " + c.currentState + ") ");
                        foreach (var c in t.GetComponents<ScaleShake>()) sb.Append("(shake target " + c.targetScale + ") ");
                        sb.Append("]");
                    }
                    var srcFront = card.transform.Find(PathOf(front));
                    sb.Append(" | prefab front " + (srcFront != null ? "s" + srcFront.localScale + " r" + srcFront.localEulerAngles : "none"));
                }
                int cards = 0;
                foreach (var ci in UnityEngine.Object.FindObjectsOfType<CardInfo>()) if (Vector3.Distance(ci.transform.position, go.transform.position) < 40f) cards++;
                sb.Append(" cards near " + cards + " timeScale " + Time.timeScale);
                EnginePlugin.L.LogInfo(sb.ToString());
            }
        }

        // ------------------------------------------------------------------ camera and files

        private static Color32[] Shoot(int pw, int ph, Vector3 center, Quaternion rot, float halfHeight) => Shoot(pw, ph, center, rot, halfHeight, new Color(0f, 0f, 0f, 0f), true);

        private static Color32[] Shoot(int pw, int ph, Vector3 center, Quaternion rot, float halfHeight, Color clear, bool straighten)
        {
            if (cam == null)
            {
                var cgo = new GameObject("Vampire Rounds logo camera");
                UnityEngine.Object.DontDestroyOnLoad(cgo);
                cam = cgo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << Layer;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;
            }
            var rt = RenderTexture.GetTemporary(pw, ph, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            try
            {
                cam.targetTexture = rt;
                cam.backgroundColor = clear;
                cam.transform.rotation = rot;
                cam.transform.position = center - (rot * Vector3.forward) * 100f;
                cam.orthographicSize = halfHeight;
                cam.aspect = pw / (float)ph;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = prev;
                var px = tex.GetPixels32();
                if (!straighten) return px;
                for (int k = 0; k < px.Length; k++)
                {
                    var q = px[k];
                    if (q.a == 0 || q.a == 255) continue;
                    px[k] = new Color32((byte)Math.Min(255, q.r * 255 / q.a), (byte)Math.Min(255, q.g * 255 / q.a), (byte)Math.Min(255, q.b * 255 / q.a), q.a);
                }
                return px;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>RawImage format ("VRI1", width, height, RGBA rows bottom-up), written whole; a PNG beside it for people.</summary>
        private static void WriteRgba(string file, int w, int h, Color32[] px)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                var raw = tex.GetRawTextureData();
                using (var fs = new FileStream(file + ".tmp", FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(new[] { (byte)'V', (byte)'R', (byte)'I', (byte)'1' });
                    bw.Write(w);
                    bw.Write(h);
                    bw.Write(raw);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".tmp", file);
                File.WriteAllBytes(Path.ChangeExtension(file, ".png"), tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }

        private static void WritePng(string file, int w, int h, Color32[] px)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                File.WriteAllBytes(file, tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }

        // ------------------------------------------------------------------ developer sheet (--vampirerounds-cards)

        /// <summary>Every card ROUNDS has (mod cards included): its whole face and its logo as PNGs in cards\sheet, and what
        /// it is drawn with (renderers, layers, materials, shaders) in cards\audit.txt. Then the engine ends.</summary>
        public static IEnumerator DrawSheet()
        {
            string dir = Path.Combine(Dir, "sheet");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            // No arena here (it applies the engine's patches): the card look's own patches, so the sheet shows cards as a run does.
            try { new HarmonyLib.Harmony("vampirerounds.cards").CreateClassProcessor(typeof(CardLookPatches), true).Patch(); }
            catch (Exception e) { EnginePlugin.L.LogWarning("cards sheet: card look patches: " + e.Message); }
            Directory.CreateDirectory(dir);
            var all = Cards.All();
            // Developers: VAMPIREROUNDS_DEV_SHEET_PAUSED draws the sheet with ROUNDS's time standing still, as during VS's pauses.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_SHEET_PAUSED"))) { Time.timeScale = 0f; TimeHandler.deltaTime = 0f; }
            var main = MainCam.instance != null ? MainCam.instance.cam : Camera.main;
            int mainMask = main != null ? main.cullingMask : ~0;
            var audit = new StringBuilder();
            audit.Append("cards ").Append(all.Count).Append(", ROUNDS main camera mask ").Append(Convert.ToString(mainMask, 2)).Append('\n');
            foreach (var c in Camera.allCameras) audit.Append("camera ").Append(c.name).Append(" mask ").Append(Convert.ToString(c.cullingMask, 2)).Append(" depth ").Append(c.depth).Append('\n');
            for (int start = 0; start < all.Count; start += 16)
            {
                var batch = all.Skip(start).Take(16).ToList();
                var pics = new Picture[batch.Count];
                for (int i = 0; i < batch.Count; i++) pics[i] = Picture.Make(batch[i], i);
                yield return Settle(pics);
                for (int i = 0; i < batch.Count; i++)
                {
                    var card = batch[i];
                    string key = Key(card);
                    try
                    {
                        audit.Append("\n== ").Append(card.cardName).Append(" [").Append(Cards.Id(card)).Append("] mod ").Append(card.GetType().Assembly.GetName().Name)
                             .Append(" rarity ").Append(card.rarity).Append(" theme ").Append(card.colorTheme).Append(" art ").Append(card.cardArt != null ? card.cardArt.name : "NONE").Append('\n');
                        if (card.cardArt != null) { audit.Append(" art prefab:\n"); Layers(card.cardArt.transform, card.cardArt.transform, audit, mainMask); }
                        if (pics[i] == null) { audit.Append("  (could not be made)\n"); continue; }
                        audit.Append(" as drawn:\n");
                        Describe(pics[i].go.transform, pics[i].go.transform, audit, mainMask);
                        pics[i].SaveCardPng(Path.Combine(dir, key + ".png"));
                        pics[i].SaveLogo(Path.Combine(dir, key + "-logo.rgba"));
                    }
                    catch (Exception e) { audit.Append("  failed: ").Append(e.Message).Append('\n'); }
                    finally { pics[i]?.Destroy(); }
                }
                EnginePlugin.L.LogInfo("cards sheet: " + Math.Min(all.Count, start + 16) + " of " + all.Count);
                EnginePlugin.FlushLog();
            }
            File.WriteAllText(Path.Combine(Dir, "audit.txt"), audit.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "done.txt"), DateTime.Now.ToString("o"));
            EnginePlugin.Exit("card sheet drawn");
        }

        /// <summary>The art prefab's own layers (the drawn copy is on the logo layer): one ROUNDS's main camera does not
        /// draw would be missing from the pick screen.</summary>
        private static void Layers(Transform t, Transform root, StringBuilder sb, int mainMask)
        {
            string path = t == root ? "." : t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            int l = t.gameObject.layer;
            var kinds = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray());
            sb.Append("  L ").Append(path).Append(" layer ").Append(l).Append((mainMask & (1 << l)) != 0 ? "" : " NOT-IN-MAIN-CAMERA").Append(t.gameObject.activeSelf ? "" : " (inactive)").Append(" [").Append(kinds).Append("]\n");
            foreach (Transform c in t) Layers(c, root, sb, mainMask);
        }

        private static void Describe(Transform t, Transform root, StringBuilder sb, int mainMask)
        {
            string path = t == root ? "." : t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            foreach (var r in t.GetComponents<Renderer>())
            {
                var m = r.sharedMaterial;
                sb.Append("  R ").Append(path).Append(' ').Append(r.GetType().Name).Append(r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)")
                  .Append(" layer ").Append(t.gameObject.layer).Append(" mat ").Append(m != null ? m.name : "-").Append(" shader ").Append(m != null && m.shader != null ? m.shader.name : "-")
                  .Append(" sorting ").Append(r.sortingLayerName).Append('/').Append(r.sortingOrder).Append('\n');
            }
            foreach (var g in t.GetComponents<Graphic>())
            {
                var m = g.material;
                sb.Append("  G ").Append(path).Append(' ').Append(g.GetType().Name).Append(g.enabled && g.gameObject.activeInHierarchy ? "" : " (off)")
                  .Append(" color ").Append(ColorUtility.ToHtmlStringRGBA(g.color)).Append(" mat ").Append(m != null ? m.name : "-")
                  .Append(" shader ").Append(m != null && m.shader != null ? m.shader.name : "-");
                var img = g as Image;
                if (img != null) sb.Append(" sprite ").Append(img.sprite != null ? img.sprite.name : "-");
                sb.Append('\n');
            }
            foreach (var cv in t.GetComponents<Canvas>()) sb.Append("  C ").Append(path).Append(" canvas ").Append(cv.renderMode).Append(" sorting ").Append(cv.sortingLayerName).Append('/').Append(cv.sortingOrder).Append(cv.overrideSorting ? " override" : "").Append('\n');
            foreach (var ps in t.GetComponents<ParticleSystem>()) sb.Append("  P ").Append(path).Append(" particles\n");
            foreach (Transform c in t) Describe(c, root, sb, mainMask);
        }
    }

    /// <summary>
    /// A ROUNDS card as it should look wherever the engine draws it (sheets/hooks.json r_card_look). Parts drawn with ROUNDS's
    /// soft-shadow material (shader Sprites/SFSoftShadow, lit by ROUNDS's own light pass that the engine's cameras do not run)
    /// would come out dark, so they get Unity's plain sprite material, as the faces do (FaceLook). A card that comes with no
    /// art at all (41 of PCE's and other mods' cards) gets an emblem in its art space: the two letters ROUNDS's own card bar
    /// shows for it, big, in the card's colour, so it never shows an empty picture (in the pick, in VS's item slots).
    /// </summary>
    public static class CardLook
    {
        public const string EmblemName = "Vampire Rounds emblem";
        /// <summary>A card's canvas order in the hidden engine (ROUNDS's is 10 on MostFront): above the pick's face parts.</summary>
        public const int CardOrder = 500;
        private static Material unlit;

        /// <summary>ROUNDS's card bar letters for a card (CardBar.AddCard): its name's first two, "Co" for Comb.</summary>
        public static string Letters(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "?";
            string a = name.Substring(0, 1).ToUpperInvariant();
            return name.Length > 1 ? a + name.Substring(1, 1).ToLowerInvariant() : a;
        }

        /// <summary>The emblem of a card with no art, in its art space (called once its CardVisuals has started).</summary>
        public static void Emblem(CardVisuals vis, CardInfo info)
        {
            if (vis == null || info == null || info.cardArt != null) return;
            var art = vis.transform.Find("Canvas/Front/Background/Art");
            var bg = vis.transform.Find("Canvas/Front/Background") as RectTransform;
            if (art == null || bg == null || art.Find(EmblemName) != null) return;
            var go = new GameObject(EmblemName, typeof(RectTransform));
            go.layer = art.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(art, false);
            rt.localPosition = Vector3.zero;
            rt.localScale = Vector3.one;
            float w = bg.rect.width;
            rt.sizeDelta = new Vector2(w * 0.8f, w * 0.6f);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (vis.nameText != null) { t.font = vis.nameText.font; t.fontSharedMaterial = vis.nameText.fontSharedMaterial; }
            t.text = Letters(info.cardName);
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.overflowMode = TMPro.TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            // About four times the card's title, filling most of the art space as a card's picture does.
            t.fontSize = (vis.nameText != null && vis.nameText.fontSize > 0f ? vis.nameText.fontSize : w * 0.08f) * 4f;
            t.raycastTarget = false;
            Color c = info.cardColor;
            try { if (CardChoice.instance != null) c = CardChoice.instance.GetCardColor(info.colorTheme); } catch { }
            t.color = c;
            go.transform.SetAsLastSibling();
        }

        /// <summary>As ROUNDS dims a card that is not the chosen one, the emblem dims with it.</summary>
        public static void EmblemSelected(CardVisuals vis, bool selected)
        {
            var art = vis != null ? vis.transform.Find("Canvas/Front/Background/Art") : null;
            var e = art != null ? art.Find(EmblemName) : null;
            var t = e != null ? e.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (t == null) return;
            var c = t.color;
            c.a = selected ? 1f : 0.3f;
            t.color = c;
        }

        private static Material Unlit
        {
            get
            {
                if (unlit == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) unlit = new Material(shader) { name = "Vampire Rounds card (unlit)" };
                }
                return unlit;
            }
        }

        public static void Apply(GameObject card)
        {
            if (card == null || Unlit == null) return;
            foreach (var r in card.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m != null && m != unlit && m.shader != null && m.shader.name.Contains("SFSoftShadow")) r.sharedMaterial = unlit;
            }
        }
    }
}
