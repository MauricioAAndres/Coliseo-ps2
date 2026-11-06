using Gladiator.Core;
using Gladiator.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// Small flat panel: day + phase, the three training stats and the fight record.
    /// A stat that just went up blinks gold for a moment.
    /// </summary>
    public class StatsPanel : MonoBehaviour
    {
        Text dayText, recordText, derivedText, moneyText;
        readonly Text[] statTexts = new Text[3];
        readonly int[] shown = new int[3];
        readonly float[] blink = new float[3];
        GameManager gm;

        public static StatsPanel Create(RectTransform parent, Vector2 anchor, Vector2 pos)
        {
            var bg = PS2UI.Rect(parent, "StatsPanel", PS2UI.PanelBg);
            PS2UI.Anchored(bg, anchor, pos, new Vector2(236, 90));
            var border = PS2UI.Rect(bg, "Top", new Color(0.6f, 0.45f, 0.2f));
            PS2UI.Anchored(border, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(236, 2));
            border.anchorMin = new Vector2(0, 1); border.anchorMax = new Vector2(1, 1); border.sizeDelta = new Vector2(0, 2);

            var p = bg.gameObject.AddComponent<StatsPanel>();
            p.dayText = PS2UI.Text(bg, "", 13, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(7, -5), new Vector2(180, 16), PS2UI.Gold);
            for (int i = 0; i < 3; i++)
                p.statTexts[i] = PS2UI.Text(bg, "", 12, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(7 + i * 62, -23), new Vector2(60, 14), Color.white);
            p.derivedText = PS2UI.Text(bg, "", 10, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(7, -40), new Vector2(220, 12), PS2UI.Parchment);
            p.moneyText = PS2UI.Text(bg, "", 12, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(7, -55), new Vector2(220, 14), PS2UI.Gold);
            p.recordText = PS2UI.Text(bg, "", 10, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(7, -73), new Vector2(220, 12), PS2UI.Parchment);
            return p;
        }

        void Start()
        {
            gm = GameManager.Ensure();
            for (int i = 0; i < 3; i++) shown[i] = gm.Profile.Get((StatType)i);
        }

        void Update()
        {
            if (gm == null) return;
            var dc = gm.DayCycle;
            dayText.text = "DIA " + dc.Day + "  -  " + DayCycleManager.PhaseLabel(dc.Phase);
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < 3; i++)
            {
                var st = (StatType)i;
                int v = gm.Profile.Get(st);
                if (v != shown[i]) { blink[i] = 2f; shown[i] = v; }
                blink[i] = Mathf.Max(0f, blink[i] - dt);
                statTexts[i].text = PlayerProfile.Short(st) + " " + v;
                bool on = blink[i] > 0f && Mathf.Repeat(blink[i], 0.3f) > 0.12f;
                statTexts[i].color = on ? PS2UI.Gold : Color.white;
            }
            derivedText.text = StatEffects.Describe(gm.Profile);
            moneyText.text = "DENARIOS " + gm.Profile.Denarii;
            recordText.text = "COMBATES  V " + gm.Profile.Wins + "  /  D " + gm.Profile.Losses;
        }
    }
}
