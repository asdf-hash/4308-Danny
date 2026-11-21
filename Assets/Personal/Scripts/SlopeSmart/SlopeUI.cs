using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SlopeSmart
{
    /// <summary>Builds and drives all Slope Smart UI from code (tips, HUD, results). Reference: 1080x1920 portrait.</summary>
    public partial class SlopeUI
    {
        public Action OnStart, OnToggleTilt;
        Action resLeft, resRight;

        static readonly Color Ink = new Color32(0x1d, 0x2b, 0x4f, 255);
        static readonly Color Blue = new Color32(0x2f, 0x8f, 0xf0, 255);
        static readonly Color Green = new Color32(0x2f, 0xb8, 0x4f, 255);
        static readonly Color Orange = new Color32(0xf0, 0x7a, 0x2a, 255);
        static readonly Color Gold = new Color32(0xff, 0xc2, 0x1a, 255);
        static readonly Color Red = new Color32(0xf0, 0x3c, 0x4b, 255);
        static readonly Color PaleBlue = new Color32(0xdc, 0xeb, 0xff, 255);

        readonly SteeringInput input;
        Font font;
        Canvas canvas;
        RectTransform safe;

        GameObject tipsRoot, hudRoot, resultsRoot;

        // HUD
        Image[] hearts;
        Text scoreText, speedText, toastText, bannerText;
        Image progressFill, toastBg, bannerBg, leftArrow, rightArrow, brakeImg;
        float toastTimer, toastLife;
        bool bannerOn;

        // tips
        Text tiltLabel, tipsTitle, tipsHint, resLeftLabel, resRightLabel;
        readonly Text[] tipTexts = new Text[3];
        GameObject tiltButton, startButton;

        // results
        Text resultTitle, resultScore, rememberText;
        Image[] resultStars;
        GameObject newBestTag;

        public SlopeUI(SteeringInput input) { this.input = input; }

        // ───────────────────────────── construction ─────────────────────────────

        public void Build()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
                var module = es.AddComponent<InputSystemUIInputModule>();
                module.AssignDefaultActions();
            }

            var cgo = new GameObject("SlopeSmart UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            safe = Stretch(cgo.transform, "SafeArea", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            ApplySafeArea();

            BuildHud();
            BuildTips();
            BuildResults();
            BuildGear();
            HideAll();
        }

        void ApplySafeArea()
        {
            Rect r = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            safe.anchorMin = new Vector2(r.xMin / Screen.width, r.yMin / Screen.height);
            safe.anchorMax = new Vector2(r.xMax / Screen.width, r.yMax / Screen.height);
        }

        // ───────────────────────────── helpers ─────────────────────────────

        static RectTransform Stretch(Transform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            return rt;
        }

        static RectTransform Anchored(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        static Image Img(RectTransform rt, Sprite s, Color c, bool sliced = false, bool raycast = false)
        {
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = s;
            im.color = c;
            im.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
            im.raycastTarget = raycast;
            return im;
        }

        Text Txt(Transform parent, string content, int size, Color color, TextAnchor align,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 oMin, Vector2 oMax, bool outline = false, Color? outlineColor = null)
        {
            var rt = Stretch(parent, "Text", anchorMin, anchorMax, oMin, oMax);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontStyle = FontStyle.Bold;
            t.text = content;
            t.color = color;
            t.alignment = align;
            t.fontSize = size;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = Mathf.Max(12, size / 3);
            t.resizeTextMaxSize = size;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = outlineColor ?? Blue;
                o.effectDistance = new Vector2(size / 14f, -size / 14f);
            }
            return t;
        }

        Text FillText(Transform parent, string content, int size, Color color, TextAnchor align, bool outline = false, Color? oc = null)
            => Txt(parent, content, size, color, align, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, outline, oc);

        GameObject Button(Transform parent, string name, string label, Color color, Vector2 anchor, Vector2 pivot,
            Vector2 pos, Vector2 size, Action click, Sprite icon = null)
        {
            var rt = Anchored(parent, name, anchor, pivot, pos, size);
            // darker "thick" lower lip
            var lip = Stretch(rt, "Lip", Vector2.zero, Vector2.one, new Vector2(0, -10), new Vector2(0, -10));
            Img(lip, SlopeSprites.Round, color * 0.68f + new Color(0, 0, 0, 0.32f), true);
            var face = Stretch(rt, "Face", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var img = Img(face, SlopeSprites.Round, color, true, true);
            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            var cb = btn.colors;
            cb.highlightedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
            cb.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            btn.colors = cb;
            btn.onClick.AddListener(() => click?.Invoke());
            var txt = Txt(face, label, 64, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one,
                new Vector2(icon != null ? 90 : 20, 10), new Vector2(-20, -10), true, color * 0.55f);
            if (icon != null)
            {
                var ir = Anchored(face, "Icon", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(70, 70));
                Img(ir, icon, Color.white);
            }
            return rt.gameObject;
        }

        static void Hold(GameObject go, Action down, Action up)
        {
            var trig = go.AddComponent<EventTrigger>();
            var d = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            d.callback.AddListener(_ => down());
            var u = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
            u.callback.AddListener(_ => up());
            trig.triggers.Add(d);
            trig.triggers.Add(u);
        }

        // ───────────────────────────── HUD ─────────────────────────────

        void BuildHud()
        {
            hudRoot = Stretch(safe, "HUD", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var root = (RectTransform)hudRoot.transform;

            // hearts
            hearts = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var rt = Anchored(root, "Heart" + i, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40 + i * 104, -36), new Vector2(92, 92));
                hearts[i] = Img(rt, SlopeSprites.Heart, Red);
                var o = rt.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0.45f, 0.05f, 0.1f, 0.9f);
                o.effectDistance = new Vector2(3, -3);
            }

            // score pill
            var pill = Anchored(root, "ScorePill", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -40), new Vector2(330, 88));
            Img(pill, SlopeSprites.Round, new Color(1, 1, 1, 0.85f), true);
            scoreText = FillText(pill, "0", 50, Ink, TextAnchor.MiddleCenter);

            // progress bar
            var bar = Stretch(root, "Progress", new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -172), new Vector2(-40, -140));
            Img(bar, SlopeSprites.Round, new Color(1, 1, 1, 0.55f), true);
            var fill = Stretch(bar, "Fill", Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
            progressFill = Img(fill, SlopeSprites.Round, Gold, true);
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillAmount = 0;

            // toast
            var toast = Anchored(root, "Toast", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -210), new Vector2(940, 130));
            toastBg = Img(toast, SlopeSprites.Round, new Color(Ink.r, Ink.g, Ink.b, 0.86f), true);
            toastText = FillText(toast, "", 48, Color.white, TextAnchor.MiddleCenter);
            toastText.rectTransform.offsetMin = new Vector2(24, 8);
            toastText.rectTransform.offsetMax = new Vector2(-24, -8);

            // slow zone banner
            var banner = Anchored(root, "ZoneBanner", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -360), new Vector2(760, 110));
            bannerBg = Img(banner, SlopeSprites.Round, Orange, true);
            bannerText = FillText(banner, "SLOW ZONE!", 54, Color.white, TextAnchor.MiddleCenter, true, new Color(0.5f, 0.2f, 0f, 1f));

            // speed
            speedText = Txt(root, "0 km/h", 46, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-260, 270), new Vector2(260, 340), true, Ink);

            // brake
            var brake = Button(root, "Brake", "BRAKE", Orange, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(0, 80), new Vector2(540, 180), () => { });
            brakeImg = brake.transform.Find("Face").GetComponent<Image>();
            // brake is a hold button: remove click, add down/up
            Hold(brake, () => input.BrakeHeld = true, () => input.BrakeHeld = false);

            // steering edge zones (also show chevrons)
            BuildEdge(root, -1);
            BuildEdge(root, +1);
        }

        void BuildEdge(RectTransform root, int dir)
        {
            float ax = dir < 0 ? 0f : 1f;
            // invisible tall touch strip along the screen edge (full height, below the HUD top)
            var zone = Stretch(root, dir < 0 ? "EdgeLeft" : "EdgeRight",
                new Vector2(dir < 0 ? 0f : 0.78f, 0f), new Vector2(dir < 0 ? 0.22f : 1f, 0.82f), Vector2.zero, Vector2.zero);
            Img(zone, null, new Color(1, 1, 1, 0.004f), false, true);
            Hold(zone.gameObject, () => input.EdgeDown(dir), () => input.EdgeUp(dir));

            var disc = Anchored(root, dir < 0 ? "ArrowLeft" : "ArrowRight", new Vector2(ax, 0), new Vector2(ax, 0),
                new Vector2(dir < 0 ? 24 : -24, 110), new Vector2(150, 150));
            Img(disc, SlopeSprites.Circle, new Color(1, 1, 1, 0.55f));
            var chev = Anchored(disc, "Chevron", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 100));
            var ci = Img(chev, SlopeSprites.Chevron, Blue);
            if (dir < 0) chev.localScale = new Vector3(-1, 1, 1);
            if (dir < 0) leftArrow = ci; else rightArrow = ci;
        }

        // ───────────────────────────── Tips ─────────────────────────────

        void BuildTips()
        {
            tipsRoot = Stretch(safe, "Tips", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var root = (RectTransform)tipsRoot.transform;

            var title = Anchored(root, "Title", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 930), new Vector2(980, 170));
            tipsTitle = Txt(title, "Slope Smart", 140, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true, Blue);

            var card = Anchored(root, "Card", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 340), new Vector2(960, 560));
            Img(card, SlopeSprites.Round, Color.white, true);
            Txt(card, "PIP'S TIPS", 34, Blue, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -64), new Vector2(0, -12));

            string[] tips = { "Stay in control.", "People ahead go first.", "Slow down in slow zones." };
            for (int i = 0; i < 3; i++)
            {
                var row = Anchored(card, "Tip" + (i + 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -80 - i * 156), new Vector2(900, 136));
                Img(row, SlopeSprites.Round, PaleBlue, true);
                var num = Anchored(row, "Num", new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(88, 88));
                Img(num, SlopeSprites.Circle, Blue);
                FillText(num, (i + 1).ToString(), 56, Color.white, TextAnchor.MiddleCenter);
                tipTexts[i] = Txt(row, tips[i], 54, Ink, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(140, 6), new Vector2(-20, -6));
            }

            tipsHint = Txt(root, "Tilt your device or tap the screen edges to steer.\nHold BRAKE to slow down.", 38, Ink, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-480, 215), new Vector2(480, 330), true, Color.white);

            tiltButton = Button(root, "TiltToggle", "TILT: ON", Blue, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-250, 50), new Vector2(430, 140), () => OnToggleTilt?.Invoke());
            tiltLabel = tiltButton.GetComponentInChildren<Text>();
            tiltLabel.resizeTextMaxSize = 52;

            startButton = Button(root, "Start", "START!", Green, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(250, 50), new Vector2(430, 140), () => OnStart?.Invoke());
        }

        // ───────────────────────────── Results ─────────────────────────────

        void BuildResults()
        {
            resultsRoot = Stretch(safe, "Results", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            var root = (RectTransform)resultsRoot.transform;

            var title = Anchored(root, "Title", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 1000), new Vector2(980, 150));
            resultTitle = Txt(title, "AMAZING!", 130, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, true, Green);

            var card = Anchored(root, "Card", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 340), new Vector2(960, 640));
            Img(card, SlopeSprites.Round, Color.white, true);

            resultStars = new Image[3];
            float[] xs = { -230, 0, 230 };
            float[] ys = { -30, 6, -30 };
            for (int i = 0; i < 3; i++)
            {
                var rt = Anchored(card, "Star" + i, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(xs[i], ys[i]), new Vector2(200, 200));
                resultStars[i] = Img(rt, SlopeSprites.Star, Gold);
            }

            var score = Anchored(card, "Score", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-40, -250), new Vector2(620, 100));
            resultScore = FillText(score, "Score: 0", 76, Ink, TextAnchor.MiddleCenter);

            var tag = Anchored(card, "NewBest", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(330, -232), new Vector2(240, 56));
            Img(tag, SlopeSprites.Round, Red, true);
            FillText(tag, "NEW BEST!", 32, Color.white, TextAnchor.MiddleCenter);
            newBestTag = tag.gameObject;

            var rem = Anchored(card, "Remember", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(880, 250));
            Img(rem, SlopeSprites.Round, new Color32(0xdf, 0xf5, 0xe6, 255), true);
            Txt(rem, "REMEMBER", 32, Green * 0.85f, TextAnchor.MiddleCenter, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -52), new Vector2(0, -8));
            rememberText = Txt(rem, "", 50, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(24, 12), new Vector2(-24, -56));

            var bl = Button(root, "PlayAgain", "PLAY AGAIN", Green, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(-250, 50), new Vector2(430, 140), () => resLeft?.Invoke());
            resLeftLabel = bl.GetComponentInChildren<Text>();
            var br = Button(root, "Home", "HOME", Blue, new Vector2(0.5f, 0), new Vector2(0.5f, 0),
                new Vector2(250, 50), new Vector2(430, 140), () => resRight?.Invoke());
            resRightLabel = br.GetComponentInChildren<Text>();
        }

        // ───────────────────────────── public API ─────────────────────────────

        void HideAll()
        {
            tipsRoot.SetActive(false);
            hudRoot.SetActive(false);
            resultsRoot.SetActive(false);
            dressRoot.SetActive(false);
        }

        public void SetResultButtons(string leftLabel, Action left, string rightLabel, Action right)
        {
            resLeftLabel.text = leftLabel; resLeft = left;
            resRightLabel.text = rightLabel; resRight = right;
        }

        public void ShowTips(string title, string[] tips, string hint, bool tiltAvailable, bool tiltOn)
        {
            HideAll();
            tipsRoot.SetActive(true);
            tipsTitle.text = title;
            tipsHint.text = hint;
            for (int i = 0; i < 3; i++) tipTexts[i].text = tips[i];
            tiltButton.SetActive(tiltAvailable);
            ((RectTransform)startButton.transform).anchoredPosition = new Vector2(tiltAvailable ? 250 : 0, 50);
            SetTiltLabel(tiltOn);
        }

        public void SetTiltLabel(bool on) { if (tiltLabel != null) tiltLabel.text = on ? "TILT: ON" : "TILT: OFF"; }

        public void ShowHud()
        {
            HideAll();
            hudRoot.SetActive(true);
            SetBanner(false, "");
            toastTimer = 0;
            toastBg.gameObject.SetActive(false);
        }

        public void ShowResults(int stars, int score, bool newBest, string title, Color titleColor, string remember)
        {
            HideAll();
            resultsRoot.SetActive(true);
            resultTitle.text = title;
            resultTitle.GetComponent<Outline>().effectColor = titleColor;
            resultScore.text = "Score: " + score;
            rememberText.text = remember;
            newBestTag.SetActive(newBest);
            for (int i = 0; i < 3; i++)
                resultStars[i].color = i < stars ? Gold : new Color(0.8f, 0.84f, 0.9f, 1f);
        }

        public void SetHearts(int n)
        {
            for (int i = 0; i < hearts.Length; i++)
                hearts[i].color = i < n ? Red : new Color(0.25f, 0.28f, 0.36f, 0.45f);
        }

        public void SetScore(int s) { scoreText.text = s.ToString(); }
        public void SetProgress(float p) { progressFill.fillAmount = Mathf.Clamp01(p); }
        public void SetSpeed(float kmh) { speedText.text = Mathf.RoundToInt(kmh) + " km/h"; }

        public void Toast(string msg, float seconds = 2.2f)
        {
            toastText.text = msg;
            toastTimer = toastLife = seconds;
            toastBg.gameObject.SetActive(true);
            SetAlpha(1f);
        }

        void SetAlpha(float a)
        {
            var c = toastBg.color; c.a = 0.86f * a; toastBg.color = c;
            var t = toastText.color; t.a = a; toastText.color = t;
        }

        public void SetBanner(bool on, string msg)
        {
            bannerOn = on;
            bannerBg.gameObject.SetActive(on);
            if (on) bannerText.text = msg;
        }

        public void Tick(float dt)
        {
            TickGear(dt);
            if (toastTimer > 0)
            {
                toastTimer -= dt;
                if (toastTimer <= 0) toastBg.gameObject.SetActive(false);
                else SetAlpha(Mathf.Clamp01(toastTimer / 0.4f));
            }
            if (bannerOn)
            {
                float k = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * 10f);
                bannerBg.color = Orange * k + new Color(0, 0, 0, 1 - k);
            }
            if (hudRoot.activeSelf)
            {
                // highlight pressed controls
                if (leftArrow != null) leftArrow.color = input.LeftHeld ? Orange : Blue;
                if (rightArrow != null) rightArrow.color = input.RightHeld ? Orange : Blue;
                if (brakeImg != null) brakeImg.color = input.BrakePressed() ? new Color32(0xc9, 0x55, 0x14, 255) : Orange;
            }
        }
    }
}
