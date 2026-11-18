using UnityEngine;
using UnityEngine.SceneManagement;

namespace Calculatorio
{
    public class MainMenu : MonoBehaviour
    {
        [Header("Sphere/Planet")]
        public Vector2Int sphereSegments = new Vector2Int(48, 96);
        public float radius = 2.2f;
        public float amplitude = 0.35f;
        public float frequency = 1.1f;
        public float evolveSpeed = 0.15f;
        public Vector3 rotationEulerPerSec = new Vector3(0f, 10f, 0f);
        public int seed = 1337;

        [Header("UI Scaling")]
        public bool autoScale = true;
        public Vector2 referenceResolution = new Vector2(1920, 1080);
        [Tooltip("Extra multiplier on top of the auto scale")]
        public float uiScale = 1.6f;

        [Header("Layout")]
        [Tooltip("Pixels ABOVE the planet top where the title block ends")]
        public float titleOffsetPx = 40f;
        public float baseButtonWidth = 220f;
        public float baseButtonHeight = 48f;
        public float baseButtonGap = 16f;
        public float buttonRowYFrac = 0.78f; // buttons near bottom

        [Header("Scene Names")]
        public string planetScene = "Planet";
        public string countryScene = "Country";
        public string cityScene = "City";

        NoiseSphere _sphere;

        void Awake()
        {
            // Camera
            if (Camera.main == null)
            {
                var camGO = new GameObject("Main Camera");
                var cam = camGO.AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.06f, 0.07f, 0.10f);
                cam.transform.position = new Vector3(0, 0, -7.5f);
                cam.transform.rotation = Quaternion.identity;
            }
            else
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = new Color(0.06f, 0.07f, 0.10f);
                Camera.main.transform.position = new Vector3(0, 0, -7.5f);
                Camera.main.transform.rotation = Quaternion.identity;
            }

            // Light
            if (FindObjectOfType<Light>() == null)
            {
                var lightGO = new GameObject("Directional Light");
                var l = lightGO.AddComponent<Light>();
                l.type = LightType.Directional;
                l.color = new Color(1f, 0.97f, 0.9f);
                l.intensity = 1.15f;
                lightGO.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            }

            // Planet (procedural)
            var sphereGO = new GameObject("PlanetPreview");
            _sphere = sphereGO.AddComponent<NoiseSphere>();
            _sphere.latSegments = Mathf.Max(6, sphereSegments.x);
            _sphere.lonSegments = Mathf.Max(8, sphereSegments.y);
            _sphere.baseRadius = Mathf.Max(0.1f, radius);
            _sphere.noiseAmplitude = Mathf.Max(0f, amplitude);
            _sphere.noiseFrequency = Mathf.Max(0.01f, frequency);
            _sphere.noiseEvolveSpeed = Mathf.Max(0f, evolveSpeed);
            _sphere.rotationEulerPerSec = rotationEulerPerSec;
            _sphere.seed = seed;
            _sphere.Build();
        }

        void OnGUI()
        {
            // --- Scale for readability (sizes only, not positions) ---
            float auto = 1f;
            if (autoScale && referenceResolution.x > 0 && referenceResolution.y > 0)
                auto = Mathf.Min(Screen.width / referenceResolution.x, Screen.height / referenceResolution.y);
            float S = Mathf.Max(0.8f, auto * uiScale);

            // --- Title/subtitle block ABOVE the planet, with fixed 40 px gap ---
            float titleH = 42f * S;
            float subtitleH = 30f * S;
            float spacing = 4f * S;

            float yTopPlanet = SphereTopScreenY();
            float blockH = titleH + spacing + subtitleH;
            float yStart = Mathf.Max(10f, yTopPlanet - titleOffsetPx - blockH);

            // Title
            GUI.Label(
                new Rect(0, yStart, Screen.width, titleH),
                "CALCULATORIO",
                CenterStyle(Mathf.RoundToInt(30 * S))
            );

            // Subtitle (still above planet)
            GUI.Label(
                new Rect(0, yStart + titleH + spacing, Screen.width, subtitleH),
                "Tweak sectors. Balance needs. Keep them happy.",
                CenterStyle(Mathf.RoundToInt(18 * S), new Color(0.85f, 0.9f, 1f))
            );

            // --- Buttons row (unchanged) ---
            float bw = baseButtonWidth * S;
            float bh = baseButtonHeight * S;
            float gap = baseButtonGap * S;

            float totalW = bw * 3f + gap * 2f;
            float startX = (Screen.width - totalW) * 0.5f;
            float y = Screen.height * buttonRowYFrac;

            if (GUI.Button(new Rect(startX + 0 * (bw + gap), y, bw, bh), "Planet", ButtonStyle(Mathf.RoundToInt(18 * S))))
                TryLoad(planetScene);
            if (GUI.Button(new Rect(startX + 1 * (bw + gap), y, bw, bh), "Country", ButtonStyle(Mathf.RoundToInt(18 * S))))
                TryLoad(countryScene);
            if (GUI.Button(new Rect(startX + 2 * (bw + gap), y, bw, bh), "City", ButtonStyle(Mathf.RoundToInt(18 * S))))
                TryLoad(cityScene);

            #if UNITY_STANDALONE
            if (GUI.Button(new Rect(Screen.width - (bw * 0.8f + 16), 16f, bw * 0.8f, bh * 0.6f),
                           "Quit", ButtonStyle(Mathf.RoundToInt(14 * S))))
                Application.Quit();
            #endif
        }

        // --- Screen-space Y for the TOP of the sphere (IMGUI coords: y=0 top) ---
        float SphereTopScreenY()
        {
            var cam = Camera.main;
            if (cam == null) return Screen.height * 0.25f; // fallback

            float r = (_sphere != null ? _sphere.baseRadius + _sphere.noiseAmplitude * 1.05f
                                       : radius + amplitude * 1.05f);

            Vector3 topWorld = new Vector3(0f, +r, 0f);
            Vector3 sp = cam.WorldToScreenPoint(topWorld);
            return Screen.height - sp.y;
        }

        // ---------- Helpers ----------
        static void TryLoad(string scene)
        {
            if (!string.IsNullOrEmpty(scene))
            {
                try { SceneManager.LoadScene(scene); }
                catch { Debug.LogError($"Scene '{scene}' not found in Build Settings."); }
            }
        }

        GUIStyle CenterStyle(int size, Color? color = null)
        {
            var s = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                alignment = TextAnchor.UpperCenter
            };
            s.normal.textColor = color ?? Color.white;
            return s;
        }

        GUIStyle ButtonStyle(int size)
        {
            return new GUIStyle(GUI.skin.button) { fontSize = size };
        }
    }
}
