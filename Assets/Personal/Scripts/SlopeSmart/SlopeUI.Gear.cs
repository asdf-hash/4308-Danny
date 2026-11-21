using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SlopeSmart
{
    /// <summary>"Gear Up!" wardrobe screen: drag items from the wardrobe onto the skier.</summary>
    public partial class SlopeUI
    {
        public Action<int> OnGearDrop;
        public Action OnGearDone;

        class TileView
        {
            public RectTransform rt;
            public Image bg;
            public Text label;
            public GearTile drag;
            public float flash;
            public bool rejected, worn;
        }

        static readonly Color TileBase = new Color32(0xe8, 0xf1, 0xff, 255);
        static readonly Color TileBad = new Color32(0xff, 0xd6, 0xd9, 255);

        GameObject dressRoot;
        RectTransform dropZone, ghost;
        Image dropGlow;
        Text wornText, gearToastText, doneLabel;
        Image gearToastBg;
        float gearToastTimer;
        TileView[] tiles;
        readonly List<GameObject> chips = new List<GameObject>();
        bool dragging;
        RectTransform wardrobeCard, doneRt;

        /// <summary>Screen x (0..1) where the skier should stand: centre of the free area left of the wardrobe.</summary>
        public float DressSkierFrac { get; private set; } = 0.28f;

        // ───────────── icons ─────────────

        /// <summary>Builds an icon from its shapes. 'size' is the on-screen square size in canvas units.</summary>
        RectTransform BuildIcon(Transform parent, GearItem item, float size, Vector2 pos, Vector2 anchor)
        {
            var root = Anchored(parent, "Icon", anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(100, 100));
            root.localScale = Vector3.one * (size / 100f);
            if (item.icon != null)
            {
                var art = Stretch(root, "Art", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var im = Img(art, item.icon, Color.white);
                im.preserveAspect = true;
                return root;
            }
            foreach (var s in item.shapes)
            {
                var rt = Anchored(root, "S", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(s.x, s.y), new Vector2(s.w, s.h));
                rt.localRotation = Quaternion.Euler(0, 0, s.rot);
                Img(rt, s.circle ? SlopeSprites.Circle : SlopeSprites.Round, s.color, !s.circle);
            }
            return root;
        }

        // ───────────── construction ─────────────

        void BuildGear()
        {
            dressRoot = Stretch(safe, "GearUp", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var root = (RectTransform)dressRoot.transform;

            // area where a dropped item counts as "put on the skier"
            dropZone = Stretch(root, "DropZone", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 200), new Vector2(500, -260));
            dropGlow = Img(dropZone, SlopeSprites.Round, new Color(1, 1, 1, 0f), true);

            var title = Anchored(root, "Title", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -24), new Vector2(760, 110));
            Txt(title, "Gear Up!", 100, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true, Blue);
            Txt(root, "Drag the right gear onto me!", 40, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(-440, -190), new Vector2(440, -130), true, Color.white);

            var pill = Anchored(root, "WornPill", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -30), new Vector2(250, 80));
            Img(pill, SlopeSprites.Round, new Color(1, 1, 1, 0.85f), true);
            wornText = FillText(pill, "Worn: 0", 42, Ink, TextAnchor.MiddleCenter);

            var toast = Anchored(root, "GearToast", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -215), new Vector2(940, 120));
            gearToastBg = Img(toast, SlopeSprites.Round, Green, true);
            gearToastText = FillText(toast, "", 44, Color.white, TextAnchor.MiddleCenter);
            gearToastText.rectTransform.offsetMin = new Vector2(24, 8);
            gearToastText.rectTransform.offsetMax = new Vector2(-24, -8);
            toast.gameObject.SetActive(false);

            // wardrobe card
            var card = Anchored(root, "Wardrobe", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 190), new Vector2(530, 1200));
            wardrobeCard = card;
            Img(card, SlopeSprites.Round, new Color(1, 1, 1, 0.96f), true);
            Txt(card, "WARDROBE", 32, Blue, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -52), new Vector2(0, -8));

            int n = GearItems.All.Length;
            tiles = new TileView[n];
            for (int i = 0; i < n; i++)
            {
                var item = GearItems.All[i];
                var rt = Anchored(card, "Tile_" + item.label, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(156, 176));
                var bg = Img(rt, SlopeSprites.Round, TileBase, true, true);
                BuildIcon(rt, item, 112f, new Vector2(78, -76), new Vector2(0, 1));
                var label = Txt(rt, item.label, 28, Ink, TextAnchor.MiddleCenter, new Vector2(0, 0), new Vector2(1, 0), new Vector2(4, 4), new Vector2(-4, 44));
                var gt = rt.gameObject.AddComponent<GearTile>();
                gt.index = i;
                gt.Begin = BeginTileDrag;
                gt.Drag = DragTile;
                gt.End = EndTileDrag;
                tiles[i] = new TileView { rt = rt, bg = bg, label = label, drag = gt };
            }

            var done = Button(root, "Done", "DONE", Green, new Vector2(1, 0), new Vector2(1, 0),
                new Vector2(-20, 40), new Vector2(520, 124), () => OnGearDone?.Invoke());
            doneRt = (RectTransform)done.transform;
            doneLabel = done.GetComponentInChildren<Text>();
        }

        // ───────────── dragging ─────────────

        void BeginTileDrag(int idx, PointerEventData e)
        {
            dragging = true;
            var go = new GameObject("DragGhost", typeof(RectTransform));
            ghost = (RectTransform)go.transform;
            ghost.SetParent(safe, false);
            ghost.SetAsLastSibling();
            ghost.sizeDelta = new Vector2(100, 100);
            ghost.pivot = new Vector2(0.5f, 0.5f);
            var item = GearItems.All[idx];
            var icon = BuildIcon(ghost, item, 170f, Vector2.zero, new Vector2(0.5f, 0.5f));
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            MoveGhost(e);
            tiles[idx].bg.color = new Color(0.8f, 0.85f, 0.95f, 1f);
        }

        void DragTile(int idx, PointerEventData e) => MoveGhost(e);

        void MoveGhost(PointerEventData e)
        {
            if (ghost == null) return;
            // lift above the finger so it stays visible
            ghost.position = e.position + new Vector2(0, 90f * (canvas != null ? canvas.scaleFactor : 1f));
        }

        void EndTileDrag(int idx, PointerEventData e)
        {
            bool inside = false;
            if (ghost != null)
                inside = RectTransformUtility.RectangleContainsScreenPoint(dropZone, (Vector2)ghost.position, null);
            if (ghost != null) UnityEngine.Object.Destroy(ghost.gameObject);
            ghost = null;
            dragging = false;
            if (!tiles[idx].worn && !tiles[idx].rejected) tiles[idx].bg.color = TileBase;
            if (inside) OnGearDrop?.Invoke(idx);
        }

        // ───────────── public API (gear) ─────────────

        public void ShowDress(int[] order)
        {
            HideAll();
            dressRoot.SetActive(true);
            foreach (var c in chips) UnityEngine.Object.Destroy(c);
            chips.Clear();
            dragging = false;
            if (ghost != null) UnityEngine.Object.Destroy(ghost.gameObject);
            ghost = null;

            // Wardrobe sits on the right; tall screens use 3 columns, wide/short ones use 6.
            Canvas.ForceUpdateCanvases();
            float sf = canvas != null && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
            float cw = Screen.safeArea.width / sf, ch = Screen.safeArea.height / sf;
            int cols = ch >= 1500f ? 3 : 6;
            int rows = Mathf.CeilToInt(order.Length / (float)cols);
            float cardW = cols * 166 + 14, cardH = 62 + rows * 186 + 8;
            wardrobeCard.sizeDelta = new Vector2(cardW, cardH);
            doneRt.sizeDelta = new Vector2(Mathf.Min(cardW, 520f), 124);
            float freeW = Mathf.Max(260f, cw - cardW - 40f);
            dropZone.offsetMax = new Vector2(freeW, dropZone.offsetMax.y);
            DressSkierFrac = Mathf.Clamp(freeW * 0.6f / cw, 0.18f, 0.42f);

            for (int slot = 0; slot < order.Length; slot++)
            {
                var tv = tiles[order[slot]];
                int col = slot % cols, row = slot / cols;
                tv.rt.anchoredPosition = new Vector2(12 + col * 166, -(62 + row * 186));
                tv.rt.gameObject.SetActive(true);
                tv.bg.color = TileBase;
                tv.label.color = Ink;
                tv.drag.locked = false;
                tv.rejected = tv.worn = false;
                tv.flash = 0;
            }
            SetWornCount(0);
            SetDoneLabel("DONE");
            gearToastBg.gameObject.SetActive(false);
            gearToastTimer = 0;
        }

        public void MarkWorn(int idx)
        {
            var tv = tiles[idx];
            tv.worn = true;
            tv.drag.locked = true;
            tv.rt.gameObject.SetActive(false);

            // chip in a 2-column strip along the left edge, beside the skier
            int n = chips.Count;
            int col = n % 2, row = n / 2;
            var chip = Anchored(dressRoot.transform, "Chip", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(58 + col * 100, -330 - row * 104), new Vector2(92, 92));
            Img(chip, SlopeSprites.Circle, new Color(1, 1, 1, 0.92f));
            BuildIcon(chip, GearItems.All[idx], 64f, Vector2.zero, new Vector2(0.5f, 0.5f));
            chips.Add(chip.gameObject);
        }

        public void MarkRejected(int idx)
        {
            var tv = tiles[idx];
            tv.rejected = true;
            tv.drag.locked = true;
            tv.flash = 0.7f;
            tv.bg.color = TileBad;
            tv.label.color = new Color(0.65f, 0.15f, 0.2f, 1f);
        }

        public void SetWornCount(int n) { wornText.text = "Worn: " + n; }
        public void SetDoneLabel(string s) { doneLabel.text = s; }

        public void GearToast(string msg, bool good, float seconds = 2.6f)
        {
            gearToastText.text = msg;
            gearToastBg.color = good ? Green : Red;
            gearToastBg.gameObject.SetActive(true);
            gearToastTimer = seconds;
        }

        void TickGear(float dt)
        {
            if (!dressRoot.activeSelf) return;
            var g = dropGlow.color;
            g.a = Mathf.MoveTowards(g.a, dragging ? 0.28f : 0f, dt * 2f);
            dropGlow.color = g;

            if (gearToastTimer > 0)
            {
                gearToastTimer -= dt;
                if (gearToastTimer <= 0) gearToastBg.gameObject.SetActive(false);
            }
            foreach (var tv in tiles)
            {
                if (tv.flash > 0)
                {
                    tv.flash -= dt;
                    float s = Mathf.Sin(tv.flash * 50f) * 8f * Mathf.Clamp01(tv.flash / 0.7f);
                    tv.rt.localRotation = Quaternion.Euler(0, 0, s);
                    if (tv.flash <= 0) tv.rt.localRotation = Quaternion.identity;
                }
            }
        }
    }
}
