using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2CppVampireSurvivors.UI;
using UnityEngine;
using UnityEngine.UI;

namespace VampireRounds.Vs
{
    /// <summary>
    /// ROUNDS's card bar where VS shows a hero's weapons and items (sheets/systems.json "card_logos"): in a Vampire Rounds run
    /// each guy's HUD panel (VS's GameEquipmentPanel) shows his ROUNDS cards as ROUNDS's card bar does, a square with the
    /// card's two letters on the guy's colour per card, in the order he took them, ten to a row and as many rows as he has
    /// cards. VS's weapon and item slots stay where they are, unseen: VS still collects what its pickups give and uses it.
    /// The deck and the squares come from the hidden ROUNDS (the engine's CardIcons: deck&lt;seat&gt;.txt and
    /// cards\chip&lt;seat&gt;-&lt;key&gt;.rgba, re-read when the seat's Q_DeckSerial moves).
    /// </summary>
    public static class CardSlots
    {
        /// <summary>Squares to a row (ROUNDS's bar is one long row; ten of VS's slot size fill the top left up to VS's
        /// clock), and the gap between them.</summary>
        private const int PerRow = 10;
        private const float Gap = 3f;
        /// <summary>Each seat's colour (sheets/characters.json color): a square waiting for its picture shows it.</summary>
        private static readonly Color[] SeatColor = { new Color(0.957f, 0.478f, 0.129f, 0.6f), new Color(0.180f, 0.490f, 0.878f, 0.6f) };

        private sealed class Card { public string key, name; public Color color; }
        private sealed class Cell { public GameObject go; public Image icon; public string key; public bool logo; }

        private sealed class Seat
        {
            public int seat;
            public int serial = int.MinValue;
            public bool refresh;
            public readonly List<Card> cards = new List<Card>();
            public IntPtr panel;
            public RectTransform grid;
            public GridLayoutGroup layout;
            public readonly List<Cell> cells = new List<Cell>();
            public Vector2 cell = new Vector2(40f, 40f), spacing = new Vector2(2f, 2f);
            public RectOffset padding;
            public GameObject prefab;
        }

        private static readonly Seat[] seats = { new Seat { seat = 0 }, new Seat { seat = 1 } };
        private static readonly Dictionary<string, Sprite> logos = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, DateTime> logoStamp = new Dictionary<string, DateTime>();
        private static readonly HashSet<IntPtr> dumped = new HashSet<IntPtr>();
        private static Sprite blank;
        private static bool shownMarked;

        public static int Shown { get; private set; }

        public static void Tick()
        {
            var L = Link.L;
            if (L == null || !Session.InRun) { Shown = 0; return; }
            int shown = 0;
            for (int s = 0; s < seats.Length; s++)
            {
                var c = Session.SeatChar[s];
                if (c == null) continue;
                var st = seats[s];
                int serial = L.I(ArenaLink.RSlot(s) + ArenaLink.Q_DeckSerial);
                if (serial != st.serial && Link.EngineAlive) { st.serial = serial; ReadDeck(s, st); }
                GameEquipmentPanel panel = null;
                try { panel = GameEquipmentPanel.GetPanelForCharacter(c); } catch { }
                if (panel == null) continue;
                if (st.panel != panel.Pointer || st.grid == null) Build(s, st, panel);
                if (st.grid == null) continue;
                HideVs(panel);
                Place(st, panel);
                shown += Fill(st);
            }
            Shown = shown;
            if (shown > 0 && !shownMarked) { shownMarked = true; TestMode.Mark("card-logo-shown"); }
        }

        // ------------------------------------------------------------------ the deck

        /// <summary>deck&lt;seat&gt;.txt: "key TAB name TAB rarity TAB colour" per card, in the order taken.</summary>
        private static void ReadDeck(int seat, Seat st)
        {
            st.cards.Clear();
            st.refresh = true;
            string f = Path.Combine(Paths.DataDir(), "deck" + seat + ".txt");
            try
            {
                if (!File.Exists(f)) return;
                foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    var part = line.Split('\t');
                    if (part.Length < 2 || part[0].Length == 0) continue;
                    var card = new Card { key = part[0], name = part[1], color = new Color(0.25f, 0.25f, 0.28f, 1f) };
                    if (part.Length > 3) { Color col; if (ColorUtility.TryParseHtmlString("#" + part[3], out col)) card.color = col; }
                    st.cards.Add(card);
                }
            }
            catch (Exception e) { Mod.Once("card slots: deck " + seat, e); }
        }

        /// <summary>A card's square of ROUNDS's card bar for a seat, loaded again when the engine has drawn a newer one.</summary>
        private static Sprite Logo(int seat, string cardKey, bool recheck)
        {
            string key = "chip" + seat + "-" + cardKey;
            Sprite have;
            logos.TryGetValue(key, out have);
            if (have != null && !recheck) return have;
            string f = Path.Combine(Paths.DataDir(), "cards", key + ".rgba");
            try
            {
                if (!File.Exists(f)) return have;
                var stamp = File.GetLastWriteTimeUtc(f);
                DateTime seen;
                if (have != null && logoStamp.TryGetValue(key, out seen) && seen == stamp) return have;
                var tex = RawImage.Load(f);
                if (tex == null) return have;
                tex.name = "Vampire Rounds logo " + key;
                var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = tex.name;
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                logos[key] = sprite;
                logoStamp[key] = stamp;
                return sprite;
            }
            catch (Exception e) { Mod.Once("card slots: logo " + key, e); return have; }
        }

        private static Sprite Blank()
        {
            if (blank != null) return blank;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            blank = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            blank.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return blank;
        }

        // ------------------------------------------------------------------ VS's panel

        /// <summary>Our slots beside VS's weapon row, sized and spaced as VS lays its own slots.</summary>
        private static void Build(int seatIndex, Seat st, GameEquipmentPanel panel)
        {
            st.panel = panel.Pointer;
            st.cells.Clear();
            st.grid = null;
            st.refresh = true;
            var weapons = panel._WeaponContainer;
            if (weapons == null) return;
            st.prefab = panel._WeaponPrefab;
            st.padding = null;
            // VS's slot as drawn (its first slot, else the slot prefab), and the spacing of its row.
            var vsGrid = weapons.GetComponent<GridLayoutGroup>();
            if (vsGrid != null) { st.cell = vsGrid.cellSize; st.spacing = vsGrid.spacing; st.padding = vsGrid.padding; }
            var hor = weapons.GetComponent<HorizontalLayoutGroup>();
            if (hor != null) { st.spacing = new Vector2(hor.spacing, hor.spacing); st.padding = hor.padding; }
            var spawned = panel._spawnedWeaponSlots;
            RectTransform first = null;
            if (spawned != null && spawned.Count > 0 && spawned[0] != null) first = spawned[0].GetComponent<RectTransform>();
            if (first == null && st.prefab != null) first = st.prefab.GetComponent<RectTransform>();
            if (first != null)
            {
                // VS lays its slots out a moment after the panel appears (36 units before, its grid's 45 after): the larger.
                var size = first.rect.size;
                if (size.x < 1f || size.y < 1f) size = first.sizeDelta;
                if (size.x >= 1f && size.y >= 1f) st.cell = Vector2.Max(st.cell, size);
            }
            st.spacing = new Vector2(Gap, Gap);
            var go = new GameObject("Vampire Rounds cards " + seatIndex);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(weapons.parent, false);
            rt.localScale = weapons.localScale;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 1f);
            var ignore = go.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;
            var layout = go.AddComponent<GridLayoutGroup>();
            layout.cellSize = st.cell;
            layout.spacing = st.spacing;
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = PerRow;
            st.grid = rt;
            st.layout = layout;
            Mod.Log("card slots: " + (seatIndex == 0 ? "Orange" : "Blue") + "'s ROUNDS card bar in VS's item panel (squares " + st.cell.x.ToString("0") + "x" + st.cell.y.ToString("0")
                + ", gap " + st.spacing.x.ToString("0") + ", " + PerRow + " to a row)");
            if (DevConsole.On && dumped.Add(panel.Pointer)) Dump(panel.transform);
        }

        /// <summary>VS's own weapon and item slots: there, working, unseen.</summary>
        private static void HideVs(GameEquipmentPanel panel)
        {
            Hide(panel._WeaponContainer);
            Hide(panel._AccessoryContainer);
        }

        private static void Hide(RectTransform t)
        {
            if (t == null) return;
            var g = t.GetComponent<CanvasGroup>();
            if (g == null) g = t.gameObject.AddComponent<CanvasGroup>();
            if (g.alpha != 0f) g.alpha = 0f;
            if (g.blocksRaycasts) g.blocksRaycasts = false;
        }

        /// <summary>Where VS's first slot is (its weapon row's top-left corner, inside its padding).</summary>
        private static void Place(Seat st, GameEquipmentPanel panel)
        {
            var weapons = panel._WeaponContainer;
            if (weapons == null) return;
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            weapons.GetWorldCorners(corners);
            Vector3 topLeft = corners[1];
            if (st.padding != null)
            {
                Vector3 s = weapons.lossyScale;
                topLeft += weapons.right * st.padding.left * s.x - weapons.up * st.padding.top * s.y;
            }
            if ((st.grid.position - topLeft).sqrMagnitude > 1e-8f) st.grid.position = topLeft;
        }

        /// <summary>One slot per card; the rest of the slots made earlier are put away.</summary>
        private static int Fill(Seat st)
        {
            int n = st.cards.Count;
            for (int i = 0; i < n; i++)
            {
                if (i >= st.cells.Count) st.cells.Add(MakeCell(st, i));
                var cell = st.cells[i];
                if (cell == null || cell.go == null) { st.cells[i] = cell = MakeCell(st, i); }
                if (cell == null) continue;
                var card = st.cards[i];
                if (st.refresh || cell.key != card.key)
                {
                    var logo = Logo(st.seat, card.key, st.refresh);
                    cell.key = card.key;
                    cell.logo = logo != null;
                    cell.icon.sprite = logo != null ? logo : Blank();
                    cell.icon.color = logo != null ? Color.white : SeatColor[st.seat];
                    cell.icon.preserveAspect = true;
                    cell.icon.enabled = true;
                    cell.go.name = "card " + card.name;
                }
                if (!cell.go.activeSelf) cell.go.SetActive(true);
            }
            for (int i = n; i < st.cells.Count; i++)
                if (st.cells[i] != null && st.cells[i].go != null && st.cells[i].go.activeSelf) st.cells[i].go.SetActive(false);
            st.refresh = false;
            int rows = Mathf.Max(1, (n + PerRow - 1) / PerRow);
            var size = new Vector2(PerRow * st.cell.x + (PerRow - 1) * st.spacing.x, rows * st.cell.y + (rows - 1) * st.spacing.y);
            if (st.grid.sizeDelta != size) st.grid.sizeDelta = size;
            int withLogo = 0;
            for (int i = 0; i < n && i < st.cells.Count; i++) if (st.cells[i] != null && st.cells[i].logo) withLogo++;
            return withLogo;
        }

        /// <summary>One square: the picture of ROUNDS's square fills it (it is its own frame, as in ROUNDS's bar).</summary>
        private static Cell MakeCell(Seat st, int i)
        {
            try
            {
                var go = new GameObject("card square");
                go.AddComponent<RectTransform>().SetParent(st.grid, false);
                var icon = go.AddComponent<Image>();
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                return new Cell { go = go, icon = icon };
            }
            catch (Exception e) { Mod.Once("card slots: square", e); return null; }
        }

        /// <summary>Developer runs: VS's item panel as it is laid out (to place the logos where its slots are).</summary>
        private static void Dump(Transform t, int depth = 0)
        {
            if (depth > 6) return;
            var rt = t.GetComponent<RectTransform>();
            var sb = new StringBuilder("card slots: dump " + new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " (off)"));
            if (rt != null) sb.Append(" rect " + rt.rect.size + " pos " + rt.anchoredPosition + " anchors " + rt.anchorMin + "-" + rt.anchorMax + " pivot " + rt.pivot + " scale " + rt.localScale);
            foreach (var c in t.GetComponents<Component>()) if (c != null) sb.Append(" [" + c.GetIl2CppType().Name + "]");
            Mod.Log(sb.ToString());
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1);
        }
    }
}
