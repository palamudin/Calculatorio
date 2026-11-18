using UnityEngine;

namespace Calculatorio
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class NoiseSphere : MonoBehaviour
    {
        [Header("Mesh")]
        public int latSegments = 48;
        public int lonSegments = 96;
        public float baseRadius = 2.2f;

        [Header("Noise / Height")]
        [Range(0.0f, 1.0f)] public float seaLevel = 0.45f;   // <— below this = ocean (flat)
        public float noiseAmplitude = 0.35f;                  // land extrusion amount
        public float noiseFrequency = 1.1f;                   // base frequency
        public int   octaves = 3;
        public float lacunarity = 2.0f;
        public float gain = 0.5f;
        public float noiseEvolveSpeed = 0.15f;                // animate noise (optional)
        public int   seed = 1337;

        [Header("Motion")]
        public Vector3 rotationEulerPerSec = new Vector3(0f, 10f, 0f);

        [Header("Texture")]
        public int texWidth = 1024;
        public int texHeight = 512;
        [Range(0.001f, 0.15f)] public float shoreWidth = 0.03f; // color blend width around sea level

        Mesh _mesh;
        Material _mat;
        Texture2D _tex;
        float _t;

        // seeds for “3D-ish” Perlin combo (seamless over sphere)
        Vector3 _seedOff;

        void Awake()  { Build(); }
        void OnEnable(){ if (_mesh == null) Build(); }
        void OnValidate()
        {
            latSegments = Mathf.Max(6, latSegments);
            lonSegments = Mathf.Max(8, lonSegments);
            seaLevel = Mathf.Clamp01(seaLevel);
            if (Application.isPlaying == false) Build();
        }

        public void Build()
        {
            Random.InitState(seed);
            _seedOff = new Vector3(Random.value * 1000f, Random.value * 1000f, Random.value * 1000f);

            // Mesh
            _mesh = GenerateSphere();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            // Material
            var sh = PickShader();
            if (_mat != null) DestroyImmediate(_mat);
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };

            // Texture (colors follow the SAME height function as the mesh)
            _tex = GeneratePlanetTexture(texWidth, texHeight);
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", _tex);
            if (_mat.HasProperty("_MainTex")) _mat.SetTexture("_MainTex", _tex);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", Color.white);
            if (_mat.HasProperty("_Color"))     _mat.SetColor("_Color", Color.white);

            GetComponent<MeshRenderer>().sharedMaterial = _mat;
        }

        void Update()
        {
            _t += Time.deltaTime * noiseEvolveSpeed;
            transform.Rotate(rotationEulerPerSec * Time.deltaTime, Space.Self);

            // If you want a static planet, set noiseEvolveSpeed = 0.
        }

        // ────────────────────────── internals ──────────────────────────

        Shader PickShader()
        {
            string[] names = {
                "Unlit/Texture", "Legacy Shaders/Diffuse", "Unlit/Color",
                "Standard", "Sprites/Default", "UI/Default"
            };
            foreach (var n in names) { var s = Shader.Find(n); if (s) return s; }
            return Shader.Find("Hidden/InternalErrorShader");
        }

        Mesh GenerateSphere()
        {
            int vCount = (latSegments + 1) * (lonSegments + 1);
            var verts   = new Vector3[vCount];
            var normals = new Vector3[vCount];
            var uvs     = new Vector2[vCount];
            var tris    = new int[latSegments * lonSegments * 6];

            int vi = 0;

            for (int lat = 0; lat <= latSegments; lat++)
            {
                float v = lat / (float)latSegments;
                float phi = Mathf.PI * v; // 0..PI

                for (int lon = 0; lon <= lonSegments; lon++)
                {
                    float u = lon / (float)lonSegments;
                    float theta = Mathf.PI * 2f * u;

                    // Direction on unit sphere
                    Vector3 dir = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta)
                    );

                    // Height 0..1 from spherical noise
                    float h01 = Height01(dir);

                    // Ocean stays flat; land extrudes above sea level
                    float land = Mathf.Max(0f, (h01 - seaLevel) / Mathf.Max(1e-5f, 1f - seaLevel));
                    float r = baseRadius + land * noiseAmplitude;

                    verts[vi]   = dir * r;
                    normals[vi] = dir;    // good enough for gentle displacements
                    uvs[vi]     = new Vector2(u, v);
                    vi++;
                }
            }

            // Indices
            int w = lonSegments + 1;
            int ti = 0;
            for (int lat = 0; lat < latSegments; lat++)
            {
                for (int lon = 0; lon < lonSegments; lon++)
                {
                    int a = lat * w + lon;
                    int b = a + w;
                    int c = a + 1;
                    int d = b + 1;

                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = c;
                    tris[ti++] = c; tris[ti++] = b; tris[ti++] = d;
                }
            }

            var mesh = new Mesh();
            mesh.name = "NoiseSphere";
            mesh.indexFormat = (vi > 65000)
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Fractal “3D-ish” Perlin from direction vector (seamless across 0..1 in U)
        float Height01(Vector3 dir)
        {
            float f = noiseFrequency;
            float a = 1f;
            float sum = 0f;
            float norm = 0f;

            for (int i = 0; i < Mathf.Max(1, octaves); i++)
            {
                // 3 two-D samples mixed to emulate 3D noise
                float n =
                    Mathf.PerlinNoise(_seedOff.x + dir.x * f + _t, _seedOff.y + dir.y * f + _t) * 0.6f +
                    Mathf.PerlinNoise(_seedOff.y + dir.y * f * 2f - _t, _seedOff.z + dir.z * f * 2f + _t) * 0.3f +
                    Mathf.PerlinNoise(_seedOff.z + dir.z * f * 4f + _t, _seedOff.x + dir.x * f * 4f - _t) * 0.1f;

                sum  += n * a;
                norm += a;
                f *= lacunarity;
                a *= gain;
            }
            return Mathf.Clamp01(sum / Mathf.Max(1e-5f, norm));
        }

        Texture2D GeneratePlanetTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            // palette
            Color oceanDeep = new Color(0.05f, 0.18f, 0.35f);
            Color oceanShal = new Color(0.05f, 0.32f, 0.55f);
            Color shore     = new Color(0.85f, 0.80f, 0.55f);
            Color landLow   = new Color(0.18f, 0.45f, 0.20f);
            Color landHigh  = new Color(0.65f, 0.60f, 0.50f);
            Color snow      = new Color(0.92f, 0.94f, 0.96f);

            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                float phi = Mathf.PI * v;

                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1);
                    float theta = Mathf.PI * 2f * u;

                    // Same direction used for mesh -> perfect match
                    Vector3 dir = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta)
                    );

                    float h01 = Height01(dir);

                    // color by “depth/height” around sea level
                    Color c;
                    if (h01 < seaLevel)
                    {
                        float d = Mathf.InverseLerp(seaLevel - 0.25f, seaLevel, h01);
                        c = Color.Lerp(oceanDeep, oceanShal, d);
                    }
                    else if (h01 < seaLevel + shoreWidth)
                    {
                        float t = Mathf.InverseLerp(seaLevel, seaLevel + shoreWidth, h01);
                        c = Color.Lerp(oceanShal, shore, t);
                    }
                    else if (h01 < 0.70f)
                    {
                        float t = Mathf.InverseLerp(seaLevel + shoreWidth, 0.70f, h01);
                        c = Color.Lerp(landLow, landHigh, t);
                    }
                    else
                    {
                        float t = Mathf.InverseLerp(0.70f, 0.95f, h01);
                        c = Color.Lerp(landHigh, snow, t);
                    }

                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply(false, false);
            return tex;
        }
    }
}
