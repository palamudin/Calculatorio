using UnityEngine;
using System.Collections.Generic;

public class PianoKeyboardUI : MonoBehaviour
{
    [Header("Wiring")]
    public AudioMenuUI host;            // set by AudioMenuUI before DrawEmbedded
    public bool playOnPress = true;
    public float noteLen = 0.20f;

    [Header("Piano")]
    [Range(0,8)] public int baseOctave = 3;          // starting octave (C3 default)
    [Range(1,4)] public int octavesVisible = 4;      // 4 by default to reduce key width

    // Extra: chromatic key mapping (numbers + letters)
    // This covers a long run of semitones for fast chord coverage.
    KeyCode[] chromaticKeys = new KeyCode[]
    {
        // number row (10)
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
        KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0,
        // top row (12-ish)
        KeyCode.Q,KeyCode.W,KeyCode.E,KeyCode.R,KeyCode.T,KeyCode.Y,KeyCode.U,KeyCode.I,KeyCode.O,KeyCode.P,KeyCode.LeftBracket,KeyCode.RightBracket,
        // home row (11)
        KeyCode.A,KeyCode.S,KeyCode.D,KeyCode.F,KeyCode.G,KeyCode.H,KeyCode.J,KeyCode.K,KeyCode.L,KeyCode.Semicolon,KeyCode.Quote,
        // bottom row (7)
        KeyCode.Z,KeyCode.X,KeyCode.C,KeyCode.V,KeyCode.B,KeyCode.N,KeyCode.M
    };

    // state
    bool _activeThisFrame = false;
    HashSet<int> _held = new HashSet<int>();

    // visuals
    Color cWhite = new Color(0.92f,0.92f,0.92f,1f), cWhiteEdge=new Color(0.65f,0.65f,0.65f,1f), cBlack=new Color(0.12f,0.12f,0.12f,1f), cPress=new Color(0.35f,0.65f,1f,0.35f);
    Texture2D _px; Texture2D Px(){ if(_px==null){ _px=new Texture2D(1,1,TextureFormat.RGBA32,false); _px.SetPixel(0,0,Color.white); _px.Apply(); } return _px; }

    void LateUpdate(){ _activeThisFrame = false; } // only active when DrawEmbedded is called

    public void DrawEmbedded(Rect area)
    {
        if (host == null || !host.IsEditorOpen) return;
        _activeThisFrame = true;

        GUI.BeginGroup(area, GUI.skin.box);
        GUILayout.BeginArea(new Rect(8,8,area.width-16,area.height-16));

        // Controls row: Base Octave + Visible Octaves (field + knob)
        GUILayout.BeginHorizontal();
        GUILayout.Label("Base Octave", GUILayout.Width(92));
        baseOctave = IntField(baseOctave, 0, 8, 36);
        float bo = baseOctave; KnobTiny(ref bo, 0f, 8f, 32f); baseOctave = Mathf.RoundToInt(bo);
        GUILayout.Space(16);

        GUILayout.Label("Visible", GUILayout.Width(60));
        octavesVisible = IntField(octavesVisible, 1, 4, 32);
        float ov = octavesVisible; KnobTiny(ref ov, 1f, 4f, 32f); octavesVisible = Mathf.Clamp(Mathf.RoundToInt(ov),1,4);

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("Click keys or use numbers + letters for chromatic play. (Held keys highlight)");        

        // Process keyboard chromatic mapping
        ProcessChromaticKeys();

        // Draw piano + handle mouse
        Rect kb = GUILayoutUtility.GetRect(area.width-16, area.height-72, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        DrawPiano(kb);
        HandleMouse(kb);

        GUILayout.EndArea();
        GUI.EndGroup();
    }

    void ProcessChromaticKeys()
    {
        if(!_activeThisFrame) return;
        int baseMidi = 12*(baseOctave+1); // C of base octave
        for (int i=0;i<chromaticKeys.Length;i++)
        {
            if (Input.GetKeyDown(chromaticKeys[i])) PlayMidi(baseMidi + i);
            if (Input.GetKeyUp(chromaticKeys[i])) _held.Remove(baseMidi + i);
        }
    }

    void PlayMidi(int midi)
    {
        if (host == null || !host.IsEditorOpen) return;

        float f = 440f * Mathf.Pow(2f,(midi-69)/12f);
        _held.Add(midi);

        // write frequency into the WORK preset
        host.SetCurrentFrequency(Mathf.Clamp(f,20f,20000f));

        // audition the *current edited* preset with overridden duration
        if (playOnPress)
        {
            var p = host.CurrentPreset;
            if (p != null) host.PlayPresetNow(p, Mathf.Max(0.05f, noteLen));
        }
    }

    // Drawing / hit testing
    void DrawPiano(Rect kb)
    {
        int whitePerOct=7, whiteCount=whitePerOct*octavesVisible;
        float wW=Mathf.Max(10f, kb.width/whiteCount);   // ensure narrower keys by showing more octaves
        float bH=kb.height*0.62f, bW=wW*0.62f;

        // whites
        for(int o=0;o<octavesVisible;o++)
        for(int w=0;w<whitePerOct;w++)
        {
            int gi=o*whitePerOct+w; Rect r=new Rect(kb.x+gi*wW, kb.y, wW-1, kb.height);
            int midi=White2Midi(o,w);
            GUI.color=cWhite; GUI.DrawTexture(r,Px()); GUI.color=Color.white;
            if (_held.Contains(midi)){ GUI.color=cPress; GUI.DrawTexture(r,Px()); GUI.color=Color.white; }
            GUI.color=cWhiteEdge;
            GUI.DrawTexture(new Rect(r.x,r.y,r.width,1),Px()); GUI.DrawTexture(new Rect(r.x,r.yMax-1,r.width,1),Px());
            GUI.DrawTexture(new Rect(r.x,r.y,1,r.height),Px()); GUI.DrawTexture(new Rect(r.xMax-1,r.y,1,r.height),Px());
            GUI.color=Color.white;
        }
        // blacks
        for(int o=0;o<octavesVisible;o++)
        {
            int[] pos={0,1,3,4,5};
            foreach(int bp in pos)
            {
                float cx=kb.x+(o*whitePerOct+bp+1)*wW;
                Rect r=new Rect(cx-bW*0.5f, kb.y, bW, bH);
                int midi=Black2Midi(o,bp);
                GUI.color=cBlack; GUI.DrawTexture(r,Px()); GUI.color=Color.white;
                if (_held.Contains(midi)){ GUI.color=cPress; GUI.DrawTexture(r,Px()); GUI.color=Color.white; }
            }
        }
    }

    void HandleMouse(Rect kb)
    {
        var e=Event.current; if(e==null) return; if(!kb.Contains(e.mousePosition)) return;
        if(e.type!=EventType.MouseDown && e.type!=EventType.MouseUp) return;
        int midi = Mouse2Midi(kb, e.mousePosition); if (midi<0) return;
        if (e.type==EventType.MouseDown) PlayMidi(midi); else _held.Remove(midi);
        e.Use();
    }

    // helpers
    static readonly int[] WHITE2={0,2,4,5,7,9,11};
    int White2Midi(int o,int w){ int baseC=12*(baseOctave+o+1); return baseC+WHITE2[Mathf.Clamp(w,0,6)]; }
    int Black2Midi(int o,int bp){ int baseC=12*(baseOctave+o+1); switch(bp){ case 0: return baseC+1; case 1: return baseC+3; case 3: return baseC+6; case 4: return baseC+8; case 5: return baseC+10; default: return baseC+1; } }

    int Mouse2Midi(Rect kb, Vector2 p)
    {
        int whitePerOct=7, whiteCount=whitePerOct*octavesVisible;
        float wW=Mathf.Max(10f, kb.width/whiteCount), bH=kb.height*0.62f, bW=wW*0.62f;

        // blacks first
        for(int o=0;o<octavesVisible;o++)
        {
            int[] pos={0,1,3,4,5};
            foreach(int bp in pos)
            { float cx=kb.x+(o*whitePerOct+bp+1)*wW; Rect r=new Rect(cx-bW*0.5f,kb.y,bW,bH); if(r.Contains(p)) return Black2Midi(o,bp); }
        }
        int idx=Mathf.Clamp(Mathf.FloorToInt((p.x-kb.x)/wW),0,whiteCount-1);
        int oIdx=idx/whitePerOct; int wIdx=idx%whitePerOct; return White2Midi(oIdx,wIdx);
    }

    int IntField(int v,int min,int max,float w){ string s=GUILayout.TextField(v.ToString(), GUILayout.Width(w)); if(int.TryParse(s,out int nv)) v=Mathf.Clamp(nv,min,max); return v; }

    void KnobTiny(ref float value, float min, float max, float size)
    {
        Rect r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        int id = GUIUtility.GetControlID(FocusType.Passive);
        var e = Event.current;
        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (r.Contains(e.mousePosition)) { GUIUtility.hotControl = id; e.Use(); }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id)
                {
                    float norm = Mathf.InverseLerp(min, max, value);
                    norm -= e.delta.y * 0.006f;
                    norm += e.delta.x * 0.004f;
                    value = Mathf.Lerp(min, max, Mathf.Clamp01(norm));
                    e.Use();
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id) { GUIUtility.hotControl = 0; e.Use(); }
                break;
        }
        // face + notch
        GUI.color = new Color(0.34f,0.34f,0.34f,1f); GUI.DrawTexture(r, Px()); GUI.color = Color.white;
        float t = Mathf.InverseLerp(min, max, value);
        float angleDeg = Mathf.Lerp(-135f, 135f, t);
        Vector2 pivot = r.center;
        Matrix4x4 prev = GUI.matrix;
        GUIUtility.RotateAroundPivot(angleDeg, pivot);
        GUI.color = Color.black;
        float notchW = 2f, notchL = r.height * 0.38f;
        Rect notch = new Rect(pivot.x - notchW * 0.5f, pivot.y - notchL + 5f, notchW, notchL);
        GUI.DrawTexture(notch, Px());
        GUI.color = Color.white;
        GUI.matrix = prev;
    }
}
