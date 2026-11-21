using UnityEngine;

namespace SlopeSmart
{
    /// <summary>Procedurally drawn UI sprites (rounded box, circle, heart, star, chevron). No image assets needed.</summary>
    public static class SlopeSprites
    {
        const int N = 64;
        static Sprite round, circle, heart, star, chevron;

        public static Sprite Round => round != null ? round : round = Make(RoundCoverage, new Vector4(26, 26, 26, 26));
        public static Sprite Circle => circle != null ? circle : circle = Make(CircleCoverage, Vector4.zero);
        public static Sprite Heart => heart != null ? heart : heart = Make(HeartCoverage, Vector4.zero);
        public static Sprite Star => star != null ? star : star = Make(StarCoverage, Vector4.zero);
        public static Sprite Chevron => chevron != null ? chevron : chevron = Make(ChevronCoverage, Vector4.zero);

        delegate float Coverage(float u, float v); // u,v in [-1,1], v up; returns inside (0/1)

        static Sprite Make(Coverage cov, Vector4 border)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color32[N * N];
            const int S = 3; // supersample
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float a = 0;
                for (int sy = 0; sy < S; sy++)
                for (int sx = 0; sx < S; sx++)
                {
                    float u = ((x + (sx + 0.5f) / S) / N) * 2f - 1f;
                    float v = ((y + (sy + 0.5f) / S) / N) * 2f - 1f;
                    a += cov(u, v);
                }
                a /= S * S;
                px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, border);
        }

        static float RoundCoverage(float u, float v)
        {
            // box half-size 1, corner radius 26/32 ~ 0.8125 of half size
            const float r = 0.8125f;
            float qx = Mathf.Abs(u) - (1f - r);
            float qy = Mathf.Abs(v) - (1f - r);
            float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude - r;
            return d <= 0 ? 1f : 0f;
        }

        static float CircleCoverage(float u, float v) => (u * u + v * v) <= 0.94f ? 1f : 0f;

        static float HeartCoverage(float u, float v)
        {
            float x = u * 1.25f;
            float y = v * 1.25f + 0.1f;
            float a = x * x + y * y - 1f;
            return (a * a * a - x * x * y * y * y) <= 0 ? 1f : 0f;
        }

        static float StarCoverage(float u, float v)
        {
            // 5-point star, outer radius 0.98, inner 0.4, point up
            const int pts = 10;
            Vector2 p = new Vector2(u, v + 0.05f);
            bool inside = false;
            Vector2 prev = StarPoint(pts - 1);
            for (int i = 0; i < pts; i++)
            {
                Vector2 cur = StarPoint(i);
                if (((cur.y > p.y) != (prev.y > p.y)) &&
                    (p.x < (prev.x - cur.x) * (p.y - cur.y) / (prev.y - cur.y) + cur.x))
                    inside = !inside;
                prev = cur;
            }
            return inside ? 1f : 0f;
        }

        static Vector2 StarPoint(int i)
        {
            float r = (i % 2 == 0) ? 0.98f : 0.42f;
            float ang = Mathf.PI / 2f + i * Mathf.PI / 5f;
            return new Vector2(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r);
        }

        static float ChevronCoverage(float u, float v)
        {
            // ">" made of two thick segments
            Vector2 p = new Vector2(u, v);
            float d = Mathf.Min(SegDist(p, new Vector2(-0.35f, 0.7f), new Vector2(0.4f, 0f)),
                                SegDist(p, new Vector2(0.4f, 0f), new Vector2(-0.35f, -0.7f)));
            return d <= 0.17f ? 1f : 0f;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }
    }
}
