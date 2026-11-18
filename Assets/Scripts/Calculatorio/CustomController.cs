using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif

namespace Calculatorio
{
    [Serializable]
    public class CustomDesign
    {
        public List<MainElement> mains = new();
        public List<Link> links = new();
    }

    [Serializable]
    public class MainElement
    {
        public string name = "Main";
        public float min = 0f;
        public float max = 100f;

        // Thresholds are in the same units as scope
        public float goal = 60f;
        public float crisis = 20f;

        public List<SubElement> subs = new();
    }

    [Serializable]
    public class SubElement
    {
        public string name = "Sub";
        public float min = 0f;
        public float max = 100f;
    }

    [Serializable]
    public class Link
    {
        // From node (main index + optional sub index)
        public int fromMain = 0;
        public int fromSub  = -1;  // -1 = main-level, >=0 = sub index

        // To node (main index + optional sub index)
        public int toMain = 0;
        public int toSub  = -1;    // -1 = main-level, >=0 = sub index

        // Effect weight: destination += normalized(source) * weight per tick (semantics are up to your sim)
        public float weight = 0f;  // -1 .. +1 typical
    }

    public class CustomController : MonoBehaviour
    {
        // ───────────────────────────── Config / Style ─────────────────────────────
        public int minMainElements = 6;
        public int minSubsPerMain  = 3;

        // Modal + Wizard
        bool wizardOpen = true;
        int step = 0; // 0: mains, 1: subs, 2: thresholds, 3: links, 4: summary

        // Working design
        CustomDesign design = new();

        // UI state
        Vector2 mainsScroll, subsScroll, thrScroll, linksScroll;
        int selectedMainForSubs = 0;
        int selFromMain = 0, selFromSub = -1;
        int selToMain = 0, selToSub = -1;

        // Preview runtime (placeholder)
        bool showPreview = false;
        Dictionary<int, float> previewStocks = new();

        // ───────────────────────────── Lifecycle ─────────────────────────────
        void Start()
        {
            Application.targetFrameRate = 120;

            if (Camera.main)
            {
                Camera.main.backgroundColor = new Color(0.03f, 0.04f, 0.07f);
                Camera.main.orthographic = true;
                Camera.main.orthographicSize = 7f;
            }
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (wizardOpen) wizardOpen = false; // close modal
                else SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
            }
        }

        void OnGUI()
        {
            DrawBackdrop();

            // Title stripe
            float S = 1f;
            FillAbs(new Rect(0, 0, Screen.width, 3f), new Color(0.2f, 0.7f, 1f, 0.35f));
            GUI.Label(new Rect(10, 10, Screen.width - 20, 28),
                "CALCULATORIO — Custom Scenario",
                LabelStyle(20, S, new Color(0.85f, 0.95f, 1f, 0.95f)));

            // Modal Wizard
            if (wizardOpen) { DrawWizard(S); return; }

            // After wizard: simple summary + (optional) tiny preview
            DrawSummaryAndPreview(S);
        }

        // ───────────────────────────── Wizard ─────────────────────────────
        void DrawWizard(float S)
        {
            float pw = Mathf.Min(1120f, Screen.width - 80f);
            float ph = Mathf.Min(Screen.height * 0.90f, Screen.height - 60f);
            Rect panel = new Rect(Mathf.Round((Screen.width - pw)/2f), Mathf.Round((Screen.height - ph)/2f), Mathf.Round(pw), Mathf.Round(ph));

            DrawModalBackdropAndConsume(panel);

            FillAbs(panel, new Color(0.10f, 0.12f, 0.16f, 1f));
            FrameAbs(panel, new Color(1,1,1,0.2f), 1f);

            float pad = 14f;
            float x = panel.x + pad, y = panel.y + pad, w = panel.width - pad*2f, h = panel.height - pad*2f;

            GUI.Label(new Rect(x, y, w * 0.65f, 30f), "Custom Scenario Setup", LabelStyle(22, S));
            if (GUI.Button(new Rect(x + w - 130f, y, 130f, 30f), "Close (Esc)", ButtonStyle(14)))
            {
                wizardOpen = false;
                return;
            }
            y += 36f;

            // Steps header
            string[] steps = { "Main Elements", "Sub-Elements", "Thresholds", "Correlations", "Summary" };
            step = GUI.SelectionGrid(new Rect(x, y, w, 28f), step, steps, steps.Length);
            y += 36f;

            Rect view = new Rect(x, y, w, h - (y - (panel.y + pad)));
            switch (step)
            {
                case 0: DrawStepMains(view); break;
                case 1: DrawStepSubs(view); break;
                case 2: DrawStepThresholds(view); break;
                case 3: DrawStepLinks(view); break;
                default: DrawStepSummary(view); break;
            }

            // Navigation
            float navH = 36f;
            float bx = x;
            float by = panel.yMax - pad - navH;
            if (GUI.Button(new Rect(bx, by, 120f, navH), "◀ Back", ButtonStyle(14))) step = Mathf.Max(0, step - 1);
            bx += 126f;

            if (step < 4)
            {
                bool canNext = CanAdvance(step, out string why);
                using (new GUIEnabled(canNext))
                {
                    if (GUI.Button(new Rect(bx, by, 140f, navH), "Next ▶", ButtonStyle(14)))
                        step = Mathf.Min(4, step + 1);
                }
                if (!canNext)
                    GUI.Label(new Rect(bx + 150f, by + 8f, w - (bx - x) - 160f, 24f),
                        why, LabelStyle(12, S, new Color(1f, 0.7f, 0.7f, 0.95f)));
            }
            else
            {
                if (GUI.Button(new Rect(bx, by, 220f, navH), "Finish & Close Wizard", ButtonStyle(16)))
                {
                    wizardOpen = false;
                    // Initialize preview values
                    previewStocks.Clear();
                    for (int i = 0; i < design.mains.Count; i++)
                        previewStocks[i] = Mathf.Lerp(design.mains[i].min, design.mains[i].max, 0.5f);
                }
            }
        }

        bool CanAdvance(int curStep, out string why)
        {
            why = "";
            if (curStep == 0)
            {
                if (design.mains.Count < minMainElements) { why = $"Add at least {minMainElements} main elements."; return false; }
                if (design.mains.Any(m => string.IsNullOrWhiteSpace(m.name))) { why = "All main elements must have a name."; return false; }
                if (design.mains.Any(m => m.max <= m.min)) { why = "Each main element needs min < max."; return false; }
            }
            else if (curStep == 1)
            {
                if (design.mains.Any(m => m.subs.Count < minSubsPerMain))
                { why = $"Each main element needs at least {minSubsPerMain} sub-elements."; return false; }
                if (design.mains.Any(m => m.subs.Any(s => string.IsNullOrWhiteSpace(s.name))))
                { why = "All sub-elements must have names."; return false; }
                if (design.mains.Any(m => m.subs.Any(s => s.max <= s.min)))
                { why = "Each sub-element needs min < max."; return false; }
            }
            else if (curStep == 2)
            {
                foreach (var m in design.mains)
                {
                    if (m.goal <= m.crisis) { why = $"'{m.name}' goal should be > crisis."; return false; }
                    if (m.goal > m.max || m.goal < m.min) { why = $"'{m.name}' goal must be within its scope."; return false; }
                    if (m.crisis > m.max || m.crisis < m.min) { why = $"'{m.name}' crisis must be within its scope."; return false; }
                }
            }
            return true;
        }

        void DrawStepMains(Rect view)
        {
            float S = 1f;
            Rect content = new Rect(0, 0, view.width - 16f, Mathf.Max(400f, design.mains.Count * 90f + 120f));
            mainsScroll = GUI.BeginScrollView(view, mainsScroll, content);

            float cy = 0f;
            GUI.Label(new Rect(0, cy, content.width, 22f),
                $"Create at least {minMainElements} main elements (name + value scope).",
                LabelStyle(14, S)); cy += 28f;

            if (GUI.Button(new Rect(0, cy, 160f, 28f), "＋ Add Main", ButtonStyle(14)))
            {
                design.mains.Add(new MainElement { name = $"Element {design.mains.Count+1}", min = 0f, max = 100f, goal = 60f, crisis = 20f });
            }
            cy += 36f;

            for (int i = 0; i < design.mains.Count; i++)
            {
                var m = design.mains[i];
                Rect row = new Rect(0, cy, content.width, 80f);
                FillLocal(row, new Color(1,1,1,0.05f));
                FrameLocal(row, new Color(1,1,1,0.12f), 1f);

                GUI.Label(new Rect(8, cy + 6, 70, 20), "Name:", LabelStyle(13, S));
                m.name = GUI.TextField(new Rect(70, cy + 6, 220, 22), m.name);

                GUI.Label(new Rect(300, cy + 6, 60, 20), "Min:", LabelStyle(13, S));
                m.min = FloatField(new Rect(340, cy + 6, 80, 22), m.min);
                GUI.Label(new Rect(430, cy + 6, 60, 20), "Max:", LabelStyle(13, S));
                m.max = FloatField(new Rect(470, cy + 6, 80, 22), m.max);

                using (new GUIEnabled(design.mains.Count > 0))
                {
                    if (GUI.Button(new Rect(row.width - 110, cy + 6, 100, 22), "－ Remove", ButtonStyle(12)))
                    {
                        design.mains.RemoveAt(i);
                        if (selectedMainForSubs >= design.mains.Count) selectedMainForSubs = Mathf.Max(0, design.mains.Count - 1);
                        cy += 84f; // skip proper spacing after removal
                        continue;
                    }
                }

                GUI.Label(new Rect(8, cy + 36, row.width - 16, 20),
                    $"Sub-elements: {m.subs.Count}   Goal: {m.goal:0.##}   Crisis: {m.crisis:0.##}",
                    LabelStyle(12, S, new Color(0.85f,0.95f,1f,0.9f)));

                design.mains[i] = m;
                cy += 84f;
            }

            GUI.EndScrollView();
        }

        void DrawStepSubs(Rect view)
        {
            float S = 1f;
            if (design.mains.Count == 0)
            {
                GUI.Label(view, "Add some main elements first.", LabelStyle(14, S));
                return;
            }

            Rect content = new Rect(0, 0, view.width - 16f, Mathf.Max(420f, 300f));
            subsScroll = GUI.BeginScrollView(view, subsScroll, content);

            float leftW = Mathf.Min(280f, content.width * 0.35f);
            float rightW = content.width - leftW - 12f;

            // Left: mains list
            FillLocal(new Rect(0, 0, leftW, content.height), new Color(1,1,1,0.04f));
            FrameLocal(new Rect(0, 0, leftW, content.height), new Color(1,1,1,0.12f), 1f);

            GUI.Label(new Rect(8, 6, leftW - 16, 20), "Main Elements", LabelStyle(15, S));
            for (int i = 0; i < design.mains.Count; i++)
            {
                bool isSel = (i == selectedMainForSubs);
                var old = GUI.backgroundColor;
                if (isSel) GUI.backgroundColor = new Color(0.30f, 0.55f, 1f, 0.90f);
                if (GUI.Button(new Rect(8, 30 + i * 32, leftW - 16, 28), design.mains[i].name, ButtonStyle(13)))
                    selectedMainForSubs = i;
                GUI.backgroundColor = old;
            }

            // Right: subs for selected main
            var m = design.mains[selectedMainForSubs];
            float x = leftW + 12f;
            GUI.Label(new Rect(x, 6, rightW, 20), $"{m.name} — Sub-Elements (min {minSubsPerMain})", LabelStyle(15, S));

            if (GUI.Button(new Rect(x, 32, 140f, 26f), "＋ Add Sub", ButtonStyle(13)))
                m.subs.Add(new SubElement { name = $"Sub {m.subs.Count+1}", min = m.min, max = m.max });

            float cy = 64f;
            for (int j = 0; j < m.subs.Count; j++)
            {
                var s = m.subs[j];
                Rect row = new Rect(x, cy, rightW, 72f);
                FillLocal(row, new Color(1,1,1,0.05f));
                FrameLocal(row, new Color(1,1,1,0.12f), 1f);

                GUI.Label(new Rect(x + 8, cy + 6, 70, 20), "Name:", LabelStyle(13, S));
                s.name = GUI.TextField(new Rect(x + 60, cy + 6, 220, 22), s.name);

                GUI.Label(new Rect(x + 290, cy + 6, 60, 20), "Min:", LabelStyle(13, S));
                s.min = FloatField(new Rect(x + 330, cy + 6, 80, 22), s.min);
                GUI.Label(new Rect(x + 420, cy + 6, 60, 20), "Max:", LabelStyle(13, S));
                s.max = FloatField(new Rect(x + 460, cy + 6, 80, 22), s.max);

                using (new GUIEnabled(m.subs.Count > 0))
                {
                    if (GUI.Button(new Rect(row.xMax - 100, cy + 6, 92, 22), "－ Remove", ButtonStyle(12)))
                    {
                        m.subs.RemoveAt(j);
                        cy += 76f;
                        continue;
                    }
                }

                m.subs[j] = s;
                cy += 76f;
            }

            design.mains[selectedMainForSubs] = m;

            GUI.EndScrollView();
        }

        void DrawStepThresholds(Rect view)
        {
            float S = 1f;
            Rect content = new Rect(0, 0, view.width - 16f, Mathf.Max(460f, design.mains.Count * 80f + 80f));
            thrScroll = GUI.BeginScrollView(view, thrScroll, content);

            float cy = 0f;
            GUI.Label(new Rect(0, cy, content.width, 22f),
                "Set Goal and Crisis thresholds for each main element. (Goal > Crisis and both within scope.)",
                LabelStyle(14, S)); cy += 28f;

            for (int i = 0; i < design.mains.Count; i++)
            {
                var m = design.mains[i];
                Rect row = new Rect(0, cy, content.width, 70f);
                FillLocal(row, new Color(1,1,1,0.05f));
                FrameLocal(row, new Color(1,1,1,0.12f), 1f);

                GUI.Label(new Rect(8, cy + 6, 300, 20), m.name, LabelStyle(15, S, new Color(0.9f,0.95f,1f,1f)));
                GUI.Label(new Rect(8, cy + 28, 200, 20), $"Scope: [{m.min:0.##}, {m.max:0.##}]", LabelStyle(12, S));

                // Sliders inside crisp containers
                Rect goalTrack = new Rect(260, cy + 12, 280, 16);
                DrawSliderContainerInScroll(view, thrScroll, goalTrack, S);
                m.goal = GUI.HorizontalSlider(goalTrack, m.goal, m.min, m.max);
                GUI.Label(new Rect(260, cy + 30, 280, 18), $"Goal: {m.goal:0.##}", LabelStyle(12, S));

                Rect crisisTrack = new Rect(560, cy + 12, 280, 16);
                DrawSliderContainerInScroll(view, thrScroll, crisisTrack, S);
                m.crisis = GUI.HorizontalSlider(crisisTrack, m.crisis, m.min, m.max);
                GUI.Label(new Rect(560, cy + 30, 280, 18), $"Crisis: {m.crisis:0.##}", LabelStyle(12, S));

                if (m.crisis >= m.goal)
                    GUI.Label(new Rect(850, cy + 12, 260, 20), "Crisis should be < Goal", LabelStyle(12, S, new Color(1f,0.7f,0.7f,0.95f)));

                design.mains[i] = m;
                cy += 78f;
            }

            GUI.EndScrollView();
        }

        void DrawStepLinks(Rect view)
        {
            float S = 1f;
            Rect content = new Rect(0, 0, view.width - 16f, Mathf.Max(520f, 260f + design.links.Count * 36f));
            linksScroll = GUI.BeginScrollView(view, linksScroll, content);

            float cy = 0f;
            GUI.Label(new Rect(0, cy, content.width, 22f),
                "Define correlations (links) between sources and destinations. Weight ≈ effect strength per tick.",
                LabelStyle(14, S)); cy += 28f;

            // Source selector
            GUI.Label(new Rect(0, cy, 140, 20), "From Main:", LabelStyle(13, S));
            selFromMain = IntPopup(new Rect(100, cy, 220, 20), selFromMain, design.mains.Select(m => m.name).ToArray());
            GUI.Label(new Rect(330, cy, 80, 20), "From Sub:", LabelStyle(13, S));
            selFromSub  = SubPopup(new Rect(400, cy, 240, 20), selFromMain, selFromSub);
            cy += 28f;

            // Destination selector
            GUI.Label(new Rect(0, cy, 140, 20), "To Main:", LabelStyle(13, S));
            selToMain = IntPopup(new Rect(100, cy, 220, 20), selToMain, design.mains.Select(m => m.name).ToArray());
            GUI.Label(new Rect(330, cy, 80, 20), "To Sub:", LabelStyle(13, S));
            selToSub  = SubPopup(new Rect(400, cy, 240, 20), selToMain, selToSub);
            cy += 28f;

            // Weight slider
            GUI.Label(new Rect(0, cy, 90, 20), "Weight:", LabelStyle(13, S));
            Rect wtTrack = new Rect(80, cy + 2, 280, 16);
            DrawSliderContainerInScroll(view, linksScroll, wtTrack, S);
            float wVal = GUI.HorizontalSlider(wtTrack, 0f, -1f, 1f);
            GUI.Label(new Rect(370, cy, 120, 20), $"{wVal:+0.00;-0.00;0.00}", LabelStyle(13, S));
            using (new GUIEnabled(!(selFromMain == selToMain && selFromSub == selToSub)))
            {
                if (GUI.Button(new Rect(500, cy - 2, 120, 24), "＋ Add Link", ButtonStyle(13)))
                {
                    design.links.Add(new Link
                    {
                        fromMain = selFromMain,
                        fromSub  = selFromSub,
                        toMain   = selToMain,
                        toSub    = selToSub,
                        weight   = wVal
                    });
                }
            }
            cy += 36f;

            // Existing links list
            GUI.Label(new Rect(0, cy, content.width, 20), $"Links: {design.links.Count}", LabelStyle(13, S)); cy += 24f;

            for (int i = 0; i < design.links.Count; i++)
            {
                var L = design.links[i];
                Rect row = new Rect(0, cy, content.width, 28f);
                FillLocal(row, new Color(1,1,1,0.05f));
                FrameLocal(row, new Color(1,1,1,0.12f), 1f);

                string from = NodeLabel(L.fromMain, L.fromSub);
                string to   = NodeLabel(L.toMain, L.toSub);
                string txt  = $"{from}  →  {to}    w={L.weight:+0.00;-0.00;0.00}";
                GUI.Label(new Rect(8, cy+4, row.width-120, 20), txt, LabelStyle(13, S));

                if (GUI.Button(new Rect(row.width - 100, cy + 3, 92, 22), "－ Remove", ButtonStyle(12)))
                { design.links.RemoveAt(i); i--; cy += 32f; continue; }

                cy += 32f;
            }

            GUI.EndScrollView();
        }

        void DrawStepSummary(Rect view)
        {
            float S = 1f;
            Rect content = new Rect(0, 0, view.width - 16f, Mathf.Max(420f, design.mains.Count * 36f + design.links.Count * 24f + 100f));
            var scr = Vector2.zero;
            scr = GUI.BeginScrollView(view, scr, content);

            float cy = 0f;
            GUI.Label(new Rect(0, cy, content.width, 22f), "Summary", LabelStyle(16, S)); cy += 26f;

            GUI.Label(new Rect(0, cy, content.width, 20f),
                $"Main elements: {design.mains.Count}   |   Links: {design.links.Count}", LabelStyle(13, S)); cy += 24f;

            for (int i = 0; i < design.mains.Count; i++)
            {
                var m = design.mains[i];
                GUI.Label(new Rect(0, cy, content.width, 18f),
                    $"{i+1}. {m.name}   scope[{m.min:0.##}, {m.max:0.##}]   goal:{m.goal:0.##}   crisis:{m.crisis:0.##}",
                    LabelStyle(13, S, new Color(0.85f,0.95f,1f,0.95f))); cy += 20f;
                for (int j = 0; j < m.subs.Count; j++)
                {
                    var s = m.subs[j];
                    GUI.Label(new Rect(16, cy, content.width-16, 18f),
                        $"• {s.name}  scope[{s.min:0.##}, {s.max:0.##}]", LabelStyle(12, S));
                    cy += 18f;
                }
                cy += 6f;
            }

            if (design.links.Count > 0)
            {
                cy += 6f;
                GUI.Label(new Rect(0, cy, content.width, 18f), "Links:", LabelStyle(14, S)); cy += 22f;

                for (int i = 0; i < design.links.Count; i++)
                {
                    var L = design.links[i];
                    GUI.Label(new Rect(0, cy, content.width, 18f),
                        $"{NodeLabel(L.fromMain, L.fromSub)} → {NodeLabel(L.toMain, L.toSub)}    w={L.weight:+0.00;-0.00;0.00}",
                        LabelStyle(12, S));
                    cy += 18f;
                }
            }

            GUI.EndScrollView();
        }

        // ───────────────────────────── After-wizard view / preview ─────────────────────────────
        void DrawSummaryAndPreview(float S)
        {
            float margin = 10f;
            Rect top = new Rect(margin, 44f, Screen.width - margin*2f, Mathf.Max(160f, Screen.height * 0.35f));
            Rect bottom = new Rect(margin, top.yMax + margin, top.width, Screen.height - top.yMax - margin*2f);

            // Summary card
            FillAbs(top, new Color(0.06f, 0.07f, 0.11f, 1f));
            FrameAbs(top, new Color(0.2f, 0.4f, 0.8f, 0.25f), 1f);
            GUI.Label(new Rect(top.x + 10, top.y + 8, top.width - 20, 24f),
                $"Custom Setup — {design.mains.Count} main elements, {design.links.Count} links",
                LabelStyle(18, S, new Color(0.85f, 0.95f, 1f, 0.95f)));

            if (GUI.Button(new Rect(top.x + top.width - 220f, top.y + 8f, 100f, 24f), showPreview ? "Hide Bars" : "Show Bars", ButtonStyle(13)))
                showPreview = !showPreview;
            if (GUI.Button(new Rect(top.x + top.width - 110f, top.y + 8f, 100f, 24f), "Reopen Wizard", ButtonStyle(13)))
                wizardOpen = true;

            // Quick preview: vertical bars for mains with sliders to nudge values (not a full sim—just sanity check UI)
            if (showPreview && design.mains.Count > 0)
            {
                FillAbs(bottom, new Color(0.05f, 0.06f, 0.10f, 1f));
                FrameAbs(bottom, new Color(0.2f,0.4f,0.8f,0.25f), 1f);

                int n = design.mains.Count;
                float gap = 10f;
                float colW = (bottom.width - gap * (n + 1)) / Mathf.Max(1, n);
                float barH = bottom.height - 70f;

                for (int i = 0; i < n; i++)
                {
                    var m = design.mains[i];
                    float nx = bottom.x + gap + i * (colW + gap);
                    Rect bar = new Rect(nx, bottom.y + 60f, colW, barH);

                    // frame & fill
                    FillAbs(bar, new Color(1,1,1,0.05f));
                    FrameAbs(bar, new Color(1,1,1,0.12f), 1f);

                    float v = previewStocks.TryGetValue(i, out var cur) ? cur : Mathf.Lerp(m.min, m.max, 0.5f);
                    float norm = Mathf.InverseLerp(m.min, m.max, v);
                    float fillH = Mathf.Max(2f, (bar.height - 2f) * norm);
                    Rect fill = new Rect(bar.x + 1, bar.y + bar.height - 1 - fillH, bar.width - 2f, fillH);
                    FillAbs(fill, new Color(0.30f, 0.70f, 1.00f, 0.75f));

                    // goal / crisis lines
                    float gy = Mathf.Lerp(bar.yMax - 1, bar.y + 1, Mathf.InverseLerp(m.min, m.max, m.goal));
                    float cy = Mathf.Lerp(bar.yMax - 1, bar.y + 1, Mathf.InverseLerp(m.min, m.max, m.crisis));
                    FillAbs(new Rect(bar.x + 1, gy, bar.width - 2f, 1f), new Color(0.7f,1f,0.7f,0.9f));
                    FillAbs(new Rect(bar.x + 1, cy, bar.width - 2f, 1f), new Color(1f,0.6f,0.6f,0.9f));

                    // labels
                    GUI.Label(new Rect(nx, bottom.y + 6f, colW, 18f), m.name, LabelStyle(14, S, new Color(0.9f,0.95f,1f,1f)));
                    GUI.Label(new Rect(nx, bottom.y + 24f, colW, 18f), $"v={v:0.##}", LabelStyle(12, S));

                    // adjuster slider (crisp container)
                    Rect track = new Rect(nx + 6f, bar.y - 20f, colW - 12f, 14f);
                    DrawSliderContainerAbs(track, S);
                    v = GUI.HorizontalSlider(track, v, m.min, m.max);
                    previewStocks[i] = v;
                }
            }
        }

        // ───────────────────────────── Helpers ─────────────────────────────
        string NodeLabel(int main, int sub)
        {
            if (main < 0 || main >= design.mains.Count) return "?";
            var m = design.mains[main];
            if (sub < 0) return m.name;
            if (sub >= m.subs.Count) return m.name + "/?";
            return $"{m.name}/{m.subs[sub].name}";
        }

        int IntPopup(Rect r, int idx, string[] options)
        {
            if (options == null || options.Length == 0) return 0;
            idx = Mathf.Clamp(idx, 0, options.Length - 1);
            // Simple faux-popup using a grid of 1 row
            return GUI.SelectionGrid(r, idx, options, Mathf.Max(1, options.Length));
        }

        int SubPopup(Rect r, int mainIdx, int currentSub)
        {
            if (mainIdx < 0 || mainIdx >= design.mains.Count) return -1;
            var subs = design.mains[mainIdx].subs;
            if (subs.Count == 0)
            {
                GUI.Label(r, "(main level)", LabelStyle(12, 1f));
                return -1;
            }
            // “Main-level” + each sub
            var opts = new List<string> { "(main level)" };
            opts.AddRange(subs.Select(s => s.name));
            int idx = Mathf.Clamp(currentSub + 1, 0, opts.Count - 1);
            int sel = GUI.SelectionGrid(r, idx, opts.ToArray(), Mathf.Max(1, opts.Count));
            return sel - 1; // back to -1..N-1
        }

        float FloatField(Rect r, float value)
        {
            string txt = GUI.TextField(r, value.ToString("0.##"));
            if (float.TryParse(txt, out var v)) return v;
            return value;
        }

        // Backdrop
        void DrawBackdrop()
        {
            FillAbs(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.04f, 0.07f, 1f));
        }

        // Modal background + dim + consume input behind
        void DrawModalBackdropAndConsume(Rect modalRect)
        {
            FillAbs(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.28f));
            var e = Event.current;
            if (e == null) return;
            bool pointerEvt = e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.MouseDrag || e.type == EventType.ScrollWheel;
            bool keyEvt     = e.type == EventType.KeyDown   || e.type == EventType.KeyUp;
            if (pointerEvt && !modalRect.Contains(e.mousePosition)) e.Use();
            if (keyEvt) e.Use();
        }

        // Drawing utils (crisp)
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
        static Rect Round(Rect r) => new Rect(Mathf.Round(r.x), Mathf.Round(r.y), Mathf.Round(r.width), Mathf.Round(r.height));
        static Rect Inflate(Rect r, float dx, float dy) => new Rect(r.x - dx, r.y - dy, r.width + 2f*dx, r.height + 2f*dy);

        static void FillAbs(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c; GUI.DrawTexture(Round(r), Px()); GUI.color = prev;
        }
        static void FrameAbs(Rect r, Color c, float t)
        {
            r = Round(r);
            FillAbs(new Rect(r.x, r.y, r.width, t), c);
            FillAbs(new Rect(r.x, r.yMax - t, r.width, t), c);
            FillAbs(new Rect(r.x, r.y, t, r.height), c);
            FillAbs(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
        static void FillLocal(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c; GUI.DrawTexture(Round(r), Px()); GUI.color = prev;
        }
        static void FrameLocal(Rect r, Color c, float t)
        {
            r = Round(r);
            FillLocal(new Rect(0, 0, r.width, t), c);
            FillLocal(new Rect(0, r.height - t, r.width, t), c);
            FillLocal(new Rect(0, 0, t, r.height), c);
            FillLocal(new Rect(r.width - t, 0, t, r.height), c);
        }

        GUIStyle LabelStyle(int basePx, float S, Color? col = null) => new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(basePx * S),
            alignment = TextAnchor.UpperLeft,
            wordWrap = true,
            normal = { textColor = col ?? Color.white }
        };
        GUIStyle ButtonStyle(int basePx) => new GUIStyle(GUI.skin.button) { fontSize = basePx };

        // Slider containers: absolute & in-scroll
        void DrawSliderContainerAbs(Rect track, float S)
        {
            Rect bg = Inflate(track, 6f * S, 4f * S);
            FillAbs(bg, new Color(1,1,1,0.06f));
            FrameAbs(bg, new Color(1,1,1,0.15f), 1f);
        }
        void DrawSliderContainerInScroll(Rect view, Vector2 scroll, Rect localTrack, float S)
        {
            Rect scr = new Rect(view.x + localTrack.x, view.y + localTrack.y - scroll.y, localTrack.width, localTrack.height);
            Rect bg = Inflate(scr, 6f * S, 4f * S);
            FillAbs(bg, new Color(1,1,1,0.06f));
            FrameAbs(bg, new Color(1,1,1,0.15f), 1f);
        }

        // Enable/disable scope helper
        struct GUIEnabled : IDisposable
        {
            bool prev;
            public GUIEnabled(bool enabled) { prev = GUI.enabled; GUI.enabled = enabled; }
            public void Dispose() { GUI.enabled = prev; }
        }
    }
}
