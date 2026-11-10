using System.Collections.Generic;
using UnityEngine;

namespace Gladiator.PS2Style
{
    /// <summary>
    /// Time of day for the town, the ludus and the arena, from the town clock (TownSimulation.Hour):
    ///   06:00-07:00 dawn, 07:00-18:00 day, 18:00-19:00 dusk, 19:00-06:00 night.
    /// Drives the sun (or the moon at night: colour, strength, angle), the sky dome's colours, the fog / horizon colour,
    /// the global ambient dimmer of the PS2 shader (baked vertex ambient gets darker and bluer at night) and a layer of
    /// drifting low-poly clouds tinted by the hour. Added by each scene's bootstrap with <see cref="Attach"/>.
    /// </summary>
    public class DayNightCycle : MonoBehaviour
    {
        public enum Phase { Night, Dawn, Day, Dusk }

        struct Look
        {
            public Color Horizon, Top, Sun, Cloud, Dim;
            public float SunIntensity;
            public static Look Lerp(Look a, Look b, float t) => new Look
            {
                Horizon = Color.Lerp(a.Horizon, b.Horizon, t), Top = Color.Lerp(a.Top, b.Top, t), Sun = Color.Lerp(a.Sun, b.Sun, t),
                Cloud = Color.Lerp(a.Cloud, b.Cloud, t), Dim = Color.Lerp(a.Dim, b.Dim, t), SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
            };
        }

        public static DayNightCycle Instance { get; private set; }
        static readonly int DimId = Shader.PropertyToID("_PS2_AmbientDim");

        /// <summary>Hour used when there is no town clock (tests can force one).</summary>
        public static float? ForcedHour;
        public float Hour { get; private set; }
        public Phase Current { get; private set; }
        public int CloudCount => clouds.Count;

        Light sun;
        Camera cam;
        Mesh sky;
        float[] skyT;
        Color[] skyCols;
        Vector3 domeCenter;
        float domeRadius;
        Look day, dawn, dusk, night;
        Quaternion sunDayRot;
        float sunDayIntensity;
        Color sunDayColor;
        Material cloudMat, cloudShade;
        Transform cloudRoot;
        float cloudLow = float.MaxValue;
        readonly List<Transform> clouds = new List<Transform>();
        float refresh;

        public static string PhaseName(Phase p) => p == Phase.Dawn ? "AMANECER" : p == Phase.Day ? "DIA" : p == Phase.Dusk ? "ATARDECER" : "NOCHE";

        /// <summary>Sets the cycle up for the current scene: its sun, sky dome ("SkyDome") and camera.</summary>
        public static DayNightCycle Attach(Transform parent)
        {
            var go = new GameObject("DayNightCycle");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<DayNightCycle>();
            c.Init();
            return c;
        }

        void Init()
        {
            Instance = this;
            sun = RenderSettings.sun;
            cam = Camera.main;
            if (sun != null) { sunDayRot = sun.transform.rotation; sunDayIntensity = sun.intensity; sunDayColor = sun.color; }
            Color fog = RenderSettings.fogColor, top = new Color(0.42f, 0.56f, 0.74f);
            var dome = GameObject.Find("SkyDome");
            if (dome != null && dome.TryGetComponent<MeshFilter>(out var mf))
            {
                sky = mf.mesh; // own copy: recoloured
                var v = sky.vertices;
                var col = sky.colors;
                domeCenter = dome.transform.position;
                domeRadius = 1f;
                foreach (var p in v) domeRadius = Mathf.Max(domeRadius, p.y);
                skyT = new float[v.Length];
                skyCols = new Color[v.Length];
                float best = -1f;
                for (int i = 0; i < v.Length; i++)
                {
                    skyT[i] = Mathf.Pow(Mathf.Clamp01(v[i].y / domeRadius), 0.55f);
                    if (v[i].y > best && col != null && i < col.Length) { best = v[i].y; top = col[i]; }
                }
            }
            else { domeCenter = Vector3.zero; domeRadius = 200f; }

            day = new Look { Horizon = fog, Top = top, Sun = sunDayColor, SunIntensity = sunDayIntensity, Cloud = new Color(0.97f, 0.96f, 0.93f), Dim = Color.black };
            dawn = new Look { Horizon = new Color(0.96f, 0.62f, 0.45f), Top = new Color(0.42f, 0.46f, 0.66f), Sun = new Color(1f, 0.62f, 0.4f), SunIntensity = 0.6f,
                              Cloud = new Color(1f, 0.72f, 0.6f), Dim = new Color(0.25f, 0.32f, 0.35f) };
            dusk = new Look { Horizon = new Color(0.95f, 0.48f, 0.28f), Top = new Color(0.3f, 0.26f, 0.5f), Sun = new Color(1f, 0.5f, 0.28f), SunIntensity = 0.55f,
                              Cloud = new Color(0.98f, 0.55f, 0.4f), Dim = new Color(0.3f, 0.4f, 0.42f) };
            night = new Look { Horizon = new Color(0.09f, 0.11f, 0.2f), Top = new Color(0.02f, 0.03f, 0.09f), Sun = new Color(0.55f, 0.65f, 0.95f), SunIntensity = 0.32f,
                               Cloud = new Color(0.22f, 0.24f, 0.32f), Dim = new Color(0.66f, 0.6f, 0.42f) };
            BuildClouds();
            Apply(true);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Shader.SetGlobalVector(DimId, Vector4.zero);
        }

        void BuildClouds()
        {
            cloudMat = new Material(PS2Materials.LitShader) { name = "PS2_clouds" };
            cloudMat.SetTexture("_BaseMap", PS2Textures.White);
            cloudMat.SetColor("_BaseColor", Color.white);
            cloudMat.SetFloat("_LightingAmount", 0f);
            cloudMat.SetFloat("_FogAmount", 0f);
            cloudShade = new Material(cloudMat) { name = "PS2_clouds_shade" };
            var root = cloudRoot = new GameObject("Clouds").transform;
            root.SetParent(transform, false);
            var rng = new System.Random(1234);
            float R = domeRadius;
            int n = 16;
            for (int k = 0; k < n; k++)
            {
                var cloud = new GameObject("Cloud" + k).transform;
                cloud.SetParent(root, false);
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, dist = Mathf.Lerp(0.15f, 0.72f, (float)rng.NextDouble()) * R;
                cloud.position = domeCenter + new Vector3(Mathf.Cos(ang) * dist, Mathf.Lerp(0.3f, 0.42f, (float)rng.NextDouble()) * R, Mathf.Sin(ang) * dist);
                cloud.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                // a flat cluster of blocky puffs (PS2 style)
                int puffs = 4 + rng.Next(5);
                float size = Mathf.Lerp(0.05f, 0.09f, (float)rng.NextDouble()) * R;
                for (int i = 0; i < puffs; i++)
                {
                    var p = GameObject.CreatePrimitive(i % 2 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                    Destroy(p.GetComponent<Collider>());
                    p.transform.SetParent(cloud, false);
                    float s = size * Mathf.Lerp(0.6f, 1.1f, (float)rng.NextDouble());
                    p.transform.localPosition = new Vector3((i - puffs * 0.5f) * size * 0.55f, ((float)rng.NextDouble() - 0.3f) * size * 0.25f, ((float)rng.NextDouble() - 0.5f) * size * 0.6f);
                    p.transform.localScale = new Vector3(s * 1.4f, s * 0.45f, s);
                    p.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 40f - 20f, 0f);
                    var r = p.GetComponent<Renderer>();
                    r.sharedMaterial = i % 2 == 0 ? cloudMat : cloudShade; // two tones: they do not look flat
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                cloudLow = Mathf.Min(cloudLow, cloud.position.y - size);
                clouds.Add(cloud);
            }
        }

        static float SmoothStep(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        Look LookAt(float h)
        {
            // 06:00 night -> 06:30 dawn -> 07:00 day ... 18:00 day -> 18:30 dusk -> 19:00 night
            if (h >= 6f && h < 6.5f) return Look.Lerp(night, dawn, SmoothStep(6f, 6.5f, h));
            if (h >= 6.5f && h < 7f) return Look.Lerp(dawn, day, SmoothStep(6.5f, 7f, h));
            if (h >= 7f && h < 18f) return day;
            if (h >= 18f && h < 18.5f) return Look.Lerp(day, dusk, SmoothStep(18f, 18.5f, h));
            if (h >= 18.5f && h < 19f) return Look.Lerp(dusk, night, SmoothStep(18.5f, 19f, h));
            return night;
        }

        public static Phase PhaseAt(float h) =>
            h >= 6f && h < 7f ? Phase.Dawn : h >= 7f && h < 18f ? Phase.Day : h >= 18f && h < 19f ? Phase.Dusk : Phase.Night;

        float CurrentHour()
        {
            if (ForcedHour.HasValue) return ForcedHour.Value;
            var sim = Gladiator.Town.TownSimulation.Instance;
            return sim != null ? sim.Hour : 10f;
        }

        void Update()
        {
            // clouds drift with the wind and come round again
            float drift = 1.2f * Time.deltaTime;
            foreach (var c in clouds)
            {
                c.position += Vector3.right * drift;
                Vector3 rel = c.position - domeCenter;
                if (rel.x > domeRadius * 0.75f) c.position -= Vector3.right * domeRadius * 1.5f;
            }
            // a camera up among the clouds (bird's-eye views) sees the town, not a white sheet
            if (cam == null) cam = Camera.main;
            if (cloudRoot != null && cam != null)
            {
                bool show = cam.transform.position.y < cloudLow - 4f;
                if (cloudRoot.gameObject.activeSelf != show) cloudRoot.gameObject.SetActive(show);
            }
            if ((refresh -= Time.unscaledDeltaTime) > 0f) return;
            refresh = 0.2f;
            Apply(false);
        }

        void Apply(bool first)
        {
            Hour = CurrentHour();
            Current = PhaseAt(Hour);
            var look = LookAt(Hour);
            RenderSettings.fogColor = look.Horizon;
            if (cam == null) cam = Camera.main;
            if (cam != null) cam.backgroundColor = look.Horizon;
            Shader.SetGlobalVector(DimId, new Vector4(look.Dim.r, look.Dim.g, look.Dim.b, 0f));
            if (sun != null)
            {
                sun.color = look.Sun;
                sun.intensity = look.SunIntensity;
                // the sun climbs from the east at 6, peaks at noon and sets in the west at 19; at night the moon
                float h = Hour;
                bool isNight = h < 6f || h >= 19f;
                float t = isNight ? 0.5f : Mathf.InverseLerp(6f, 19f, h);
                float pitch = isNight ? 55f : Mathf.Lerp(8f, 62f, Mathf.Sin(t * Mathf.PI));
                float yaw = isNight ? 200f : Mathf.Lerp(80f, 280f, t);
                sun.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            if (sky != null)
            {
                for (int i = 0; i < skyT.Length; i++) skyCols[i] = Color.Lerp(look.Horizon, look.Top, skyT[i]);
                sky.colors = skyCols;
            }
            if (cloudMat != null) cloudMat.SetColor("_BaseColor", look.Cloud);
            if (cloudShade != null) cloudShade.SetColor("_BaseColor", Color.Lerp(look.Cloud, look.Top, 0.22f) * 0.92f);
        }
    }
}
