using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif
using static Calculatorio.U;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define USE_NEW_INPUT
using UnityEngine.InputSystem;
#endif

namespace Calculatorio
{
    public class GameController : MonoBehaviour
    {
        [Header("World")]
        public MapSize mapSize = MapSize.Planet;
        public int seed = 0;

        [Header("Time")]
        public float ticksPerSecond = 4f;
        public float timeScale = 1f;
        public bool pause = false;

        [Header("UI Layout")]
        public float controlBarFraction = 1f / 7f; // bottom dock height

        [Header("UI Scaling")]
        public bool autoScale = true;
        public Vector2 referenceResolution = new Vector2(1920, 1080);
        public float uiScale = 1.35f; // applies to TOP (bars) only

        [Header("Bars Layout")]
        public float minBarWidthPx = 170f;
        public float colGapPx = 12f;
        public float rowGapPx = 14f;

        [Header("Legends & Relations")]
        public bool highlightLinks = true;
        public bool drawLinkLines = true;
        [Range(0.15f, 0.85f)] public float dimOthers = 0.35f;
        public bool alwaysShowCenterLegends = false; // Alt shows when false

        [Header("Keys")]
        public KeyCode overlayKey = KeyCode.Escape;

        [Header("Trend / Ticker")]
        public float trendHalfLifeSeconds = 3f;
        public float tickerWidthPx = 6f;
        public float arrowShowThreshold = 0.02f;

        [Header("Peril / Game Over")]
        public float needFailSeconds = 25f;
        public float satFailSeconds  = 35f;
        public float needCrisisFrac  = 0.25f;   // <25% of per-cap need == crisis
        public float satCrisisThreshold = 20f;

        [Header("Bar Colors")]
        public bool allowColorCustomization = true;
        public bool useCustomBarColors = false;

        // Drilldown composition
        [Header("Drilldown")]
        public bool enableCompositionPopup = true;
        public float compositionImpactScale = 1f;
        public float compositionPerCapUnit = 1e-5f;

        // === NEW: Trade overlay ===
        bool tradeOpen = false; // modal window for trade

        // ======== Local pretty formatters (no scientific notation) ========
        public static string PrettySmall(float v, int maxDecimals = 6) => PrettySmall((double)v, maxDecimals);
        public static string PrettySmall(double v, int maxDecimals = 6)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
            double av = Math.Abs(v);
            if (av < 1e-12) return "0";

            int d = av >= 1      ? 2
                  : av >= 0.1    ? 3
                  : av >= 0.01   ? 4
                  : av >= 0.001  ? 5
                  : Math.Min(maxDecimals, 6);

            string s = v.ToString("F" + d, CultureInfo.InvariantCulture);
            if (s.Contains("."))
            {
                s = s.TrimEnd('0');
                if (s.EndsWith(".")) s = s[..^1];
                if (s == "-0") s = "0";
            }
            return s;
        }

        public static string PrettySmart(float v)  => PrettySmart((double)v);
        public static string PrettySmart(double v)
        {
            double av = Math.Abs(v);
            if (av < 1000d) return PrettySmall(v);

            string[] suf = { "k", "M", "B", "T", "Q" };
            int idx = -1;
            while (av >= 1000d && idx < suf.Length - 1) { av /= 1000d; idx++; }
            double scaled = v / Math.Pow(1000d, idx + 1);

            string fmt = (Math.Abs(scaled) >= 100d) ? "0"
                       : (Math.Abs(scaled) >= 10d)  ? "0.#"
                       :                               "0.##";

            return scaled.ToString(fmt, CultureInfo.InvariantCulture) + suf[idx];
        }

        public static string PrettyDelta(float v)  => PrettyDelta((double)v);
        public static string PrettyDelta(double v)
        {
            string s = PrettySmall(v);
            if (s != "0" && !s.StartsWith("-")) s = "+" + s;
            return s;
        }

        // World + sim
        WorldState world;
        float acc;

        // Overlays
        bool overlayOpen = false;
        bool guideOpen = false;
        bool gameOver = false;
        string gameOverReason = "";

        // Settings tabs & scroll per-tab
        int settingsTab = 0;
        readonly string[] settingsTabs = { "Simulation", "UI", "Bars", "World", "Session" };
        Vector2 simScroll, uiScroll, barsScroll, worldScroll, sessionScroll;

        // Misc overlay state
        string seedField;
        int mapChoice;

        // Which sector is expanded in the dock
        SectorType? expandedSector = null;

        // Trends
        readonly Dictionary<Resource, float> lastValue = new();
        readonly Dictionary<Resource, float> emaRate   = new(); // units/sec
        float ln2 = Mathf.Log(2f);

        // Bars & hover
        readonly Dictionary<Resource, Rect> barRects = new();
        Resource? hovered = null;

        // Dependency graph
        readonly Dictionary<Resource, HashSet<Resource>> upstreamOf = new();
        readonly Dictionary<Resource, HashSet<Resource>> downstreamOf = new();

        // Dashboard order
        readonly Resource[] order =
        {
            Resource.FOOD, Resource.ENERGY, Resource.WATER, Resource.RAW,
            Resource.GOODS, Resource.SERVICES, Resource.MONEY, Resource.WASTE,
            Resource.POLLUTION, Resource.KNOWLEDGE, Resource.LABOR, Resource.POP
        };

        // Custom color map
        readonly Dictionary<Resource, Color> customColors = new();

        // Peril timers
        float tFoodCrisis, tWaterCrisis, tEnergyCrisis, tSatCrisis;

        // Composition types
        [Serializable] class Impact { public Resource res; public float deltaPerCap; }
        [Serializable] class SubItem
        {
            public string name;
            public float weight;     // normalized sum==1
            public string hint;
            public List<Impact> impacts = new();
        }

        readonly Dictionary<Resource, SubItem[]> compositions = new();  // 5 subitems per resource
        bool compOpen = false;
        Resource compRes;
        Vector2 compScroll;

        // --- Modal guard ---
        bool ModalActive => overlayOpen || guideOpen || compOpen || tradeOpen || gameOver;

        // --- Modal snapshot / input guard ---
        Texture2D _modalSnapshot;
        Rect _activeModalRect;
        bool _modalSnapshotDirty;

        // ─────────────────────────────────────────────────────────────────────────────

        void Start()
        {
            Application.targetFrameRate = 120;
            if (Camera.main)
            {
                Camera.main.backgroundColor = new Color(0.03f, 0.04f, 0.07f);
                Camera.main.orthographic = true;
                Camera.main.orthographicSize = 7f;
            }
            InitDefaultCompositions();
            ResetWorld();
        }

        void ResetWorld()
        {
            world = Presets.NewWorld(mapSize, seed);
            acc = 0f;
            seedField = seed.ToString();
            mapChoice = (int)mapSize;

            foreach (Resource r in Enum.GetValues(typeof(Resource)))
            {
                float v = world.Get(r);
                lastValue[r] = v;
                emaRate[r] = 0f;
            }
            RebuildResourceGraphs();

            // peril reset
            tFoodCrisis = tWaterCrisis = tEnergyCrisis = tSatCrisis = 0f;
            gameOver = false; gameOverReason = "";
            tradeOpen = false;

            // default expanded sector
            expandedSector = world.sectors.Count > 0 ? world.sectors.Keys.OrderBy(k => k).First() : (SectorType?)null;

            if (customColors.Count == 0) ResetColorsToDefaults();

            compOpen = false;
        }

        void ResetColorsToDefaults()
        {
            foreach (Resource r in order) customColors[r] = DefaultColorFor(r);
        }

        void RebuildResourceGraphs()
        {
            upstreamOf.Clear(); downstreamOf.Clear();
            foreach (Resource r in Enum.GetValues(typeof(Resource)))
            {
                upstreamOf[r] = new HashSet<Resource>();
                downstreamOf[r] = new HashSet<Resource>();
            }

            foreach (var kv in world.sectors)
            {
                var def = kv.Value.def;
                var inputs  = def.io.Where(e => e.perTick < 0f).Select(e => e.resource).Distinct();
                var outputs = def.io.Where(e => e.perTick > 0f).Select(e => e.resource).Distinct();

                foreach (var o in outputs)
                    foreach (var i in inputs) upstreamOf[o].Add(i);

                foreach (var i in inputs)
                    foreach (var o in outputs) downstreamOf[i].Add(o);
            }
        }

        void Update()
        {
            // Escape closes the current modal if any; otherwise opens Settings
            if (EscPressed())
            {
                if (compOpen || overlayOpen || guideOpen || tradeOpen) { CloseModal(); }
                else if (!gameOver) { OpenSettings(); }
            }

            // Help toggles guide via modal helpers
            if (HelpPressed())
            {
                if (guideOpen) CloseModal();
                else OpenGuide();
            }

            if (gameOver) return;

            if (SpacePressed() && !ModalActive) pause = !pause;
            if (FasterPressed() && !ModalActive) timeScale *= 1.25f;
            if (SlowerPressed() && !ModalActive) timeScale = Mathf.Max(0.25f, timeScale/1.25f);

            if (pause || ModalActive) return;

            float simAdvanced = 0f;
            acc += Time.deltaTime * Mathf.Max(0.01f, timeScale);
            float step = 1f / Mathf.Max(0.01f, ticksPerSecond);
            while (acc >= step)
            {
                Sim.Tick(world, step);
                ApplyMapLimits(world);
                acc -= step;
                simAdvanced += step;
            }
            if (simAdvanced > 0f)
            {
                ApplyCompositionExternalities(world, simAdvanced);
                UpdateTrends(simAdvanced);
                CheckPeril(simAdvanced);
            }
        }

        // map-size limits
        void ApplyMapLimits(WorldState w)
        {
            float pop = Mathf.Max(1f, w.population);
            (float raw, float water, float energy, float food) caps = mapSize switch
            {
                MapSize.City    => (0.0004f, 0.0006f, 0.0005f, 0.0005f),
                MapSize.Country => (0.0008f, 0.0009f, 0.0009f, 0.0008f),
                _               => (0.0016f, 0.0016f, 0.0016f, 0.0014f),
            };
            ClampStock(w, Resource.RAW,       caps.raw     * pop,    5_000f);
            ClampStock(w, Resource.WATER,     caps.water   * pop,    5_000f);
            ClampStock(w, Resource.ENERGY,    caps.energy  * pop,    5_000f);
            ClampStock(w, Resource.FOOD,      caps.food    * pop,    5_000f);
        }
        void ClampStock(WorldState w, Resource r, float max, float buffer)
        {
            float cur = w.Get(r);
            float lim = Mathf.Max(0f, max + buffer);
            if (cur > lim) w.stock[r] = lim;
        }

        void CheckPeril(float dtSim)
        {
            float wantFood   = WantRef(Resource.FOOD);
            float wantWater  = WantRef(Resource.WATER);
            float wantEnergy = WantRef(Resource.ENERGY);

            bool foodCrisis   = world.Get(Resource.FOOD)   < needCrisisFrac * wantFood;
            bool waterCrisis  = world.Get(Resource.WATER)  < needCrisisFrac * wantWater;
            bool energyCrisis = world.Get(Resource.ENERGY) < needCrisisFrac * wantEnergy;
            bool satCrisisNow = world.satisfaction < satCrisisThreshold;

            tFoodCrisis   = foodCrisis   ? tFoodCrisis   + dtSim : 0f;
            tWaterCrisis  = waterCrisis  ? tWaterCrisis  + dtSim : 0f;
            tEnergyCrisis = energyCrisis ? tEnergyCrisis + dtSim : 0f;
            tSatCrisis    = satCrisisNow ? tSatCrisis    + dtSim : 0f;

            if (tFoodCrisis > needFailSeconds)   { TriggerGameOver("Famine: food critically low for too long."); return; }
            if (tWaterCrisis > needFailSeconds)  { TriggerGameOver("Water shortage: supply critically low for too long."); return; }
            if (tEnergyCrisis > needFailSeconds) { TriggerGameOver("Blackouts: energy critically low for too long."); return; }
            if (tSatCrisis > satFailSeconds)     { TriggerGameOver("Unrest: satisfaction remained critically low."); return; }
        }

        void TriggerGameOver(string reason)
        {
            gameOver = true;
            pause = true;
            overlayOpen = false;
            guideOpen = false;
            compOpen = false;
            tradeOpen = false;
            gameOverReason = reason;

            CaptureModalSnapshotImmediate();
            _modalSnapshotDirty = true;
            StartCoroutine(CaptureModalSnapshotEndOfFrame());
        }

        void UpdateTrends(float dtSim)
        {
            float a = 1f - Mathf.Exp(-ln2 * dtSim / Mathf.Max(0.0001f, trendHalfLifeSeconds));
            foreach (Resource r in Enum.GetValues(typeof(Resource)))
            {
                float cur = world.Get(r);
                float rate = (cur - lastValue[r]) / dtSim;
                emaRate[r] = Mathf.Lerp(emaRate[r], rate, a);
                lastValue[r] = cur;
            }
        }

        void OnGUI()
        {
            try
            {
                DrawBackdrop();

                float auto = 1f;
                if (autoScale && referenceResolution.x > 0 && referenceResolution.y > 0)
                    auto = Mathf.Min(Screen.width / referenceResolution.x, Screen.height / referenceResolution.y);
                float STop = Mathf.Max(0.85f, auto * uiScale);

                float margin  = 10f * STop;
                float bottomH = Mathf.Max(Screen.height * controlBarFraction, 150f);
                float SBottom = Mathf.Clamp(bottomH / 240f, 0.9f, 1.6f);
                float topH    = Screen.height - bottomH - margin * 3f;

                Rect top    = new Rect(margin, margin, Screen.width - 2 * margin, topH);
                Rect bottom = new Rect(margin, Screen.height - bottomH - margin, Screen.width - 2 * margin, bottomH);

                if (ModalActive)
                {
                    if (overlayOpen) { DrawOverlay(STop); return; }
                    if (guideOpen)   { DrawGuide(STop); return; }
                    if (tradeOpen)   { DrawTradeOverlay(STop); return; }
                    if (compOpen)    { DrawCompositionPopup(STop); return; }
                    if (gameOver)    { DrawGameOver(STop); return; }
                }

                DrawTopDashboard(top, STop);
                DrawBottomControls(bottom, SBottom);

                GUI.Label(new Rect(margin, Screen.height - 22f, Screen.width - 2 * margin, 20f),
                    $"Map: {mapSize} | Ticks/s: {ticksPerSecond:F1} | TimeScale: {timeScale:F2} | " +
                    "Pause:[Space]  Slower[, ] Faster[. ]  Settings:[Esc]  Guide:[H]  Legends:[Alt]",
                    LabelStyle(12, 1f, new Color(0.8f, 0.9f, 1f, 0.9f)));
            }
            catch (Exception ex)
            {
                GUI.Label(new Rect(10, 10, 1200, 24), "GUI error: " + ex.Message);
            }
        }

        // ---------- TOP: dashboard ----------
        void DrawTopDashboard(Rect r, float S)
        {
            FillAbs(r, new Color(0.06f, 0.07f, 0.11f, 1f));
            FrameAbs(r, new Color(0.2f, 0.4f, 0.8f, 0.25f), 1f);
            FillAbs(new Rect(r.x, r.y, r.width, Mathf.Round(3f * S)), new Color(0.2f, 0.7f, 1f, 0.35f));

            float pad = 12f * S;
            float x = r.x + pad, y = r.y + pad, w = r.width - pad * 2f, h = r.height - pad * 2f;

            GUI.Label(new Rect(x, y, w * 0.55f, Mathf.Round(24f * S)),
                $"CALCULATORIO — {mapSize}    POP: {PrettySmart(world.population)}    Sat: {world.satisfaction:F1}    PriceIdx: {world.priceLevel:F2}",
                LabelStyle(18, S, new Color(0.85f, 0.95f, 1f, 0.95f)));

            float btnW = 110f * S, btnH = 24f * S;
            if (!world.trade.enabled) GUI.enabled = false;
            if (GUI.Button(new Rect(x + w - btnW, y, btnW, btnH), "⚖ Trade", ButtonStyle(Mathf.RoundToInt(13 * S))))
                OpenTrade();
            GUI.enabled = true;

            y += Mathf.Round(30f * S);

            barRects.Clear();
            hovered = null;

            int n = order.Length;
            float minW = Mathf.Max(120f, minBarWidthPx) * S;
            float colGap = Mathf.Max(6f, colGapPx) * S;
            float rowGap = Mathf.Max(8f, rowGapPx) * S;

            int cols = Mathf.Clamp(Mathf.FloorToInt((w + colGap) / (minW + colGap)), 1, n);
            int rows = Mathf.CeilToInt(n / (float)cols);

            float colW = (w - colGap * (cols - 1)) / cols;
            float barH = (h - rowGap * (rows - 1)) / rows;
            if (barH < 32f * S) barH = 32f * S;

            for (int i = 0; i < n; i++)
            {
                int cx = i % cols, cy = i / cols;
                float bx = x + cx * (colW + colGap);
                float by = y + cy * (barH + rowGap);
                barRects[order[i]] = new Rect(Mathf.Round(bx), Mathf.Round(by), Mathf.Round(colW), Mathf.Round(barH));
            }

            Vector2 mp = Event.current.mousePosition;
            foreach (var kv in barRects) { if (kv.Value.Contains(mp)) { hovered = kv.Key; break; } }

            foreach (var res in order)
            {
                Rect barR = barRects[res];
                float v = world.Get(res);
                bool doDim = (hovered.HasValue && hovered.Value != res && highlightLinks);
                float alphaMul = doDim ? (1f - dimOthers) : 1f;

                if (GUI.Button(new Rect(barR.x, barR.y, barR.width, barR.height), GUIContent.none, GUIStyle.none))
                    if (enableCompositionPopup) OpenComposition(res);

                DrawBarBox(barR, res, v, S, alphaMul);
            }

            if (drawLinkLines && hovered.HasValue) DrawConnectionsWaterfall(hovered.Value, S);

            if (alwaysShowCenterLegends || AltHeld())
            {
                foreach (var res in order)
                {
                    Rect barR = barRects[res];
                    float v = world.Get(res);
                    bool doDim = (hovered.HasValue && hovered.Value != res && highlightLinks);
                    float alphaMul = doDim ? (1f - dimOthers) : 1f;
                    DrawBarCenterLegend(barR, res, v, S, alphaMul);
                }
            }

            if (hovered.HasValue) DrawHoverTooltip(hovered.Value, Event.current.mousePosition, S);
        }

        // VERTICAL bars (bottom → top fill)
        void DrawBarBox(Rect bar, Resource res, float value, float S, float alphaMul)
        {
            GUI.BeginGroup(bar);
            Rect R = new Rect(0, 0, bar.width, bar.height);

            float norm = Mathf.Clamp01(Normalize(res, value, world.population));

            FillLocal(R, new Color(1,1,1,0.05f * alphaMul));
            FrameLocal(R, new Color(0.5f,0.8f,1f,0.10f * alphaMul), 1f);

            float tw = Mathf.Max(2f, tickerWidthPx * S);
            Rect tickArea = new Rect(R.width - tw - 1f, 0f, tw, R.height);
            FillLocal(tickArea, new Color(1,1,1,0.06f * alphaMul));
            FillLocal(new Rect(tickArea.x, tickArea.y + tickArea.height/2f, tickArea.width, 1f),
                      new Color(1,1,1,0.25f * alphaMul));

            float rightPad = tw + 3f * S;
            Rect content = new Rect(1f, 1f, R.width - rightPad - 2f, R.height - 2f);

            float fillH = Mathf.Max(2f, content.height * norm);
            Rect fill = new Rect(content.x, content.y + (content.height - fillH), content.width, fillH);
            var col = ColorForRes(res); col.a *= alphaMul;
            FillLocal(fill, col);

            if (HasPerCapNeed(res))
                FillLocal(new Rect(content.x, content.y + content.height * 0.5f, content.width, 1f), new Color(1,1,1,0.18f * alphaMul));

            float rate = emaRate.TryGetValue(res, out var r) ? r : 0f;
            float tnorm = TrendNormalized(res, rate);
            float len   = Mathf.Clamp01(Math.Abs(tnorm)) * (tickArea.height / 2f);
            if (tnorm > 0f)
                FillLocal(new Rect(tickArea.x, tickArea.y + tickArea.height/2f - len, tw, len),
                          new Color(0.2f, 0.95f, 0.4f, 0.85f * alphaMul));
            else if (tnorm < 0f)
                FillLocal(new Rect(tickArea.x, tickArea.y + tickArea.height/2f, tw, len),
                          new Color(1.0f, 0.35f, 0.35f, 0.85f * alphaMul));

            if (Mathf.Abs(tnorm) > arrowShowThreshold)
            {
                string arrow = tnorm > 0 ? "▲" : "▼";
                var c = tnorm > 0 ? new Color(0.6f,1f,0.6f,0.95f * alphaMul)
                                  : new Color(1f,0.6f,0.6f,0.95f * alphaMul);
                var aStyle = LabelStyle(12, S, c);
                GUI.Label(new Rect(R.width - (18f * S), 2f * S, 16f * S, 16f * S), arrow, aStyle);
            }

            GUI.EndGroup();
        }

        void DrawBarCenterLegend(Rect bar, Resource res, float value, float S, float alphaMul)
        {
            GUI.BeginGroup(bar);
            Rect R = new Rect(0, 0, bar.width, bar.height);

            float rate = emaRate.TryGetValue(res, out var r) ? r : 0f;

            string name  = res.ToString();
            string tick  = TickerString(res, rate);
            string stock = $"Stock: {PrettySmart(value)} | Δ/min: {PrettySmall(rate * 60f)}";
            string tail  = "";
            float need = WantRef(res);
            if (HasPerCapNeed(res) && need > 0f)
            {
                float cov = value / Mathf.Max(1f, need);
                tail = $"Need: {PrettySmart(need)} | Cov: {cov:0.00}×";
            }
            string info = string.IsNullOrEmpty(tail)
                        ? $"{name}\n{tick}\n{stock}"
                        : $"{name}\n{tick}\n{stock}\n{tail}";

            var styleInfo = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                fontSize = Mathf.RoundToInt(12 * S),
                normal = { textColor = new Color(0.90f,0.98f,1f,0.95f * alphaMul) }
            };
            float tw = Mathf.Max(2f, tickerWidthPx * S);
            Rect textArea = new Rect(4f * S, 4f * S, R.width - (tw + 8f * S), R.height - 8f * S);
            ShadowLabel(textArea, info, styleInfo, 1.25f);

            GUI.EndGroup();
        }

        void DrawHoverTooltip(Resource res, Vector2 mouse, float S)
        {
            float value = world.Get(res);
            float rate = emaRate.TryGetValue(res, out var r) ? r : 0f;

            string name  = res.ToString();
            string tick  = TickerString(res, rate);
            string stock = $"Stock: {PrettySmart(value)}   Δ/min: {PrettySmall(rate * 60f)}";
            string tail  = "";
            float need = WantRef(res);
            if (HasPerCapNeed(res) && need > 0f)
            {
                float cov = value / Mathf.Max(1f, need);
                tail = $"Need: {PrettySmart(need)}   Coverage: {cov:0.00}×";
            }
            string txt = string.IsNullOrEmpty(tail) ? $"{name}\n{tick}\n{stock}" : $"{name}\n{tick}\n{stock}\n{tail}";

            var st = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                fontSize = Mathf.RoundToInt(13 * S),
                normal = { textColor = new Color(0.95f,0.99f,1f,0.98f) },
                padding = new RectOffset(8,8,6,6)
            };

            float maxW = 360f * S;
            float height = st.CalcHeight(new GUIContent(txt), maxW);
            Rect box = new Rect(mouse.x + 16f * S, mouse.y + 16f * S, maxW, height);
            if (box.xMax > Screen.width - 8f) box.x = Screen.width - 8f - box.width;
            if (box.yMax > Screen.height - 8f) box.y = Screen.height - 8f - box.height;

            FillAbs(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), new Color(0,0,0,0.45f));
            FrameAbs(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), new Color(0.7f,0.9f,1f,0.35f), 1f);

            GUI.Label(box, txt, st);
        }

        void DrawConnectionsWaterfall(Resource center, float S)
        {
            Color inCol  = new Color(0.35f, 0.85f, 1f, 0.85f);
            Color outCol = new Color(1.00f, 0.70f, 0.25f, 0.85f);
            float t = 2.5f * S;
            float notch = 8f * S;

            Rect cr = barRects[center];
            Vector2 cTop = new Vector2(cr.center.x, cr.y + 6f * S);

            foreach (var u in upstreamOf[center])
            {
                if (!barRects.TryGetValue(u, out var ur)) continue;
                Vector2 uTop = new Vector2(ur.center.x, ur.y + 6f * S);
                float y = Mathf.Min(uTop.y, cTop.y) - 6f * S;

                FillAbs(new Rect(uTop.x - t/2f, Mathf.Min(uTop.y, y), t, Mathf.Abs(y - uTop.y)), inCol);
                FillAbs(new Rect(Mathf.Min(uTop.x, cTop.x), y - t/2f, Mathf.Abs(cTop.x - uTop.x), t), inCol);
                FillAbs(new Rect(cTop.x - t/2f, y, t, notch), inCol);
            }

            foreach (var d in downstreamOf[center])
            {
                if (!barRects.TryGetValue(d, out var dr)) continue;
                Vector2 dTop = new Vector2(dr.center.x, dr.y + 6f * S);
                float y = Mathf.Min(dTop.y, cTop.y) - 6f * S;

                FillAbs(new Rect(cTop.x - t/2f, cTop.y, t, Mathf.Abs(y - cTop.y)), outCol);
                FillAbs(new Rect(Mathf.Min(cTop.x, dTop.x), y - t/2f, Mathf.Abs(dTop.x - cTop.x), t), outCol);
                FillAbs(new Rect(dTop.x - t/2f, y, t, notch), outCol);
            }
        }

        // ---------- Bottom controls ----------
        void DrawBottomControls(Rect r, float S)
        {
            FillAbs(r, new Color(0.05f,0.06f,0.10f,1f));
            FrameAbs(r, new Color(0.2f,0.4f,0.8f,0.25f), 1f);

            float pad = 10f * S;
            float x = r.x + pad, y = r.y + pad, w = r.width - pad*2f, h = r.height - pad*2f;

            float gap  = 8f * S;
            float btnH = Mathf.Clamp(28f * S, 24f, Mathf.Min(48f, h * 0.35f));
            float iconW = Mathf.Max(44f * S, 44f);

            float labelW = Mathf.Clamp(w * 0.45f, 260f, w * 0.60f);
            GUI.Label(new Rect(x, y, labelW, btnH),
                $"POP: {PrettySmart(world.population)}   Sat: {world.satisfaction:F1}   PriceIdx: {world.priceLevel:F2}",
                LabelStyle(Mathf.RoundToInt(16 * S), 1f, new Color(0.85f,0.95f,1f,0.95f)));

            float bx = x + labelW + gap;
            if (GUI.Button(new Rect(bx, y, iconW, btnH), "⏯")) { pause = !pause; }                      bx += iconW + gap;
            if (GUI.Button(new Rect(bx, y, iconW, btnH), "↺"))  { ResetWorld(); }                        bx += iconW + gap;
            if (GUI.Button(new Rect(bx, y, iconW, btnH), "⏪"))  { timeScale = Mathf.Max(0.25f, timeScale/1.25f); } bx += iconW + gap;
            if (GUI.Button(new Rect(bx, y, iconW, btnH), "⏩"))  { timeScale *= 1.25f; }                  bx += iconW + gap;

            if (GUI.Button(new Rect(bx, y, iconW, btnH), "⚙"))  { OpenSettings(); } bx += iconW + gap;
            if (GUI.Button(new Rect(bx, y, iconW, btnH), "❓"))  { OpenGuide();    } bx += iconW + gap;

            y += btnH + gap;

            float pillsH = Mathf.Clamp(36f * S, 30f, Mathf.Min(56f, h * 0.30f));
            DrawSectorGrid(new Rect(x, y, w, pillsH), S);
            y += pillsH + gap;

            float editorH = h - (btnH + gap + pillsH + gap);
            DrawSectorExpanded(new Rect(x, y, w, editorH), S);
        }

        void DrawSectorGrid(Rect r, float S)
        {
            var ordered = world.sectors.Keys.OrderBy(k => k).ToArray();
            if (ordered.Length == 0) return;

            float gap = 8f * S;
            float minCellW = 90f * S;
            float minCellH = Mathf.Clamp(32f * S, 28f, 56f);

            int cols = Mathf.Clamp(Mathf.FloorToInt((r.width + gap) / (minCellW + gap)), 1, ordered.Length);
            int rows = Mathf.CeilToInt(ordered.Length / (float)cols);

            float cellW = (r.width - gap * (cols - 1)) / cols;
            float cellH = Mathf.Min(r.height, (r.height - gap * (rows - 1)) / Mathf.Max(1, rows));
            cellH = Mathf.Max(cellH, minCellH);

            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(14 * S),
                alignment = TextAnchor.MiddleCenter
            };

            for (int i = 0; i < ordered.Length; i++)
            {
                int cx = i % cols;
                int cy = i / cols;

                float x = r.x + cx * (cellW + gap);
                float y = r.y + cy * (cellH + gap);
                var pill = new Rect(x, y, cellW, cellH);

                var s = ordered[i];
                string label = Short(s);

                var oldBG = GUI.backgroundColor;
                if (expandedSector.HasValue && expandedSector.Value.Equals(s))
                    GUI.backgroundColor = new Color(0.30f, 0.55f, 1f, 0.90f);

                if (GUI.Button(pill, label, style))
                    expandedSector = s;

                GUI.backgroundColor = oldBG;
            }
        }

        string Short(SectorType t) => t switch
        {
            SectorType.Raw         => "RAW",
            SectorType.Energy      => "PWR",
            SectorType.Agriculture => "AGRI",
            SectorType.Industry    => "IND",
            SectorType.Housing     => "HOME",
            SectorType.Education   => "EDU",
            SectorType.Health      => "HLTH",
            SectorType.Transport   => "TRNS",
            SectorType.Utilities   => "UTIL",
            SectorType.Security    => "SEC",
            SectorType.Environment => "ENV",
            SectorType.Leisure     => "LSR",
            SectorType.Government  => "GOV",
            _ => t.ToString().ToUpperInvariant()
        };

        void DrawSectorExpanded(Rect r, float S)
        {
            FillAbs(r, new Color(0.08f,0.09f,0.13f,1f));
            FrameAbs(r, new Color(0.2f,0.4f,0.8f,0.25f), 1f);

            if (!expandedSector.HasValue || !world.sectors.ContainsKey(expandedSector.Value))
            {
                GUI.Label(new Rect(r.x + 10f, r.y + 8f, r.width - 20f, 24f * S),
                    "Select a sector above to edit.", LabelStyle(Mathf.RoundToInt(14 * S), 1f));
                return;
            }

            var st = world.sectors[expandedSector.Value];
            float pad = 10f * S;
            float x = r.x + pad, y = r.y + pad, w = r.width - pad*2f;

            GUI.Label(new Rect(x, y, w * 0.65f, 26f * S), $"{st.def.name}", LabelStyle(Mathf.RoundToInt(18 * S), 1f, new Color(0.9f,0.95f,1f,1f)));
            if (GUI.Button(new Rect(x + w - 110f * S, y, 110f * S, 24f * S), "Reset 1.0", ButtonStyle(Mathf.RoundToInt(13 * S))))
                st.intensity = 1f;
            y += 30f * S;

            float sliderH = 18f * S;
            Rect intensityRect = new Rect(x, y, w * 0.75f, sliderH);
            DrawSliderContainerAbs(intensityRect, S);
            st.intensity = GUI.HorizontalSlider(intensityRect, st.intensity, 0f, 2f);
            GUI.Label(new Rect(x + w * 0.77f, y - 2f * S, w * 0.2f, sliderH + 4f * S),
                $"Intensity: {st.intensity:0.00}×", LabelStyle(Mathf.RoundToInt(14 * S), 1f));
            y += sliderH + 10f * S;

            GUI.Label(new Rect(x, y, w, 20f * S), "I/O (approx. per tick @ current intensity):", LabelStyle(Mathf.RoundToInt(14 * S), 1f));
            y += 22f * S;

            float colW = w * 0.48f;
            float line = 18f * S;

            var inputs  = st.def.io.Where(e => e.perTick < 0f).ToArray();
            var outputs = st.def.io.Where(e => e.perTick > 0f).ToArray();
            int rows = Math.Max(inputs.Length, outputs.Length);
            for (int i = 0; i < rows; i++)
            {
                if (i < inputs.Length)
                {
                    var e = inputs[i];
                    GUI.Label(new Rect(x, y, colW, line),
                        $"{e.resource,-10}  {PrettySmall(-e.perTick * st.intensity)}", LabelStyle(Mathf.RoundToInt(12 * S), 1f));
                }
                if (i < outputs.Length)
                {
                    var e = outputs[i];
                    GUI.Label(new Rect(x + colW + 20f * S, y, colW, line),
                        $"{e.resource,-10}  {PrettySmall(e.perTick * st.intensity)}", LabelStyle(Mathf.RoundToInt(12 * S), 1f));
                }
                y += line;
            }
        }

        // ---------- NEW: Trade Overlay ----------
        void DrawTradeOverlay(float S)
        {
            float pw = Mathf.Min(900f * S, Screen.width - 80f * S);
            float ph = Mathf.Min(0.70f * Screen.height, Screen.height - 100f * S);
            float px = Mathf.Round((Screen.width  - pw) * 0.5f);
            float py = Mathf.Round((Screen.height - ph) * 0.5f);
            _activeModalRect = new Rect(px, py, Mathf.Round(pw), Mathf.Round(ph));

            DrawModalBackdropAndConsume(_activeModalRect);

            Rect panel = _activeModalRect;
            FillAbs(panel, new Color(0.10f, 0.12f, 0.16f, 1f));
            FrameAbs(panel, new Color(1,1,1,0.2f), 1f);

            float pad = 14f * S;
            float x = panel.x + pad, y = panel.y + pad, w = panel.width - pad*2f, h = panel.height - pad*2f;

            GUI.Label(new Rect(x, y, w * 0.7f, 30f * S), "Trade — Imports & Exports", LabelStyle(22, S));
            if (GUI.Button(new Rect(x + w - 120f * S, y, 120f * S, 28f * S), "Close", ButtonStyle(14))) { CloseModal(); return; }
            y += 36f * S;

            if (!world.trade.enabled)
            {
                GUI.Label(new Rect(x, y, w, 24f * S), "Trade is disabled for this scenario.", LabelStyle(16, S));
                return;
            }

            Rect view = new Rect(x, y, w, h - 10f * S);
            float contentH = 420f * S;
            Rect content = new Rect(0, 0, w - 16f * S, contentH);
            var scroll = Vector2.zero;
            scroll = GUI.BeginScrollView(view, scroll, content);

            float cy = 0f;
            GUI.Label(new Rect(0, cy, w, 20f * S), "Set preferences (0..1). Higher import preference pulls more of that resource; export preference pushes surplus.", LabelStyle(13, S)); cy += 26f * S;

            float colW = (w - 10f * S) / 2f;
            DrawTradeGroupLocal(view, ref scroll, new Rect(0, cy, colW, 300f * S), true,  new[] { Resource.FOOD, Resource.ENERGY, Resource.WATER, Resource.RAW }, S);
            DrawTradeGroupLocal(view, ref scroll, new Rect(colW + 10f * S, cy, colW, 300f * S), false, new[] { Resource.GOODS, Resource.SERVICES }, S);

            GUI.EndScrollView();
        }

        void DrawTradeGroupLocal(Rect view, ref Vector2 scroll, Rect r, bool isImport, Resource[] list, float S)
        {
            float pad = 8f * S;
            float y = r.y + pad;
            float x = r.x + pad;
            float w = r.width - pad*2f;

            string title = isImport ? "Imports (0..1 preference)" : "Exports (0..1 preference)";
            GUI.Label(new Rect(x, y, w, 20f * S), title, LabelStyle(Mathf.RoundToInt(14 * S), 1f));
            y += 22f * S;

            float line = 24f * S;
            foreach (var res in list)
            {
                GUI.Label(new Rect(x, y, w * 0.35f, line), res.ToString(), LabelStyle(Mathf.RoundToInt(13 * S), 1f));
                float v = isImport ? world.trade.importPref[res] : world.trade.exportPref[res];

                Rect track = new Rect(x + w * 0.36f, y + (line - 16f * S) / 2f, w * 0.44f, 16f * S);
                DrawSliderContainerInScroll(view, scroll, track, S);
                v = GUI.HorizontalSlider(track, v, 0f, 1f);

                if (isImport) world.trade.importPref[res] = v; else world.trade.exportPref[res] = v;
                GUI.Label(new Rect(x + w * 0.82f, y, w * 0.18f, line), $"{v:0.00}", LabelStyle(Mathf.RoundToInt(13 * S), 1f));
                y += line;
            }
        }

        // ---------- ESC OVERLAY (Tabbed Settings) ----------
        void DrawOverlay(float S)
        {
            float pw = Mathf.Min(1120f * S, Screen.width - 80f * S);
            float ph = Mathf.Min(Screen.height * 0.90f, Screen.height - 60f * S);
            _activeModalRect = new Rect(Mathf.Round((Screen.width - pw)/2f), Mathf.Round((Screen.height - ph)/2f), Mathf.Round(pw), Mathf.Round(ph));

            DrawModalBackdropAndConsume(_activeModalRect);

            Rect panel = _activeModalRect;
            FillAbs(panel, new Color(0.10f, 0.12f, 0.16f, 1f));
            FrameAbs(panel, new Color(1,1,1,0.2f), 1f);

            float pad = 14f * S;
            float x = panel.x + pad, y = panel.y + pad, w = panel.width - pad*2f, h = panel.height - pad*2f;

            GUI.Label(new Rect(x, y, w * 0.6f, 30f * S), "Settings", LabelStyle(22, S));
            if (GUI.Button(new Rect(x + w - 130f * S, y, 130f * S, 30f * S), "Close (Esc)", ButtonStyle(14))) { CloseModal(); return; }
            y += 36f * S;

            float tabH = 28f * S;
            settingsTab = GUI.SelectionGrid(new Rect(x, y, w, tabH), settingsTab, settingsTabs, settingsTabs.Length);
            y += tabH + 8f * S;

            Rect view = new Rect(x, y, w, h - (y - (panel.y + pad)));
            float contentWidth = w - 16f * S;

            switch (settingsTab)
            {
                case 0: // Simulation
                {
                    float contentH = 700f * S;
                    Rect content = new Rect(0, 0, contentWidth, contentH);
                    simScroll = GUI.BeginScrollView(view, simScroll, content);

                    float cy = 0f, line = 30f * S;

                    cy = SectionHeaderLocal("Simulation", cy, S, contentWidth);
                    LabeledLocal(ref cy, "Paused", S, () =>
                    {
                        bool p = GUI.Toggle(RowRectA_Local(cy, S), pause, "");
                        if (p != pause) pause = p;
                    }, line, contentWidth);

                    LabeledLocal(ref cy, "Ticks Per Second", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, simScroll, track, S);
                        ticksPerSecond = GUI.HorizontalSlider(track, ticksPerSecond, 0.25f, 30f);
                        GUI.Label(RowRectC_Local(cy, S), $"{ticksPerSecond:0.00}", LabelStyle(14, S));
                    }, line, contentWidth);

                    LabeledLocal(ref cy, "Time Scale", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, simScroll, track, S);
                        timeScale = GUI.HorizontalSlider(track, timeScale, 0.25f, 4f);
                        GUI.Label(RowRectC_Local(cy, S), $"{timeScale:0.00}", LabelStyle(14, S));
                    }, line, contentWidth);

                    cy += 10f * S;
                    cy = SectionHeaderLocal("Peril", cy, S, contentWidth);
                    LabeledLocal(ref cy, "Need Crisis Threshold", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, simScroll, track, S);
                        needCrisisFrac = GUI.HorizontalSlider(track, needCrisisFrac, 0.05f, 0.6f);
                        GUI.Label(RowRectC_Local(cy, S), $"{needCrisisFrac*100f:0}%", LabelStyle(14, S));
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Need Fail (seconds)", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, simScroll, track, S);
                        needFailSeconds = GUI.HorizontalSlider(track, needFailSeconds, 5f, 120f);
                        GUI.Label(RowRectC_Local(cy, S), $"{needFailSeconds:0}", LabelStyle(14, S));
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Sat Crisis Threshold", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, simScroll, track, S);
                        satCrisisThreshold = GUI.HorizontalSlider(track, satCrisisThreshold, 5f, 60f);
                        GUI.Label(RowRectC_Local(cy, S), $"{satCrisisThreshold:0}", LabelStyle(14, S));
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Sat Fail (seconds)", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, simScroll, track, S);
                        satFailSeconds = GUI.HorizontalSlider(track, satFailSeconds, 10f, 180f);
                        GUI.Label(RowRectC_Local(cy, S), $"{satFailSeconds:0}", LabelStyle(14, S));
                    }, line, contentWidth);

                    GUI.EndScrollView();
                    break;
                }
                case 1: // UI
                {
                    float contentH = 900f * S;
                    Rect content = new Rect(0, 0, contentWidth, contentH);
                    uiScroll = GUI.BeginScrollView(view, uiScroll, content);

                    float cy = 0f, line = 30f * S;

                    cy = SectionHeaderLocal("UI & Layout", cy, S, contentWidth);
                    LabeledLocal(ref cy, "Dock Height (fraction)", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, uiScroll, track, S);
                        controlBarFraction = GUI.HorizontalSlider(track, controlBarFraction, 0.06f, 0.35f);
                        GUI.Label(RowRectC_Local(cy, S), $"{controlBarFraction:0.000}", LabelStyle(14, S));
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "UI Scale (Bars only)", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, uiScroll, track, S);
                        uiScale = GUI.HorizontalSlider(track, uiScale, 0.8f, 2.5f);
                        GUI.Label(RowRectC_Local(cy, S), $"{uiScale:0.00}", LabelStyle(14, S));
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Auto Scale (by resolution)", S, () =>
                    {
                        autoScale = GUI.Toggle(RowRectB_Local(cy, S), autoScale, "");
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Min Bar Width (px)", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, uiScroll, track, S);
                        minBarWidthPx = GUI.HorizontalSlider(track, minBarWidthPx, 100f, 360f);
                        GUI.Label(RowRectC_Local(cy, S), $"{minBarWidthPx:0}", LabelStyle(14, S));
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Bar Column Gap (px)", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, uiScroll, track, S);
                        colGapPx = GUI.HorizontalSlider(track, colGapPx, 0f, 40f);
                        GUI.Label(RowRectC_Local(cy, S), $"{colGapPx:0}", LabelStyle(14, S));
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Bar Row Gap (px)", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, uiScroll, track, S);
                        rowGapPx = GUI.HorizontalSlider(track, rowGapPx, 0f, 40f);
                        GUI.Label(RowRectC_Local(cy, S), $"{rowGapPx:0}", LabelStyle(14, S));
                    }, line, contentWidth);

                    cy += 10f * S;
                    cy = SectionHeaderLocal("Legends & Relations", cy, S, contentWidth);
                    LabeledLocal(ref cy, "Always Show Center Legends", S, () =>
                    {
                        alwaysShowCenterLegends = GUI.Toggle(RowRectB_Local(cy, S), alwaysShowCenterLegends, "");
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Highlight Links", S, () =>
                    {
                        highlightLinks = GUI.Toggle(RowRectB_Local(cy, S), highlightLinks, "");
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Draw Link Lines", S, () =>
                    {
                        drawLinkLines = GUI.Toggle(RowRectB_Local(cy, S), drawLinkLines, "");
                    }, line, contentWidth);
                    LabeledLocal(ref cy, "Dim Non-related", S, () =>
                    {
                        Rect track = RowRectB_Local(cy, S);
                        DrawSliderContainerInScroll(view, uiScroll, track, S);
                        dimOthers = GUI.HorizontalSlider(track, dimOthers, 0.15f, 0.85f);
                        GUI.Label(RowRectC_Local(cy, S), $"{dimOthers:0.00}", LabelStyle(14, S));
                    }, line, contentWidth);

                    GUI.EndScrollView();
                    break;
                }
                case 2: // Bars (Colors)
                {
                    float contentH = 2000f * S;
                    Rect content = new Rect(0, 0, contentWidth, contentH);
                    barsScroll = GUI.BeginScrollView(view, barsScroll, content);

                    float cy = 0f, line = 30f * S;
                    cy = SectionHeaderLocal("Bars — Colors", cy, S, contentWidth);

                    if (allowColorCustomization)
                    {
                        LabeledLocal(ref cy, "Use Custom Colors", S, () =>
                        {
                            useCustomBarColors = GUI.Toggle(RowRectB_Local(cy, S), useCustomBarColors, "");
                        }, line, contentWidth);

                        if (GUI.Button(new Rect(0, cy, 180f * S, line), "Reset to Defaults", ButtonStyle(14)))
                            ResetColorsToDefaults();
                        cy += line + 8f * S;

                        float rowH = 24f * S;
                        foreach (var res in order)
                        {
                            GUI.Label(new Rect(0, cy, 120f * S, rowH), res.ToString(), LabelStyle(14, S));
                            var col = customColors[res];
                            var previewR = new Rect(140f * S, cy + 2f * S, 32f * S, rowH - 4f * S);
                            FillAbs(ToScreenRectInScroll(view, previewR, barsScroll), col);
                            FrameAbs(ToScreenRectInScroll(view, previewR, barsScroll), new Color(1,1,1,0.25f), 1f);

                            float sx = 190f * S;
                            float sw = 150f * S;
                            float gap = 18f * S;

                            Rect rR = new Rect(sx, cy + (rowH-16f*S)/2f, sw, 16f * S);
                            Rect gR = new Rect(sx + sw + gap, cy + (rowH-16f*S)/2f, sw, 16f * S);
                            Rect bR = new Rect(sx + sw + gap + sw + gap, cy + (rowH-16f*S)/2f, sw, 16f * S);

                            DrawSliderContainerInScroll(view, barsScroll, rR, S);
                            DrawSliderContainerInScroll(view, barsScroll, gR, S);
                            DrawSliderContainerInScroll(view, barsScroll, bR, S);

                            col.r = GUI.HorizontalSlider(rR, col.r, 0f, 1f);
                            GUI.Label(new Rect(sx + sw + 4f*S, cy, 20f * S, rowH), "R", LabelStyle(12, S));

                            col.g = GUI.HorizontalSlider(gR, col.g, 0f, 1f);
                            GUI.Label(new Rect(sx + sw + gap + sw + 4f*S, cy, 20f * S, rowH), "G", LabelStyle(12, S));

                            col.b = GUI.HorizontalSlider(bR, col.b, 0f, 1f);
                            GUI.Label(new Rect(sx + sw + gap + sw + gap + sw + 4f*S, cy, 20f * S, rowH), "B", LabelStyle(12, S));

                            col.a = 0.95f;
                            customColors[res] = col;

                            cy += rowH + 8f * S;
                        }
                        cy += 10f * S;
                    }
                    else
                    {
                        GUI.Label(new Rect(0, cy, contentWidth, line), "Color customization disabled.", LabelStyle(14, S));
                        cy += line;
                    }

                    GUI.EndScrollView();
                    break;
                }
                case 3: // World
                {
                    float contentH = 700f * S;
                    Rect content = new Rect(0, 0, contentWidth, contentH);
                    worldScroll = GUI.BeginScrollView(view, worldScroll, content);

                    float cy = 0f, line = 30f * S;

                    cy = SectionHeaderLocal("World", cy, S, contentWidth);
                    GUI.Label(new Rect(0, cy, 160f * S, line), "Map Size:", LabelStyle(14, S));
                    string[] sizes = { "City", "Country", "Planet" };
                    mapChoice = GUI.SelectionGrid(new Rect(180f * S, cy, 380f * S, line * 1.2f), mapChoice, sizes, 3);
                    cy += line + 8f * S;

                    GUI.Label(new Rect(0, cy, 160f * S, line), "Seed:", LabelStyle(14, S));
                    seedField = GUI.TextField(new Rect(180f * S, cy, 220f * S, line), seedField);
                    if (GUI.Button(new Rect(405f * S, cy, 100f * S, line), "Random", ButtonStyle(14)))
                    {
                        seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
                        seedField = seed.ToString();
                    }
                    if (GUI.Button(new Rect(510f * S, cy, 200f * S, line), "Rebuild World", ButtonStyle(14)))
                    {
                        mapSize = (MapSize)mapChoice;
                        if (int.TryParse(seedField, out var parsed)) seed = parsed;
                        ResetWorld();
                    }
                    cy += line + 16f * S;

                    GUI.Label(new Rect(0, cy, 200f * S, line), "Enable Trade", LabelStyle(14, S));
                    world.trade.enabled = GUI.Toggle(new Rect(180f * S, cy + 3f * S, 24f * S, 24f * S), world.trade.enabled, "");
                    cy += line;

                    GUI.EndScrollView();
                    break;
                }
                case 4: // Session
                {
                    float contentH = 400f * S;
                    Rect content = new Rect(0, 0, contentWidth, contentH);
                    sessionScroll = GUI.BeginScrollView(view, sessionScroll, content);

                    float cy = 0f, line = 30f * S;

                    cy = SectionHeaderLocal("Session", cy, S, contentWidth);
                    if (GUI.Button(new Rect(0, cy, 180f * S, line), pause ? "Resume" : "Pause", ButtonStyle(14))) pause = !pause;
                    if (GUI.Button(new Rect(190f * S, cy, 180f * S, line), "Reset World", ButtonStyle(14))) ResetWorld();
                    if (GUI.Button(new Rect(380f * S, cy, 180f * S, line), "Main Menu", ButtonStyle(14))) TryLoad("MainMenu");
                    if (GUI.Button(new Rect(570f * S, cy, 200f * S, line), "Close Settings", ButtonStyle(14))) { CloseModal(); return; }

                    GUI.EndScrollView();
                    break;
                }
            }
        }

        // ---------- Composition Popup (centered & modal) ----------
        void DrawCompositionPopup(float S)
        {
            float pw = Mathf.Min(980f * S, Screen.width - 80f * S);
            float ph = Mathf.Min(0.78f * Screen.height, Screen.height - 100f * S);
            float px = Mathf.Round((Screen.width  - pw) * 0.5f);
            float py = Mathf.Round((Screen.height - ph) * 0.5f);
            _activeModalRect = new Rect(px, py, Mathf.Round(pw), Mathf.Round(ph));

            DrawModalBackdropAndConsume(_activeModalRect);

            Rect panel = _activeModalRect;
            FillAbs(panel, new Color(0.10f, 0.12f, 0.16f, 1f));
            FrameAbs(panel, new Color(1,1,1,0.2f), 1f);

            float pad = 14f * S;
            float x = panel.x + pad, y = panel.y + pad, w = panel.width - pad*2f, h = panel.height - pad*2f;

            var items = compositions[compRes];
            GUI.Label(new Rect(x, y, w * 0.7f, 30f * S), $"{compRes} — Composition", LabelStyle(22, S));
            if (GUI.Button(new Rect(x + w - 120f * S, y, 120f * S, 28f * S), "Close", ButtonStyle(14))) { CloseModal(); return; }
            y += 36f * S;

            Normalize(items);

            Rect view = new Rect(x, y, w, h - 46f * S);
            float rowH = 56f * S;
            float contentH = items.Length * (rowH + 14f * S) + 300f * S;
            Rect content = new Rect(0, 0, w - 16f * S, contentH);
            compScroll = GUI.BeginScrollView(view, compScroll, content);

            float cy = 0f;
            GUI.Label(new Rect(0, cy, w, 20f * S),
                "Redistribute the internal mix (sliders sum to 100%). Mix impacts other resources a bit each tick.",
                LabelStyle(14, S));
            cy += 24f * S;

            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];

                Rect row = new Rect(0, cy, w, rowH);
                FillAbs(ToScreenRectInScroll(view, row, compScroll), new Color(1,1,1,0.05f));
                FrameAbs(ToScreenRectInScroll(view, row, compScroll), new Color(1,1,1,0.15f), 1f);

                GUI.Label(new Rect(8f * S, cy + 4f * S, w * 0.30f, 20f * S), it.name, LabelStyle(16, S, new Color(0.9f,0.95f,1f,1f)));
                GUI.Label(new Rect(8f * S, cy + 24f * S, w * 0.60f, 18f * S), it.hint, LabelStyle(12, S, new Color(0.8f,0.9f,1f,0.9f)));

                float v = it.weight;
                Rect track = new Rect(w * 0.48f, cy + 8f * S, w * 0.42f, 16f * S);
                DrawSliderContainerInScroll(view, compScroll, track, S);
                v = GUI.HorizontalSlider(track, v, 0f, 1f);
                it.weight = v;

                GUI.Label(new Rect(w * 0.91f, cy + 4f * S, 70f * S, 20f * S), $"{it.weight*100f:0.#}%", LabelStyle(14, S));

                string impTxt = (it.impacts != null && it.impacts.Count > 0)
                    ? string.Join("  ", it.impacts.Select(k => $"{k.res}{PrettyDelta(k.deltaPerCap)}"))
                    : "no direct impacts";
                GUI.Label(new Rect(w * 0.48f, cy + 26f * S, w * 0.46f, 16f * S),
                    impTxt, LabelStyle(11, S, new Color(0.85f,0.92f,1f,0.8f)));

                items[i] = it;
                cy += rowH + 14f * S;
            }

            if (GUI.Button(new Rect(0, cy, 160f * S, 26f * S), "Reset Mix  (even)", ButtonStyle(14)))
                Even(items);
            GUI.Label(new Rect(170f * S, cy, 360f * S, 26f * S), $"Impact scale: {compositionImpactScale:0.00}  (per-cap unit {PrettySmall(compositionPerCapUnit)})", LabelStyle(12, S));

            Rect scaleTrack = new Rect(520f * S, cy + 6f * S, 220f * S, 14f * S);
            DrawSliderContainerInScroll(view, compScroll, scaleTrack, S);
            compositionImpactScale = GUI.HorizontalSlider(scaleTrack, compositionImpactScale, 0f, 2f);

            GUI.EndScrollView();
        }

        // ---------- Guide & Game Over ----------
        void DrawGuide(float S)
        {
            float pw = Mathf.Min(1040f * S, Screen.width - 80f * S);
            float ph = Mathf.Min(Screen.height * 0.86f, Screen.height - 80f * S);
            _activeModalRect = new Rect((Screen.width - pw)/2f, (Screen.height - ph)/2f, pw, ph);

            DrawModalBackdropAndConsume(_activeModalRect);

            Rect panel = _activeModalRect;
            FillAbs(panel, new Color(0.10f, 0.12f, 0.16f, 1f));
            FrameAbs(panel, new Color(1,1,1,0.2f), 1f);

            float pad = 14f * S;
            float x = panel.x + pad, y = panel.y + pad, w = panel.width - pad*2f, h = panel.height - pad*2f;

            GUI.Label(new Rect(x, y, w * 0.7f, 30f * S), "How to Play — Alpha", LabelStyle(22, S));
            if (GUI.Button(new Rect(x + w - 130f * S, y, 130f * S, 30f * S), "Close (H)", ButtonStyle(14))) { CloseModal(); return; }
            y += 36f * S;

            Rect view = new Rect(x, y, w, h - 50f * S);
            Rect content = new Rect(0, 0, w - 14f * S, 1400f * S);
            var _scroll = Vector2.zero;
            _scroll = GUI.BeginScrollView(view, _scroll, content);

            float cy = 0f;

            GUI.Label(new Rect(0, cy, w, 24f * S), "Goal", LabelStyle(18, S)); cy += 28f * S;
            GUI.Label(new Rect(0, cy, w, 20f * S),
                "Grow and keep satisfaction high. Adjust sector intensities; use trade (City/Country) to cover deficits.",
                LabelStyle(14, S)); cy += 28f * S;

            GUI.Label(new Rect(0, cy, w, 24f * S), "Reading the Bars", LabelStyle(18, S)); cy += 28f * S;
            Bullet(ref cy, w, S, "Bars show stocks vs per-cap needs; right rail shows trend; hover for details.");
            Bullet(ref cy, w, S, "Click a bar to drill into its sub-items and redistribute internal mix.");

            cy += 10f * S;
            GUI.Label(new Rect(0, cy, w, 24f * S), "Dependencies", LabelStyle(18, S)); cy += 28f * S;
            Bullet(ref cy, w, S, "Hover highlights relations: cyan = inputs; amber = outputs.");

            cy += 10f * S;
            GUI.Label(new Rect(0, cy, w, 24f * S), "Controls", LabelStyle(18, S)); cy += 28f * S;
            Bullet(ref cy, w, S, "[Space] Pause/Resume   [ , ] Slower   [ . ] Faster   [Esc] Settings   [H] Guide   [Alt] Legends.");

            cy += 10f * S;
            GUI.Label(new Rect(0, cy, w, 24f * S), "Fail States (Peril)", LabelStyle(18, S)); cy += 28f * S;
            Bullet(ref cy, w, S, $"Game over if Food/Water/Energy stay under {needCrisisFrac*100f:0}% of demand for {needFailSeconds:0}s, or satisfaction under {satCrisisThreshold:0} for {satFailSeconds:0}s.");

            GUI.EndScrollView();
        }
        void Bullet(ref float cy, float w, float S, string text)
        {
            GUI.Label(new Rect(16f * S, cy, w - 16f * S, 22f * S), "• " + text, LabelStyle(14, S));
            cy += 24f * S;
        }

        void DrawGameOver(float S)
        {
            float pw = Mathf.Min(740f * S, Screen.width - 80f * S);
            float ph = Mathf.Min(320f * S, Screen.height - 80f * S);
            _activeModalRect = new Rect((Screen.width - pw)/2f, (Screen.height - ph)/2f, pw, ph);

            DrawModalBackdropAndConsume(_activeModalRect);

            Rect panel = _activeModalRect;
            FillAbs(panel, new Color(0.10f, 0.12f, 0.16f, 1f));
            FrameAbs(panel, new Color(1,1,1,0.2f), 1f);

            float pad = 16f * S;
            float x = panel.x + pad, y = panel.y + pad, w = panel.width - pad*2f;

            GUI.Label(new Rect(x, y, w, 28f * S), "GAME OVER", LabelStyle(24, S, new Color(1f,0.7f,0.7f,1f))); y += 34f * S;
            GUI.Label(new Rect(x, y, w, 22f * S), gameOverReason, LabelStyle(16, S)); y += 36f * S;

            float btnW = 180f * S, btnH = 30f * S, gap = 12f * S;
            float bx = panel.center.x - (btnW*1.5f + gap);
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "Retry", ButtonStyle(14))) { gameOver = false; pause = false; CloseModal(); }
            bx += btnW + gap;
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "Reset World", ButtonStyle(14))) { gameOver = false; ResetWorld(); CloseModal(); }
            bx += btnW + gap;
            if (GUI.Button(new Rect(bx, y, btnW, btnH), "Main Menu", ButtonStyle(14))) TryLoad("MainMenu");
        }

        // ---------- layout helpers ----------
        float SectionHeaderLocal(string title, float cy, float S, float contentW)
        {
            GUI.Label(new Rect(0, cy, 460f * S, 24f * S), title, LabelStyle(18, S));
            GUI.Box(new Rect(0, cy + 22f * S, contentW, 1f), GUIContent.none);
            return cy + 30f * S;
        }
        void LabeledLocal(ref float cy, string label, float S, System.Action drawer, float lineH, float contentW)
        {
            GUI.Label(new Rect(0, cy, 160f * S, lineH), label, LabelStyle(14, S));
            drawer?.Invoke();
            cy += lineH;
        }
        Rect RowRectA_Local(float cy, float S) => new Rect(180f * S, cy + 3f * S, 24f * S, 24f * S);
        Rect RowRectB_Local(float cy, float S) => new Rect(180f * S, cy + 6f * S, 420f * S, 18f * S);
        Rect RowRectC_Local(float cy, float S) => new Rect(610f * S, cy, 140f * S, 24f * S);

        // Input shim
#if USE_NEW_INPUT
        bool EscPressed()   { return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame; }
        bool HelpPressed()  { return Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame; }
        bool SpacePressed() { return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame; }
        bool FasterPressed(){ return Keyboard.current != null && Keyboard.current.periodKey.wasPressedThisFrame; }
        bool SlowerPressed(){ return Keyboard.current != null && Keyboard.current.commaKey.wasPressedThisFrame; }
        bool AltHeld()      { return Keyboard.current != null && ((Keyboard.current.leftAltKey?.isPressed ?? false) || (Keyboard.current.rightAltKey?.isPressed ?? false)); }
#else
        bool EscPressed()   { return Input.GetKeyDown(KeyCode.Escape); }
        bool HelpPressed()  { return Input.GetKeyDown(KeyCode.H); }
        bool SpacePressed() { return Input.GetKeyDown(KeyCode.Space); }
        bool FasterPressed(){ return Input.GetKeyDown(KeyCode.Period); }
        bool SlowerPressed(){ return Input.GetKeyDown(KeyCode.Comma); }
        bool AltHeld()      { return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt); }
#endif

        // Normalization / helpers
        static float Normalize(Resource r, float value, float pop)
        {
            switch (r)
            {
                case Resource.POP:        return Mathf.Clamp01(value / 5_000_000_000f);
                case Resource.LABOR:      return Mathf.Clamp01(value / 2_000_000_000f);
                case Resource.MONEY:      return Mathf.Clamp01(value / 10_000_000f);
                case Resource.POLLUTION:  return Mathf.Clamp01(value / (pop*0.004f + 2000f));
                case Resource.WASTE:      return Mathf.Clamp01(value / (pop*0.003f + 2000f));
                default:
                    float want = 0.0012f * pop;
                    return Mathf.Clamp01(value / (want * 2f + 1f));
            }
        }
        static bool HasPerCapNeed(Resource r)
        {
            return r == Resource.FOOD || r == Resource.ENERGY || r == Resource.WATER
                || r == Resource.GOODS || r == Resource.SERVICES || r == Resource.RAW;
        }
        float TrendNormalized(Resource r, float ratePerSec)
        {
            float want = WantRef(r);
            float refScale = Mathf.Max(1f, want * 0.02f);
            return (float)Math.Tanh(ratePerSec / refScale);
        }
        float WantRef(Resource r)
        {
            float wantPerCap = r switch
            {
                Resource.FOOD => 0.0012f,
                Resource.ENERGY => 0.0010f,
                Resource.WATER => 0.0010f,
                Resource.GOODS => 0.0010f,
                Resource.SERVICES => 0.0010f,
                Resource.RAW => 0.0009f,
                _ => 0f
            };
            return wantPerCap > 0f ? wantPerCap * world.population : Mathf.Max(100f, world.Get(r) * 0.02f);
        }

        string TickerString(Resource r, float ratePerSec)
        {
            float wantPerMin = WantRef(r) * 60f;
            if (wantPerMin <= 0f) wantPerMin = Mathf.Max(1f, world.Get(r) * 0.01f);
            float ppm = (ratePerSec * 60f) / Mathf.Max(1e-6f, wantPerMin);
            string arrow = ppm > 0.001f ? "▲" : (ppm < -0.001f ? "▼" : "•");
            float val = Mathf.Abs(ppm) * 100f;
            return $"{arrow} {val:0.0}%/min";
        }

        void ShadowLabel(Rect r, string text, GUIStyle style, float shadowPx = 1f)
        {
            var shadow = new GUIStyle(style);
            var c = style.normal.textColor;
            shadow.normal.textColor = new Color(0,0,0, c.a * 0.70f);
            GUI.Label(new Rect(r.x + shadowPx, r.y + shadowPx, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
        }

        // draw helpers
        static Texture2D _px;
        static Texture2D Px()
        {
            if (_px == null)
            {
                _px = new Texture2D(1,1,TextureFormat.RGBA32,false);
                _px.SetPixel(0,0,Color.white);
                _px.Apply(false,false);
                _px.wrapMode = TextureWrapMode.Repeat;
                _px.filterMode = FilterMode.Point;
            }
            return _px;
        }
        static void FillAbs(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c; GUI.DrawTexture(Round(r), Px()); GUI.color = prev;
        }
        static void FrameAbs(Rect r, Color c, float thickness)
        {
            r = Round(r);
            FillAbs(new Rect(r.x, r.y, r.width, thickness), c);
            FillAbs(new Rect(r.x, r.yMax - thickness, r.width, thickness), c);
            FillAbs(new Rect(r.x, r.y, thickness, r.height), c);
            FillAbs(new Rect(r.xMax - thickness, r.y, thickness, r.height), c);
        }
        static void FillLocal(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c; GUI.DrawTexture(Round(r), Px()); GUI.color = prev;
        }
        static void FrameLocal(Rect r, Color c, float thickness)
        {
            r = Round(r);
            FillLocal(new Rect(0, 0, r.width, thickness), c);
            FillLocal(new Rect(0, r.height - thickness, r.width, thickness), c);
            FillLocal(new Rect(0, 0, thickness, r.height), c);
            FillLocal(new Rect(r.width - thickness, 0, thickness, r.height), c);
        }

        GUIStyle LabelStyle(int basePx, float S, Color? colorOverride = null)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(basePx * S),
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                normal = { textColor = colorOverride ?? Color.white }
            };
        }
        GUIStyle ButtonStyle(int basePx) => new GUIStyle(GUI.skin.button) { fontSize = basePx };

        // Colors
        Color ColorForRes(Resource r)
        {
            var def = DefaultColorFor(r);
            if (!useCustomBarColors) return def;
            if (customColors.TryGetValue(r, out var c))
            {
                if (c.a <= 0.001f) c.a = def.a;
                return c;
            }
            return def;
        }
        static Color DefaultColorFor(Resource r)
        {
            return r switch
            {
                Resource.FOOD       => new Color(0.50f, 0.90f, 0.40f, 0.95f),
                Resource.ENERGY     => new Color(1.00f, 0.80f, 0.20f, 0.95f),
                Resource.WATER      => new Color(0.30f, 0.70f, 1.00f, 0.95f),
                Resource.RAW        => new Color(0.70f, 0.60f, 0.50f, 0.95f),
                Resource.GOODS      => new Color(0.90f, 0.60f, 0.40f, 0.95f),
                Resource.SERVICES   => new Color(0.80f, 0.80f, 0.95f, 0.95f),
                Resource.MONEY      => new Color(0.60f, 1.00f, 0.60f, 0.95f),
                Resource.WASTE      => new Color(0.60f, 0.60f, 0.60f, 0.95f),
                Resource.POLLUTION  => new Color(1.00f, 0.30f, 0.30f, 0.95f),
                Resource.KNOWLEDGE  => new Color(0.70f, 0.90f, 1.00f, 0.95f),
                Resource.LABOR      => new Color(0.90f, 0.90f, 0.50f, 0.95f),
                Resource.POP        => new Color(0.90f, 0.90f, 0.90f, 0.95f),
                _                   => new Color(1f, 1f, 1f, 0.95f),
            };
        }

        // Scene load helper
        static void TryLoad(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return;
#if UNITY_5_3_OR_NEWER
            try { SceneManager.LoadScene(scene); }
            catch { Debug.LogError($"Scene '{scene}' not found in Build Settings."); }
#else
            try { Application.LoadLevel(scene); }
            catch { Debug.LogError("Scene load failed (legacy API). Ensure scene is in Build Settings."); }
#endif
        }

        // =============== Composition model & effects ===============
        void InitDefaultCompositions()
        {
            SubItem[] P(params SubItem[] a) { Normalize(a); return a; }
            SubItem SItem(string n, float w, string hint, params Impact[] impacts)
                => new SubItem { name = n, weight = w, hint = hint, impacts = impacts?.ToList() ?? new List<Impact>() };
            Impact I(Resource r, float d) => new Impact { res = r, deltaPerCap = d };

            compositions[Resource.FOOD] = P(
                SItem("Grains",     0.32f, "Staples; moderate water; low pollution.",
                      I(Resource.WATER, -4 * compositionPerCapUnit),
                      I(Resource.WASTE, +1 * compositionPerCapUnit)),
                SItem("Produce",    0.22f, "Veg & fruit; high perishability.",
                      I(Resource.WASTE, +2 * compositionPerCapUnit),
                      I(Resource.WATER, -3 * compositionPerCapUnit)),
                SItem("Meat",       0.22f, "Resource intensive; adds pollution.",
                      I(Resource.WATER, -8 * compositionPerCapUnit),
                      I(Resource.POLLUTION, +3 * compositionPerCapUnit)),
                SItem("Dairy",      0.12f, "Cold chain energy; some waste.",
                      I(Resource.ENERGY, -2 * compositionPerCapUnit),
                      I(Resource.WASTE,  +1 * compositionPerCapUnit)),
                SItem("Processed",  0.12f, "Industrial processing uses energy.",
                      I(Resource.ENERGY, -3 * compositionPerCapUnit))
            );

            compositions[Resource.ENERGY] = P(
                SItem("Fossil",     0.45f, "Cheap but polluting.",
                      I(Resource.POLLUTION, +6 * compositionPerCapUnit)),
                SItem("Nuclear",    0.15f, "Stable baseload; waste handling.",
                      I(Resource.WASTE, +1 * compositionPerCapUnit)),
                SItem("Hydro",      0.15f, "Water reliance; low emissions.",
                      I(Resource.WATER, -1 * compositionPerCapUnit)),
                SItem("Wind/Solar", 0.20f, "Intermittent; clean build-out.",
                      I(Resource.RAW,   -1 * compositionPerCapUnit)),
                SItem("Biofuel",    0.05f, "Land & water competition.",
                      I(Resource.WATER, -1 * compositionPerCapUnit),
                      I(Resource.POLLUTION, +1 * compositionPerCapUnit))
            );

            compositions[Resource.WATER] = P(
                SItem("Surface",       0.40f, "Rivers/lakes; env. sensitive.",
                      I(Resource.POLLUTION, +1 * compositionPerCapUnit)),
                SItem("Groundwater",   0.30f, "Aquifers; overdraw risks.",
                      I(Resource.POLLUTION, +1 * compositionPerCapUnit)),
                SItem("Desalination",  0.10f, "Energy hungry.",
                      I(Resource.ENERGY, -4 * compositionPerCapUnit)),
                SItem("Recycled",      0.15f, "Treatment costs energy.",
                      I(Resource.ENERGY, -1 * compositionPerCapUnit)),
                SItem("Imported",      0.05f, "Transport/logistics.",
                      I(Resource.SERVICES, -0.5f * compositionPerCapUnit))
            );

            compositions[Resource.RAW] = P(
                SItem("Metals",      0.28f, "Steel/aluminum/copper.",
                      I(Resource.ENERGY, -2 * compositionPerCapUnit)),
                SItem("Minerals",    0.22f, "Cement/gypsum.",
                      I(Resource.POLLUTION, +1 * compositionPerCapUnit)),
                SItem("Timber",      0.18f, "Sustainable if managed.",
                      I(Resource.POLLUTION, 0)),
                SItem("Hydrocarbons",0.22f, "Feedstock for industry.",
                      I(Resource.POLLUTION, +2 * compositionPerCapUnit)),
                SItem("Rare Earths", 0.10f, "High-tech inputs; dirty mining.",
                      I(Resource.POLLUTION, +2 * compositionPerCapUnit))
            );

            compositions[Resource.GOODS] = P(
                SItem("Consumer",     0.30f, "Short life, adds waste.",
                      I(Resource.WASTE, +3 * compositionPerCapUnit)),
                SItem("Capital",      0.20f, "Machinery boosts services later.",
                      I(Resource.SERVICES, +1 * compositionPerCapUnit)),
                SItem("Construction", 0.20f, "Heavy raw materials.",
                      I(Resource.RAW, -2 * compositionPerCapUnit)),
                SItem("Electronics",  0.15f, "E-waste tail.",
                      I(Resource.WASTE, +2 * compositionPerCapUnit)),
                SItem("Vehicles",     0.15f, "Energy & pollution downstream.",
                      I(Resource.POLLUTION, +2 * compositionPerCapUnit))
            );

            compositions[Resource.SERVICES] = P(
                SItem("Healthcare", 0.22f, "Improves health outcomes.",
                      I(Resource.WASTE, +1 * compositionPerCapUnit)),
                SItem("Education",  0.22f, "Builds knowledge.",
                      I(Resource.KNOWLEDGE, +3 * compositionPerCapUnit)),
                SItem("Finance",    0.18f, "Greases investment.",
                      I(Resource.MONEY, +2 * compositionPerCapUnit)),
                SItem("Logistics",  0.18f, "Moves goods/inputs.",
                      I(Resource.ENERGY, -2 * compositionPerCapUnit)),
                SItem("Government", 0.20f, "Stability; taxes spent.",
                      I(Resource.MONEY, -1 * compositionPerCapUnit))
            );

            compositions[Resource.MONEY] = P(
                SItem("Taxes",         0.20f, "Funds services.",
                      I(Resource.SERVICES, +1 * compositionPerCapUnit)),
                SItem("Wages",         0.30f, "Household demand.",
                      I(Resource.GOODS, +1 * compositionPerCapUnit),
                      I(Resource.SERVICES, +1 * compositionPerCapUnit)),
                SItem("Trade Balance", 0.15f, "Imports/exports tilt.",
                      I(Resource.GOODS, +0.5f * compositionPerCapUnit)),
                SItem("Savings",       0.20f, "Delayed consumption.",
                      I(Resource.GOODS, -0.5f * compositionPerCapUnit)),
                SItem("Investment",    0.15f, "Future productivity.",
                      I(Resource.KNOWLEDGE, +1 * compositionPerCapUnit))
            );

            compositions[Resource.WASTE] = P(
                SItem("Solid",     0.40f, "Landfill/recycling."),
                SItem("Organic",   0.20f, "Can compost → less pollution.",
                      I(Resource.POLLUTION, -1 * compositionPerCapUnit)),
                SItem("Industrial",0.20f, "Hazards if unmanaged.",
                      I(Resource.POLLUTION, +2 * compositionPerCapUnit)),
                SItem("E-waste",   0.10f, "Recover rare metals.",
                      I(Resource.RAW, +0.5f * compositionPerCapUnit)),
                SItem("Hazardous", 0.10f, "Expensive to handle.",
                      I(Resource.SERVICES, -0.5f * compositionPerCapUnit))
            );

            compositions[Resource.POLLUTION] = P(
                SItem("CO₂",         0.50f, "Climate forcing."),
                SItem("NOx/SOx",     0.15f, "Air quality."),
                SItem("Water Toxins",0.15f, "Eutrophication."),
                SItem("Plastics",    0.10f, "Long-lived waste."),
                SItem("Noise/Light", 0.10f, "Urban livability.")
            );

            compositions[Resource.KNOWLEDGE] = P(
                SItem("Basic Research", 0.25f, "Long-horizon benefits."),
                SItem("Applied R&D",    0.25f, "Near-term productivity."),
                SItem("Vocational",     0.20f, "Skilled labor."),
                SItem("Digital Infra",  0.15f, "ICT capacity.",
                      I(Resource.ENERGY, -0.5f * compositionPerCapUnit)),
                SItem("Culture/Media",  0.15f, "Civic capacity.")
            );

            compositions[Resource.LABOR] = P(
                SItem("Skilled",     0.25f, "Higher productivity.",
                      I(Resource.SERVICES, +0.5f * compositionPerCapUnit)),
                SItem("Unskilled",   0.25f, "Essential tasks."),
                SItem("Industrial",  0.20f, "Manufacturing."),
                SItem("Logistics",   0.15f, "Transport/warehousing.",
                      I(Resource.ENERGY, -0.5f * compositionPerCapUnit)),
                SItem("Public Sector",0.15f, "Admin/education/health.")
            );

            compositions[Resource.POP] = P(
                SItem("Children",     0.20f, "Future labor; edu demand.",
                      I(Resource.SERVICES, -0.5f * compositionPerCapUnit)),
                SItem("Working Age",  0.55f, "Main labor force.",
                      I(Resource.LABOR, +1.0f * compositionPerCapUnit)),
                SItem("Seniors",      0.15f, "Healthcare demand.",
                      I(Resource.SERVICES, -0.5f * compositionPerCapUnit)),
                SItem("Migrants",     0.05f, "Dynamic labor shifts.",
                      I(Resource.LABOR, +0.2f * compositionPerCapUnit)),
                SItem("Visitors",     0.05f, "Tourism services.",
                      I(Resource.SERVICES, +0.3f * compositionPerCapUnit))
            );

            foreach (Resource r in Enum.GetValues(typeof(Resource)))
            {
                if (!compositions.ContainsKey(r))
                {
                    compositions[r] = Enumerable.Range(1, 5)
                        .Select(i => SItem($"{r} Part {i}", 1f/5f, $"Sub-component {i}")).ToArray();
                }
            }
        }

        void ApplyCompositionExternalities(WorldState w, float dt)
        {
            float pop = Mathf.Max(1f, w.population);
            foreach (var kv in compositions)
            {
                var items = kv.Value;
                float baseScale = compositionImpactScale * pop;

                for (int i = 0; i < items.Length; i++)
                {
                    var it = items[i];
                    if (it.impacts == null) continue;
                    float weight = Mathf.Clamp01(it.weight);
                    foreach (var imp in it.impacts)
                    {
                        float delta = imp.deltaPerCap * baseScale * weight * dt;
                        float cur = w.Get(imp.res);
                        w.stock[imp.res] = Mathf.Max(0f, cur + delta);
                    }
                }
            }
        }

        // ── MODAL CONTROL HELPERS ─────────────────────────────────────────────────────
        void CloseAllModals()
        {
            overlayOpen = false;   // Settings
            guideOpen   = false;   // Guide
            tradeOpen   = false;   // Trade
            compOpen    = false;   // Drilldown
            // gameOver stays as-is; it's its own modal state.
        }

        void OpenSettings()                 => OpenModal(() => overlayOpen = true);
        void OpenGuide()                    => OpenModal(() => guideOpen   = true);
        void OpenTrade()                    => OpenModal(() => tradeOpen   = true);
        void OpenComposition(Resource r)    => OpenModal(() => { compRes = r; compOpen = true; });

        void OpenModal(Action opener)
        {
            CaptureModalSnapshotImmediate();
            _modalSnapshotDirty = true;
            StartCoroutine(CaptureModalSnapshotEndOfFrame());

            CloseAllModals();
            opener?.Invoke();
            pause = true;
        }

        void CloseModal()
        {
            CloseAllModals();
            if (_modalSnapshot != null) { Destroy(_modalSnapshot); _modalSnapshot = null; }
            pause = false;
        }

        void CaptureModalSnapshotImmediate()
        {
            if (_modalSnapshot != null) { Destroy(_modalSnapshot); _modalSnapshot = null; }
            try { _modalSnapshot = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch { _modalSnapshot = null; }
        }

        System.Collections.IEnumerator CaptureModalSnapshotEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            CaptureModalSnapshotImmediate();
            _modalSnapshotDirty = false;
        }

        void DrawBackdrop()
        {
            FillAbs(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.04f, 0.07f, 1f));
        }

        void DrawModalBackdropAndConsume(Rect modalRect)
        {
            if (_modalSnapshot != null)
            {
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _modalSnapshot, ScaleMode.StretchToFill);
            }
            else
            {
                DrawBackdrop();
            }

            FillAbs(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.28f));

            var e = Event.current;
            if (e == null) return;

            bool pointerEvt = e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.MouseDrag || e.type == EventType.ScrollWheel;
            bool keyEvt     = e.type == EventType.KeyDown   || e.type == EventType.KeyUp;

            if (pointerEvt && !modalRect.Contains(e.mousePosition)) e.Use();
            if (keyEvt) e.Use();
        }

        static void Normalize(SubItem[] arr)
        {
            float s = Mathf.Max(0f, arr.Sum(a => Mathf.Max(0f, a.weight)));
            if (s <= 1e-6f) { Even(arr); return; }
            for (int i = 0; i < arr.Length; i++) arr[i].weight = Mathf.Max(0f, arr[i].weight) / s;
        }
        static void Even(SubItem[] arr)
        {
            float w = 1f / Mathf.Max(1, arr.Length);
            for (int i = 0; i < arr.Length; i++) arr[i].weight = w;
        }

        // ======== NEW: slider container helpers (crisp & aligned) ========
        static Rect Inflate(Rect r, float dx, float dy) => new Rect(r.x - dx, r.y - dy, r.width + 2f*dx, r.height + 2f*dy);
        static Rect Round(Rect r) => new Rect(Mathf.Round(r.x), Mathf.Round(r.y), Mathf.Round(r.width), Mathf.Round(r.height));

        static Rect ToScreenRectInScroll(Rect view, Rect local, Vector2 scroll)
        {
            return new Rect(view.x + local.x, view.y + local.y - scroll.y, local.width, local.height);
        }

        void DrawSliderContainerAbs(Rect track, float S)
        {
            Rect bg = Inflate(track, 6f * S, 4f * S);
            FillAbs(bg, new Color(1,1,1,0.06f));
            FrameAbs(bg, new Color(1,1,1,0.15f), 1f);
        }

        void DrawSliderContainerInScroll(Rect view, Vector2 scroll, Rect localTrack, float S)
        {
            Rect scr = ToScreenRectInScroll(view, localTrack, scroll);
            Rect bg = Inflate(scr, 6f * S, 4f * S);
            FillAbs(bg, new Color(1,1,1,0.06f));
            FrameAbs(bg, new Color(1,1,1,0.15f), 1f);
        }
    }
}
