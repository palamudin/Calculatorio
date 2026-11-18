using UnityEngine;
using System;
using System.Collections.Generic;

// Modal host with tabs: Editor / Sequencer / Mixer
// - Layered synth (per-layer engine + modulators). Tabs: Layer 1, +Add, ✕ delete
// - Sequencer tab shows Sequencer first, then Mixer below it (Mixer tab still present)
// - Filter Cutoff/Q always editable (Filter toggle just gates apply at playback)
// - Start from pure sine + one modulator on new/open (for editing bootstrap)
// - Header: Master volume, Live/Loop, larger BPM knob + editable field
// - Scope container: Waveform (3/4 height, continuous line) + Envelope (1/5 height) + padding
// - Keyboard at bottom on all tabs
// - Enforce >= 50px vertical padding between major UI blocks
public class AudioMenuUI : MonoBehaviour
{
    public ProceduralSynth synth;
    SoundPresetLibrary _lib;
int _editingIndex = -1;
    // External embedded UIs (assign in Inspector or auto-find)
    public SequencerUI sequencerUI;
    public PianoKeyboardUI pianoUI;

    // Modal + tabs
    enum Modal { None, Settings, SoundsList, SoundRack }
    enum RackTab { Editor, Sequencer, Mixer }
    Modal _modal = Modal.None;
    RackTab _tab = RackTab.Editor;

    // UI skin/font
    GUISkin _skin;
    int _fontSize = 14;

    // Layered synth
    [Serializable] public class FMMod { public float ratio = 2f; public float index = 0f; }

    [Serializable] public class LayerState
    {
        public string name = "Layer";
        public SoundPreset p = new SoundPreset();
        public List<FMMod> mods = new List<FMMod>();
        public float baseFmIndex = 0f;
    }

    List<LayerState> _layers = new List<LayerState>();
    int _activeLayer = 0;

    // Convenience: current working layer/preset
    public SoundPreset _work => (_layers.Count>0 ? _layers[_activeLayer].p : null);
    List<FMMod> CurrentMods => (_layers.Count>0 ? _layers[_activeLayer].mods : _tmpEmptyMods);
    List<FMMod> _tmpEmptyMods = new List<FMMod>();

    // Header audition
    bool _livePreview = false;
    bool _loopAudition = false;
    float _loopBPM = 120f; string _loopBPMText = "120";
    float _lastTriggerAt = -1f, _scheduledNext = -1f;

    // Master volume (UI; wire to synth when available)
    float _masterVol = 0.8f;

    // Mixer (standalone for now; reused under Sequencer tab and Mixer tab)
    [Serializable] public class MixerChannel
    {
        public string name = "Ch";
        public float volume = 0.8f;   // 0..1
        public float pan = 0f;        // -1..1
        public bool mute = false;
        public bool solo = false;
        public bool fxReverb = false, fxDelay=false, fxDist=false;
    }
    public List<MixerChannel> mixer = new List<MixerChannel>();

    // Look
    Texture2D _px;  Texture2D Px(){ if(_px==null){ _px=new Texture2D(1,1,TextureFormat.RGBA32,false); _px.SetPixel(0,0,Color.white); _px.Apply(); } return _px; }
    Texture2D _px2; Texture2D Px2(){ if(_px2==null){ _px2=new Texture2D(1,1,TextureFormat.RGBA32,false); _px2.SetPixel(0,0,Color.white); _px2.Apply(); } return _px2; }
    Texture2D _panelTex;
    Color kFace = new Color(0.20f,0.20f,0.22f,1f);
    Color kRim  = new Color(0.08f,0.08f,0.10f,1f);
    Color kHL   = new Color(0.85f,0.85f,0.88f,1f);

    public bool IsEditorOpen => _modal == Modal.SoundRack;
    public SoundPreset CurrentPreset => _work;

    // ---------- spacing helper ----------
    void BlockSpacer() => GUILayout.Space(50); // enforce >= 50px between blocks

    void Awake()
    {
        if (synth == null) synth = FindObjectOfType<ProceduralSynth>();
        _lib = SoundPresetLibrary.LoadOrCreateDefault();
        if (sequencerUI == null) sequencerUI = FindObjectOfType<SequencerUI>();
        if (pianoUI == null) pianoUI = FindObjectOfType<PianoKeyboardUI>();
        _panelTex = MakePanel(64, 64);

        if (mixer.Count == 0) for (int i=0;i<6;i++) mixer.Add(new MixerChannel{ name=$"Ch {i+1}"});
    }

    Texture2D MakePanel(int w,int h)
    {
        var t=new Texture2D(w,h,TextureFormat.RGBA32,false);
        var c=new Color32(30,30,32,255); var b=new Color32(24,24,26,255);
        for(int y=0;y<h;y++) for(int x=0;x<w;x++) t.SetPixel(x,y, ((x^y)&7)==0?c:b);
        t.Apply(); return t;
    }

    void Update()
    {
        if (_modal==Modal.SoundRack && _livePreview && _work!=null && synth!=null)
        {
            ApplyModulatorsToLayerPreviewOnly(_layers[_activeLayer]);
            synth.UpdateLivePreview(_work);
        }

        if (_modal==Modal.SoundRack && _loopAudition && _work!=null && synth!=null)
        {
            if (_lastTriggerAt < 0f && _scheduledNext < 0f) TriggerNowAndScheduleNext();
            else if (_scheduledNext > 0f && Time.unscaledTime >= _scheduledNext) TriggerNowAndScheduleNext();
        }
    }

    void ApplyFont()
    {
        if (_skin == null) _skin = Instantiate(GUI.skin);
        foreach (var s in new[]
        { _skin.label, _skin.button, _skin.box, _skin.toggle, _skin.textField, _skin.textArea, _skin.window,
          _skin.horizontalSlider, _skin.horizontalSliderThumb })
            s.fontSize = _fontSize;
        GUI.skin = _skin;
    }

    Rect CenteredRect(float w,float h)=> new Rect(Mathf.Round((Screen.width-w)/2f), Mathf.Round((Screen.height-h)/2f), w,h);

    // ---------- CLICK-THROUGH BLOCK ----------
    int _modalDepth = -10000;
    void BeginModalFrame(Rect rect)
    {
        GUI.depth = _modalDepth;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var e = Event.current;
        if (e != null && (e.isMouse || e.type==EventType.ScrollWheel))
            if(!rect.Contains(e.mousePosition)){ e.Use(); }
    }
    void EndModalFrame(){}

    void OnGUI()
    {
        ApplyFont();

        if (_modal == Modal.None)
        {
            if (GUI.Button(new Rect(10,10,110,28), "☰ Audio"))
                _modal = Modal.Settings;
            return;
        }

        switch (_modal)
        {
            case Modal.Settings:   DrawSettingsModal();   break;
            case Modal.SoundsList: DrawSoundsListModal(); break;
            case Modal.SoundRack:  DrawSoundRackModal();  break;
        }
    }

    // ---------------- Settings ----------------
    void DrawSettingsModal()
    {
        var rect = CenteredRect(1140, 630);
        BeginModalFrame(rect);
        GUI.BeginGroup(rect, GUI.skin.window);
        GUILayout.BeginArea(new Rect(16, 44, rect.width-32, rect.height-60));
        GUILayout.Label("Audio Settings", GUI.skin.box);
        GUILayout.Space(8);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Font Size", GUILayout.Width(120));
        _fontSize = Mathf.RoundToInt(GUILayout.HorizontalSlider(_fontSize, 10, 24, GUILayout.Width(420)));
        GUILayout.Label(_fontSize.ToString(), GUILayout.Width(40));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Sounds →", GUILayout.Width(140))) _modal = Modal.SoundsList;
        if (GUILayout.Button("Close", GUILayout.Width(100))) _modal = Modal.None;
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
        GUI.EndGroup();
        EndModalFrame();
    }

    // ---------------- Sounds List ----------------
    Vector2 _listScroll;
    void DrawSoundsListModal()
    {
        var rect = CenteredRect(1350, 900);
        BeginModalFrame(rect);
        GUI.BeginGroup(rect, GUI.skin.window);
        GUILayout.BeginArea(new Rect(16, 44, rect.width-32, rect.height-60));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Sounds", GUI.skin.box, GUILayout.Width(120));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ New", GUILayout.Width(100)))
        {
            var p=new SoundPreset{ name="New", engine=SoundEngineType.Additive, sustain=0f };
            if (p.harmonicAmps==null) p.harmonicAmps=new List<float>();
            while(p.harmonicAmps.Count<32) p.harmonicAmps.Add(0f);
            p.harmonicAmps[1]=1f; // pure sine
            p.awFreq = 220f;
            _lib.presets.Add(p);
        }
        if (GUILayout.Button("Save Library", GUILayout.Width(140))) _lib.Save();
        GUILayout.EndHorizontal();

        _listScroll = GUILayout.BeginScrollView(_listScroll, GUI.skin.box);
        for(int i=0;i<_lib.presets.Count;i++)
        {
            var p=_lib.presets[i];
            GUILayout.BeginHorizontal(GUI.skin.box);
            p.name = GUILayout.TextField(p.name, GUILayout.Width(360));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶ Play", GUILayout.Width(70))) AuditionSingle(p);
            if (GUILayout.Button("■ Stop", GUILayout.Width(70))) { if(synth) synth.StopAll(); _lastTriggerAt=_scheduledNext=-1f; }
            if (GUILayout.Button("Edit", GUILayout.Width(70)))
            {
                _editingIndex=i;
                InitLayersFromPreset(p);
                _livePreview=false; if(synth) synth.StopLivePreview();
                _lastTriggerAt=_scheduledNext=-1f;
                _tab = RackTab.Editor;
                _modal=Modal.SoundRack;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6);
        if (GUILayout.Button("← Back", GUILayout.Width(120))) _modal = Modal.Settings;

        GUILayout.EndArea();
        GUI.EndGroup();
        EndModalFrame();
    }

    void InitLayersFromPreset(SoundPreset src)
    {
        _layers.Clear();
        var L = new LayerState{ name="Layer 1", p=src.Clone() };
        if (L.p.engine==SoundEngineType.Additive || L.p.engine==SoundEngineType.Wavetable)
        {
            if (L.p.harmonicAmps==null) L.p.harmonicAmps=new List<float>();
            while(L.p.harmonicAmps.Count<32) L.p.harmonicAmps.Add(0f);
            L.p.harmonicAmps[1]=1f; // pure sine
        }
        L.baseFmIndex = L.p.fmIndex;
        L.mods.Clear(); L.mods.Add(new FMMod()); // start with one modulator
        _layers.Add(L);
        _activeLayer = 0;
    }

    // ---------------- Sound Rack (Tabbed) ----------------
    void DrawSoundRackModal()
{
    float targetW = Mathf.Min(Screen.width - 60, 1640f);
    float targetH = Mathf.Min(Screen.height - 60, 1880f);
    var rect = CenteredRect(targetW, targetH);

    BeginModalFrame(rect);
    GUI.BeginGroup(rect, GUI.skin.window);

    // ---------- HEADER AT TOP (5px padding) ----------
    const float headerH = 36f;
    const float headerPad = 5f; // <- only 5px padding from top
    GUILayout.BeginArea(new Rect(16, headerPad, rect.width - 32, headerH));
    GUILayout.BeginHorizontal();
    GUILayout.Label("Sound Rack", GUILayout.Width(120));

    // Tabs
    var prevC = GUI.color;
    GUI.color = (_tab==RackTab.Editor)? Color.white : new Color(1,1,1,0.6f);
    if (GUILayout.Button("Editor", GUILayout.Width(90))) _tab = RackTab.Editor;
    GUI.color = (_tab==RackTab.Sequencer)? Color.white : new Color(1,1,1,0.6f);
    if (GUILayout.Button("Sequencer", GUILayout.Width(110))) _tab = RackTab.Sequencer;
    GUI.color = (_tab==RackTab.Mixer)? Color.white : new Color(1,1,1,0.6f);
    if (GUILayout.Button("Mixer", GUILayout.Width(90))) _tab = RackTab.Mixer;
    GUI.color = prevC;

    GUILayout.Space(16);
    GUILayout.Label("Master", GUILayout.Width(52));
    LKnob(" ", ref _masterVol, 0f, 1f, 36f, 46f, "0.00");

    GUILayout.Space(12);
    GUILayout.Label("Live", GUILayout.Width(36));
    bool prevLive=_livePreview;
    _livePreview = GUILayout.Toggle(_livePreview, GUIContent.none, GUILayout.Width(36));
    if(!prevLive && _livePreview && _work!=null) { ApplyModulatorsToLayerPreviewOnly(_layers[_activeLayer]); synth.StartLivePreview(_work); }
    if( prevLive && !_livePreview) synth.StopLivePreview();

    GUILayout.Space(10);
    GUILayout.Label("Loop", GUILayout.Width(36));
    bool prevLoop=_loopAudition;
    _loopAudition = GUILayout.Toggle(_loopAudition, GUIContent.none, GUILayout.Width(36));
    if(!prevLoop && _loopAudition && _lastTriggerAt<0f) TriggerNowAndScheduleNext();
    if( prevLoop && !_loopAudition) { _lastTriggerAt=_scheduledNext=-1f; }

    // BPM (bigger knob + editable)
    GUILayout.Space(12);
    GUILayout.Label("BPM", GUILayout.Width(34));
    float bpmF; float.TryParse(_loopBPMText, out bpmF);
    bpmF = Mathf.Clamp(bpmF, 20f, 300f);
    LKnob(" ", ref bpmF, 20f, 300f, 42f, 56f, "0");
    _loopBPM = bpmF;
    _loopBPMText = GUILayout.TextField(_loopBPM.ToString("0"), GUILayout.Width(50));

    GUILayout.FlexibleSpace();
    if (GUILayout.Button("▶ Play", GUILayout.Width(90))) { ApplyModulatorsToLayerPreviewOnly(_layers[_activeLayer]); AuditionAllLayers(); StartManualOneShotSchedule(); }
    if (GUILayout.Button("■ Stop", GUILayout.Width(90))) { if(synth) synth.StopAll(); _lastTriggerAt=_scheduledNext=-1f; }
    GUI.color = Color.green;
    if (GUILayout.Button("Save", GUILayout.Width(90)))
    {
        PersistPreviewIntoLayers();
        if (_editingIndex>=0 && _editingIndex<_lib.presets.Count && _layers.Count>0)
            _lib.presets[_editingIndex]=_layers[0].p.Clone();
        _lib.Save();
        _modal=Modal.SoundsList; synth.StopLivePreview(); _lastTriggerAt=_scheduledNext=-1f;
    }
    GUI.color = Color.white;
    if (GUILayout.Button("Cancel", GUILayout.Width(90)))
    { _modal=Modal.SoundsList; synth.StopLivePreview(); _lastTriggerAt=_scheduledNext=-1f; }
    GUILayout.EndHorizontal();
    GUILayout.EndArea();

    // ---------- BODY OFFSET DOWN BY max(5% screen, 100px) ----------
    float contentTopOffset = Mathf.Max(Screen.height * 0.05f, 100f);

    var body = new Rect(16, headerPad + headerH + contentTopOffset,
                        rect.width - 32,
                        rect.height - (headerPad + headerH + contentTopOffset) - 16);

    GUI.BeginGroup(body);
    if (_panelTex) GUI.DrawTexture(new Rect(0,0,body.width,body.height), _panelTex, ScaleMode.StretchToFill);

    float pad = 16f;
    GUILayout.BeginArea(new Rect(pad, pad, body.width - 2*pad, body.height - 2*pad));

    // Layer tabs row
    GUILayout.BeginHorizontal();
    for (int i=0;i<_layers.Count;i++)
    {
        var c = GUI.color;
        GUI.color = (i==_activeLayer)? Color.white : new Color(1,1,1,0.65f);
        if (GUILayout.Button(_layers[i].name, GUILayout.Width(90))) { _activeLayer = i; }
        GUI.color = c;
    }
    if (GUILayout.Button("+ Add", GUILayout.Width(70)))
    {
        var newL = new LayerState{ name=$"Layer {_layers.Count+1}" };
        newL.p.engine = SoundEngineType.Additive; newL.p.sustain=0f; newL.p.awFreq=220f;
        if (newL.p.harmonicAmps==null) newL.p.harmonicAmps=new List<float>();
        while(newL.p.harmonicAmps.Count<32) newL.p.harmonicAmps.Add(0f);
        newL.p.harmonicAmps[1]=1f;
        newL.mods.Add(new FMMod());
        _layers.Add(newL); _activeLayer=_layers.Count-1;
    }
    if (_layers.Count>1 && GUILayout.Button("✕", GUILayout.Width(36)))
    {
        _layers.RemoveAt(_activeLayer);
        _activeLayer = Mathf.Clamp(_activeLayer-1, 0, Mathf.Max(0,_layers.Count-1));
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    BlockSpacer(); // 50px after layer row

    // Name + Engine row
    GUILayout.BeginHorizontal();
    GUILayout.Label("Name", GUILayout.Width(60));
    if(_work!=null) _layers[_activeLayer].name = (_work.name = GUILayout.TextField(_layers[_activeLayer].name, GUILayout.Width(360)));
    GUILayout.Space(18);
    GUILayout.Label("Engine", GUILayout.Width(60));
    if(_work!=null) _work.engine = (SoundEngineType)EnumCycle(_work.engine, 160);
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();

    BlockSpacer();

    // Tab body
    if (_tab == RackTab.Editor)         DrawEditorTab(body);
    else if (_tab == RackTab.Sequencer) DrawSequencerWithMixer(body);
    else                                DrawMixerTab(body);

    BlockSpacer();

    // Keyboard at bottom of body
    GUILayout.Label("Keyboard", GUI.skin.box);
    var kbRect = GUILayoutUtility.GetRect(body.width - 2*pad, 220, GUILayout.ExpandWidth(true));
    if (pianoUI != null) { pianoUI.host = this; pianoUI.DrawEmbedded(kbRect); }

    GUILayout.EndArea();
    GUI.EndGroup();

    GUI.EndGroup();
    EndModalFrame();
}


    // Sequencer tab: Sequencer then Mixer underneath
    void DrawSequencerWithMixer(Rect body)
    {
        GUILayout.Label("Sequencer", GUI.skin.box);
        var seqRect = GUILayoutUtility.GetRect(body.width-32, 300, GUILayout.ExpandWidth(true));
        if (sequencerUI != null) { sequencerUI.host = this; sequencerUI.DrawEmbedded(seqRect); }

        BlockSpacer();

        DrawMixerTab(body);
    }

    // -------- Editor Tab: Engine knobs + Dual Scope + Envelope knobs + Modulators --------
    void DrawEditorTab(Rect body)
    {
        if (_work == null) { GUILayout.Label("No layer selected."); return; }

        // ======= Engine knobs (Pan/Filter + Engine-specific) =======
        GUILayout.Label("Controls", GUI.skin.box);
        GUILayout.BeginHorizontal(GUI.skin.box);

        // Pan + Filter (ALWAYS editable)
        LKnob("Pan", ref _work.pan, -1f, 1f);
        _work.filterEnabled = GUILayout.Toggle(_work.filterEnabled, "Filter", GUILayout.Width(70));
        LKnob("Cutoff", ref _work.filterCutoff, 20f, 20000f, 44f, 70f, "0");
        LKnob("Q",      ref _work.filterQ,      0.1f, 20f);

        GUILayout.Space(14);
        if (_work.engine == SoundEngineType.FM)
        {
            LKnob("Carrier",  ref _work.fmCarrier, 20f, 20000f, 44f, 70f, "0");
            LKnob("ModRatio", ref _work.fmModRatio, 0.25f, 16f);
            LKnob("BaseIndex",ref _layers[_activeLayer].baseFmIndex, 0f, 12f);
            GUILayout.Label($"Eff: {(Mathf.Max(0f,_layers[_activeLayer].baseFmIndex+SumModIndices())).ToString("0.00")}", GUILayout.Width(80));
        }
        else if (_work.engine == SoundEngineType.Additive || _work.engine == SoundEngineType.Wavetable)
        {
            LKnob("Freq", ref _work.awFreq, 20f, 20000f, 44f, 70f, "0");
        }
        else if (_work.engine == SoundEngineType.Kick)
        {
            LKnob("BaseFreq", ref _work.kickBaseFreq, 20f, 20000f, 44f, 70f, "0");
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        BlockSpacer();

        // ======= Dual scope (waveform + envelope) =======
        GUILayout.Label("Scope", GUI.skin.box);
        var scArea = GUILayoutUtility.GetRect(body.width-32, 460, GUILayout.ExpandWidth(true));
        DrawDualScope(scArea, _work, CurrentMods, _layers[_activeLayer].baseFmIndex);

        BlockSpacer();

        // ======= Envelope knobs group =======
        GUILayout.Label("Envelope", GUI.skin.box);
        GUILayout.BeginHorizontal(GUI.skin.box);
        LKnob("Attack",  ref _work.attack,  0f, 2.0f);
        LKnob("Decay",   ref _work.decay,   0f, 2.0f);
        LKnob("Sustain", ref _work.sustain, 0f, 1.0f);
        LKnob("Release", ref _work.release, 0f, 3.0f);
        LKnob("Length",  ref _work.seconds, 0.05f, 10f, 44f, 56f, "0.##");
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        BlockSpacer();

        // ======= Modulators (per-layer) =======
        GUILayout.Label("Modulators", GUI.skin.box);
        if (CurrentMods.Count == 0) CurrentMods.Add(new FMMod());

        GUILayout.BeginHorizontal();
        for (int i=0;i<CurrentMods.Count;i++)
            if (GUILayout.Button($"Mod {i+1}", GUILayout.Width(80))) _activeModLocal = i;
        if (GUILayout.Button("+ Add", GUILayout.Width(70))) { CurrentMods.Add(new FMMod()); if(CurrentMods.Count>64) CurrentMods.RemoveAt(CurrentMods.Count-1); }
        if (CurrentMods.Count>1 && GUILayout.Button("✕", GUILayout.Width(36))) { CurrentMods.RemoveAt(_activeModLocal); _activeModLocal = Mathf.Clamp(_activeModLocal-1,0,Mathf.Max(0,CurrentMods.Count-1)); }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        _activeModLocal = Mathf.Clamp(_activeModLocal, 0, Mathf.Max(0,CurrentMods.Count-1));
        var m = CurrentMods[_activeModLocal];

        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label($"Selected: Mod {_activeModLocal+1}", GUILayout.Width(150));
        LKnob("Ratio", ref m.ratio, 0.25f, 16f);
        LKnob("Index", ref m.index, 0f, 16f);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }
    int _activeModLocal = 0;

    // ----- Mixer tab -----
    Vector2 _mixerScroll;

void DrawMixerTab(Rect body)
{
    GUILayout.Label("Channel Mixer", GUI.skin.box);
    _mixerScroll = GUILayout.BeginScrollView(_mixerScroll, GUI.skin.box, GUILayout.Height(320));

    for (int i=0;i<mixer.Count;i++)
    {
        var ch = mixer[i];
        GUILayout.BeginHorizontal(GUI.skin.box);

        ch.name = GUILayout.TextField(ch.name, GUILayout.Width(150));

        GUILayout.Space(6);

        GUILayout.Label("Vol", GUILayout.Width(28));
        ch.volume = GUILayout.HorizontalSlider(ch.volume, 0f, 1f, GUILayout.Width(160));
        GUILayout.Label(ch.volume.ToString("0.00"), GUILayout.Width(44));

        GUILayout.Space(8);

        GUILayout.Label("Pan", GUILayout.Width(28));
        ch.pan = GUILayout.HorizontalSlider(ch.pan, -1f, 1f, GUILayout.Width(140));
        GUILayout.Label(ch.pan.ToString("0.00"), GUILayout.Width(44));

        GUILayout.Space(10);

        ch.mute = GUILayout.Toggle(ch.mute, "M", GUILayout.Width(32));
        ch.solo = GUILayout.Toggle(ch.solo, "S", GUILayout.Width(32));

        GUILayout.Space(12);   // <-- was "12));" before (extra ')')

        ch.fxReverb = GUILayout.Toggle(ch.fxReverb, "Reverb", GUILayout.Width(70));
        ch.fxDelay  = GUILayout.Toggle(ch.fxDelay,  "Delay",  GUILayout.Width(60));
        ch.fxDist   = GUILayout.Toggle(ch.fxDist,   "Dist",   GUILayout.Width(56));

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("✕", GUILayout.Width(26)))
        {
            mixer.RemoveAt(i);
            i--;
            GUILayout.EndHorizontal();
            continue;
        }

        GUILayout.EndHorizontal();
    }

    GUILayout.EndScrollView();

    GUILayout.BeginHorizontal();
    if (GUILayout.Button("+ Add Channel", GUILayout.Width(130)))
        mixer.Add(new MixerChannel{ name=$"Ch {mixer.Count+1}"});
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();
}


    // ----- Dual scope (Waveform 3/4 height + Envelope 1/5 height, continuous line) -----
    void DrawDualScope(Rect area, SoundPreset p, List<FMMod> mods, float baseFmIndex)
    {
        GUI.BeginGroup(area, GUI.skin.box);
        var inner = new Rect(6,6, area.width-12, area.height-12);

        float waveH = inner.height * 0.75f;
        float envH  = inner.height * 0.20f;
        float padH  = inner.height - waveH - envH;
        var waveR = new Rect(inner.x, inner.y, inner.width, waveH);
        var envR  = new Rect(inner.x, inner.y + waveH + padH*0.5f, inner.width, envH);

        GUI.color = new Color(1,1,1,0.05f); GUI.DrawTexture(waveR, Px()); GUI.DrawTexture(envR, Px()); GUI.color=Color.white;
        GUI.color = new Color(1,1,1,0.10f); GUI.DrawTexture(new Rect(waveR.x, waveR.center.y, waveR.width, 1), Px()); GUI.color=Color.white;

        if (p != null)
        {
            int W = Mathf.Max(2, Mathf.RoundToInt(waveR.width));
            float dur = 0.02f;
            float t=0f;

            Func<float,float> sample = (tt)=>0f;
            if (p.engine == SoundEngineType.FM)
            {
                float car = Mathf.Max(20f, p.fmCarrier);
                float eff = Mathf.Max(0f, baseFmIndex + SumModIndices(mods));
                float ratio = Mathf.Max(0.01f, p.fmModRatio);
                sample = (tt) => {
                    float mod = Mathf.Sin(2*Mathf.PI*(car*ratio)*tt) * eff;
                    return Mathf.Sin(2*Mathf.PI*car*tt + mod);
                };
            }
            else if (p.engine == SoundEngineType.Additive || p.engine == SoundEngineType.Wavetable)
            {
                float f0 = Mathf.Max(20f, p.awFreq);
                int baseH = (p.harmonicAmps!=null ? p.harmonicAmps.Count : 0);
                int size = Mathf.Max(16, baseH);
                float[] amps = new float[size];
                for (int h=0; h<baseH; h++) amps[h] = p.harmonicAmps[h];
                for (int i=0;i<mods.Count;i++)
                {
                    var md=mods[i]; if (md.index<=0f) continue;
                    int h = Mathf.Clamp(Mathf.RoundToInt(md.ratio), 1, size-1);
                    amps[h] = Mathf.Clamp01(amps[h] + md.index * 0.5f);
                }
                sample = (tt) => {
                    float s=0f; for (int h=1; h<size; h++) if (amps[h]>0f) s+= Mathf.Sin(2*Mathf.PI*(f0*h)*tt)*amps[h];
                    return Mathf.Clamp(s, -1f, 1f);
                };
            }
            else // Kick/Noise simplified
            {
                sample = (tt)=> Mathf.Sin(2*Mathf.PI*80f*tt) * Mathf.Exp(-20f*tt);
            }

            float prevX = waveR.x;
            float prevY = waveR.center.y;
            for (int x = 0; x < W; x++)
            {
                float nx = waveR.x + x;
                float v  = sample(t);
                float ny = waveR.center.y - v*(waveR.height*0.45f);
                DrawLine(prevX, prevY, nx, ny, 2f);
                prevX = nx; prevY = ny;
                t += dur/W;
            }
        }

        DrawEnvelopeOverlay(envR, p);
        GUI.EndGroup();
    }

    void DrawLine(float x1,float y1,float x2,float y2,float thickness)
    {
        float dx = x2-x1, dy=y2-y1;
        int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(dx) + Mathf.Abs(dy)));
        for (int i=0;i<=steps;i++)
        {
            float t = i/(float)steps;
            float px = Mathf.Lerp(x1,x2,t), py = Mathf.Lerp(y1,y2,t);
            GUI.DrawTexture(new Rect(px, py, thickness, thickness), Px2());
        }
    }

    void DrawEnvelopeOverlay(Rect g, SoundPreset p)
    {
        if (p == null) return;
        float total = Mathf.Max(0.1f, p.seconds + Mathf.Max(0f, p.release));
        float a = Mathf.Max(0f, p.attack);
        float d = Mathf.Max(0f, p.decay);
        float r = Mathf.Max(0f, p.release);
        float sLvl = Mathf.Clamp01(p.sustain);

        float tA = Mathf.Min(a, total*0.9f);
        float tD = Mathf.Min(tA + d, total*0.95f);
        float tR = Mathf.Max(tD, total - r);
        float tE = total;

        Vector2[] pts = new Vector2[5];
        pts[0] = new Vector2(g.x,                     g.yMax);
        pts[1] = new Vector2(g.x + (tA/total)*g.width, g.yMax - 1f * g.height);
        pts[2] = new Vector2(g.x + (tD/total)*g.width, g.yMax - sLvl * g.height);
        pts[3] = new Vector2(g.x + (tR/total)*g.width, g.yMax - sLvl * g.height);
        pts[4] = new Vector2(g.x + (tE/total)*g.width, g.yMax);

        GUI.color = new Color(0.95f,0.95f,0.98f, 0.85f);
        for (int i=1;i<pts.Length;i++) DrawLine(pts[i-1].x, pts[i-1].y, pts[i].x, pts[i].y, 2f);
        GUI.color = Color.white;
    }

    // ----- studio knobs -----
    bool Knob(ref float value, float min, float max, float size)
    {
        Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;

        GUI.color = kRim;  GUI.DrawTexture(r, Px2());
        Rect inset = new Rect(r.x+2, r.y+2, r.width-4, r.height-4);
        GUI.color = kFace; GUI.DrawTexture(inset, Px2());
        GUI.color = new Color(1,1,1,0.06f); GUI.DrawTexture(new Rect(inset.x, inset.center.y, inset.width, inset.height*0.5f), Px2());
        GUI.color = Color.white;

        float old = value;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (inset.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    float t = Mathf.InverseLerp(min,max,value);
                    t -= e.delta.y * 0.0045f;
                    t += e.delta.x * 0.0020f;
                    value = Mathf.Lerp(min,max, Mathf.Clamp01(t));
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
        }

        float tNorm = Mathf.InverseLerp(min, max, value);
        float ang = Mathf.Lerp(-140f, 140f, tNorm);
        Vector2 pivot = inset.center;
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(ang, pivot);
        GUI.color = kHL;
        GUI.DrawTexture(new Rect(pivot.x-1f, pivot.y - inset.height*0.38f, 2f, inset.height*0.32f), Px2());
        GUI.matrix = m; GUI.color = Color.white;

        return !Mathf.Approximately(old, value);
    }

    void LKnob(string label, ref float v, float min, float max, float size = 44f, float fieldW=56f, string fmt="0.###")
    {
        GUILayout.BeginVertical(GUILayout.Width(size + fieldW + 14f));
        GUILayout.Label(label, GUILayout.Width(size + fieldW + 14f));
        GUILayout.BeginHorizontal();
        Knob(ref v, min, max, size);
        string s = GUILayout.TextField(v.ToString(fmt), GUILayout.Width(fieldW));
        if (float.TryParse(s, out float nv)) v = Mathf.Clamp(nv, min, max);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    // ---- modulators helpers ----
    float SumModIndices(){ return SumModIndices(CurrentMods); }
    float SumModIndices(List<FMMod> mods){ float sum=0f; for (int i=0;i<mods.Count;i++) sum += Mathf.Max(0f, mods[i].index); return sum; }

    void ApplyModulatorsToLayerPreviewOnly(LayerState L)
    {
        if (L==null || L.p==null) return;
        if (L.p.engine == SoundEngineType.FM) L.p.fmIndex = Mathf.Max(0f, L.baseFmIndex + SumModIndices(L.mods));
    }

    void PersistPreviewIntoLayers()
    {
        foreach(var L in _layers)
            if (L.p.engine == SoundEngineType.FM)
            { L.p.fmIndex = Mathf.Max(0f, L.baseFmIndex + SumModIndices(L.mods)); L.baseFmIndex = L.p.fmIndex; }
    }

    // ------------- helpers -------------
    object EnumCycle(object value, float width)
    {
        var t=value.GetType(); var names=System.Enum.GetNames(t);
        int idx=(int)System.Enum.Parse(t,value.ToString());
        if (GUILayout.Button(names[idx], GUILayout.Width(width))) idx=(idx+1)%names.Length;
        return System.Enum.Parse(t,names[idx]);
    }

    // Loop helpers
    float GetPresetDuration(SoundPreset p){ if(p==null) return 0.3f; return Mathf.Clamp(p.seconds + Mathf.Min(0.25f, p.release*0.3f), 0.05f, 10f); }
    float NextLoopPeriod() => Mathf.Clamp(60f/Mathf.Max(20f,_loopBPM), 0.2f, 10f);
    void TriggerNowAndScheduleNext(){ ApplyModulatorsToLayerPreviewOnly(_layers[_activeLayer]); AuditionAllLayers(); _lastTriggerAt=Time.unscaledTime; _scheduledNext=_lastTriggerAt+Mathf.Max(GetPresetDuration(_work), NextLoopPeriod()); }
    void StartManualOneShotSchedule(){ _lastTriggerAt=Time.unscaledTime; _scheduledNext=_loopAudition?(_lastTriggerAt+Mathf.Max(GetPresetDuration(_work),NextLoopPeriod())):-1f; }

    // Keyboard bridge (affects ACTIVE layer)
    public void SetCurrentFrequency(float freqHz)
    {
        if (_work==null) return;
        switch(_work.engine){
            case SoundEngineType.FM: _work.fmCarrier=Mathf.Clamp(freqHz,20,20000); break;
            case SoundEngineType.Additive:
            case SoundEngineType.Wavetable: _work.awFreq=Mathf.Clamp(freqHz,20,20000); break;
            case SoundEngineType.Kick: _work.kickBaseFreq=Mathf.Clamp(freqHz,20,20000); break;
        }
    }

    // Playback: all layers stacked = multi-timbral
    void AuditionAllLayers()
    {
        if (synth==null) return;
        foreach (var L in _layers) PlayLayerNow(L, -1f);
    }

    void AuditionSingle(SoundPreset p)
    {
        if (synth==null || p==null) return;
        var env = new ProceduralSynth.ADSR(p.attack, p.decay, p.sustain, p.release);
        var fx  = ProceduralSynth.VoiceFX.Default;
        fx.pan = p.pan; fx.filterEnabled=p.filterEnabled; fx.filterType=p.filterType; fx.filterCutoff=p.filterCutoff; fx.filterQ=p.filterQ;
        float secs = Mathf.Max(0.05f, p.seconds);
        switch(p.engine){
            case SoundEngineType.FM: synth.TriggerFM(Mathf.Max(20f,p.fmCarrier), Mathf.Max(0f,p.fmIndex), p.fmModRatio, secs, fx, env); break;
            case SoundEngineType.Additive:
            case SoundEngineType.Wavetable: synth.TriggerAdditive(Mathf.Max(20f,p.awFreq), p.harmonicAmps.ToArray(), secs, fx, env); break;
            case SoundEngineType.NoiseBurst: synth.TriggerSnare(); break;
            case SoundEngineType.Kick: synth.TriggerKick(); break;
        }
    }

    public void PlayLayerNow(LayerState L, float secondsOverride = -1f)
    {
        if (synth == null || L == null || L.p==null) return;
        var p = L.p;
        var env = new ProceduralSynth.ADSR(p.attack, p.decay, p.sustain, p.release);
        var fx  = ProceduralSynth.VoiceFX.Default;
        fx.pan = p.pan; fx.filterEnabled=p.filterEnabled; fx.filterType=p.filterType; fx.filterCutoff=p.filterCutoff; fx.filterQ=p.filterQ;

        float secs = (secondsOverride > 0f) ? secondsOverride : Mathf.Max(0.05f, p.seconds);

        switch (p.engine)
        {
            case SoundEngineType.FM:
            {
                float sum = SumModIndices(L.mods);
                synth.TriggerFM(Mathf.Max(20f, p.fmCarrier), Mathf.Max(0f, L.baseFmIndex + sum), p.fmModRatio, secs, fx, env);
                for (int i=0;i<L.mods.Count;i++)
                {
                    var m = L.mods[i];
                    if (m.index <= 0.0001f) continue;
                    var fxx = fx; fxx.pan = Mathf.Clamp(fx.pan + Mathf.Lerp(-0.35f, 0.35f, i/(float)Mathf.Max(1,L.mods.Count-1)), -1f, 1f);
                    float car = Mathf.Max(20f, p.fmCarrier * Mathf.Clamp(m.ratio, 0.25f, 16f));
                    float idx = Mathf.Clamp(m.index, 0f, 16f);
                    synth.TriggerFM(car, idx, p.fmModRatio, secs, fxx, env);
                }
                break;
            }
            case SoundEngineType.Additive:
            case SoundEngineType.Wavetable:
            {
                float baseFreq = Mathf.Max(20f, p.awFreq);
                List<float> amps = new List<float>(p.harmonicAmps != null ? p.harmonicAmps : new List<float>());
                if (amps.Count < 32) { while (amps.Count < 32) amps.Add(0f); }
                for (int i=0;i<L.mods.Count;i++)
                {
                    var m=L.mods[i]; if (m.index<=0f) continue;
                    int h = Mathf.Clamp(Mathf.RoundToInt(m.ratio), 1, amps.Count-1);
                    amps[h] = Mathf.Clamp01(amps[h] + m.index * 0.5f);
                }
                synth.TriggerAdditive(baseFreq, amps.ToArray(), secs, fx, env);
                break;
            }
            case SoundEngineType.NoiseBurst: synth.TriggerSnare(); break;
            case SoundEngineType.Kick:       synth.TriggerKick();  break;
        }
    }

    // Restores API used by PianoKeyboardUI / SequencerUI
    public void PlayPresetNow(SoundPreset p, float secondsOverride = -1f)
    {
        if (synth == null || p == null) return;

        int li = -1;
        for (int i = 0; i < _layers.Count; i++)
        {
            if (object.ReferenceEquals(_layers[i].p, p) || _layers[i].p.name == p.name)
            { li = i; break; }
        }

        if (li >= 0) { PlayLayerNow(_layers[li], secondsOverride); return; }

        var env = new ProceduralSynth.ADSR(p.attack, p.decay, p.sustain, p.release);
        var fx  = ProceduralSynth.VoiceFX.Default;
        fx.pan = p.pan; fx.filterEnabled = p.filterEnabled; fx.filterType = p.filterType; fx.filterCutoff = p.filterCutoff; fx.filterQ = p.filterQ;

        float secs = (secondsOverride > 0f) ? secondsOverride : Mathf.Max(0.05f, p.seconds);

        switch (p.engine)
        {
            case SoundEngineType.FM:
                synth.TriggerFM(Mathf.Max(20f, p.fmCarrier), Mathf.Max(0f, p.fmIndex), p.fmModRatio, secs, fx, env);
                break;
            case SoundEngineType.Additive:
            case SoundEngineType.Wavetable:
                synth.TriggerAdditive(Mathf.Max(20f, p.awFreq),
                                      (p.harmonicAmps != null ? p.harmonicAmps.ToArray() : new float[]{1f,0f,0f}),
                                      secs, fx, env);
                break;
            case SoundEngineType.NoiseBurst: synth.TriggerSnare(); break;
            case SoundEngineType.Kick:       synth.TriggerKick();  break;
        }
    }
}
