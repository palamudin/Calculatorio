using UnityEngine;
using System.Collections.Generic;

/// Lightweight “SVG-style” theme without external packages.
/// Renders rounded rectangles, circles, tracks, etc. into Texture2D and caches them.
public class SvgTheme
{
    public struct Theme
    {
        public Color panelBg;
        public Color panelBorder;
        public Color knobFace;
        public Color knobRing;
        public Color tick;
        public Color buttonBg;
        public Color buttonBorder;
        public Color buttonText;

        public Color switchOn;
        public Color switchOff;
        public Color switchThumb;
        public Color sliderTrack;
        public Color sliderFill;
        public Color sliderHandle;
    }

    public Theme Colors;
    readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

    public SvgTheme(bool dark = true)
    {
        if (dark)
        {
            Colors = new Theme {
                panelBg     = new Color(0.12f, 0.12f, 0.12f, 1f),
                panelBorder = new Color(0.18f, 0.18f, 0.18f, 1f),
                knobFace    = new Color(0.34f, 0.34f, 0.34f, 1f),
                knobRing    = new Color(0.20f, 0.20f, 0.20f, 1f),
                tick        = Color.black,
                buttonBg    = new Color(0.22f, 0.22f, 0.22f, 1f),
                buttonBorder= new Color(0.35f, 0.35f, 0.35f, 1f),
                buttonText  = new Color(0.92f, 0.92f, 0.92f, 1f),
                switchOn    = new Color(0.21f, 0.55f, 0.35f, 1f),
                switchOff   = new Color(0.25f, 0.25f, 0.28f, 1f),
                switchThumb = new Color(0.93f, 0.93f, 0.95f, 1f),
                sliderTrack = new Color(0.26f, 0.26f, 0.28f, 1f),
                sliderFill  = new Color(0.45f, 0.58f, 0.88f, 1f),
                sliderHandle= new Color(0.92f, 0.92f, 0.92f, 1f)
            };
        }
        else
        {
            Colors = new Theme {
                panelBg     = new Color(0.95f, 0.95f, 0.97f, 1f),
                panelBorder = new Color(0.85f, 0.85f, 0.90f, 1f),
                knobFace    = new Color(0.88f, 0.88f, 0.90f, 1f),
                knobRing    = new Color(0.75f, 0.75f, 0.80f, 1f),
                tick        = Color.black,
                buttonBg    = new Color(0.90f, 0.90f, 0.93f, 1f),
                buttonBorder= new Color(0.65f, 0.65f, 0.70f, 1f),
                buttonText  = Color.black,
                switchOn    = new Color(0.25f, 0.62f, 0.40f, 1f),
                switchOff   = new Color(0.85f, 0.85f, 0.88f, 1f),
                switchThumb = new Color(0.97f, 0.97f, 0.99f, 1f),
                sliderTrack = new Color(0.86f, 0.86f, 0.90f, 1f),
                sliderFill  = new Color(0.35f, 0.50f, 0.88f, 1f),
                sliderHandle= new Color(0.30f, 0.30f, 0.32f, 1f)
            };
        }
    }

    // -------- Public factory methods (cached) --------

    public Texture2D Panel(int width, int height, float radius = 10f, float border = 1.5f)
    {
        string key = $"panel_{width}x{height}_{radius}_{border}_{Colors.panelBg.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(width, height, true);
        FillRoundedRect(tex, new RectInt(0,0,width,height), radius, Colors.panelBg);
        StrokeRoundedRect(tex, new RectInt(0,0,width,height), radius, Colors.panelBorder, Mathf.CeilToInt(border));
        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    public Texture2D Button(int width, int height, float radius = 8f)
    {
        string key = $"btn_{width}x{height}_{radius}_{Colors.buttonBg.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(width, height, true);
        FillRoundedRect(tex, new RectInt(0,0,width,height), radius, Colors.buttonBg);
        StrokeRoundedRect(tex, new RectInt(0,0,width,height), radius, Colors.buttonBorder, 1);
        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    public Texture2D KnobFace(int size, int tickCount = 11)
    {
        string key = $"knob_{size}_{tickCount}_{Colors.knobFace.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(size, size, true);

        float rOuter = size * 0.48f;
        float rInner = rOuter - Mathf.Max(2f, size * 0.06f);
        FillCircle(tex, size/2f, size/2f, rOuter, Colors.knobFace);
        FillCircle(tex, size/2f, size/2f, rInner, Colors.knobRing);

        // ticks
        float a0 = -135f * Mathf.Deg2Rad, a1 = 135f * Mathf.Deg2Rad;
        for (int i = 0; i < tickCount; i++)
        {
            float t = (tickCount == 1) ? 0f : i / (float)(tickCount - 1);
            float a = Mathf.Lerp(a0, a1, t);
            Vector2 p0 = new Vector2(size/2f + Mathf.Cos(a) * (rOuter - 3f),
                                     size/2f + Mathf.Sin(a) * (rOuter - 3f));
            Vector2 p1 = new Vector2(size/2f + Mathf.Cos(a) * (rOuter - 9f),
                                     size/2f + Mathf.Sin(a) * (rOuter - 9f));
            DrawLine(tex, p0, p1, Colors.tick);
        }

        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    public Texture2D SwitchBG(int width, int height, bool on)
    {
        string key = $"switch_{width}x{height}_{on}_{Colors.switchOn.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(width, height, true);
        float radius = height * 0.5f;
        FillRoundedRect(tex, new RectInt(0,0,width,height), radius, on ? Colors.switchOn : Colors.switchOff);
        StrokeRoundedRect(tex, new RectInt(0,0,width,height), radius, Colors.panelBorder, 1);
        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    public Texture2D SwitchThumb(int diameter)
    {
        string key = $"switchThumb_{diameter}_{Colors.switchThumb.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(diameter, diameter, true);
        FillCircle(tex, diameter/2f, diameter/2f, diameter*0.48f, Colors.switchThumb);
        StrokeCircle(tex, diameter/2f, diameter/2f, diameter*0.48f, Colors.panelBorder, 1);
        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    public Texture2D SliderTrack(int width, int height)
    {
        string key = $"sTrack_{width}x{height}_{Colors.sliderTrack.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(width, height, true);
        float radius = height * 0.5f;
        FillRoundedRect(tex, new RectInt(0,0,width,height), radius, Colors.sliderTrack);
        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    public Texture2D SliderFill(int width, int height)
    {
        string key = $"sFill_{width}x{height}_{Colors.sliderFill.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(width, height, true);
        float radius = height * 0.5f;
        FillRoundedRect(tex, new RectInt(0,0,width,height), radius, Colors.sliderFill);
        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    public Texture2D SliderHandle(int size)
    {
        string key = $"sHandle_{size}_{Colors.sliderHandle.r}";
        if (_cache.TryGetValue(key, out var tex)) return tex;
        tex = MakeTex(size, size, true);
        FillCircle(tex, size/2f, size/2f, size*0.48f, Colors.sliderHandle);
        StrokeCircle(tex, size/2f, size/2f, size*0.48f, Colors.panelBorder, 1);
        tex.Apply(false, true);
        _cache[key] = tex; return tex;
    }

    // -------- Minimal raster “vector” helpers --------

    Texture2D MakeTex(int w, int h, bool transparent)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        var cols = new Color[w * h];
        var c = transparent ? new Color(0,0,0,0) : Color.clear;
        for (int i = 0; i < cols.Length; i++) cols[i] = c;
        tex.SetPixels(cols);
        return tex;
    }

    static void SetPixelSafe(Texture2D tex, int x, int y, Color c)
    {
        if ((uint)x < (uint)tex.width && (uint)y < (uint)tex.height)
            tex.SetPixel(x, y, c);
    }

    void FillRoundedRect(Texture2D tex, RectInt r, float radius, Color col)
    {
        int w = tex.width, h = tex.height;
        int x0 = r.x, y0 = r.y, x1 = r.x + r.width - 1, y1 = r.y + r.height - 1;
        float rr = Mathf.Max(0f, radius);
        // Fast fill center block
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                // Corner distance check
                bool inside = true;
                if (rr > 0f)
                {
                    // top-left
                    if (x < x0 + rr && y > y1 - rr)
                        inside = (Vector2.SqrMagnitude(new Vector2(x - (x0 + rr), y - (y1 - rr))) <= rr*rr);
                    // top-right
                    else if (x > x1 - rr && y > y1 - rr)
                        inside = (Vector2.SqrMagnitude(new Vector2(x - (x1 - rr), y - (y1 - rr))) <= rr*rr);
                    // bottom-left
                    else if (x < x0 + rr && y < y0 + rr)
                        inside = (Vector2.SqrMagnitude(new Vector2(x - (x0 + rr), y - (y0 + rr))) <= rr*rr);
                    // bottom-right
                    else if (x > x1 - rr && y < y0 + rr)
                        inside = (Vector2.SqrMagnitude(new Vector2(x - (x1 - rr), y - (y0 + rr))) <= rr*rr);
                }
                if (inside) SetPixelSafe(tex, x, y, col);
            }
        }
    }

    void StrokeRoundedRect(Texture2D tex, RectInt r, float radius, Color col, int thickness)
    {
        thickness = Mathf.Max(1, thickness);
        // Outer fill
        StrokeRect(tex, r, col, thickness, radius);
    }

    void StrokeRect(Texture2D tex, RectInt r, Color col, int t, float radius = 0f)
    {
        // top/bottom bars
        for (int i = 0; i < t; i++)
        {
            FillRoundedRect(tex, new RectInt(r.x, r.y + r.height - 1 - i, r.width, 1), radius, col);
            FillRoundedRect(tex, new RectInt(r.x, r.y + i, r.width, 1), radius, col);
        }
        // left/right bars
        for (int i = 0; i < t; i++)
        {
            FillRoundedRect(tex, new RectInt(r.x + i, r.y, 1, r.height), radius, col);
            FillRoundedRect(tex, new RectInt(r.x + r.width - 1 - i, r.y, 1, r.height), radius, col);
        }
    }

    void FillCircle(Texture2D tex, float cx, float cy, float r, Color col)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r));
        int x1 = Mathf.Min(tex.width - 1, Mathf.CeilToInt(cx + r));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r));
        int y1 = Mathf.Min(tex.height - 1, Mathf.CeilToInt(cy + r));
        float rr = r * r;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rr)
                    tex.SetPixel(x, y, col);
    }

    void StrokeCircle(Texture2D tex, float cx, float cy, float r, Color col, int thickness)
    {
        float r0 = Mathf.Max(0, r - thickness * 0.5f);
        float r1 = r + thickness * 0.5f;
        float r0s = r0 * r0, r1s = r1 * r1;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r1));
        int x1 = Mathf.Min(tex.width - 1, Mathf.CeilToInt(cx + r1));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r1));
        int y1 = Mathf.Min(tex.height - 1, Mathf.CeilToInt(cy + r1));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (d2 >= r0s && d2 <= r1s) tex.SetPixel(x, y, col);
            }
    }

    // Bresenham-ish line
    void DrawLine(Texture2D tex, Vector2 a, Vector2 b, Color col)
    {
        int x0 = Mathf.RoundToInt(a.x), y0 = Mathf.RoundToInt(a.y);
        int x1 = Mathf.RoundToInt(b.x), y1 = Mathf.RoundToInt(b.y);
        int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy, e2;
        while (true)
        {
            SetPixelSafe(tex, x0, y0, col);
            if (x0 == x1 && y0 == y1) break;
            e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }
}
