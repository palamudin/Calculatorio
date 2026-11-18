using UnityEngine;
using System;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class ProceduralSynth : MonoBehaviour
{
    [Range(0f, 1f)] public float masterGain = 0.6f; // treated as SFX volume by the UI
    public int maxVoices = 16;

    int _sampleRate;
    List<Voice> _voices = new List<Voice>();

    // --- Stop/Panic state ---
    bool _stopping = false;
    float _stopRemain = 0f;
    float _stopTotal = 0f;

    // Unity tanh workaround
    static float Tanhf(float x) => (float)System.Math.Tanh((double)x);

    void Awake()
    {
        _sampleRate = AudioSettings.outputSampleRate;
        var src = GetComponent<AudioSource>();
        src.playOnAwake = true;
        src.loop = true;
        var clip = AudioClip.Create("silent", _sampleRate, 1, _sampleRate, false);
        src.clip = clip;
        src.Play();
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        Array.Clear(data, 0, data.Length);

        // Render normal voices
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            var v = _voices[i];
            if (!v.active) { _voices.RemoveAt(i); continue; }
            v.Render(data, channels, masterGain);
            if (!v.active) _voices.RemoveAt(i);
        }

        // Live preview (continuous)
        if (_live != null) _live.Render(data, channels, masterGain);

        // Fade-out if stopping
        if (_stopping)
        {
            int frames = data.Length / channels;
            float blockSeconds = (float)frames / _sampleRate;

            float g0 = Mathf.Clamp01(_stopRemain / _stopTotal);
            _stopRemain -= blockSeconds;
            float g1 = Mathf.Clamp01(_stopRemain / _stopTotal);

            float g = 0.5f * (g0 + g1);
            if (g < 1f) for (int i = 0; i < data.Length; i++) data[i] *= g;

            if (_stopRemain <= 0f) { _stopping = false; _voices.Clear(); }
        }
    }

    // -------- Stop API --------
    public void StopAll(float fadeSeconds = 0.03f)
    {
        if (_voices.Count == 0 && _live == null) return;
        _stopping = true;
        _stopTotal = Mathf.Max(0.001f, fadeSeconds);
        _stopRemain = _stopTotal;
        _live = null; // also stop live immediately
    }
    public void KillAll()
    {
        _stopping = false;
        _stopRemain = _stopTotal = 0f;
        _voices.Clear();
        _live = null;
    }

    // ---------- Public Trigger API ----------
    public struct VoiceFX
    {
        public float pan;           // -1..1
        public bool filterEnabled;
        public FilterType filterType;
        public float filterCutoff;  // Hz
        public float filterQ;       // 0.5..10
        public static VoiceFX Default => new VoiceFX { pan = 0f, filterEnabled = false, filterType = FilterType.Lowpass, filterCutoff = 1200f, filterQ = 0.707f };
    }

    // Old API
    public void TriggerAdditive(float freq, float[] harmonicAmps, float seconds, ADSR adsr = null)
        => TriggerAdditive(freq, harmonicAmps, seconds, VoiceFX.Default, adsr);
    public void TriggerFM(float carrierFreq, float modIndex, float modRatio, float seconds, ADSR adsr = null)
        => TriggerFM(carrierFreq, modIndex, modRatio, seconds, VoiceFX.Default, adsr);
    public void TriggerAM(float carrierFreq, float modFreq, float modDepth, float seconds, ADSR adsr = null)
        => TriggerAM(carrierFreq, modFreq, modDepth, seconds, VoiceFX.Default, adsr);
    public void TriggerWavetable(float freq, float[] harmonicAmps, float seconds, ADSR adsr = null, int tableSize = 2048)
        => TriggerWavetable(freq, harmonicAmps, seconds, VoiceFX.Default, adsr, tableSize);

    // New API (with FX)
    public void TriggerAdditive(float freq, float[] harmonicAmps, float seconds, VoiceFX fx, ADSR adsr = null)
    {
        if (_voices.Count >= maxVoices) return;
        _voices.Add(Voice.Additive(freq, harmonicAmps, seconds, adsr, _sampleRate, fx));
    }
    public void TriggerFM(float carrierFreq, float modIndex, float modRatio, float seconds, VoiceFX fx, ADSR adsr = null)
    {
        if (_voices.Count >= maxVoices) return;
        _voices.Add(Voice.FM(carrierFreq, modIndex, modRatio, seconds, adsr, _sampleRate, fx));
    }
    public void TriggerAM(float carrierFreq, float modFreq, float modDepth, float seconds, VoiceFX fx, ADSR adsr = null)
    {
        if (_voices.Count >= maxVoices) return;
        _voices.Add(Voice.AM(carrierFreq, modFreq, modDepth, seconds, adsr, _sampleRate, fx));
    }
    public void TriggerWavetable(float freq, float[] harmonicAmps, float seconds, VoiceFX fx, ADSR adsr = null, int tableSize = 2048)
    {
        if (_voices.Count >= maxVoices) return;
        _voices.Add(Voice.Wavetable(freq, harmonicAmps, seconds, adsr, _sampleRate, tableSize, fx));
    }

    public void TriggerLaser()
    {
        var env = ADSR.Fast(0.001f, 0.02f, 0.4f, 0.06f);
        var fx = VoiceFX.Default; fx.filterEnabled = true; fx.filterType = FilterType.Highpass; fx.filterCutoff = 600f; fx.pan = -0.2f;
        TriggerFM(800f, 2.0f, 1.0f, 0.15f, fx, env);
    }
    public void TriggerKick()
    {
        if (_voices.Count >= maxVoices) return;
        var env = new ADSR(0.001f, 0.01f, 0.0f, 0.12f);
        var fx = VoiceFX.Default;
        _voices.Add(Voice.DecaySineKick(80f, 180f, 35f, 0.15f, env, _sampleRate, fx));
    }
    public void TriggerSnare()
    {
        if (_voices.Count >= maxVoices) return;
        var env = ADSR.Fast(0.001f, 0.03f, 0f, 0.10f);
        var fx = VoiceFX.Default; fx.filterEnabled = true; fx.filterType = FilterType.Highpass; fx.filterCutoff = 3000f;
        _voices.Add(Voice.NoiseBurst(0.12f, env, _sampleRate, fx));
    }

    // ---------- ADSR, Biquad, Voices ----------
    public class ADSR
    {
        public float A, D, S, R;
        float _t, _gate = 1f, _releaseStartTime;
        public ADSR(float attack, float decay, float sustainLevel, float release)
        { A = Mathf.Max(0f, attack); D = Mathf.Max(0f, decay); S = Mathf.Clamp01(sustainLevel); R = Mathf.Max(0f, release); }
        public static ADSR Fast(float a, float d, float s, float r) => new ADSR(a, d, s, r);
        public void Release(float tNow) { _gate = 0f; _releaseStartTime = tNow; }
        public float Eval(float t, out bool active)
        {
            _t = t;
            if (_gate > .5f)
            {
                if (t < A) { active = true; return (A <= 1e-6f) ? 1f : (t / A); }
                t -= A;
                if (t < D) { active = true; return Mathf.Lerp(1f, S, t / Mathf.Max(1e-6f, D)); }
                active = true; return S;
            }
            float tr = Mathf.Max(0f, _t - _releaseStartTime);
            float levelAtRelease = EvalAt(_releaseStartTime);
            float env = Mathf.Lerp(levelAtRelease, 0f, tr / Mathf.Max(1e-6f, R));
            active = tr < R && env > 1e-5f; return Mathf.Max(0f, env);
        }
        float EvalAt(float t)
        {
            if (t < A) return (A <= 1e-6f) ? 1f : (t / A);
            t -= A; if (t < D) return Mathf.Lerp(1f, S, t / Mathf.Max(1e-6f, D));
            return S;
        }
    }

    class Biquad
    {
        float a0, a1, a2, b0, b1, b2;
        float x1, x2, y1, y2;
        public void Set(FilterType type, float cutoffHz, float q, int sampleRate)
        {
            cutoffHz = Mathf.Clamp(cutoffHz, 30f, sampleRate * 0.45f);
            q = Mathf.Clamp(q, 0.1f, 20f);
            float w0 = 2f * Mathf.PI * (cutoffHz / sampleRate);
            float cos = Mathf.Cos(w0);
            float sin = Mathf.Sin(w0);
            float alpha = sin / (2f * q);
            switch (type)
            {
                case FilterType.Lowpass:  b0=(1f-cos)*.5f; b1=1f-cos; b2=(1f-cos)*.5f; a0=1f+alpha; a1=-2f*cos; a2=1f-alpha; break;
                case FilterType.Highpass: b0=(1f+cos)*.5f; b1=-(1f+cos); b2=(1f+cos)*.5f; a0=1f+alpha; a1=-2f*cos; a2=1f-alpha; break;
                default:                  b0=sin*.5f; b1=0f; b2=-sin*.5f; a0=1f+alpha; a1=-2f*cos; a2=1f-alpha; break;
            }
            b0/=a0; b1/=a0; b2/=a0; a1/=a0; a2/=a0; a0=1f;
        }
        public float Process(float x)
        {
            float y = b0*x + b1*x1 + b2*x2 - a1*y1 - a2*y2;
            x2=x1; x1=x; y2=y1; y1=y; return y;
        }
    }

    abstract class Voice
    {
        protected int sampleRate;
        protected float t;
        protected float duration;
        protected ADSR env;
        public bool active = true;

        protected float pan; bool useFilter; Biquad biquad;
        protected float gL => Mathf.Cos((pan + 1f) * Mathf.PI * 0.25f);
        protected float gR => Mathf.Sin((pan + 1f) * Mathf.PI * 0.25f);

        protected Voice(float seconds, ADSR adsr, int sr, ProceduralSynth.VoiceFX fx)
        {
            duration = seconds; env = adsr ?? new ADSR(0.005f, 0.05f, 0.8f, 0.1f); sampleRate = sr;
            pan = Mathf.Clamp(fx.pan, -1f, 1f);
            useFilter = fx.filterEnabled;
            if (useFilter) { biquad = new Biquad(); biquad.Set(fx.filterType, fx.filterCutoff, fx.filterQ, sampleRate); }
        }

        public void Render(float[] data, int channels, float masterGain)
        {
            if (!active) return;
            int frames = data.Length / channels; float dt = 1f / sampleRate;
            for (int i = 0; i < frames; i++)
            {
                if (t > duration && env != null) env.Release(t);
                bool envActive = true; float amp = env != null ? env.Eval(t, out envActive) : 1f;
                float s = Sample(t); if (useFilter) s = biquad.Process(s);
                s = Tanhf(s * 2.0f) * amp * masterGain;
                float l = s * gL, r = s * gR;
                if (channels == 1) data[i] += (l + r) * 0.7071f;
                else { int idx = i * channels; data[idx] += l; data[idx + 1] += r; }
                t += dt; if (!envActive) { active = false; break; }
            }
        }

        protected abstract float Sample(float time);

        public static Voice Additive (float f, float[] a, float s, ADSR e, int sr, ProceduralSynth.VoiceFX fx) => new AdditiveVoice(f, a, s, e, sr, fx);
        public static Voice FM       (float c, float i, float r, float s, ADSR e, int sr, ProceduralSynth.VoiceFX fx) => new FMVoice(c, i, r, s, e, sr, fx);
        public static Voice AM       (float c, float m, float d, float s, ADSR e, int sr, ProceduralSynth.VoiceFX fx) => new AMVoice(c, m, d, s, e, sr, fx);
        public static Voice Wavetable(float f, float[] a, float s, ADSR e, int sr, int N, ProceduralSynth.VoiceFX fx) => new WavetableVoice(f, a, s, e, sr, N, fx);
        public static Voice DecaySineKick(float b, float ds, float de, float s, ADSR e, int sr, ProceduralSynth.VoiceFX fx) => new KickVoice(b, ds, de, s, e, sr, fx);
        public static Voice NoiseBurst(float s, ADSR e, int sr, ProceduralSynth.VoiceFX fx) => new NoiseVoice(s, e, sr, fx);
    }

    class AdditiveVoice : Voice
    {
        float freq; float[] amps;
        public AdditiveVoice(float f, float[] a, float seconds, ADSR adsr, int sr, ProceduralSynth.VoiceFX fx)
            : base(seconds, adsr, sr, fx) { freq = f; amps = a ?? new float[] { 1f }; }
        protected override float Sample(float time)
        {
            float s = 0f; for (int k = 0; k < amps.Length; k++) if (amps[k] != 0f)
                s += amps[k] * Mathf.Sin(2f * Mathf.PI * (k + 1) * freq * time);
            return s;
        }
    }

    class FMVoice : Voice
    {
        float fc, fm, index;
        public FMVoice(float carrierFreq, float modIndex, float modRatio, float seconds, ADSR adsr, int sr, ProceduralSynth.VoiceFX fx)
            : base(seconds, adsr, sr, fx)
        { fc = Mathf.Max(1f, carrierFreq); fm = Mathf.Max(1f, carrierFreq * Mathf.Max(0.0001f, modRatio)); index = Mathf.Max(0f, modIndex); }
        protected override float Sample(float time)
        {
            float mod = index * Mathf.Sin(2f * Mathf.PI * fm * time);
            return Mathf.Sin(2f * Mathf.PI * fc * time + mod);
        }
    }

    class AMVoice : Voice
    {
        float fc, fm, depth;
        public AMVoice(float carrierFreq, float modFreq, float modDepth, float seconds, ADSR adsr, int sr, ProceduralSynth.VoiceFX fx)
            : base(seconds, adsr, sr, fx)
        { fc = carrierFreq; fm = modFreq; depth = Mathf.Clamp01(modDepth); }
        protected override float Sample(float time)
        {
            float car = Mathf.Sin(2f * Mathf.PI * fc * time);
            float m = (1f - depth) + depth * Mathf.Sin(2f * Mathf.PI * fm * time);
            return car * m;
        }
    }

    class WavetableVoice : Voice
    {
        float freq; float[] table; float phase; float phaseInc;
        public WavetableVoice(float f, float[] harmonicAmps, float seconds, ADSR adsr, int sr, int tableSize, ProceduralSynth.VoiceFX fx)
            : base(seconds, adsr, sr, fx)
        {
            freq = f; table = BuildTable(harmonicAmps, tableSize); phaseInc = freq / sr;
        }
        float[] BuildTable(float[] amps, int N)
        {
            if (amps == null || amps.Length == 0) amps = new float[] { 1f };
            var t = new float[N];
            for (int n = 0; n < N; n++)
            {
                float acc = 0f, x = (float)n / N;
                for (int k = 0; k < amps.Length; k++) acc += amps[k] * Mathf.Sin(2f * Mathf.PI * (k + 1) * x);
                t[n] = acc;
            }
            float max = 1e-6f; for (int i = 0; i < N; i++) max = Mathf.Max(max, Mathf.Abs(t[i]));
            for (int i = 0; i < N; i++) t[i] /= max;
            return t;
        }
        protected override float Sample(float time)
        {
            phase += phaseInc; phase -= Mathf.Floor(phase);
            float fIdx = phase * table.Length;
            int i0 = (int)fIdx; int i1 = (i0 + 1) % table.Length;
            float frac = fIdx - i0; return Mathf.Lerp(table[i0], table[i1], frac);
        }
    }

    class KickVoice : Voice
    {
        float baseFreq, startHz, endHz;
        public KickVoice(float baseF, float dropStart, float dropEnd, float seconds, ADSR adsr, int sr, ProceduralSynth.VoiceFX fx)
            : base(seconds, adsr, sr, fx) { baseFreq = baseF; startHz = dropStart; endHz = dropEnd; }
        protected override float Sample(float time)
        {
            float drop = Mathf.Lerp(startHz, endHz, time / Mathf.Max(1e-5f, duration));
            float f = baseFreq + drop;
            return Mathf.Sin(2f * Mathf.PI * f * time) * Mathf.Exp(-6f * time);
        }
    }

    class NoiseVoice : Voice
    {
        System.Random rng = new System.Random(1337);
        float z1;
        public NoiseVoice(float seconds, ADSR adsr, int sr, ProceduralSynth.VoiceFX fx) : base(seconds, adsr, sr, fx) { }
        protected override float Sample(float time)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            float a = 0.12f; z1 = Mathf.Lerp(z1, x, a); return z1;
        }
    }

    // ===== Live Preview =====
    LiveVoice _live;

    class LiveParams
    {
        // FX
        public float pan = 0f; public bool filterEnabled = false; public FilterType filterType = FilterType.Lowpass; public float filterCutoff = 1200f; public float filterQ = 0.707f;
        // Engines
        public SoundEngineType engine = SoundEngineType.FM;
        public float fmCarrier = 220f, fmIndex = 2.5f, fmRatio = 2f;
        public float awFreq = 220f; public float[] harmonics = new float[] { 1f };

        public void FromPreset(SoundPreset p)
        {
            pan = p.pan; filterEnabled = p.filterEnabled; filterType = p.filterType; filterCutoff = p.filterCutoff; filterQ = p.filterQ;
            engine = p.engine; fmCarrier = p.fmCarrier; fmIndex = p.fmIndex; fmRatio = p.fmModRatio;
            awFreq = p.awFreq; harmonics = (p.harmonicAmps != null) ? p.harmonicAmps.ToArray() : new float[] { 1f };
        }
    }

    class LiveVoice
    {
        readonly int sr;
        readonly LiveParams P = new LiveParams();
        Biquad biq = null;
        float pan, gL, gR;
        FilterType ftype; bool fOn; float fCut, fQ;

        float fm_phaseC, fm_phaseM;
        float wt_phase;
        float[] wavetable; int wtN; float wtFreq;

        public LiveVoice(int sampleRate) { sr = sampleRate; }
        void RecalcFX()
        {
            pan = Mathf.Clamp(P.pan, -1f, 1f);
            gL = Mathf.Cos((pan + 1f) * Mathf.PI * 0.25f);
            gR = Mathf.Sin((pan + 1f) * Mathf.PI * 0.25f);
            fOn = P.filterEnabled; ftype = P.filterType; fCut = P.filterCutoff; fQ = P.filterQ;
            if (fOn)
            {
                if (biq == null) biq = new Biquad();
                biq.Set(ftype, fCut, fQ, sr);
            }
            else biq = null;
        }
        void BuildTable(float[] amps, int N)
        {
            if (amps == null || amps.Length == 0) amps = new float[] { 1f };
            if (wavetable == null || wtN != N) { wavetable = new float[N]; wtN = N; }
            for (int n = 0; n < N; n++)
            {
                float acc = 0f, x = (float)n / N;
                for (int k = 0; k < amps.Length; k++) acc += amps[k] * Mathf.Sin(2f * Mathf.PI * (k + 1) * x);
                wavetable[n] = acc;
            }
            float max = 1e-6f; for (int i = 0; i < N; i++) max = Mathf.Max(max, Mathf.Abs(wavetable[i]));
            for (int i = 0; i < N; i++) wavetable[i] /= max;
        }

        public void SetFromPreset(SoundPreset p)
        {
            P.FromPreset(p);
            RecalcFX();
            if (P.engine == SoundEngineType.Wavetable || P.engine == SoundEngineType.Additive)
            {
                wtFreq = P.awFreq; BuildTable(P.harmonics, 2048);
            }
        }

        public void UpdateFromPreset(SoundPreset p)
        {
            P.FromPreset(p);
            RecalcFX();
            if (P.engine == SoundEngineType.Wavetable || P.engine == SoundEngineType.Additive)
            {
                if (Mathf.Abs(P.awFreq - wtFreq) > 0.01f || wavetable == null || wtN != 2048)
                { wtFreq = P.awFreq; BuildTable(P.harmonics, 2048); }
            }
        }

        public void Render(float[] data, int channels, float master)
        {
            // Only continuous engines in live
            if (P.engine != SoundEngineType.FM &&
                P.engine != SoundEngineType.Wavetable &&
                P.engine != SoundEngineType.Additive) return;

            int frames = data.Length / channels;

            for (int i = 0; i < frames; i++)
            {
                float s = 0f;
                switch (P.engine)
                {
                    case SoundEngineType.FM:
                        {
                            float incC = P.fmCarrier / sr;
                            float incM = (P.fmCarrier * Mathf.Max(0.0001f, P.fmRatio)) / sr;
                            fm_phaseM += incM; fm_phaseM -= Mathf.Floor(fm_phaseM);
                            float mod = P.fmIndex * Mathf.Sin(2f * Mathf.PI * fm_phaseM);
                            fm_phaseC += incC; fm_phaseC -= Mathf.Floor(fm_phaseC);
                            s = Mathf.Sin(2f * Mathf.PI * fm_phaseC + mod);
                        }
                        break;

                    case SoundEngineType.Wavetable:
                    case SoundEngineType.Additive:
                        {
                            float inc = P.awFreq / sr;
                            wt_phase += inc; wt_phase -= Mathf.Floor(wt_phase);
                            float fIdx = wt_phase * wtN;
                            int i0 = (int)fIdx; int i1 = (i0 + 1) % wtN;
                            float frac = fIdx - i0;
                            s = Mathf.Lerp(wavetable[i0], wavetable[i1], frac);
                        }
                        break;
                }

                if (biq != null) s = biq.Process(s);
                s = Tanhf(s * 2.0f) * master;

                float l = s * gL, r = s * gR;
                if (channels == 1) data[i] += (l + r) * 0.7071f;
                else { int idx = i * channels; data[idx] += l; data[idx + 1] += r; }
            }
        }
    }

    public void StartLivePreview(SoundPreset p) { _live = new LiveVoice(_sampleRate); _live.SetFromPreset(p); }
    public void UpdateLivePreview(SoundPreset p) { if (_live != null) _live.UpdateFromPreset(p); }
    public void StopLivePreview() { _live = null; }
}


