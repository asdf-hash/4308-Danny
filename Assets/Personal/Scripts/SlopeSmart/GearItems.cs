using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SlopeSmart
{
    /// <summary>One drawn piece of an item icon, in a 100x100 icon space centred on 0,0.</summary>
    public struct IconShape
    {
        public bool circle;
        public float x, y, w, h, rot;
        public Color color;
    }

    public class GearItem
    {
        public string label;
        public bool good;
        public bool safety;      // true = safety gear, false = warmth gear (both are required when good)
        public string why;       // shown in the feedback toast
        public Sprite icon;      // optional custom art; falls back to the drawn shapes
        public IconShape[] shapes;
    }

    public static class GearItems
    {
        static IconShape R(float x, float y, float w, float h, float rot, string hex) => Make(false, x, y, w, h, rot, hex);
        static IconShape C(float x, float y, float w, float h, float rot, string hex) => Make(true, x, y, w, h, rot, hex);

        static IconShape Make(bool circle, float x, float y, float w, float h, float rot, string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return new IconShape { circle = circle, x = x, y = y, w = w, h = h, rot = rot, color = c };
        }

        static GearItem G(string label, bool safety, string why, params IconShape[] s) =>
            new GearItem { label = label, good = true, safety = safety, why = why, shapes = s };

        static GearItem B(string label, string why, params IconShape[] s) =>
            new GearItem { label = label, good = false, safety = false, why = why, shapes = s };

        public const int GoodCount = 12;

        public static readonly GearItem[] All =
        {
            // ───── good (12): safety first, then staying warm ─────
            G("Helmet", true, "Helmet! It protects your head.",
                C(0, 6, 76, 66, 0, "#e63946"), R(0, -16, 84, 16, 0, "#9d1c27"), R(0, 14, 12, 44, 0, "#f7f7f7")),
            G("Goggles", true, "Goggles protect your eyes from sun and snow.",
                R(0, 0, 92, 30, 0, "#23314f"), R(0, 0, 62, 42, 0, "#38b6ff"), R(-12, 8, 18, 10, 20, "#ffffffaa")),
            G("Ski Gloves", true, "Ski gloves keep your hands warm and dry.",
                R(-18, 22, 11, 32, 0, "#3b4cc0"), R(-6, 26, 11, 38, 0, "#3b4cc0"), R(6, 26, 11, 38, 0, "#3b4cc0"), R(18, 22, 11, 32, 0, "#3b4cc0"),
                R(0, -8, 48, 46, 0, "#2d3a8c"), R(-32, -2, 12, 28, 35, "#2d3a8c"), R(0, -34, 56, 16, 0, "#cfd6e6")),
            G("Snow Pants", false, "Snow pants keep you dry and warm.",
                R(-15, -4, 30, 74, 3, "#26408b"), R(15, -4, 30, 74, -3, "#26408b"), R(0, 32, 66, 16, 0, "#3558b5"),
                R(-15, -20, 30, 6, 0, "#ffd23f"), R(15, -20, 30, 6, 0, "#ffd23f")),
            G("Ski Boots", true, "Ski boots hold your feet to your skis.",
                R(-8, 10, 32, 52, 0, "#f07a2a"), R(8, -18, 60, 28, 0, "#f07a2a"), R(10, -34, 66, 8, 0, "#222831"),
                R(-8, 14, 24, 7, 0, "#ffffff"), R(-8, 0, 24, 7, 0, "#ffffff")),
            G("Jacket", false, "A warm waterproof jacket. Nice!",
                R(-34, 4, 20, 60, 12, "#c92a3a"), R(34, 4, 20, 60, -12, "#c92a3a"), R(0, 0, 56, 70, 0, "#e63946"),
                R(0, 0, 5, 66, 0, "#ffffff"), R(0, 34, 40, 12, 0, "#9d1c27")),
            G("Hand Warmers", false, "Hand warmers beat frozen fingers.",
                R(0, 0, 60, 40, 0, "#ff8a3d"), C(0, 0, 22, 22, 0, "#ffd23f"), C(-18, 0, 8, 8, 0, "#ffffffaa"), C(18, 0, 8, 8, 0, "#ffffffaa")),
            G("Toe Warmers", false, "Toe warmers keep your feet toasty.",
                C(0, 0, 78, 40, 0, "#ffe08a"), C(0, 0, 32, 18, 0, "#ff8a3d"), C(-24, 0, 8, 8, 0, "#ffffffaa")),
            G("Ski Socks", false, "Ski socks stay warm and wick sweat.",
                R(-8, 16, 30, 56, 0, "#1fa8a0"), R(8, -18, 52, 26, 0, "#1fa8a0"),
                R(-8, 36, 30, 8, 0, "#ffffff"), R(-8, 26, 30, 5, 0, "#ffd23f")),
            G("Skis", true, "Skis! You can't ski without them.",
                R(-14, 0, 14, 96, 8, "#2f8fe0"), R(14, 0, 14, 96, 8, "#f0b020"),
                R(-12, -8, 12, 18, 8, "#222831"), R(16, -8, 12, 18, 8, "#222831")),
            G("Poles", true, "Poles help you balance and turn.",
                R(-16, 0, 6, 92, -14, "#9aa7bd"), R(16, 0, 6, 92, 14, "#9aa7bd"),
                C(-5, 42, 14, 14, 0, "#e63946"), C(5, 42, 14, 14, 0, "#e63946"),
                C(-27, -38, 20, 9, 0, "#444b5c"), C(27, -38, 20, 9, 0, "#444b5c")),
            G("Neck Warmer", false, "A neck warmer is warm and can't catch on lifts.",
                R(0, 0, 74, 46, 0, "#17a398"), R(0, -10, 74, 6, 0, "#ffffff66"),
                C(0, 20, 66, 22, 0, "#0f7e75"), C(0, 20, 48, 12, 0, "#0a5a54")),

            // ───── red herrings (6) ─────
            B("Scarf", "Scarves can tangle in lifts. Use a neck warmer.",
                R(0, 18, 84, 22, -8, "#d62839"), R(22, -14, 22, 60, -10, "#d62839"), R(0, 18, 10, 22, -8, "#ffffff"),
                R(22, -46, 22, 6, -10, "#b01e2f")),
            B("Cotton Socks", "Cotton gets damp and cold. Wear ski socks.",
                R(-8, 16, 30, 56, 0, "#f4f4f4"), R(8, -18, 52, 26, 0, "#f4f4f4"), R(-8, 36, 30, 6, 0, "#9aa7bd")),
            B("Knit Mittens", "Knitted mittens soak up snow. Use ski gloves.",
                C(-30, 0, 24, 30, 0, "#f28ab2"), R(0, -6, 54, 58, 0, "#f28ab2"), R(0, -36, 56, 18, 0, "#ffffff"),
                R(0, 6, 30, 5, 0, "#ffffff99"), R(0, -6, 30, 5, 0, "#ffffff99")),
            B("Wool Hat", "A wool hat can't protect your head like a helmet.",
                C(0, 6, 72, 62, 0, "#8e5ad8"), R(0, -22, 76, 20, 0, "#6c3fb5"), C(0, 40, 22, 22, 0, "#ffffff")),
            B("Jeans", "Jeans soak up water and freeze. Use snow pants.",
                R(-15, -4, 30, 74, 3, "#5b86c8"), R(15, -4, 30, 74, -3, "#5b86c8"), R(0, 32, 66, 16, 0, "#4a72b0"),
                R(-16, 20, 16, 14, 0, "#3d63a0"), C(0, 32, 8, 8, 0, "#ffd23f")),
            B("Sunglasses", "Sunglasses can fall off. Use goggles.",
                C(-22, 0, 36, 26, 0, "#111419"), C(22, 0, 36, 26, 0, "#111419"), R(0, 6, 14, 5, 0, "#111419"),
                R(-42, 6, 18, 5, 0, "#111419"), R(42, 6, 18, 5, 0, "#111419")),
        };
    }

    /// <summary>Drag source for a wardrobe tile. Forwards drag events to the UI.</summary>
    public class GearTile : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int index;
        public bool locked;
        public Action<int, PointerEventData> Begin, Drag, End;

        public void OnBeginDrag(PointerEventData e) { if (!locked) Begin?.Invoke(index, e); }
        public void OnDrag(PointerEventData e) { if (!locked) Drag?.Invoke(index, e); }
        public void OnEndDrag(PointerEventData e) { if (!locked) End?.Invoke(index, e); }
    }
}
