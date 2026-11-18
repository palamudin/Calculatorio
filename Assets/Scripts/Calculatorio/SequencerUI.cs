using UnityEngine;
using System;
using System.Collections.Generic;

/// Embedded multi-channel sequencer with local BPM and Files/Channels.
/// Triggers bank presets per channel via host.PlayPresetNow().
public class SequencerUI : MonoBehaviour
{
    // --------- NEW: host back-reference (used by AudioMenuUI) ----------
    [Header("Wiring")]
    public AudioMenuUI host; // AudioMenuUI sets this before calling DrawEmbedded

    [Header("Clock")]
    [Range(30,300)] public int bpm = 120;
    public bool playing = false;
    [Range(1,8)]  public int bars = 2;
    [Range(4,32)] public int stepsPerBar = 16;

    [Header("Look")]
    public float cellSpacing = 2f;
    public Color panel = new Color(0.10f,0.10f,0.12f,1f);
    public Color grid1 = new Color(0.18f,0.18f,0.20f,1f);
    public Color grid2 = new Color(0.13f,0.13f,0.15f,1f);
    public Color beatLine = new Color(1,1,1,0.06f);
    public Color barLine  = new Color(1,1,1,0.10f);
    public Color playhead = new Color(0.30f,0.65f,1f,0.45f);

    static readonly Color[] TRACKS =
    {
        new Color(0.78f,0.39f,0.92f,1f), new Color(0.88f,0.64f,0.18f,1f),
        new Color(0.24f,0.78f,0.82f,1f), new Color(0.35f,0.52f,0.96f,1f),
        new Color(0.94f,0.40f,0.40f,1f), new Color(0.56f,0.84f,0.36f,1f),
        new Color(0.90f,0.52f,0.73f,1f), new Color(0.94f,0.75f,0.40f,1f)
    };

    [Serializable] public class Channel {
        public int presetIndex=-1; public string presetName="";
        public bool[] on; public float[] vel;
        public Channel(int steps){ on=new bool[steps]; vel=new float[steps]; for(int i=0;i<steps;i++) vel[i]=0.8f; }
        public Channel Clone(){ var c=new Channel(on.Length); Array.Copy(on,c.on,on.Length); Array.Copy(vel,c.vel,vel.Length); c.presetIndex=presetIndex; c.presetName=presetName; return c; }
    }
    [Serializable] public class Clip {
        public string name="Clip"; public int bars=2; public int spb=16; public List<Channel> channels=new List<Channel>();
        public Clip Snapshot(SequencerUI q){ name="Clip "+DateTime.Now.ToString("HH:mm:ss"); bars=q.bars; spb=q.stepsPerBar; channels.Clear(); foreach(var ch in q.channels) channels.Add(ch.Clone()); return this; }
    }

    public List<Channel> channels = new List<Channel>();
    List<Clip> _clips = new List<Clip>();
    int _selClip=-1, _selCh=0;

    int _step=0; float _acc=0f; bool _activeThisFrame=false;

    Texture2D _px; Texture2D Px(){ if(_px==null){ _px=new Texture2D(1,1,TextureFormat.RGBA32,false); _px.SetPixel(0,0,Color.white); _px.Apply(); } return _px; }
    SoundPresetLibrary _lib;

    void Awake()
    {
        _lib = SoundPresetLibrary.LoadOrCreateDefault();
        if (channels.Count==0) channels.Add(new Channel(TotalSteps()));
        CachePresetNames();
    }
    void LateUpdate(){ _activeThisFrame=false; }

    int TotalSteps() => Mathf.Max(1, bars*stepsPerBar);
    void EnsureSize()
    {
        int steps = TotalSteps();
        foreach(var ch in channels)
        {
            if (ch.on==null || ch.on.Length!=steps)
            {
                var oldOn = ch.on; var oldVel = ch.vel;
                ch.on=new bool[steps]; ch.vel=new float[steps];
                if (oldOn!=null){ int c=Mathf.Min(steps,oldOn.Length); Array.Copy(oldOn,ch.on,c); Array.Copy(oldVel,ch.vel,c); }
                for(int i=0;i<steps;i++) if(ch.vel[i]<=0f) ch.vel[i]=0.8f;
            }
        }
    }
    void CachePresetNames()
    {
        for (int i=0;i<channels.Count;i++)
        {
            var ch=channels[i];
            if (_lib!=null && ch.presetIndex>=0 && ch.presetIndex<_lib.presets.Count) ch.presetName=_lib.presets[ch.presetIndex].name;
        }
    }

    public void DrawEmbedded(Rect area)
    {
        // Safety: allow AudioMenuUI to forget to assign; soft-find once.
        if (host == null) host = FindObjectOfType<AudioMenuUI>();
        if (host == null || !host.IsEditorOpen) return;

        _activeThisFrame=true; EnsureSize();

        // Local clock runs only while embedded
        if (playing)
        {
            float spb = 60f / Mathf.Max(30,bpm);
            float stepDur = spb * 4f / stepsPerBar;
            _acc += Time.unscaledDeltaTime;
            while (_acc >= stepDur)
            {
                _acc -= stepDur; _step = (_step+1) % TotalSteps();

                // Trigger channels on this step
                for (int ci=0; ci<channels.Count; ci++)
                {
                    var ch = channels[ci];
                    if (!ch.on[_step]) continue;

                    SoundPreset p = null;
                    if (_lib != null && ch.presetIndex >= 0 && ch.presetIndex < _lib.presets.Count)
                        p = _lib.presets[ch.presetIndex];
                    else
                        p = host.CurrentPreset; // fallback to edited preset

                    if (p != null) host.PlayPresetNow(p, stepDur);
                }
            }
        }

        // UI container
        GUI.BeginGroup(area, GUI.skin.box);
        GUI.color = panel; GUI.DrawTexture(new Rect(0,0,area.width,area.height), Px()); GUI.color = Color.white;

        float leftW = Mathf.Min(300f, area.width*0.35f);
        Rect left = new Rect(8,8, leftW-12, area.height-16);
        Rect right= new Rect(leftW+4,8, area.width-leftW-12, area.height-16);

        DrawLeft(left);
        DrawRight(right);

        GUI.EndGroup();
    }

    enum LeftTab { Files, Channels } LeftTab _tab=LeftTab.Files;
    Vector2 _scrollFiles, _scrollCh;
    int _dragFrom=-1;

    void DrawLeft(Rect r)
    {
        GUI.BeginGroup(r, GUI.skin.box);
        GUILayout.BeginArea(new Rect(6,6,r.width-12,r.height-12));

        GUILayout.BeginHorizontal();
        if (Tab("Files", _tab==LeftTab.Files)) _tab=LeftTab.Files;
        if (Tab("Sequencer Channels", _tab==LeftTab.Channels)) _tab=LeftTab.Channels;
        GUILayout.EndHorizontal();
        GUILayout.Space(6);

        if (_tab==LeftTab.Files)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Current", GUILayout.Width(120))) { var c=new Clip().Snapshot(this); _clips.Add(c); _selClip=_clips.Count-1; }
            GUI.enabled = _selClip>=0 && _selClip<_clips.Count;
            if (GUILayout.Button("Remove", GUILayout.Width(90))) { _clips.RemoveAt(_selClip); _selClip=Mathf.Clamp(_selClip-1,-1,_clips.Count-1); }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            _scrollFiles = GUILayout.BeginScrollView(_scrollFiles, false, true);
            for(int i=0;i<_clips.Count;i++)
            {
                var row = GUILayoutUtility.GetRect(10,26, GUILayout.ExpandWidth(true));
                GUI.color = (i==_selClip)? new Color(0.3f,0.5f,0.9f,0.25f) : new Color(1,1,1,0.08f);
                GUI.DrawTexture(row,Px()); GUI.color=Color.white;

                Rect grab=new Rect(row.x+4,row.y+4,18,18);
                GUI.color=new Color(1,1,1,0.2f); GUI.DrawTexture(grab,Px()); GUI.color=Color.white;
                GUI.Label(new Rect(grab.x+3,grab.y-1,grab.width,grab.height),"≡");
                _clips[i].name = GUI.TextField(new Rect(row.x+28,row.y+3,row.width-32,20), _clips[i].name);

                if (Event.current.type==EventType.MouseDown && row.Contains(Event.current.mousePosition)){ _selClip=i; Event.current.Use(); }
                if (row.Contains(Event.current.mousePosition) && Event.current.type==EventType.MouseDown) _dragFrom=i;
                if (_dragFrom>=0 && Event.current.type==EventType.MouseDrag)
                { int over=i; if (over!=_dragFrom){ var tmp=_clips[_dragFrom]; _clips.RemoveAt(_dragFrom); _clips.Insert(over,tmp); _dragFrom=over; } Event.current.Use(); }
                if (Event.current.type==EventType.MouseUp) _dragFrom=-1;
            }
            GUILayout.EndScrollView();
        }
        else
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Sound", GUILayout.Width(110)))
            {
                var ch=new Channel(TotalSteps());
                if (_lib!=null && _lib.presets.Count>0){ ch.presetIndex=0; ch.presetName=_lib.presets[0].name; }
                channels.Add(ch); _selCh=channels.Count-1;
            }
            GUI.enabled = channels.Count>0;
            if (GUILayout.Button("Remove Selected", GUILayout.Width(140)))
            { channels.RemoveAt(_selCh); _selCh=Mathf.Clamp(_selCh-1,0,Mathf.Max(0,channels.Count-1)); }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            _scrollCh = GUILayout.BeginScrollView(_scrollCh, false, true);
            for(int i=0;i<channels.Count;i++)
            {
                var ch=channels[i];
                var row = GUILayoutUtility.GetRect(10,28, GUILayout.ExpandWidth(true));
                GUI.color = (i==_selCh)? new Color(0.3f,0.5f,0.9f,0.25f) : new Color(1,1,1,0.08f);
                GUI.DrawTexture(row,Px()); GUI.color=Color.white;

                var sw=TRACKS[i%TRACKS.Length]; GUI.color=sw; GUI.DrawTexture(new Rect(row.x+6,row.y+5,18,18),Px()); GUI.color=Color.white;

                int idx=ch.presetIndex; string label=(idx>=0 && _lib!=null && idx<_lib.presets.Count)? _lib.presets[idx].name : "(none)";
                if (GUI.Button(new Rect(row.x+28,row.y+4,row.width-34,20), label))
                {
                    if (_lib!=null && _lib.presets.Count>0){ idx=(idx+1)%_lib.presets.Count; ch.presetIndex=idx; ch.presetName=_lib.presets[idx].name; }
                }

                if (Event.current.type==EventType.MouseDown && row.Contains(Event.current.mousePosition)){ _selCh=i; Event.current.Use(); }
            }
            GUILayout.EndScrollView();
        }

        GUILayout.EndArea();
        GUI.EndGroup();
    }

    void DrawRight(Rect r)
    {
        GUI.BeginGroup(r, GUI.skin.box);
        GUILayout.BeginArea(new Rect(6,6,r.width-12,r.height-12));

        GUILayout.BeginHorizontal();
        bool newPlaying = GUILayout.Toggle(playing, playing? "■ Stop":"▶ Play", GUI.skin.button, GUILayout.Width(90));
        if (newPlaying!=playing){ playing=newPlaying; if(playing) _acc=0f; }
        GUILayout.Space(10);
        GUILayout.Label("BPM", GUILayout.Width(34));
        string bpmText = GUILayout.TextField(bpm.ToString(), GUILayout.Width(46)); if(int.TryParse(bpmText,out int nb)) bpm=Mathf.Clamp(nb,30,300);
        GUILayout.Space(12); GUILayout.Label("Bars", GUILayout.Width(34));
        int nbars = IntField(bars,1,8,40); GUILayout.Space(10); GUILayout.Label("Steps/Bar", GUILayout.Width(76));
        int nspb = IntField(stepsPerBar,4,32,44);
        if (nbars!=bars || nspb!=stepsPerBar){ bars=nbars; stepsPerBar=nspb; EnsureSize(); _step=0; }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        if (channels.Count==0){ GUILayout.Label("Add a channel on the left to begin."); GUILayout.EndArea(); GUI.EndGroup(); return; }
        _selCh = Mathf.Clamp(_selCh,0,channels.Count-1);
        var ch = channels[_selCh];

        int total = TotalSteps();
        float cellW = Mathf.Max(12f, (r.width - 24f - (total+1)*cellSpacing)/total);
        float cellH = Mathf.Max(64f, r.height - 70f);
        Rect grid = GUILayoutUtility.GetRect(r.width-24f, cellH, GUILayout.ExpandWidth(true));
        grid = new Rect(grid.x, grid.y, r.width-24f, cellH);

        GUI.color=grid1; GUI.DrawTexture(new Rect(grid.x+cellSpacing, grid.y+cellSpacing, grid.width-2*cellSpacing, grid.height-2*cellSpacing), Px()); GUI.color=Color.white;

        for(int s=0;s<=total;s++){
            float x=grid.x+cellSpacing+s*(cellW+cellSpacing);
            Color c=(s%stepsPerBar==0)?barLine:(s%(stepsPerBar/4)==0?beatLine:new Color(0,0,0,0));
            if(c.a>0){ GUI.color=c; GUI.DrawTexture(new Rect(x-0.75f,grid.y+cellSpacing,1.5f,grid.height-2*cellSpacing), Px()); GUI.color=Color.white; }
        }

        Event e=Event.current; bool md=e.type==EventType.MouseDown, dr=e.type==EventType.MouseDrag, mu=e.type==EventType.MouseUp;
        bool painting=false, paintState=false, velMode=false; if(md||dr) velMode=e.shift;

        for(int s=0;s<total;s++)
        {
            Rect cell = new Rect(grid.x+cellSpacing+s*(cellW+cellSpacing), grid.y+cellSpacing, cellW, grid.height-2*cellSpacing);

            GUI.color=new Color(1,1,1,0.05f);
            GUI.DrawTexture(new Rect(cell.x,cell.y,cell.width,1),Px());
            GUI.DrawTexture(new Rect(cell.x,cell.yMax-1,cell.width,1),Px());
            GUI.DrawTexture(new Rect(cell.x,cell.y,1,cell.height),Px());
            GUI.DrawTexture(new Rect(cell.xMax-1,cell.y,1,cell.height),Px());
            GUI.color=Color.white;

            if (ch.on[s])
            {
                float v=Mathf.Clamp01(ch.vel[s]);
                var baseCol=TRACKS[_selCh%TRACKS.Length];
                var c=Color.Lerp(new Color(baseCol.r*0.25f,baseCol.g*0.25f,baseCol.b*0.25f,1f), baseCol, 0.55f+v*0.45f);
                c.a=0.95f; GUI.color=c;
                Rect inner=new Rect(cell.x+1.5f, cell.y+1.5f + (1f-v)*(cell.height-3f), cell.width-3f, v*(cell.height-3f));
                GUI.DrawTexture(inner,Px()); GUI.color=Color.white;
            }

            if (playing && s==_step){ Rect ph=new Rect(cell.x-1.5f, cell.y, 3f, cell.height); GUI.color=playhead; GUI.DrawTexture(ph,Px()); GUI.color=Color.white; }

            if (md && cell.Contains(e.mousePosition))
            {
                painting=true; paintState=!ch.on[s];
                if (velMode){ float v=Mathf.Clamp01(1f-(e.mousePosition.y-cell.y)/cell.height); ch.on[s]=true; ch.vel[s]=v; }
                else { ch.on[s]=paintState; if(ch.on[s] && ch.vel[s]<=0f) ch.vel[s]=0.8f; }
                e.Use();
            }
            else if (dr && cell.Contains(e.mousePosition))
            {
                if (velMode){ float v=Mathf.Clamp01(1f-(e.mousePosition.y-cell.y)/cell.height); ch.on[s]=true; ch.vel[s]=v; }
                else if (painting){ ch.on[s]=paintState; }
            }
        }
        if (mu) { /* end paint */ }

        GUILayout.EndArea();
        GUI.EndGroup();
    }

    bool Tab(string t,bool on){ var prev=GUI.color; GUI.color= on? Color.white:new Color(1,1,1,0.6f); bool b=GUILayout.Button(t,GUILayout.Height(22)); GUI.color=prev; return b; }
    int IntField(int v,int min,int max,float w){ string s=GUILayout.TextField(v.ToString(), GUILayout.Width(w)); if(int.TryParse(s,out int nv)) v=Mathf.Clamp(nv,min,max); return v; }
}
