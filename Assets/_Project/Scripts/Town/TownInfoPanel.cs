using System.Collections.Generic;
using System.Text;
using Gladiator.PS2Style;
using Gladiator.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Gladiator.Town
{
    /// <summary>
    /// Town clock (always on screen) and, with F3, a window onto the simulation: what the 100 townsfolk are doing,
    /// who is in the plaza and at the fountain, who is near you and the timetable of the nearest one.
    /// F4 shows a marker where each person who is outdoors is "logically" (no bodies yet: phase 3).
    /// With the window open, Page Up / Page Down move the clock one hour.
    /// </summary>
    public class TownInfoPanel : MonoBehaviour
    {
        TownBootstrap town;
        TownSimulation sim;
        Text clock, body;
        GameObject panel;
        Transform markerRoot;
        readonly List<Transform> markers = new List<Transform>();
        float refresh;

        public bool PanelOpen => panel != null && panel.activeSelf;
        public bool MarkersOn { get; private set; }

        public void Init(TownBootstrap t, Camera cam)
        {
            town = t;
            sim = TownSimulation.Ensure();
            var root = PS2UI.CreateCanvas(gameObject, cam);
            clock = PS2UI.Text(root, "", 16, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-14, -10), new Vector2(260, 22), PS2UI.Gold);
            var p = PS2UI.Rect(root, "SimPanel", new Color(0.06f, 0.04f, 0.03f, 0.82f));
            // the HUD canvas is the 448-line frame: keep the window inside its top-right quarter
            PS2UI.Anchored(p, new Vector2(1, 1), new Vector2(-8, -34), new Vector2(300, 212)); // pivot = top-right corner, under the clock
            body = PS2UI.Text(p, "", 9, TextAnchor.UpperLeft, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(288, 204), PS2UI.Parchment);
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            panel = p.gameObject;
            panel.SetActive(false);
            markerRoot = new GameObject("SimMarkers").transform;
        }

        public void SetPanel(bool on) { if (panel != null) panel.SetActive(on); refresh = 0f; }
        public void SetMarkers(bool on) { MarkersOn = on; if (markerRoot != null) markerRoot.gameObject.SetActive(on); refresh = 0f; }

        void Update()
        {
            if (sim == null || town == null) return;
            clock.text = "DIA " + sim.Day + "   " + TownSimulation.Clock(sim.Hour);
            var k = Keyboard.current;
            bool paused = Gladiator.Core.GameInput.Paused;
            if (k != null && !paused)
            {
                if (k.f3Key.wasPressedThisFrame) SetPanel(!PanelOpen);
                if (k.f4Key.wasPressedThisFrame) SetMarkers(!MarkersOn);
                if (PanelOpen && k.pageUpKey.wasPressedThisFrame) { sim.Hour = Mathf.Repeat(sim.Hour + 1f, 24f); refresh = 0f; }
                if (PanelOpen && k.pageDownKey.wasPressedThisFrame) { sim.Hour = Mathf.Repeat(sim.Hour - 1f, 24f); refresh = 0f; }
            }
            if ((refresh -= Time.unscaledDeltaTime) > 0f) return;
            refresh = 0.25f;
            if (PanelOpen) body.text = Describe();
            if (MarkersOn) UpdateMarkers();
        }

        string Who(CitizenData c, float hour)
        {
            var l = sim.WhereIs(c, hour);
            return c.Name + " (" + c.JobName + ", " + (l.Walking ? "de camino" : TownSimulation.ActivityName(l.Activity)) + ")";
        }

        static string Short(CitizenData c)
        {
            int sp = c.Name.IndexOf(' ');
            return sp > 0 ? c.Name.Substring(0, sp) + " " + c.Name[sp + 1] + "." : c.Name;
        }

        string Describe()
        {
            float h = sim.Hour;
            var sb = new StringBuilder();
            sb.Append("EL PUEBLO  ").Append(sim.Citizens.Count).Append(" hab.  ").Append(TownSimulation.Clock(h))
              .Append("     F3 cerrar  F4 marcas  RePag/AvPag +-1h\n");
            var census = sim.Census(h);
            sb.Append("TU FAMA: ").Append(TownSimulation.ReputationLabel(sim.Reputation)).Append(" (").Append(sim.Reputation.ToString("0")).Append(")   ");
            if (town.Spawner != null) sb.Append("con cuerpo cerca de ti: ").Append(town.Spawner.ActiveCount).Append('/').Append(CitizenSpawner.PoolSize).Append("   ");
            foreach (var kv in census) sb.Append(kv.Key).Append(' ').Append(kv.Value).Append("  ");
            sb.Append("\n\n");
            var plaza = sim.At("plaza", h);
            sb.Append("PLAZA ").Append(plaza.Count).Append(": ");
            for (int i = 0; i < plaza.Count && i < 4; i++)
            {
                if (i > 0) sb.Append(", ");
                var l = sim.WhereIs(plaza[i], h);
                sb.Append(Short(plaza[i])).Append(" (").Append(plaza[i].JobName).Append(", ").Append(TownSimulation.ActivityName(l.Activity)).Append(')');
            }
            if (plaza.Count > 4) sb.Append("...");
            int market = 0;
            foreach (var c in sim.Citizens) { var l = sim.WhereIs(c, h); if (!l.Walking && l.PlaceId.StartsWith("stall")) market++; }
            sb.Append("\nFUENTE ").Append(sim.At("fountain", h).Count).Append("   MERCADO ").Append(market)
              .Append("   FUERA A MENOS DE 25 m: ").Append(sim.Near(town.Player.transform.position, 25f, h).Count).Append("\n\n");

            // the nearest person (indoors or not) and their day
            CitizenData best = null; float bestD = float.MaxValue;
            foreach (var c in sim.Citizens)
            {
                float d = (sim.WhereIs(c, h).Position - town.Player.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best != null)
            {
                var l = sim.WhereIs(best, h);
                sb.Append("MAS CERCANO: ").Append(best.Name).Append(", ").Append(best.JobName).Append(", ").Append(best.Age)
                  .Append(" (").Append(best.RelationLabel).Append(")\nahora: ")
                  .Append(l.Walking ? "de camino a " + sim.PlaceName(l.PlaceId) : TownSimulation.ActivityName(l.Activity) + " - " + sim.PlaceName(l.PlaceId)).Append('\n');
                foreach (var e in sim.ScheduleFor(best, sim.Day))
                    sb.Append(TownSimulation.Clock(e.Start)).Append(' ').Append(TownSimulation.ActivityName(e.Activity)).Append(" - ").Append(sim.PlaceName(e.PlaceId)).Append('\n');
            }
            return sb.ToString();
        }

        static Color ColorOf(Activity a, bool walking)
        {
            if (walking) return Color.white;
            switch (a)
            {
                case Activity.Trabajar: return new Color(0.85f, 0.25f, 0.15f);
                case Activity.Mercado: return new Color(0.95f, 0.75f, 0.2f);
                case Activity.Pasear: return new Color(0.3f, 0.75f, 0.3f);
                case Activity.Agua: return new Color(0.3f, 0.6f, 0.95f);
                default: return new Color(0.7f, 0.4f, 0.85f);
            }
        }

        void UpdateMarkers()
        {
            float h = sim.Hour;
            int n = 0;
            foreach (var c in sim.Citizens)
            {
                var l = sim.WhereIs(c, h);
                if (l.Indoors) continue;
                if (n >= markers.Count)
                {
                    var go = PS2Spawn.Box(markerRoot, "Citizen", new Vector3(0.3f, 1.7f, 0.3f), Vector3.zero, PS2Materials.Flat(Color.white, 0f));
                    go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    markers.Add(go.transform);
                }
                var m = markers[n++];
                m.gameObject.SetActive(true);
                m.position = l.Position + Vector3.up * 0.85f;
                m.GetComponent<Renderer>().sharedMaterial = PS2Materials.Flat(ColorOf(l.Activity, l.Walking), 0f);
            }
            for (int i = n; i < markers.Count; i++) markers[i].gameObject.SetActive(false);
        }

        /// <summary>Markers shown now (tests).</summary>
        public int VisibleMarkers { get { int n = 0; foreach (var m in markers) if (m.gameObject.activeSelf) n++; return n; } }
    }
}
