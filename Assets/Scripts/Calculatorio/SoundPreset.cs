using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public enum SoundEngineType { Additive, FM, Wavetable, NoiseBurst, Kick }
[Serializable] public enum FilterType     { Lowpass, Highpass, Bandpass }
[Serializable] public enum SoundTrigger   { Hover, Click, Alert }

[Serializable]
public class SoundPreset
{
    public string name = "New Sound";
    public SoundTrigger trigger = SoundTrigger.Click;  // << NEW

    public SoundEngineType engine = SoundEngineType.FM;

    // Common
    public float seconds = 1.0f;
    public float attack = 0.01f, decay = 0.1f, sustain = 0.6f, release = 0.2f;
    public float masterGain = 0.7f;

    // Stereo & Filter
    public float pan = 0f; // -1..1
    public bool  filterEnabled = false;
    public FilterType filterType = FilterType.Lowpass;
    public float filterCutoff = 1200f;
    public float filterQ = 0.707f;

    // FM
    public float fmCarrier = 220f;
    public float fmModRatio = 2.0f;
    public float fmIndex = 2.5f;

    // Additive & Wavetable
    public float awFreq = 220f;
    public List<float> harmonicAmps = new List<float>() { 1f, 0.5f, 0.33f, 0.25f };

    // Noise
    public float noiseSeconds = 0.2f;

    // Kick
    public float kickBaseFreq = 80f;
    public float kickDropStart = 180f;
    public float kickDropEnd = 35f;
    public float kickSeconds = 0.15f;

    public SoundPreset Clone()
    {
        return new SoundPreset {
            name = name + " Copy",
            trigger = trigger,
            engine = engine,
            seconds = seconds,
            attack = attack, decay = decay, sustain = sustain, release = release,
            masterGain = masterGain,
            pan = pan, filterEnabled = filterEnabled, filterType = filterType, filterCutoff = filterCutoff, filterQ = filterQ,
            fmCarrier = fmCarrier, fmModRatio = fmModRatio, fmIndex = fmIndex,
            awFreq = awFreq,
            harmonicAmps = new List<float>(harmonicAmps),
            noiseSeconds = noiseSeconds,
            kickBaseFreq = kickBaseFreq, kickDropStart = kickDropStart, kickDropEnd = kickDropEnd, kickSeconds = kickSeconds
        };
    }
}

[Serializable]
public class SoundPresetLibrary
{
    public List<SoundPreset> presets = new List<SoundPreset>();

    public static string SavePath => System.IO.Path.Combine(Application.persistentDataPath, "sounds.json");

    public void Save()
    {
        var json = JsonUtility.ToJson(this, true);
        System.IO.File.WriteAllText(SavePath, json);
#if UNITY_EDITOR
        Debug.Log($"[SoundPresetLibrary] Saved to {SavePath}");
#endif
    }

    public static SoundPresetLibrary LoadOrCreateDefault()
    {
        if (System.IO.File.Exists(SavePath))
        {
            try {
                var json = System.IO.File.ReadAllText(SavePath);
                var lib = JsonUtility.FromJson<SoundPresetLibrary>(json);
                if (lib != null && lib.presets != null) return lib;
            } catch { }
        }
        var d = new SoundPresetLibrary();
        d.presets.Add(new SoundPreset { name = "Laser FM", trigger = SoundTrigger.Click, engine = SoundEngineType.FM, seconds = 0.3f, fmCarrier = 800f, fmModRatio = 1.0f, fmIndex = 2.0f, attack = 0.001f, decay = 0.05f, sustain = 0.2f, release = 0.06f, filterEnabled=true, filterType=FilterType.Highpass, filterCutoff=600f, filterQ=0.8f, pan = -0.2f });
        d.presets.Add(new SoundPreset { name = "Brassy Additive", trigger = SoundTrigger.Hover, engine = SoundEngineType.Additive, awFreq = 220f, seconds = 1.2f, harmonicAmps = new List<float>{1f,0.5f,0.33f,0.25f,0.2f}, sustain=0.5f, filterEnabled=true, filterType=FilterType.Lowpass, filterCutoff=2200f, filterQ=0.7f, pan=0.1f });
        d.presets.Add(new SoundPreset { name = "Kick", trigger = SoundTrigger.Alert, engine = SoundEngineType.Kick, kickBaseFreq = 80f, kickDropStart = 180f, kickDropEnd = 35f, kickSeconds = 0.15f, attack=0.001f, decay=0.01f, sustain=0f, release=0.1f, pan=0f });
        d.Save();
        return d;
    }
}
