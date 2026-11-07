using UnityEngine;
using UnityEngine.UI;

namespace Gladiator.UI
{
    /// <summary>
    /// HUD for the ludus: stats panel, interaction prompt, messages, and a generic minigame panel
    /// (title, instructions, horizontal meter with zones/marker/fill, counter, big feedback word, result banner).
    /// </summary>
    public class TrainingHUD : MonoBehaviour
    {
        Text promptText, msgText, titleText, infoText, counterText, feedbackText, resultTitle, resultSub;
        RectTransform panel, meterBg, zoneYellow, zoneGreen, marker, fill;
        GameObject result;
        float msgTimer, feedbackTimer, resultTimer;

        const float MeterW = 320f, MeterH = 20f;

        public ShopMenu Shop { get; private set; }

        public void Init(Camera cam)
        {
            var root = PS2UI.CreateCanvas(gameObject, cam);
            StatsPanel.Create(root, new Vector2(0, 1), new Vector2(10, -8));

            promptText = PS2UI.Text(root, "", 14, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0, 70), new Vector2(500, 20), new Color(1f, 0.92f, 0.5f));
            msgText = PS2UI.Text(root, "", 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -112), new Vector2(620, 26), PS2UI.Gold);
            hintText = PS2UI.Text(root, "WASD mover   E interactuar   puerta norte: E elegir combate   puerta este: pueblo (tiendas en las herrerias)   F1 post   F2 snap   ESC menu",
                10, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(900, 14), PS2UI.Parchment);

            // ---- minigame panel (bottom centre)
            panel = PS2UI.Rect(root, "MinigamePanel", PS2UI.PanelBg);
            PS2UI.Anchored(panel, new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(380, 108));
            titleText = PS2UI.Text(panel, "", 16, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -6), new Vector2(360, 20), PS2UI.Gold);
            infoText = PS2UI.Text(panel, "", 11, TextAnchor.UpperCenter, new Vector2(0.5f, 1), new Vector2(0, -27), new Vector2(360, 14), PS2UI.Parchment);
            counterText = PS2UI.Text(panel, "", 12, TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(360, 14), Color.white);

            var frame = PS2UI.Rect(panel, "MeterFrame", new Color(0.02f, 0.01f, 0.01f));
            PS2UI.Anchored(frame, new Vector2(0.5f, 0.5f), new Vector2(0, -8), new Vector2(MeterW + 4, MeterH + 4));
            meterBg = PS2UI.Rect(frame, "MeterBg", new Color(0.32f, 0.12f, 0.08f));
            PS2UI.Stretch(meterBg, 2);
            fill = PS2UI.Rect(meterBg, "Fill", PS2UI.Green);
            zoneYellow = PS2UI.Rect(meterBg, "ZoneY", new Color(0.75f, 0.6f, 0.12f));
            zoneGreen = PS2UI.Rect(meterBg, "ZoneG", new Color(0.25f, 0.7f, 0.2f));
            marker = PS2UI.Rect(meterBg, "Marker", Color.white);
            panel.gameObject.SetActive(false);

            feedbackText = PS2UI.Text(root, "", 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(600, 34), Color.white);

            // ---- result banner
            var r = PS2UI.Rect(root, "Result", new Color(0f, 0f, 0f, 0.72f));
            r.anchorMin = new Vector2(0, 0.5f); r.anchorMax = new Vector2(1, 0.5f);
            r.sizeDelta = new Vector2(0, 70); r.anchoredPosition = new Vector2(0, 10);
            result = r.gameObject;
            resultTitle = PS2UI.Text(r, "", 28, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(600, 34), PS2UI.Gold);
            resultSub = PS2UI.Text(r, "", 12, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(600, 16), Color.white);
            result.SetActive(false);

            Shop = ShopMenu.Create(root);
        }

        // ------------------------------------------------------------------ API
        public void SetPrompt(string s) { promptText.text = s ?? ""; }

        Text hintText;
        /// <summary>The help line at the bottom (the town shows its own).</summary>
        public void SetHint(string s) { if (hintText != null) hintText.text = s ?? ""; }

        public void ShowMessage(string s, float duration = 2f) { msgText.text = s; msgTimer = duration; }

        public void ShowMinigame(string title, string info)
        {
            panel.gameObject.SetActive(true);
            titleText.text = title;
            infoText.text = info;
            counterText.text = "";
            SetZones(-1f, -1f, 0f);
            SetMarker(-1f);
            SetFill(-1f, PS2UI.Green);
        }

        public void HideMinigame() { panel.gameObject.SetActive(false); }

        public void SetCounter(string s) { counterText.text = s; }

        /// <summary>Green zone [min,max] in 0..1 with a yellow "near" margin around it. Negative hides it.</summary>
        public void SetZones(float min, float max, float yellowMargin)
        {
            bool show = min >= 0f;
            zoneGreen.gameObject.SetActive(show);
            zoneYellow.gameObject.SetActive(show && yellowMargin > 0f);
            if (!show) return;
            Span(zoneGreen, min, max);
            Span(zoneYellow, Mathf.Max(0f, min - yellowMargin), Mathf.Min(1f, max + yellowMargin));
        }

        public void SetMarker(float v)
        {
            marker.gameObject.SetActive(v >= 0f);
            if (v < 0f) return;
            marker.anchorMin = new Vector2(v, 0f); marker.anchorMax = new Vector2(v, 1f);
            marker.pivot = new Vector2(0.5f, 0.5f);
            marker.sizeDelta = new Vector2(4f, 6f);
            marker.anchoredPosition = Vector2.zero;
        }

        public void SetFill(float v, Color c)
        {
            fill.gameObject.SetActive(v >= 0f);
            if (v < 0f) return;
            fill.GetComponent<Image>().color = c;
            Span(fill, 0f, Mathf.Clamp01(v));
        }

        public void Feedback(string s, Color c, float duration = 0.7f)
        {
            feedbackText.text = s;
            feedbackText.color = c;
            feedbackTimer = duration;
        }

        public void ShowResult(string title, string sub, float duration = 2.5f)
        {
            result.SetActive(true);
            resultTitle.text = title;
            resultSub.text = sub;
            resultTimer = duration;
        }

        public void HideResult() { resultTimer = 0f; result.SetActive(false); }

        static void Span(RectTransform r, float a, float b)
        {
            r.anchorMin = new Vector2(a, 0f); r.anchorMax = new Vector2(b, 1f);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (msgTimer > 0f) { msgTimer -= dt; if (msgTimer <= 0f) msgText.text = ""; }
            if (feedbackTimer > 0f)
            {
                feedbackTimer -= dt;
                var c = feedbackText.color; c.a = Mathf.Clamp01(feedbackTimer * 3f); feedbackText.color = c;
                if (feedbackTimer <= 0f) feedbackText.text = "";
            }
            if (resultTimer > 0f) { resultTimer -= dt; if (resultTimer <= 0f) result.SetActive(false); }
        }
    }
}
