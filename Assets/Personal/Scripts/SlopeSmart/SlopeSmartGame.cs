using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopeSmart
{
    public enum GameState { Tips, Dressing, Playing, Results }

    /// <summary>
    /// "Slope Smart" lesson from Ski Safe with Pip.
    /// Steer down a fenced run (tilt or tap screen edges), hold BRAKE in slow zones,
    /// give skiers ahead space, avoid trees and rocks. Lose a heart per mistake; 0-3 stars at the end.
    /// Everything (slope, props, Pip, UI) is built at runtime from the imported ski prefabs.
    /// </summary>
    public class SlopeSmartGame : MonoBehaviour
    {
        [Header("Imported prefabs (Assets/Imported/Ski - Props for Winter Runner)")]
        public GameObject playerSkierPrefab;
        public GameObject npcSkierPrefab;
        public GameObject treePrefab;
        public GameObject rockPrefab;
        public GameObject fenceAPrefab;
        public GameObject fenceBPrefab;
        public Material snowMaterial;

        [Header("Custom gear icons (Assets/Imported/Icons)")]
        public Sprite iconHelmet;
        public Sprite iconGoggles, iconGloves, iconSnowPants, iconBoots, iconJacket;
        public Sprite iconKnitMittens, iconScarf, iconSkiSocks, iconSunglasses, iconWoolHat;

        [Header("Sound")]
        public AudioClip popClip;

        [Header("Slope")]
        public float slopeHalfWidth = 6f;
        public float slopeLength = 520f;
        public float viewAhead = 125f;

        [Header("Speed (m/s)")]
        public float minSpeed = 2.5f;
        public float maxSpeed = 12.5f;
        public float acceleration = 3f;
        public float brakeDeceleration = 9f;
        public float slowZoneLimit = 5.5f;

        [Header("Steering")]
        public float maxLateralSpeed = 7.5f;

        [Header("Look")]
        public float skierScale = 1.3f;
        public float treeScaleMin = 1.5f, treeScaleMax = 2.7f;

        // ───────────── level data ─────────────
        enum Kind { Tree, Rock, SideTree, Fence, Stripe, Gate, Finish }

        class Entry
        {
            public Kind kind;
            public GameObject prefab;
            public float x, z, yaw, scale = 1f, radius;
            public bool hit;
            public GameObject go;
        }

        class Npc
        {
            public float x0, z0, speed, phase, x, z, scale;
            public bool hit, warned, passed;
            public GameObject go;
        }

        class Zone
        {
            public float start, end;
            public bool warned, failed, cleared;
            public float overTimer;
        }

        readonly List<Entry> statics = new List<Entry>();
        readonly List<Entry> live = new List<Entry>();
        readonly List<Npc> npcs = new List<Npc>();
        readonly List<Zone> zones = new List<Zone>();
        int nextStatic;

        // ───────────── level 1: gear up ─────────────
        int level;                       // 0 = Gear Up, 1 = Slope Smart
        Transform dressSkier;
        AudioSource sfx;
        bool[] worn, tried;
        int wrongTries;
        bool doneArmed;
        const int PointsPerItem = 100, WrongPenalty = 50, MissingPenalty = 50;

        // ───────────── runtime state ─────────────
        GameState state = GameState.Tips;
        SteeringInput input;
        SlopeUI ui;
        Camera cam;
        Transform worldRoot, ground, mountains;
        Transform playerRoot, playerVisual;
        Renderer[] playerRenderers;
        Transform pip, pipFlipperL, pipFlipperR;
        float fenceSpacing = 1.4f;

        float px, pz, speed, steer, elapsed;
        float spin, invuln, shake;
        float finishTimer;
        bool finished, dead, scrapeWarned;
        int hearts, score, passBonus;
        int crashCount, spaceCount, speedCount;
        float lastKmh;
        Vector3 camPos;
        Quaternion camRot;
        bool camInit;

        const float PipOffset = 3.2f;
        const float DressDist = 4.2f;
        const int MaxHearts = 3;
        const string BestKey = "SlopeSmart.Best";
        const string GearBestKey = "SlopeSmart.GearBest";
        const string TiltKey = "SlopeSmart.Tilt";

        // ═════════════════════════════ lifecycle ═════════════════════════════

        void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            SetupOrientation();

            cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cgo.tag = "MainCamera";
                cam = cgo.GetComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.8f, 0.96f);
            cam.farClipPlane = 420f;
            cam.nearClipPlane = 0.3f;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.78f, 0.88f, 0.98f);
            RenderSettings.fogStartDistance = 120f;
            RenderSettings.fogEndDistance = 380f;

            AssignGearIcons();
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;

            input = new SteeringInput();
            input.Init();
            input.TiltEnabled = PlayerPrefs.GetInt(TiltKey, 1) == 1;

            worldRoot = new GameObject("SlopeWorld").transform;
            BuildGroundAndBackdrop();
            BuildPlayer();
            BuildPip();
            BuildDressSkier();

            ui = new SlopeUI(input)
            {
                OnStart = () => { if (level == 0) StartDressing(); else StartRun(); },
                OnGearDrop = HandleGearDrop,
                OnGearDone = HandleGearDone,
                OnToggleTilt = () =>
                {
                    input.TiltEnabled = !input.TiltEnabled;
                    PlayerPrefs.SetInt(TiltKey, input.TiltEnabled ? 1 : 0);
                    ui.SetTiltLabel(input.TiltEnabled);
                }
            };
            ui.Build();

            ShowTips(0);
        }

#if UNITY_EDITOR
        // Editor safety net: if the open scene is stale (e.g. edited on disk while open), fill any empty
        // asset references by path so the scene still works until it is reloaded/saved.
        void OnValidate()
        {
            const string icons = "Assets/Imported/Icons/";
            Sprite S(string f) => UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(icons + f + ".png");
            if (iconHelmet == null) iconHelmet = S("helmet");
            if (iconGoggles == null) iconGoggles = S("goggles");
            if (iconGloves == null) iconGloves = S("gloves");
            if (iconSnowPants == null) iconSnowPants = S("snowpants");
            if (iconBoots == null) iconBoots = S("boots");
            if (iconJacket == null) iconJacket = S("jacket");
            if (iconKnitMittens == null) iconKnitMittens = S("knitted gloves");
            if (iconScarf == null) iconScarf = S("scarf");
            if (iconSkiSocks == null) iconSkiSocks = S("ski-socks");
            if (iconSunglasses == null) iconSunglasses = S("sunglasses");
            if (iconWoolHat == null) iconWoolHat = S("toque");
            if (popClip == null) popClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Imported/Sounds/pop.wav");
        }
#endif

        void AssignGearIcons()
        {
            foreach (var it in GearItems.All)
            {
                switch (it.label)
                {
                    case "Helmet": it.icon = iconHelmet; break;
                    case "Goggles": it.icon = iconGoggles; break;
                    case "Ski Gloves": it.icon = iconGloves; break;
                    case "Snow Pants": it.icon = iconSnowPants; break;
                    case "Ski Boots": it.icon = iconBoots; break;
                    case "Jacket": it.icon = iconJacket; break;
                    case "Knit Mittens": it.icon = iconKnitMittens; break;
                    case "Scarf": it.icon = iconScarf; break;
                    case "Ski Socks": it.icon = iconSkiSocks; break;
                    case "Sunglasses": it.icon = iconSunglasses; break;
                    case "Wool Hat": it.icon = iconWoolHat; break;
                }
            }
        }

        void OnDestroy()
        {
            if (input != null) input.Shutdown();
        }

        static void SetupOrientation()
        {
#if !UNITY_EDITOR
            float dpi = Screen.dpi > 0 ? Screen.dpi : 160f;
            float diagonalInches = Mathf.Sqrt(Screen.width * (float)Screen.width + Screen.height * (float)Screen.height) / dpi;
            if (diagonalInches < 6.9f)
            {
                Screen.orientation = ScreenOrientation.Portrait;           // phone: one-thumb portrait
            }
            else
            {
                Screen.autorotateToPortrait = true;                        // tablet: any orientation
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                Screen.orientation = ScreenOrientation.AutoRotation;
            }
#endif
        }

        // ═════════════════════════════ building blocks ═════════════════════════════

        Material FlatMat(Color c, float smooth = 0.15f)
        {
            Material src = snowMaterial;
            Material m;
            if (src != null) m = new Material(src);
            else m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default"));
            m.mainTexture = null;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        }

        void BuildGroundAndBackdrop()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "Snow";
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(worldRoot, false);
            g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = new Vector3(420f, 460f, 1f);
            var gm = FlatMat(new Color(0.95f, 0.97f, 1f), 0.25f);
            g.GetComponent<Renderer>().sharedMaterial = gm;
            ground = g.transform;

            mountains = new GameObject("Mountains").transform;
            mountains.SetParent(worldRoot, false);
            var rock = FlatMat(new Color(0.72f, 0.8f, 0.9f), 0.05f);
            var snowcap = FlatMat(new Color(0.97f, 0.98f, 1f), 0.1f);
            var rng = new System.Random(7);
            for (int i = 0; i < 9; i++)
            {
                float x = -260 + i * 65 + (float)rng.NextDouble() * 30f;
                float h = 70f + (float)rng.NextDouble() * 70f;
                float w = 110f + (float)rng.NextDouble() * 60f;
                var p = Prim(PrimitiveType.Sphere, "Peak", mountains, new Vector3(x, h * 0.18f, (float)rng.NextDouble() * 40f), new Vector3(w, h, w * 0.7f), rock);
                Prim(PrimitiveType.Sphere, "Cap", p.transform, new Vector3(0, 0.36f, 0), new Vector3(0.62f, 0.4f, 0.62f), snowcap);
            }
        }

        void BuildPlayer()
        {
            playerRoot = new GameObject("Player").transform;
            if (playerSkierPrefab != null)
            {
                var v = Instantiate(playerSkierPrefab, playerRoot);
                StripColliders(v);
                v.transform.localPosition = Vector3.zero;
                v.transform.localRotation = Quaternion.identity;
                v.transform.localScale = Vector3.one * skierScale;
                playerVisual = v.transform;
                playerRenderers = v.GetComponentsInChildren<Renderer>();
            }
            else
            {
                var cap = Prim(PrimitiveType.Capsule, "PlaceholderSkier", playerRoot, new Vector3(0, 0.9f, 0), new Vector3(0.6f, 0.9f, 0.6f), FlatMat(Color.red));
                playerVisual = cap.transform;
                playerRenderers = cap.GetComponentsInChildren<Renderer>();
            }
        }

        void BuildPip()
        {
            pip = new GameObject("Pip").transform;
            pip.SetParent(worldRoot, false);
            Material black = FlatMat(new Color(0.1f, 0.13f, 0.28f), 0.3f);
            Material white = FlatMat(Color.white, 0.3f);
            Material orange = FlatMat(new Color(1f, 0.6f, 0.15f), 0.3f);
            Material red = FlatMat(new Color(0.9f, 0.18f, 0.22f), 0.3f);
            Material yellow = FlatMat(new Color(1f, 0.8f, 0.1f), 0.3f);
            Material goggle = FlatMat(new Color(0.2f, 0.65f, 0.95f), 0.8f);
            Material cheek = FlatMat(new Color(1f, 0.65f, 0.7f), 0.2f);

            Prim(PrimitiveType.Sphere, "Body", pip, new Vector3(0, 0.78f, 0), new Vector3(1.0f, 1.3f, 0.85f), black);
            Prim(PrimitiveType.Sphere, "Belly", pip, new Vector3(0, 0.74f, 0.2f), new Vector3(0.78f, 1.05f, 0.5f), white);
            Prim(PrimitiveType.Sphere, "Head", pip, new Vector3(0, 1.62f, 0.02f), new Vector3(0.82f, 0.74f, 0.76f), black);
            Prim(PrimitiveType.Sphere, "Face", pip, new Vector3(0, 1.58f, 0.2f), new Vector3(0.58f, 0.46f, 0.36f), white);
            Prim(PrimitiveType.Sphere, "EyeL", pip, new Vector3(-0.15f, 1.64f, 0.36f), new Vector3(0.1f, 0.13f, 0.06f), black);
            Prim(PrimitiveType.Sphere, "EyeR", pip, new Vector3(0.15f, 1.64f, 0.36f), new Vector3(0.1f, 0.13f, 0.06f), black);
            Prim(PrimitiveType.Sphere, "CheekL", pip, new Vector3(-0.27f, 1.54f, 0.33f), new Vector3(0.1f, 0.07f, 0.04f), cheek);
            Prim(PrimitiveType.Sphere, "CheekR", pip, new Vector3(0.27f, 1.54f, 0.33f), new Vector3(0.1f, 0.07f, 0.04f), cheek);
            Prim(PrimitiveType.Sphere, "Beak", pip, new Vector3(0, 1.55f, 0.43f), new Vector3(0.2f, 0.11f, 0.2f), orange);
            Prim(PrimitiveType.Sphere, "Scarf", pip, new Vector3(0, 1.24f, 0.03f), new Vector3(0.82f, 0.2f, 0.72f), yellow);
            Prim(PrimitiveType.Sphere, "Hat", pip, new Vector3(0, 1.95f, -0.02f), new Vector3(0.88f, 0.42f, 0.82f), red);
            Prim(PrimitiveType.Cube, "Goggles", pip, new Vector3(0, 1.84f, 0.34f), new Vector3(0.58f, 0.2f, 0.12f), goggle);
            Prim(PrimitiveType.Sphere, "FootL", pip, new Vector3(-0.22f, 0.05f, 0.18f), new Vector3(0.32f, 0.1f, 0.46f), orange);
            Prim(PrimitiveType.Sphere, "FootR", pip, new Vector3(0.22f, 0.05f, 0.18f), new Vector3(0.32f, 0.1f, 0.46f), orange);

            // flippers pivot at the shoulder
            pipFlipperL = new GameObject("FlipperL").transform;
            pipFlipperL.SetParent(pip, false);
            pipFlipperL.localPosition = new Vector3(-0.5f, 1.1f, 0);
            Prim(PrimitiveType.Sphere, "Wing", pipFlipperL, new Vector3(-0.07f, -0.3f, 0), new Vector3(0.16f, 0.7f, 0.34f), black);
            pipFlipperR = new GameObject("FlipperR").transform;
            pipFlipperR.SetParent(pip, false);
            pipFlipperR.localPosition = new Vector3(0.5f, 1.1f, 0);
            Prim(PrimitiveType.Sphere, "Wing", pipFlipperR, new Vector3(0.07f, -0.3f, 0), new Vector3(0.16f, 0.7f, 0.34f), black);

            pip.localScale = Vector3.one * 1.15f;
        }

        // ═════════════════════════════ level generation ═════════════════════════════

        float MeasureWidthX(GameObject prefab)
        {
            if (prefab == null) return 0.8f;
            var tmp = Instantiate(prefab, new Vector3(0, -500f, 0), Quaternion.identity);
            float w = 0.8f;
            var rs = tmp.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                w = Mathf.Max(0.2f, b.size.x);
            }
            Destroy(tmp);
            return w;
        }

        void BuildLevel()
        {
            statics.Clear();
            npcs.Clear();
            zones.Clear();
            nextStatic = 0;

            var rng = new System.Random(Environment.TickCount);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // slow zones
            float[] starts = { slopeLength * 0.29f, slopeLength * 0.58f, slopeLength * 0.83f };
            foreach (float s in starts) zones.Add(new Zone { start = s, end = s + 48f });

            bool InZone(float z, float pad = 10f)
            {
                foreach (var zn in zones) if (z > zn.start - pad && z < zn.end + pad) return true;
                return false;
            }

            // fences along both sides
            float fenceScale = 2.0f;
            if (fenceAPrefab != null)
            {
                fenceSpacing = MeasureWidthX(fenceAPrefab) * fenceScale * 0.98f;
                if (fenceSpacing < 0.6f) fenceSpacing = 0.6f;
            }
            int k = 0;
            for (float z = -12f; z < slopeLength + 60f; z += fenceSpacing, k++)
            {
                var prefab = (k % 2 == 0 || fenceBPrefab == null) ? fenceAPrefab : fenceBPrefab;
                if (prefab == null) break;
                statics.Add(new Entry { kind = Kind.Fence, prefab = prefab, x = -slopeHalfWidth - 0.2f, z = z, yaw = 90f, scale = fenceScale });
                statics.Add(new Entry { kind = Kind.Fence, prefab = prefab, x = slopeHalfWidth + 0.2f, z = z, yaw = 90f, scale = fenceScale });
            }

            // forest at the sides
            if (treePrefab != null)
            {
                for (float z = -25f; z < slopeLength + 140f; z += 4.2f)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        statics.Add(new Entry
                        {
                            kind = Kind.SideTree, prefab = treePrefab,
                            x = side * (slopeHalfWidth + R(2.4f, 7f)), z = z + R(-1.8f, 1.8f),
                            yaw = R(0, 360), scale = R(treeScaleMin, treeScaleMax)
                        });
                        if (rng.NextDouble() < 0.55)
                            statics.Add(new Entry
                            {
                                kind = Kind.SideTree, prefab = treePrefab,
                                x = side * (slopeHalfWidth + R(9f, 26f)), z = z + R(-2f, 2f),
                                yaw = R(0, 360), scale = R(treeScaleMin * 1.2f, treeScaleMax * 1.5f)
                            });
                    }
                }
            }

            // obstacles in the run (1-2 per row, always a gap)
            float zz = 55f;
            while (zz < slopeLength - 30f)
            {
                if (!InZone(zz, 4f))
                {
                    int count = rng.NextDouble() < 0.4 ? 2 : 1;
                    float lastX = float.NaN;
                    for (int i = 0; i < count; i++)
                    {
                        float x;
                        int tries = 0;
                        do { x = R(-slopeHalfWidth + 1.6f, slopeHalfWidth - 1.6f); tries++; }
                        while (!float.IsNaN(lastX) && Mathf.Abs(x - lastX) < 4.6f && tries < 20);
                        lastX = x;
                        bool rock = rockPrefab != null && (treePrefab == null || rng.NextDouble() < 0.4);
                        GameObject pf = rock ? rockPrefab : treePrefab;
                        if (pf == null) continue;
                        float sc = rock ? R(2.0f, 2.8f) : R(treeScaleMin, treeScaleMax);
                        statics.Add(new Entry
                        {
                            kind = rock ? Kind.Rock : Kind.Tree, prefab = pf, x = x, z = zz + R(-2f, 2f),
                            yaw = R(0, 360), scale = sc, radius = rock ? 0.55f * sc : 0.38f * sc
                        });
                    }
                }
                zz += R(20f, 34f);
            }

            // slow zone gates + ground stripes
            foreach (var zn in zones)
            {
                statics.Add(new Entry { kind = Kind.Gate, z = zn.start });
                statics.Add(new Entry { kind = Kind.Gate, z = zn.end, scale = 0.6f });
                for (float z = zn.start + 3f; z < zn.end; z += 5f)
                    statics.Add(new Entry { kind = Kind.Stripe, z = z });
            }
            statics.Add(new Entry { kind = Kind.Finish, z = slopeLength });

            statics.Sort((a, b) => a.z.CompareTo(b.z));

            // skiers ahead (slower than the player) - "people ahead go first"
            float nz = 85f;
            while (nz < slopeLength - 50f)
            {
                if (!InZone(nz, 6f))
                    npcs.Add(new Npc
                    {
                        x0 = R(-slopeHalfWidth + 2f, slopeHalfWidth - 2f), z0 = nz,
                        speed = R(4.2f, 6.0f), phase = R(0, 6.28f), scale = skierScale
                    });
                nz += R(48f, 72f);
            }
        }

        void ClearLive()
        {
            foreach (var e in live) if (e.go != null) Destroy(e.go);
            live.Clear();
            foreach (var n in npcs) if (n.go != null) Destroy(n.go);
            nextStatic = 0;
        }

        GameObject BuildGate(bool finish, float scale)
        {
            var root = new GameObject(finish ? "Finish" : "SlowGate");
            Material post = FlatMat(finish ? new Color(0.9f, 0.15f, 0.2f) : new Color(0.95f, 0.45f, 0.1f), 0.2f);
            Material bar = FlatMat(finish ? new Color(0.1f, 0.12f, 0.3f) : new Color(1f, 0.78f, 0.1f), 0.2f);
            float hw = slopeHalfWidth + 0.5f;
            float h = finish ? 5f : 4.2f;
            Prim(PrimitiveType.Cube, "PostL", root.transform, new Vector3(-hw, h * 0.5f, 0), new Vector3(0.5f, h, 0.5f), post);
            Prim(PrimitiveType.Cube, "PostR", root.transform, new Vector3(hw, h * 0.5f, 0), new Vector3(0.5f, h, 0.5f), post);
            Prim(PrimitiveType.Cube, "Bar", root.transform, new Vector3(0, h - 0.5f, 0), new Vector3(hw * 2f + 0.5f, 1.3f, 0.35f), bar);

            var txt = new GameObject("Label", typeof(TextMesh));
            txt.transform.SetParent(root.transform, false);
            txt.transform.localPosition = new Vector3(0, h - 0.5f, -0.22f);
            var tm = txt.GetComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.text = finish ? "FINISH" : (scale < 1f ? "SLOW ZONE ENDS" : "SLOW ZONE");
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = 0.16f;
            tm.fontStyle = FontStyle.Bold;
            tm.color = finish ? Color.white : new Color(0.25f, 0.1f, 0f);
            txt.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
            return root;
        }

        void SpawnStatic(Entry e)
        {
            switch (e.kind)
            {
                case Kind.Tree:
                case Kind.SideTree:
                case Kind.Rock:
                case Kind.Fence:
                    e.go = Instantiate(e.prefab, new Vector3(e.x, 0, e.z), Quaternion.Euler(0, e.yaw, 0), worldRoot);
                    e.go.transform.localScale = Vector3.one * e.scale;
                    StripColliders(e.go);
                    break;
                case Kind.Stripe:
                    e.go = Prim(PrimitiveType.Cube, "Stripe", worldRoot, new Vector3(0, 0.03f, e.z),
                        new Vector3(slopeHalfWidth * 2f, 0.04f, 0.7f), StripeMat());
                    break;
                case Kind.Gate:
                    e.go = BuildGate(false, e.scale);
                    e.go.transform.SetParent(worldRoot, false);
                    e.go.transform.position = new Vector3(0, 0, e.z);
                    break;
                case Kind.Finish:
                    e.go = BuildGate(true, 1f);
                    e.go.transform.SetParent(worldRoot, false);
                    e.go.transform.position = new Vector3(0, 0, e.z);
                    break;
            }
            if (e.go != null) live.Add(e);
        }

        Material stripeMat;
        Material StripeMat() => stripeMat != null ? stripeMat : stripeMat = FlatMat(new Color(1f, 0.55f, 0.15f), 0.1f);

        void SpawnNpc(Npc n)
        {
            if (npcSkierPrefab == null) { n.go = null; return; }
            n.go = Instantiate(npcSkierPrefab, new Vector3(n.x, 0, n.z), Quaternion.identity, worldRoot);
            StripColliders(n.go);
            n.go.transform.localScale = Vector3.one * n.scale;
        }

        // ═════════════════════════════ state changes ═════════════════════════════

        void PlacePip(float atZ, float atX)
        {
            pip.gameObject.SetActive(true);
            pip.position = new Vector3(atX, 0, atZ + PipOffset);
            pip.rotation = Quaternion.Euler(0, 180f, 0);
        }

        void ResetWorld()
        {
            ClearLive();
            BuildLevel();
        }

        void ShowTips(int lvl)
        {
            level = lvl;
            state = GameState.Tips;
            dressSkier.gameObject.SetActive(false);
            input.ResetAll();
            ResetWorld();
            px = 0; pz = 0; speed = 0; steer = 0; elapsed = 0;
            playerRoot.position = Vector3.zero;
            playerVisual.localRotation = Quaternion.identity;
            playerRoot.gameObject.SetActive(false);
            PlacePip(0, 0);
            SpawnVisible();
            if (level == 0)
                ui.ShowTips("Gear Up!",
                    new[] { "Dress warm and dry.", "Protect head, eyes, hands.", "Check you have it all!" },
                    "Drag each item onto the skier.\nCareful: not everything belongs on the slopes!",
                    false, false);
            else
                ui.ShowTips("Slope Smart",
                    new[] { "Stay in control.", "People ahead go first.", "Slow down in slow zones." },
                    "Tilt your device or tap the screen edges to steer.\nHold BRAKE to slow down.",
                    input.TiltAvailable, input.TiltEnabled);
        }

        void StartRun()
        {
            ResetWorld();
            state = GameState.Playing;
            input.ResetAll();
            input.Calibrate();

            hearts = MaxHearts; score = 0; passBonus = 0; zoneBonusTotal = 0;
            crashCount = spaceCount = speedCount = 0;
            px = 0; pz = 0; speed = 6f; steer = 0; elapsed = 0;
            spin = 0; invuln = 0; shake = 0; finishTimer = 0;
            finished = dead = scrapeWarned = false;

            playerRoot.gameObject.SetActive(true);
            SetPlayerVisible(true);
            pip.gameObject.SetActive(false);
            SpawnVisible();

            ui.ShowHud();
            ui.SetHearts(hearts);
            ui.SetScore(0);
            ui.SetProgress(0);
            ui.Toast(input.TiltAvailable && input.TiltEnabled
                ? "Tilt to steer, or tap the screen edges!"
                : "Tap the screen edges to steer!", 3.2f);
        }

        void EndRun(bool reachedFinish)
        {
            if (state != GameState.Playing) return;
            state = GameState.Results;
            input.ResetAll();

            int bonus = reachedFinish ? hearts * 100 : 0;
            int total = Mathf.Max(0, score + bonus);
            int stars = reachedFinish ? Mathf.Clamp(hearts, 1, 3) : 0;
            int best = PlayerPrefs.GetInt(BestKey, 0);
            bool newBest = total > best && reachedFinish;
            if (newBest) { PlayerPrefs.SetInt(BestKey, total); PlayerPrefs.Save(); }

            string title; Color tc;
            switch (stars)
            {
                case 3: title = "AMAZING!"; tc = new Color32(0x2f, 0xb8, 0x4f, 255); break;
                case 2: title = "GREAT JOB!"; tc = new Color32(0x2f, 0x8f, 0xf0, 255); break;
                case 1: title = "GOOD TRY!"; tc = new Color32(0xf0, 0x7a, 0x2a, 255); break;
                default: title = "OOPS! TRY AGAIN"; tc = new Color32(0xf0, 0x3c, 0x4b, 255); break;
            }

            string remember;
            if (crashCount == 0 && spaceCount == 0 && speedCount == 0)
                remember = "Stay in control. People ahead go first. Slow down in slow zones!";
            else if (spaceCount >= crashCount && spaceCount >= speedCount)
                remember = "People ahead go first. Give them lots of space!";
            else if (speedCount >= crashCount)
                remember = "Slow down in slow zones. Hold BRAKE!";
            else
                remember = "Stay in control. Steer around trees and rocks!";

            playerRoot.gameObject.SetActive(false);
            PlacePip(pz, px);
            ui.SetResultButtons("PLAY AGAIN", StartRun, "HOME", () => ShowTips(0));
            ui.ShowResults(stars, total, newBest, title, tc, remember);
        }

        // ═════════════════════════════ level 1: gear up ═════════════════════════════

        void BuildDressSkier()
        {
            GameObject go;
            if (playerSkierPrefab != null)
            {
                go = Instantiate(playerSkierPrefab, worldRoot);
                StripColliders(go);
                go.transform.localScale = Vector3.one * 1.25f;
            }
            else
            {
                go = Prim(PrimitiveType.Capsule, "PlaceholderSkier", worldRoot, new Vector3(0, 1f, 0), new Vector3(0.7f, 1f, 0.7f), FlatMat(Color.red));
            }
            dressSkier = go.transform;
            go.SetActive(false);
        }

        void AnimateDressSkier()
        {
            float t = Time.time;
            Vector3 p = pip.position;
            // stand in the free area left of the wardrobe
            bool portrait = Screen.height >= Screen.width;
            float fov = portrait ? 62f : 48f;
            float halfW = DressDist * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * (Screen.width / (float)Mathf.Max(1, Screen.height));
            float ndc = ui != null ? ui.DressSkierFrac * 2f - 1f : -0.4f;
            dressSkier.position = new Vector3(p.x + ndc * halfW, Mathf.Abs(Mathf.Sin(t * 2f)) * 0.04f, p.z);
            dressSkier.rotation = Quaternion.Euler(0, 180f + Mathf.Sin(t * 0.9f) * 12f, 0);
        }

        void StartDressing()
        {
            state = GameState.Dressing;
            input.ResetAll();
            worn = new bool[GearItems.All.Length];
            tried = new bool[GearItems.All.Length];
            wrongTries = 0;
            doneArmed = false;

            PlacePip(0, 0);
            pip.gameObject.SetActive(false);
            dressSkier.gameObject.SetActive(true);
            AnimateDressSkier();

            // shuffle the wardrobe so good and bad items are mixed
            int n = GearItems.All.Length;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            ui.ShowDress(order);
        }

        int WornGoodCount()
        {
            int c = 0;
            for (int i = 0; i < GearItems.All.Length; i++) if (worn[i]) c++;
            return c;
        }

        void HandleGearDrop(int idx)
        {
            if (state != GameState.Dressing || worn[idx] || tried[idx]) return;
            if (popClip != null && sfx != null) sfx.PlayOneShot(popClip);
            var item = GearItems.All[idx];
            if (item.good)
            {
                worn[idx] = true;
                ui.MarkWorn(idx);
                ui.SetWornCount(WornGoodCount());
                ui.GearToast(item.why, true, 2.2f);
                doneArmed = false;
                ui.SetDoneLabel("DONE");
            }
            else
            {
                tried[idx] = true;
                wrongTries++;
                ui.MarkRejected(idx);
                ui.GearToast(item.why + " (-" + WrongPenalty + ")", false, 3.2f);
            }
        }

        void HandleGearDone()
        {
            if (state != GameState.Dressing) return;
            int missing = GearItems.GoodCount - WornGoodCount();
            if (missing > 0 && !doneArmed)
            {
                doneArmed = true;
                ui.SetDoneLabel("FINISH ANYWAY");
                ui.GearToast("Wait, " + missing + " important item" + (missing > 1 ? "s are" : " is") + " missing! Check again.", false, 3.4f);
                return;
            }
            EndDressing();
        }

        void EndDressing()
        {
            state = GameState.Results;
            int goodWorn = WornGoodCount();
            int missing = GearItems.GoodCount - goodWorn;
            int raw = goodWorn * PointsPerItem - wrongTries * WrongPenalty - missing * MissingPenalty;
            int total = Mathf.Max(0, raw);
            float frac = total / (float)(GearItems.GoodCount * PointsPerItem);
            int stars = frac >= 0.9f ? 3 : frac >= 0.6f ? 2 : frac >= 0.3f ? 1 : 0;

            int best = PlayerPrefs.GetInt(GearBestKey, 0);
            bool newBest = total > best;
            if (newBest) { PlayerPrefs.SetInt(GearBestKey, total); PlayerPrefs.Save(); }

            string title; Color tc;
            switch (stars)
            {
                case 3: title = "FULLY GEARED!"; tc = new Color32(0x2f, 0xb8, 0x4f, 255); break;
                case 2: title = "ALMOST READY!"; tc = new Color32(0x2f, 0x8f, 0xf0, 255); break;
                case 1: title = "MISSING GEAR!"; tc = new Color32(0xf0, 0x7a, 0x2a, 255); break;
                default: title = "NOT READY YET!"; tc = new Color32(0xf0, 0x3c, 0x4b, 255); break;
            }

            var forgotSafety = new List<string>();
            var forgotWarm = new List<string>();
            var wrong = new List<string>();
            for (int i = 0; i < GearItems.All.Length; i++)
            {
                var it = GearItems.All[i];
                if (it.good && !worn[i]) (it.safety ? forgotSafety : forgotWarm).Add(it.label.ToLower());
                if (!it.good && tried[i]) wrong.Add(it.label.ToLower());
            }
            string remember;
            if (forgotSafety.Count > 0)
                remember = "Never ski without safety gear! You forgot: " + string.Join(", ", forgotSafety) + ".";
            else if (forgotWarm.Count > 0)
                remember = "Stay warm and dry! You forgot: " + string.Join(", ", forgotWarm) + ".";
            else if (wrong.Count > 0)
                remember = "All set! But leave out: " + string.Join(", ", wrong) + ".";
            else
                remember = "Safe and warm: helmet, goggles, gloves, boots, skis and warm layers!";
            if (forgotSafety.Count > 0 && forgotWarm.Count > 0)
                remember += " Also warm: " + string.Join(", ", forgotWarm) + ".";

            dressSkier.gameObject.SetActive(false);
            PlacePip(0, 0);
            ui.SetResultButtons("TRY AGAIN", StartDressing, "NEXT", () => ShowTips(1));
            ui.ShowResults(stars, total, newBest, title, tc, remember);
        }

        // ═════════════════════════════ update ═════════════════════════════

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (ui == null) return;

            if (state == GameState.Playing) StepPlay(dt);
            else if (state == GameState.Dressing) AnimateDressSkier();
            else AnimatePip(dt);

            UpdateWorldAnchors();
            UpdateCamera(dt);
            ui.Tick(dt);
        }

        void StepPlay(float dt)
        {
            elapsed += dt;
            steer = input.Tick(dt);
            bool braking = input.BrakePressed() && !finished && !dead;

            // ── speed
            if (finished || dead)
            {
                finishTimer += dt;
                speed = Mathf.MoveTowards(speed, 0f, 7f * dt);
                steer = Mathf.MoveTowards(steer, 0f, 4f * dt);
                if (finishTimer > (finished ? 2.0f : 1.4f)) { EndRun(finished); return; }
            }
            else
            {
                if (braking) speed = Mathf.Max(minSpeed, speed - brakeDeceleration * dt);
                else speed = Mathf.Min(maxSpeed, speed + acceleration * dt);
                speed = Mathf.Max(minSpeed, speed - Mathf.Abs(steer) * 1.1f * dt);
            }

            // ── lateral movement
            float limit = slopeHalfWidth - 0.7f;
            float grip = 0.6f + 0.4f * Mathf.Clamp01(speed / maxSpeed);
            px += steer * maxLateralSpeed * grip * dt;
            if (Mathf.Abs(px) > limit)
            {
                px = Mathf.Clamp(px, -limit, limit);
                if (!finished && !dead)
                {
                    speed = Mathf.Max(minSpeed, speed - 5f * dt);
                    if (!scrapeWarned) { scrapeWarned = true; ui.Toast("Stay inside the fences!", 1.6f); }
                }
            }
            else scrapeWarned = false;

            pz += speed * dt;
            lastKmh = speed * 3.6f;

            if (!finished && !dead)
            {
                invuln = Mathf.Max(0, invuln - dt);
                CheckObstacles();
                CheckNpcs();
                CheckZones(dt);
                score = Mathf.Max(0, Mathf.RoundToInt(pz) + passBonus + ZoneBonus());
                if (pz >= slopeLength) { finished = true; finishTimer = 0; ui.Toast("You made it!", 2.0f); }
            }

            // ── visuals
            spin = Mathf.Max(0, spin - dt);
            shake = Mathf.Max(0, shake - dt);
            float lean = steer;
            float roll = spin > 0 ? (1f - spin / 0.7f) * 360f : -lean * 14f;
            playerRoot.position = new Vector3(px, 0, pz);
            playerVisual.localRotation = Quaternion.Euler(0, lean * 24f, roll);
            BlinkPlayer();

            ui.SetScore(score);
            ui.SetProgress(pz / slopeLength);
            ui.SetSpeed(lastKmh);
            ui.SetHearts(hearts);
        }

        int zoneBonusTotal;
        int ZoneBonus() => zoneBonusTotal;

        void CheckObstacles()
        {
            if (invuln > 0) return;
            for (int i = 0; i < live.Count; i++)
            {
                var e = live[i];
                if (e.hit || (e.kind != Kind.Tree && e.kind != Kind.Rock)) continue;
                float dz = e.z - pz;
                if (dz > 2f || dz < -2f) continue;
                float dx = e.x - px;
                float r = e.radius + 0.4f;
                if (dx * dx + dz * dz < r * r)
                {
                    e.hit = true;
                    Crash("Ouch! Steer around trees and rocks.", Mistake.Crash);
                    return;
                }
            }
        }

        enum Mistake { Crash, Space, Speed }

        void Crash(string msg, Mistake why)
        {
            hearts--;
            switch (why)
            {
                case Mistake.Crash: crashCount++; break;
                case Mistake.Space: spaceCount++; break;
                case Mistake.Speed: speedCount++; break;
            }
            speed = Mathf.Max(minSpeed, speed * 0.35f);
            spin = 0.7f;
            invuln = 1.6f;
            shake = 0.35f;
            ui.SetHearts(hearts);
            if (hearts <= 0)
            {
                dead = true; finishTimer = 0;
                ui.Toast("Oh no! No hearts left.", 1.4f);
            }
            else ui.Toast(msg, 2.2f);
        }

        void CheckNpcs()
        {
            foreach (var n in npcs)
            {
                if (n.go == null || n.hit) continue;
                float dz = n.z - pz;
                float dx = n.x - px;
                float adx = Mathf.Abs(dx);

                if (invuln <= 0 && Mathf.Abs(dz) < 1.5f && adx < 0.95f)
                {
                    n.hit = true;
                    Crash("Careful! People ahead go first.", Mistake.Space);
                    continue;
                }
                if (!n.warned && Mathf.Abs(dz) < 3.2f && adx < 2.1f)
                {
                    n.warned = true;
                    spaceCount++;
                    passBonus = Mathf.Max(-score, passBonus - 50);
                    ui.Toast("Give people lots of space!", 1.8f);
                }
                if (!n.passed && dz < -4f)
                {
                    n.passed = true;
                    if (!n.warned) { passBonus += 60; ui.Toast("Nice, safe pass!", 1.2f); }
                }
            }
        }

        void CheckZones(float dt)
        {
            Zone cur = null;
            Zone upcoming = null;
            foreach (var zn in zones)
            {
                if (pz >= zn.start && pz <= zn.end) cur = zn;
                else if (pz < zn.start && zn.start - pz < 55f && upcoming == null) upcoming = zn;

                if (!zn.warned && pz > zn.start - 55f && pz < zn.start)
                {
                    zn.warned = true;
                    ui.Toast("Slow zone ahead! Hold BRAKE.", 2.4f);
                }
                if (!zn.cleared && !zn.failed && pz > zn.end)
                {
                    zn.cleared = true;
                    zoneBonusTotal += 150;
                    ui.Toast("Slow zone cleared! +150", 1.6f);
                }
            }

            if (cur != null)
            {
                bool tooFast = speed > slowZoneLimit + 0.8f;
                cur.overTimer = tooFast ? cur.overTimer + dt : Mathf.Max(0, cur.overTimer - dt * 0.5f);
                ui.SetBanner(true, tooFast ? "TOO FAST! HOLD BRAKE" : "SLOW ZONE  -  GOOD!");
                if (!cur.failed && cur.overTimer > 1.1f)
                {
                    cur.failed = true;
                    Crash("Too fast! Slow down in slow zones.", Mistake.Speed);
                }
            }
            else if (upcoming != null)
                ui.SetBanner(true, "SLOW ZONE AHEAD");
            else
                ui.SetBanner(false, "");
        }

        void BlinkPlayer()
        {
            bool on = !(invuln > 0 && Mathf.FloorToInt(Time.time * 14f) % 2 == 0);
            SetPlayerVisible(on);
        }

        void SetPlayerVisible(bool on)
        {
            if (playerRenderers == null) return;
            for (int i = 0; i < playerRenderers.Length; i++)
                if (playerRenderers[i] != null) playerRenderers[i].enabled = on;
        }

        // ═════════════════════════════ world upkeep ═════════════════════════════

        void SpawnVisible()
        {
            float focus = pz;
            while (nextStatic < statics.Count && statics[nextStatic].z < focus + viewAhead)
            {
                var e = statics[nextStatic++];
                if (e.z < focus - 25f) continue;
                SpawnStatic(e);
            }
            foreach (var n in npcs)
            {
                if (n.go == null && !n.hit)
                {
                    n.z = n.z0 + n.speed * elapsed;
                    if (n.z < focus + viewAhead && n.z > focus - 20f && !n.passed)
                    {
                        n.x = n.x0;
                        SpawnNpc(n);
                    }
                }
            }
        }

        void UpdateWorldAnchors()
        {
            // follow
            float focus = pz;
            ground.position = new Vector3(0, 0, focus + 150f);
            mountains.position = new Vector3(0, 0, focus + 340f);

            if (state == GameState.Playing) SpawnVisible();

            for (int i = live.Count - 1; i >= 0; i--)
            {
                var e = live[i];
                if (e.z < focus - 25f)
                {
                    if (e.go != null) Destroy(e.go);
                    live.RemoveAt(i);
                }
            }

            foreach (var n in npcs)
            {
                if (n.go == null) continue;
                n.z = n.z0 + n.speed * elapsed;
                n.x = Mathf.Clamp(n.x0 + Mathf.Sin(elapsed * 0.6f + n.phase) * 1.6f, -slopeHalfWidth + 1f, slopeHalfWidth - 1f);
                float sway = Mathf.Cos(elapsed * 0.6f + n.phase) * 0.6f;
                n.go.transform.position = new Vector3(n.x, 0, n.z);
                n.go.transform.rotation = Quaternion.Euler(0, sway * 20f, 0);
                if (n.z < focus - 25f) { Destroy(n.go); n.go = null; n.passed = true; }
            }
        }

        void AnimatePip(float dt)
        {
            if (pip == null || !pip.gameObject.activeSelf) return;
            float t = Time.time;
            pip.localPosition = new Vector3(pip.localPosition.x, Mathf.Abs(Mathf.Sin(t * 3f)) * 0.08f, pip.localPosition.z);
            float wave = Mathf.Sin(t * 6f);
            pipFlipperR.localRotation = Quaternion.Euler(0, 0, 120f + wave * 22f);
            pipFlipperL.localRotation = Quaternion.Euler(0, 0, -18f);
        }

        // ═════════════════════════════ camera ═════════════════════════════

        void UpdateCamera(float dt)
        {
            bool portrait = Screen.height >= Screen.width;
            float baseFov = portrait ? 62f : 48f;

            Vector3 wantPos;
            Quaternion wantRot;

            if (state == GameState.Dressing)
            {
                // straight-on shot, skier fills the left side of the screen
                Vector3 sp = pip.position;
                wantPos = new Vector3(sp.x, 1.25f, sp.z - DressDist);
                wantRot = Quaternion.LookRotation(new Vector3(0, -0.2f, DressDist));
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov, dt * 3f);
            }
            else if (state == GameState.Playing)
            {
                float t = Mathf.Clamp01(speed / maxSpeed);
                wantPos = new Vector3(px * 0.7f, 3.5f, pz - 7.4f - t * 0.8f);
                Vector3 look = new Vector3(px * 0.9f, 1.0f, pz + 7f);
                wantRot = Quaternion.LookRotation(look - wantPos);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov + t * 6f, dt * 3f);
                if (shake > 0)
                    wantPos += new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), 0) * shake * 0.5f;
            }
            else
            {
                // stage shot: Pip ahead of the camera, framed in the upper part of the screen above the UI card
                float ax = pip.position.x;
                float az = pip.position.z - PipOffset;
                wantPos = new Vector3(ax, 1.6f, az - 3.0f);
                Vector3 look = new Vector3(ax, -0.7f, az + 3.0f);
                wantRot = Quaternion.LookRotation(look - wantPos);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, baseFov, dt * 3f);
            }

            if (!camInit)
            {
                camPos = wantPos; camRot = wantRot; camInit = true;
                cam.fieldOfView = baseFov;
            }
            else
            {
                float k = 1f - Mathf.Exp(-(state == GameState.Playing ? 9f : 4f) * dt);
                camPos = Vector3.Lerp(camPos, wantPos, k);
                camRot = Quaternion.Slerp(camRot, wantRot, k);
            }
            cam.transform.SetPositionAndRotation(camPos, camRot);
        }
    }
}
